using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OpenLogicool.Host;

internal enum VisualAssistNoticeKind { Human, Assistant }
internal sealed record VisualAssistNotice(VisualAssistNoticeKind Kind, string Id);

/// <summary>詰まりを担当AIへ配達し、AI未登録の単独運用では人向けの通知設定を使う。</summary>
internal sealed record VisualAssistReviewNotifier(string Executable, string[] Arguments)
{
    internal string? AssistanceDatabasePath { get; init; }

    public static VisualAssistReviewNotifier? Create(string? humanSettings, string? assistanceDatabasePath)
    {
        if (assistanceDatabasePath is not null && new BotAssistanceStore(assistanceDatabasePath).Exists)
        {
            if (new BotAssistanceStore(assistanceDatabasePath).Read().Binding is null)
                throw new InvalidDataException("AI支援の担当が未登録です。");
            // 利用者だけが選ぶ表示は担当AIを経由しないため、決裁箱の接続設定がある時は併せて持つ。
            return (humanSettings is not null && File.Exists(humanSettings) ? Load(humanSettings) : new("", []))
                with { AssistanceDatabasePath = assistanceDatabasePath };
        }
        return humanSettings is null ? null : Load(humanSettings);
    }

    /// <summary>利用者だけが選ぶ表示を、担当AIを経由せず決裁箱へ申請する。</summary>
    public async Task<VisualAssistNotice> AskUserAsync(string evidenceDirectory, JsonElement result, CancellationToken token)
        => Executable.Length == 0
            ? throw new InvalidOperationException("決裁箱の接続設定がないため、利用者へ直接申請できません。")
            : new(VisualAssistNoticeKind.Human, await ExchangeAsync(evidenceDirectory, result, token));
    public static VisualAssistReviewNotifier Load(string path)
    {
        var value = JsonSerializer.Deserialize<VisualAssistReviewNotifier>(File.ReadAllText(path))
            ?? throw new InvalidDataException("確認通知の接続設定が空です。");
        if (string.IsNullOrWhiteSpace(value.Executable) || value.Arguments is null)
            throw new InvalidDataException("確認通知にはMCPの実行ファイルと引数が必要です。");
        return value;
    }

    public async Task<VisualAssistNotice> NotifyAsync(string evidenceDirectory, JsonElement result, CancellationToken token)
        => AssistanceDatabasePath is { } database
            ? new(VisualAssistNoticeKind.Assistant, await BotAssistanceCoordinator.Create(database).ReportAsync(evidenceDirectory, result, token))
            : new(VisualAssistNoticeKind.Human, await ExchangeAsync(evidenceDirectory, result, token));

    /// <summary>利用者へ直接申請した選択の回答を読む。未回答・取り下げ済みはnull。</summary>
    public async Task<string?> ReadAnswerAsync(VisualAssistNotice notice, CancellationToken token)
    {
        if (notice.Kind != VisualAssistNoticeKind.Human) throw new InvalidOperationException("決裁箱へ直接出した申請だけ回答を読めます。");
        var answer = await ExchangeAsync("", default, token, notice.Id, readAnswer: true);
        return answer.Length == 0 ? null : answer;
    }

    internal static string? AnswerOption(JsonElement decision) =>
        decision.GetProperty("status").GetString() == "answered"
            ? decision.GetProperty("answer").GetProperty("option_id").GetString() : null;

    public async Task ResolveAsync(VisualAssistNotice notice, CancellationToken token)
    {
        if (notice.Kind == VisualAssistNoticeKind.Assistant)
        {
            new BotAssistanceStore(AssistanceDatabasePath ?? throw new InvalidOperationException("AI支援の接続がありません。")).ObservedClear(notice.Id);
            return;
        }
        _ = await ExchangeAsync("", default, token, notice.Id);
    }

    private async Task<string> ExchangeAsync(string evidenceDirectory, JsonElement result, CancellationToken token,
        string? resolvedDecisionId = null, bool readAnswer = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        token = deadline.Token;
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in Arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("確認通知のMCPを起動できません。");
        var stderr = process.StandardError.ReadToEndAsync(token);
        var id = 0;
        try
        {
            await Call("initialize", new { protocolVersion = "2024-11-05", capabilities = new { },
                clientInfo = new { name = "openlogicool-review", version = "1.0" } });
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "notifications/initialized" }));
            if (resolvedDecisionId is not null)
            {
                var decision = await Tool("get_decision", new { decision_id = resolvedDecisionId });
                if (readAnswer) return AnswerOption(decision) ?? "";
                if (decision.GetProperty("status").GetString() is "pending" or "deferred")
                    await Tool("cancel_decision", new { decision_id = resolvedDecisionId,
                        reason = "確認済みの画面に戻り、この申請が対象としていた表示が解消しました。" });
                return resolvedDecisionId;
            }
            var decisions = await Tool("list_my_decisions", new { });
            var images = new[] { "progress-review.png", "recovery-review.png", "review-after.png" }
                .Select(name => Path.Combine(evidenceDirectory, name)).Where(File.Exists).ToArray();
            var detail = result.TryGetProperty("Detail", out var text) ? text.GetString() : "画面の確認が必要です。";
            var options = ReviewOptions(result);
            var choiceText = options.Length > 2 ? "\n画面の選択肢（OCR・添付画像も確認）:\n"
                + string.Join("\n", options.Where(option => option.Id != "stop").Select(option => option.Label)) : "";
            var monitoring = result.TryGetProperty("MonitoringContinues", out var continues) && continues.GetBoolean();
            var continuing = result.TryGetProperty("AutomaticRulesContinue", out var rulesContinue) && rulesContinue.GetBoolean();
            var title = (continuing ? "ゲーム画面に確認事項があります（動作継続）: "
                : monitoring ? "ゲーム画面の確認が必要です（監視継続）: " : "ゲームの自動進行を停止しました: ") + detail?[..Math.Min(80, detail.Length)];
            var context = detail + choiceText + (continuing
                ? "\n通常の操作規則・画面観測・回復監視は継続しています。確認通知を理由に操作全体を保留しません。回答が必要な選択は自動選択しません。"
                : monitoring
                ? "\n進行入力を止めて画面観測と回復監視を継続しています。確認済みの別画面に戻ったら自動で進行を再開します。"
                : "\n入力は停止済みです。判断結果だけでは入力を再開しません。操作者が回答を確認して再開します。")
                + "\n確認記録: " + Path.Combine(evidenceDirectory, "review.json");
            object Request(string? checkToken) => new
            {
                title, context,
                options = options.Select(option => new { id = option.Id, label = option.Label }).ToArray(),
                image_paths = images, session_label = "OpenLogicool / スクリプト主体のクエスト進行",
                check_token = checkToken,
            };
            var existing = await RefreshExisting(decisions.GetProperty("items"));
            if (existing is not null) return existing;
            var first = await Tool("request_decision", Request(null));
            var check = first.TryGetProperty("check_token", out var checkToken) ? checkToken.GetString() : null;
            if (check is not null)
            {
                existing = await RefreshExisting(first.GetProperty("decisions"));
                if (existing is not null) return existing;
            }
            var final = check is null ? first : await Tool("request_decision", Request(check));
            return final.GetProperty("decision_id").GetString()
                ?? throw new InvalidDataException("確認通知が受理されませんでした: " + final.GetRawText());

            async Task<string?> RefreshExisting(JsonElement items)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.GetProperty("title").GetString() != title
                        || item.GetProperty("status").GetString() is not ("pending" or "deferred")) continue;
                    var decisionId = item.GetProperty("decision_id").GetString()!;
                    await Tool("amend_decision", new { decision_id = decisionId, version = item.GetProperty("version").GetInt32(),
                        changes = new { context,
                            options = options.Select(option => new { id = option.Id, label = option.Label }).ToArray(), image_paths = images },
                        note = "同じ停止理由の未決申請へ、新しい画面と停止記録を反映しました。" });
                    return decisionId;
                }
                return null;
            }
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            try { await stderr; } catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }

        async Task<JsonElement> Tool(string name, object arguments)
        {
            return ReadToolResult(await Call("tools/call", new { name, arguments }));
        }
        async Task<JsonElement> Call(string method, object parameters)
        {
            var requestId = ++id;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = requestId, method, @params = parameters },
                new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(token)
                    ?? throw new EndOfStreamException("確認通知のMCPが応答前に終了しました。");
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.Number
                    || responseId.GetInt32() != requestId) continue;
                if (root.TryGetProperty("error", out var error)) throw new IOException("確認通知のMCPエラー: " + error);
                return root.GetProperty("result").Clone();
            }
        }
    }

    internal static JsonElement ReadToolResult(JsonElement response)
    {
        var payload = response.GetProperty("structuredContent");
        if (response.TryGetProperty("isError", out var failed) && failed.ValueKind == JsonValueKind.True
            && (!payload.TryGetProperty("error", out var error) || error.GetString() != "confirm_required"))
            throw new IOException("確認通知のMCPがエラーを返しました: " + response.GetRawText());
        return payload;
    }

    internal static VisualProgressOption[] ReviewOptions(JsonElement result)
    {
        var choices = result.TryGetProperty("ReviewOptions", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.Deserialize<VisualProgressOption[]>()! : [];
        return choices.Length > 0 ? [..choices, new("stop", "入力を停止したままにする")]
            : [new("inspect", "停止画面を調べて、確認済みの操作なら規則へ追加して続ける"),
               new("stop", "入力を停止したままにする")];
    }
}
