using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>
/// 規則に一致しない画面で、目的のために押す文字をJevへ選ばせる。Jevへの問い合わせは差し替え、渡す内容と結果の扱いを確かめる。
/// 画面は、実機で止まった結果の画面（退出・滞在する・新しい任務を選択）を使う。
/// </summary>
public sealed class ScreenChoiceJudgeTests
{
    private static string Fixture(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008", name);
    }

    private static readonly string[] Card =
    [
        "魔族を召喚する結界第", "0390m", "狩り場に強い魔族が出現する場所があるらしいです。",
        "・スチュアートに調査の結果を報告", "お礼", "進進行行",
    ];

    [Fact]
    public async Task 実機の結果の画面から押す文字のまとまりを読む()
    {
        var frame = ReadFrame(Fixture("field-result-screen.png"));
        var texts = ScreenTextReader.Read(await new WindowsGameOcrRecognizer().RecognizeAsync(frame), frame.Width, frame.Height);
        // 横に並ぶ3つのボタンは、別のまとまりになる。
        var exit = Assert.Single(texts, text => text.Text == "退出");
        Assert.Single(texts, text => text.Text == "滞在する");
        Assert.Single(texts, text => text.Text == "新しい任務を選択");
        // 「退出」の場所は、ボタンの中（横590〜757・縦1021〜1087）。
        Assert.InRange(exit.X * frame.Width, 600, 750);
        Assert.InRange(exit.Y * frame.Height, 1030, 1080);
        // 行の間が詰まった吹き出しの2行と、隣の説明文は混ざらない。
        Assert.Contains(texts, text => text.Text.StartsWith("[女神の庭]", StringComparison.Ordinal));
        Assert.Contains(texts, text => text.Text.StartsWith("チャレンジ", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, text => text.Text.Length < 2);
    }

    [Fact]
    public async Task ボタンの名前だけを選択肢にして選ばれた文字の場所を返す()
    {
        var requests = new List<JsonObject>();
        var judge = new ScreenChoiceJudge((body, _) =>
        {
            requests.Add(JsonNode.Parse(body.ToJsonString())!.AsObject());
            return Task.FromResult(requests.Count == 1
                ? Answers("""{ "t0": { "noul": 0.05 }, "t1": { "noul": 0.42 }, "t2": { "noul": 0.94 }, "t3": { "noul": 0.92 } }""")
                : Answers("""{ "press": { "choice": "退出", "probabilities": { "退出": 0.63, "滞在する": 0.03, "（どれも押さない）": 0.34 } } }"""));
        });
        ScreenText[] texts = [new("発見した戦利品", 0.5, 0.5), new("ESC", 0.35, 0.92), new("退出", 0.39, 0.94), new("滞在する", 0.5, 0.94)];

        var judgment = await judge.ChooseAsync("スチュアートに調査の結果を報告", Card, texts, CancellationToken.None);

        Assert.Equal("退出", judgment.Choice);
        Assert.Equal(texts[2], judgment.Target);
        Assert.Null(judgment.Reason);
        // 1回目は、目的を渡さずに、文字ごとに押して選ぶボタンの名前かを聞く。
        Assert.False(requests[0]["state"]!.AsObject().ContainsKey("goal"));
        Assert.Equal(4, requests[0]["questions"]!.AsObject().Count);
        // 2回目は、目的と背景と画面の文字を渡し、ボタンの名前（0.5以上）と「どれも押さない」だけを選択肢にする。
        var state = requests[1]["state"]!.AsObject();
        Assert.Equal("スチュアートに調査の結果を報告", state["goal"]!.GetValue<string>());
        Assert.Equal(Card, state["background"]!.AsArray().Select(line => line!.GetValue<string>()));
        Assert.Equal(4, state["screen_texts"]!.AsArray().Count);
        var press = requests[1]["questions"]!["press"]!.AsObject();
        Assert.Equal(["退出", "滞在する", ScreenChoiceJudge.None], press["criteria"]!.AsObject().Select(option => option.Key));
        // ゲームの中身（クエスト・場所の名前）は、問いの文へ書かない。
        Assert.DoesNotContain("スチュアート", press["instructions"]!.GetValue<string>());
    }

    [Fact]
    public async Task 押す場所が決まらない時は理由を返して押さない()
    {
        ScreenText[] texts = [new("退出", 0.39, 0.94), new("進行", 0.9, 0.4), new("進行", 0.9, 0.6)];
        // 押して選ぶ文字が無い。
        var noButton = await new ScreenChoiceJudge((_, _) => Task.FromResult(Answers("""{ "t0": { "noul": 0.1 }, "t1": { "noul": 0.2 } }""")))
            .ChooseAsync("目的", [], texts, CancellationToken.None);
        Assert.Null(noButton.Target);
        Assert.Equal("押して選ぶ文字が画面にありません。", noButton.Reason);
        // Jevが「どれも押さない」を選んだ。
        var none = await Sequence("""{ "t0": { "noul": 0.9 }, "t1": { "noul": 0.9 } }""",
            """{ "press": { "choice": "（どれも押さない）", "probabilities": { "（どれも押さない）": 0.8, "退出": 0.2 } } }""")
            .ChooseAsync("目的", [], texts, CancellationToken.None);
        Assert.Null(none.Target);
        Assert.Equal("Jevが「どれも押さない」を選びました。", none.Reason);
        // 選ばれた文字が画面に2つあり、どちらを押すか決められない。
        var twice = await Sequence("""{ "t0": { "noul": 0.9 }, "t1": { "noul": 0.9 } }""",
            """{ "press": { "choice": "進行", "probabilities": { "進行": 0.8, "退出": 0.2 } } }""")
            .ChooseAsync("目的", [], texts, CancellationToken.None);
        Assert.Null(twice.Target);
        Assert.Equal("選ばれた文字が画面に複数あり、押す場所を決められません。", twice.Reason);
        // 画面に文字が無い時は、Jevへ聞かない。
        var empty = await new ScreenChoiceJudge((_, _) => throw new InvalidOperationException("聞かないはず"))
            .ChooseAsync("目的", [], [], CancellationToken.None);
        Assert.Equal("画面から文字を読めません。", empty.Reason);
    }

    [Fact]
    public void 覚えたカードの目的の行を今の目的にしてカードの全文を背景にする()
    {
        var profile = VisualProgressProfile.Load(Fixture("progress.json"));
        var goal = profile.GoalFrom(new Dictionary<string, string[]> { ["side-quest"] = Card });
        Assert.Equal("スチュアートに調査の結果を報告", goal!.Value.Goal);
        Assert.Equal(Card, goal.Value.Background);
        // 目的の行が無い時と、覚えていない時は、目的なし。
        Assert.Null(profile.GoalFrom(new Dictionary<string, string[]> { ["side-quest"] = ["魔族を召喚する結界第", "お礼"] }));
        Assert.Null(profile.GoalFrom(new Dictionary<string, string[]>()));
    }

    [Fact]
    public void 規則にも通常の画面にも一致しない止まりだけを汎用の判断へ渡す()
    {
        var profile = VisualProgressProfile.Load(Fixture("progress.json"));
        var unknown = new VisualProgressSchedule(profile);
        VisualProgressChoice last = new(VisualProgressAction.Wait);
        for (var time = 0; time <= profile.UnknownTimeoutMs + 1000; time += 500)
            last = unknown.Decide(time, new(VisualProgressAction.Normal), false, hudVisible: false, due: false);
        Assert.Equal(VisualProgressAction.Review, last.Action);
        Assert.True(last.Judgeable);
        // 操作の後に、規則にも通常の画面にも一致しない画面で止まった時も渡す。
        var after = new VisualProgressSchedule(profile);
        after.RecordInput(0, new(VisualProgressAction.Key, "screen-prompt", "screen-prompt:", "Key:Space"));
        for (var time = 500; time <= profile.UnknownTimeoutMs + 1500; time += 500)
            last = after.Decide(time, new(VisualProgressAction.Normal), false, hudVisible: false, due: false);
        Assert.Equal(VisualProgressAction.Review, last.Action);
        Assert.True(last.Judgeable);
        // 待つ規則の画面で止まった時は渡さない（待つと決めた画面）。
        var waiting = new VisualProgressSchedule(profile);
        for (var time = 0; time <= profile.UnknownTimeoutMs + 1000; time += 500)
            last = waiting.Decide(time, new(VisualProgressAction.Wait, "loot-wait", "loot-wait:"), false, hudVisible: false, due: false);
        Assert.Equal(VisualProgressAction.Review, last.Action);
        Assert.False(last.Judgeable);
    }

    [Fact]
    public async Task 会話の選択肢は印とクエストの名前の決め打ちが先でどちらも無ければ判断へ渡す()
    {
        var frame = ReadFrame(Fixture("dialogue-quest-choice-screen.png"));
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var profile = VisualProgressProfile.Load(Fixture("progress.json"));
        var recognizer = new VisualProgressRecognizer(profile);
        var ocr = await recognizer.ReadOcrAsync(frame, viewport);
        void InButton(VisualProgressChoice choice)
        {
            Assert.Equal(VisualProgressAction.Click, choice.Action);
            // 「！魔族を召喚する結界」のボタン（横548〜795・縦1016〜1070）。
            Assert.InRange(choice.Point![0] * frame.Width, 555, 790);
            Assert.InRange(choice.Point[1] * frame.Height, 1020, 1066);
        }
        // 受注したクエストの名前を覚えていない時は、「！」の付いた選択肢を押す。
        var marked = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("dialogue-marked-choice", marked.RuleId);
        InButton(marked);
        // 覚えている時は、クエストの名前と同じ選択肢を押す（読み取りが名前の後ろへ付けた余分な文字は同じ名前とみなす）。
        recognizer.SetRemembered("side-quest", Card);
        var named = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("side-quest-talk", named.RuleId);
        InButton(named);
        // 「！」を読み落とした回でも、名前で拾う。
        var noMark = ocr with { Words = ocr.Words.Where(word => !word.Text.Contains('!') && !word.Text.Contains('！')).ToArray() };
        Assert.Equal("side-quest-talk", recognizer.Recognize(noMark, frame.Width, frame.Height, viewport, frame).RuleId);
        // 印も名前も無い会話の選択肢は、何も押さずに待ち、止まったら判断へ渡す。
        var waiting = new VisualProgressRecognizer(profile).Recognize(noMark, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("dialogue-choice-wait", waiting.RuleId);
        Assert.Equal(VisualProgressAction.Wait, waiting.Action);
        var schedule = new VisualProgressSchedule(profile);
        VisualProgressChoice last = new(VisualProgressAction.Wait);
        for (var time = 0; time <= profile.UnknownTimeoutMs + 1000; time += 500)
            last = schedule.Decide(time, waiting, false, hudVisible: false, due: false);
        Assert.Equal(VisualProgressAction.Review, last.Action);
        Assert.True(last.Judgeable);
    }

    [Fact]
    public async Task 追跡中の表示から覚えたクエストの今の目標を読み直す()
    {
        // 通常の画面。右の一覧の追跡中は「[女神の庭]狩りIV」で、その下に目標が並ぶ。
        var frame = ReadFrame(Fixture("tracker-hud.png"));
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var profile = VisualProgressProfile.Load(Fixture("progress.json"));
        var recognizer = new VisualProgressRecognizer(profile);
        var ocr = await recognizer.ReadOcrAsync(frame, viewport);
        // 覚えたクエストが追跡中と別の時は、読み直さない（受注のやり直しは別の規則が担う）。
        recognizer.SetRemembered("side-quest", Card);
        Assert.Null(recognizer.RefreshGoal(ocr, viewport));
        Assert.Null(recognizer.RefreshGoal(ocr, viewport));
        // 同じクエストで目標が変わっていたら、続けて2回読めた時に、目的の行だけを差し替える。
        recognizer.SetRemembered("side-quest", ["[女神の庭]狩りIV", "説明の行", "・古い目標", "お礼"]);
        Assert.Null(recognizer.RefreshGoal(ocr, viewport));
        var refreshed = recognizer.RefreshGoal(ocr, viewport);
        Assert.NotNull(refreshed);
        Assert.Equal("side-quest", refreshed.Value.Name);
        Assert.Equal("[女神の庭]狩りIV", refreshed.Value.Lines[0]);
        Assert.Equal("説明の行", refreshed.Value.Lines[1]);
        Assert.Equal("お礼", refreshed.Value.Lines[^1]);
        Assert.StartsWith("・", refreshed.Value.Lines[2]);
        Assert.DoesNotContain("古い目標", string.Concat(refreshed.Value.Lines));
        Assert.Contains("偵察ポイント", refreshed.Value.Lines[2]);
        Assert.DoesNotContain("180", refreshed.Value.Lines[2]);
        // 別のクエストの見出し（黒穴出現・ルーン昇級）は、目標に入れない。
        Assert.DoesNotContain(refreshed.Value.Lines, line => line.Contains("ルーン") || line.Contains("黒穴"));
        // 読み直した後は、同じ目標なので書き換えない。
        recognizer.SetRemembered("side-quest", refreshed.Value.Lines);
        Assert.Null(recognizer.RefreshGoal(ocr, viewport));
        Assert.Null(recognizer.RefreshGoal(ocr, viewport));
        Assert.Contains("偵察ポイント", profile.GoalFrom(new Dictionary<string, string[]> { ["side-quest"] = refreshed.Value.Lines })!.Value.Goal);
    }

    [Theory]
    [InlineData("""{ "ApiKeyFile": "", "Model": "jev-latest" }""")]
    [InlineData("""{ "ApiKeyFile": "missing-key.env", "Model": "" }""")]
    public void 接続設定の不足は読込みで拒否する(string settings)
    {
        var path = Path.Combine(Path.GetTempPath(), "screen-judge-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, settings);
            Assert.Throws<InvalidDataException>(() => ScreenJudgeSettings.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 鍵の行が無い鍵のファイルは読込みで拒否する()
    {
        var key = Path.Combine(Path.GetTempPath(), "screen-judge-key-" + Guid.NewGuid().ToString("N") + ".env");
        var path = key + ".json";
        try
        {
            File.WriteAllText(key, "OTHER=1\n");
            File.WriteAllText(path, JsonSerializer.Serialize(new { ApiKeyFile = key }));
            Assert.Throws<InvalidDataException>(() => ScreenJudgeSettings.Load(path));
            File.WriteAllText(key, "TYPESAFE_API_KEY=\"test-key\"\n");
            Assert.NotNull(ScreenJudgeSettings.Load(path));
        }
        finally { File.Delete(key); File.Delete(path); }
    }

    private static ScreenChoiceJudge Sequence(string first, string second)
    {
        var calls = 0;
        return new((_, _) => Task.FromResult(Answers(++calls == 1 ? first : second)));
    }

    private static JsonElement Answers(string answers) => JsonDocument.Parse($$"""{ "answers": {{answers}} }""").RootElement.Clone();

    private static CapturedFrame ReadFrame(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return new(ContractSchemaVersions.Revision03, "screen-judge-test", CaptureBackend.WindowsGraphicsCapture,
            1, 0, DateTimeOffset.UnixEpoch, bitmap.PixelWidth, bitmap.PixelHeight, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(bytes, bitmap.PixelWidth * 4));
    }
}
