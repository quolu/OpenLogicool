using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenLogicool.Host;

/// <summary>画面から読んだ文字のまとまり。X・Y は中心で、画像全体に対する割合。</summary>
public sealed record ScreenText(string Text, double X, double Y);

/// <summary>
/// 判断の結果。Target が押す場所で、無い時は押さない（Reason が理由）。
/// Pressable は文字ごとの「押して選ぶボタンの名前か」、Probabilities は選択肢ごとの確率。
/// </summary>
public sealed record ScreenChoiceJudgment(string Goal, string[] Background, string[] Texts,
    Dictionary<string, double> Pressable, string? Choice, Dictionary<string, double> Probabilities,
    ScreenText? Target, string? Reason, long ElapsedMs);

/// <summary>
/// 画面の規則が当てはまらない時の汎用の判断。今の目的と、画面から読んだ文字を Jev へ渡し、目的のために押す文字を選ばせる。
/// ゲームの中身（クエスト・場所・画面の名前、進め方の原則）は問いへ書かない。うまく選べない画面は、進行設定の規則を先に置く。
/// </summary>
public sealed class ScreenChoiceJudge(Func<JsonObject, CancellationToken, Task<JsonElement>> ask)
{
    public const string RuleId = "screen-judge";
    public const string None = "（どれも押さない）";
    // 実測（2026-10-11・結果の画面）: ボタンの名前は0.91以上、キーの名前「ESC」は0.46、説明文・数値は0.16以下。
    private const double PressableAtLeast = 0.5;

    public async Task<ScreenChoiceJudgment> ChooseAsync(string goal, IReadOnlyList<string> background,
        IReadOnlyList<ScreenText> texts, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var names = texts.Select(text => text.Text).Where(name => name != None).Distinct(StringComparer.Ordinal).ToArray();
        ScreenChoiceJudgment Result(Dictionary<string, double> pressable, string? choice, Dictionary<string, double> probabilities,
            ScreenText? target, string? reason) =>
            new(goal, [.. background], names, pressable, choice, probabilities, target, reason, clock.ElapsedMilliseconds);
        if (names.Length == 0) return Result([], null, [], null, "画面から文字を読めません。");
        JsonArray Screen() => new(names.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());

        // 1) 押して選ぶボタンの名前かを、文字ごとに聞く。目的は渡さない。キーの名前・説明文・数値がここで落ちる。
        var first = await ask(new JsonObject
        {
            ["state"] = new JsonObject { ["screen_texts"] = Screen() },
            ["questions"] = new JsonObject(names.Select((name, index) => KeyValuePair.Create<string, JsonNode?>($"t{index}", new JsonObject
            {
                ["type"] = "noul",
                ["instructions"] = new JsonObject
                {
                    ["label"] = name,
                    ["question"] = "ゲームの画面から読み取った文字の一覧が `screen_texts` にある。`label` は、プレイヤーが押して選ぶボタンの名前か？",
                },
                ["criteria"] = new JsonObject
                {
                    ["true"] = "押すと何かが起きるボタン・選択肢の名前",
                    ["false"] = "説明文、状態や結果の表示、場所や物の名前、数値、キーの名前",
                },
            }))),
        }, token);
        var pressable = names.Select((name, index) => (name, value: first.GetProperty("answers").GetProperty($"t{index}").GetProperty("noul").GetDouble()))
            .ToDictionary(item => item.name, item => item.value);
        var candidates = names.Where(name => pressable[name] >= PressableAtLeast).ToArray();
        if (candidates.Length == 0) return Result(pressable, null, [], null, "押して選ぶ文字が画面にありません。");

        // 2) 目的のために押すものを、押せる文字の中から選ばせる。背景があれば、目的の背景として渡す。
        var state = new JsonObject { ["goal"] = goal, ["screen_texts"] = Screen() };
        if (background.Count > 0) state["background"] = new JsonArray(background.Select(line => (JsonNode?)JsonValue.Create(line)).ToArray());
        var criteria = new JsonObject(candidates.Select(name => KeyValuePair.Create<string, JsonNode?>(name, null)));
        criteria[None] = null;
        var second = await ask(new JsonObject
        {
            ["state"] = state,
            ["questions"] = new JsonObject
            {
                ["press"] = new JsonObject
                {
                    ["type"] = "choice",
                    ["instructions"] = background.Count > 0
                        ? "ゲームを進めている。今の目的は `goal`。目的の背景は `background`。画面には `screen_texts` の文字が出ている。目的のために、今この画面で押すものを選ぶ。"
                        : "ゲームを進めている。今の目的は `goal`。画面には `screen_texts` の文字が出ている。目的のために、今この画面で押すものを選ぶ。",
                    ["criteria"] = criteria,
                },
            },
        }, token);
        var answer = second.GetProperty("answers").GetProperty("press");
        var choice = answer.GetProperty("choice").GetString();
        var probabilities = answer.GetProperty("probabilities").EnumerateObject()
            .ToDictionary(option => option.Name, option => option.Value.GetDouble());
        if (choice == None) return Result(pressable, choice, probabilities, null, "Jevが「どれも押さない」を選びました。");
        var places = texts.Where(text => text.Text == choice).ToArray();
        return places.Length == 1 ? Result(pressable, choice, probabilities, places[0], null)
            : Result(pressable, choice, probabilities, null,
                places.Length == 0 ? "選ばれた文字が画面の文字にありません。" : "選ばれた文字が画面に複数あり、押す場所を決められません。");
    }

    /// <summary>Jev（TypeSafe）へ問い合わせる。失敗は例外で知らせ、別の判断へ切り替えない。</summary>
    public static ScreenChoiceJudge Jev(HttpClient http, string apiKey, string model) => new(async (body, token) =>
    {
        body["model"] = model;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.typesafe.ai/v1/systemone")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Jevへの問い合わせに失敗しました: HTTP {(int)response.StatusCode} {text[..Math.Min(300, text.Length)]}");
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    });
}

/// <summary>
/// 判断に使うJevの接続設定。鍵はファイルの場所だけを持ち、設定・記録へ鍵そのものを書かない。
/// 鍵のファイルは TYPESAFE_API_KEY=... の行を含む。
/// </summary>
public sealed record ScreenJudgeSettings(string ApiKeyFile, string Model = "jev-latest")
{
    public const string FileName = "bot-screen-judge.json";
    private const string KeyName = "TYPESAFE_API_KEY=";

    public static ScreenChoiceJudge Load(string path)
    {
        var settings = JsonSerializer.Deserialize<ScreenJudgeSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("画面の判断の設定が空です。");
        if (string.IsNullOrWhiteSpace(settings.ApiKeyFile) || string.IsNullOrWhiteSpace(settings.Model))
            throw new InvalidDataException("画面の判断の設定には、鍵のファイルの場所とモデルを指定します。");
        var key = File.ReadLines(settings.ApiKeyFile).Select(line => line.Trim())
            .Where(line => line.StartsWith(KeyName, StringComparison.Ordinal))
            .Select(line => line[KeyName.Length..].Trim().Trim('"', '\''))
            .FirstOrDefault(value => value.Length > 0)
            ?? throw new InvalidDataException("鍵のファイルに TYPESAFE_API_KEY がありません。");
        return ScreenChoiceJudge.Jev(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, key, settings.Model);
    }
}

public static class ScreenTextReader
{
    /// <summary>
    /// 読み取った文字を、同じ行で間の空いていないまとまりにする。まとまりが、判断の選択肢と押す場所になる。
    /// 1文字だけのまとまりは選択肢にしない（景色を文字と読み違えたものが多い）。
    /// </summary>
    public static ScreenText[] Read(WindowsGameOcrResult ocr, int width, int height)
    {
        // 読み直しで足された文字（白い文字の読み直しなど）は、元の読み取りと同じ場所に重なる。先に読めたほうを残す。
        var words = new List<WindowsGameOcrWord>();
        foreach (var word in ocr.Words.Where(word => !string.IsNullOrWhiteSpace(word.Text)))
            if (!words.Any(kept => SamePlace(kept, word))) words.Add(word);
        var lines = new List<List<WindowsGameOcrWord>>();
        foreach (var word in words.OrderBy(word => word.Y + word.Height / 2))
        {
            if (lines.Count == 0 || !SameLine(lines[^1], word)) lines.Add([]);
            lines[^1].Add(word);
        }
        var texts = new List<ScreenText>();
        foreach (var line in lines)
        {
            var run = new List<WindowsGameOcrWord>();
            foreach (var word in line.OrderBy(word => word.X))
            {
                // 文字の高さの1.5倍より間が空いたら、別のまとまり（隣のボタンなど）。
                if (run.Count > 0 && word.X - (run[^1].X + run[^1].Width) > Math.Max(16, Math.Max(run[^1].Height, word.Height) * 1.5))
                {
                    Add(run);
                    run = [];
                }
                run.Add(word);
            }
            Add(run);
        }
        return [.. texts];

        // 重なりが、小さいほうの文字の面積の半分以上なら、同じ場所の文字。
        static bool SamePlace(WindowsGameOcrWord left, WindowsGameOcrWord right)
        {
            var width = Math.Min(left.X + left.Width, right.X + right.Width) - Math.Max(left.X, right.X);
            var height = Math.Min(left.Y + left.Height, right.Y + right.Height) - Math.Max(left.Y, right.Y);
            return width > 0 && height > 0
                && width * height >= 0.5 * Math.Min(left.Width * left.Height, right.Width * right.Height);
        }

        // 同じ行の文字は、縦の中心がそろう。行の間が詰まった文章を混ぜないよう、ずれは文字の高さの半分までとする。
        // 句読点のような小さい文字は中心が下へずれるので、行の高さの中に収まっていれば同じ行とする。
        static bool SameLine(List<WindowsGameOcrWord> line, WindowsGameOcrWord word) =>
            Math.Abs(word.Y + word.Height / 2 - line.Average(other => other.Y + other.Height / 2))
                <= 0.5 * Math.Min(word.Height, line.Average(other => other.Height))
            || word.Y >= line.Min(other => other.Y) - 1 && word.Y + word.Height <= line.Max(other => other.Y + other.Height) + 1;

        void Add(List<WindowsGameOcrWord> run)
        {
            var text = new StringBuilder();
            foreach (var word in run.Select(word => word.Text.Trim()))
            {
                // 英数字どうしは、語の間に空白を戻す。
                if (text.Length > 0 && char.IsAsciiLetterOrDigit(text[^1]) && char.IsAsciiLetterOrDigit(word[0])) text.Append(' ');
                text.Append(word);
            }
            if (text.Length < 2) return;
            var left = run.Min(word => word.X);
            var right = run.Max(word => word.X + word.Width);
            var top = run.Min(word => word.Y);
            var bottom = run.Max(word => word.Y + word.Height);
            texts.Add(new(text.ToString(), (left + right) / 2 / width, (top + bottom) / 2 / height));
        }
    }
}
