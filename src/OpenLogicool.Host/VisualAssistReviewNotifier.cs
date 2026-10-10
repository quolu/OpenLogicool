using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenLogicool.Host;

internal enum VisualAssistNoticeKind { Human, Assistant }
internal sealed record VisualAssistNotice(VisualAssistNoticeKind Kind, string Id);

/// <summary>詰まりを担当AIへ配達し、AI未登録の単独運用では人向けの通知設定を使う。</summary>
internal sealed record VisualAssistReviewNotifier(string Executable, string[] Arguments)
{
    internal string? AssistanceDatabasePath { get; init; }
    /// <summary>担当AIが通知を引き受けるまで待つ時間。過ぎても未受領なら利用者へ知らせる。</summary>
    [JsonIgnore] internal TimeSpan ClaimGrace { get; init; } = TimeSpan.FromMinutes(5);
    [JsonIgnore] internal Func<string, BotAssistanceCoordinator> Coordinators { get; init; } = BotAssistanceCoordinator.Create;
    /// <summary>直近の通知の見届け。結果は支援状態へ残るため、呼び出し側は完了を待たない。</summary>
    [JsonIgnore] internal Task ClaimWatch { get; private set; } = Task.CompletedTask;

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
    {
        if (AssistanceDatabasePath is not { } database)
            return new(VisualAssistNoticeKind.Human, await ExchangeAsync(evidenceDirectory, result, token));
        var store = new BotAssistanceStore(database);
        try
        {
            var report = await Coordinators(database).PublishAsync(evidenceDirectory, result, token);
            // 会話が通知を読んだかを通信で確かめられない宛先があるため、担当AIの引き受けを受領の印として見届ける。
            if (report.Published) ClaimWatch = WatchClaimAsync(store, report.Incident.Id, token);
            return new(VisualAssistNoticeKind.Assistant, report.Incident.Id);
        }
        catch (BotAssistanceDeliveryException error) when (error.IncidentId is { } id)
        {
            // 担当AIへ届かない詰まりを黙って残さない。利用者へ知らせてから、元の失敗を返す。
            try { await EscalateAsync(store, id, $"送信に失敗しました（{error.Code}: {error.Message}）", token); }
            catch (Exception escalation) when (escalation is not OperationCanceledException)
            {
                store.EscalationFailed(id, escalation.Message);
                throw new IOException($"担当AIへの通知に失敗し、利用者への知らせにも失敗しました。{error.Code}: {error.Message} ／ {escalation.Message}", error);
            }
            throw;
        }
    }

    private async Task WatchClaimAsync(BotAssistanceStore store, string incidentId, CancellationToken token)
    {
        try
        {
            await Task.Delay(ClaimGrace, token);
            if (!NeedsClaimEscalation(store.Incident(incidentId))) return;
            await EscalateAsync(store, incidentId, $"{ClaimGrace.TotalMinutes:0.#}分たっても担当AIが引き受けていません", token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { store.EscalationFailed(incidentId, error.Message); }
    }

    internal static bool NeedsClaimEscalation(BotAssistanceIncident incident) =>
        incident is { Status: "open", ObservedCleared: false, EscalationDecisionId: null };

    internal static (string Title, string Context, VisualProgressOption[] Options) EscalationRequest(BotAssistanceIncident incident, string reason) =>
        ("担当AIへBotの詰まり通知が届いていません: " + incident.Detail[..Math.Min(60, incident.Detail.Length)],
            $"Botが詰まりを検出して担当AIへ通知しましたが、{reason}。\n詰まりの内容: {incident.Detail}\n"
            + "Botの画面観測と回復監視は続いています。\n担当AIの会話を開いて、この詰まりへの対応を頼んでください。"
            + "通知の宛先が切れている時は、担当AIが assistant attach をやり直すと直ります。\n"
            + $"案件ID: {incident.Id}\n根拠: {incident.EvidenceDirectory}",
            [new("asked", "担当AIの会話で対応を頼んだ"), new("later", "今は対応しない")]);

    private async Task EscalateAsync(BotAssistanceStore store, string incidentId, string reason, CancellationToken token)
    {
        if (Executable.Length == 0)
            throw new InvalidOperationException("決裁箱の接続設定がないため、担当AIへ届いていないことを利用者へ知らせられません。");
        var incident = store.Incident(incidentId);
        store.Escalated(incidentId, await ExchangeAsync(incident.EvidenceDirectory, default, token, custom: EscalationRequest(incident, reason)));
    }

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
            var store = new BotAssistanceStore(AssistanceDatabasePath ?? throw new InvalidOperationException("AI支援の接続がありません。"));
            store.ObservedClear(notice.Id);
            // 画面が回復したら、利用者への「届いていません」の知らせも未回答のうちに取り下げる。
            if (store.Incident(notice.Id).EscalationDecisionId is { } escalation && Executable.Length > 0)
                _ = await ExchangeAsync("", default, token, escalation);
            return;
        }
        _ = await ExchangeAsync("", default, token, notice.Id);
    }

    private async Task<string> ExchangeAsync(string evidenceDirectory, JsonElement result, CancellationToken token,
        string? resolvedDecisionId = null, bool readAnswer = false,
        (string Title, string Context, VisualProgressOption[] Options)? custom = null)
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
            var (title, context, options) = custom ?? ReviewRequest();
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

            (string Title, string Context, VisualProgressOption[] Options) ReviewRequest()
            {
                var detail = result.TryGetProperty("Detail", out var text) ? text.GetString() : "画面の確認が必要です。";
                var choices = ReviewOptions(result);
                var choiceText = choices.Length > 2 ? "\n画面の選択肢（OCR・添付画像も確認）:\n"
                    + string.Join("\n", choices.Where(option => option.Id != "stop").Select(option => option.Label)) : "";
                var monitoring = result.TryGetProperty("MonitoringContinues", out var continues) && continues.GetBoolean();
                var continuing = result.TryGetProperty("AutomaticRulesContinue", out var rulesContinue) && rulesContinue.GetBoolean();
                var heading = (continuing ? "ゲーム画面に確認事項があります（動作継続）: "
                    : monitoring ? "ゲーム画面の確認が必要です（監視継続）: " : "ゲームの自動進行を停止しました: ") + detail?[..Math.Min(80, detail.Length)];
                var body = detail + choiceText + (continuing
                    ? "\n通常の操作規則・画面観測・回復監視は継続しています。確認通知を理由に操作全体を保留しません。回答が必要な選択は自動選択しません。"
                    : monitoring
                    ? "\n進行入力を止めて画面観測と回復監視を継続しています。確認済みの別画面に戻ったら自動で進行を再開します。"
                    : "\n入力は停止済みです。判断結果だけでは入力を再開しません。操作者が回答を確認して再開します。")
                    + "\n確認記録: " + Path.Combine(evidenceDirectory, "review.json");
                return (heading, body, choices);
            }

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
