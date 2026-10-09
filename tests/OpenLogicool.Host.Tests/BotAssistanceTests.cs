using System.IO;
using System.Text.Json;
using OpenLogicool.Host;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class BotAssistanceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "openlogicool-assistance-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");
    private static readonly string OldThread = Guid.NewGuid().ToString();
    private static readonly string NewThread = Guid.NewGuid().ToString();
    private static JsonElement Review(string detail) => JsonSerializer.SerializeToElement(new { Detail = detail });

    [Fact]
    public async Task 同じ未処理案件は一度だけ配達し再起動後も根拠を保持する()
    {
        var dispatcher = new Dispatcher();
        var store = new BotAssistanceStore(Database);
        var coordinator = new BotAssistanceCoordinator(store, dispatcher);
        await coordinator.AttachAsync(OldThread, root, default);
        var id = await coordinator.ReportAsync("実行A", Review("必要品が不足"), default);
        Assert.Equal(id, await new BotAssistanceCoordinator(new(Database), dispatcher)
            .ReportAsync("実行B", Review("最新の根拠"), default));
        Assert.Single(dispatcher.Sent);
        var incident = Assert.Single(new BotAssistanceStore(Database).Read().Incidents);
        Assert.Equal("queued", incident.DeliveryState);
        Assert.Equal("実行B", incident.EvidenceDirectory);
        Assert.Equal("最新の根拠", incident.Detail);
        Assert.Equal("open", incident.Status);
    }

    [Fact]
    public async Task 引き継ぎで未処理案件と担当を移し旧会話の操作を拒否する()
    {
        var dispatcher = new Dispatcher();
        var store = new BotAssistanceStore(Database);
        var coordinator = new BotAssistanceCoordinator(store, dispatcher);
        await coordinator.AttachAsync(OldThread, root, default);
        var id = await coordinator.ReportAsync("原本", Review("詰まり"), default);
        store.Claim(id, OldThread);
        var transferred = await coordinator.AttachAsync(NewThread, root, default);
        Assert.Equal(2, transferred.Binding!.Generation);
        Assert.Equal("open", Assert.Single(transferred.Incidents).Status);
        Assert.Null(transferred.Incidents[0].OwnerThreadId);
        Assert.Throws<InvalidOperationException>(() => store.Bind(OldThread, root));
        Assert.Throws<InvalidOperationException>(() => store.Claim(id, OldThread));
        Assert.Throws<InvalidOperationException>(() => store.Resolve(id, OldThread, "旧担当"));
        store.Claim(id, NewThread);
        store.ObservedClear(id);
        Assert.Equal("claimed", store.Read().Incidents[0].Status);
        store.Resolve(id, NewThread, "実測確認済み");
        var next = await coordinator.ReportAsync("次の実行", Review("別の詰まり"), default);
        Assert.NotEqual(id, next);
        Assert.Equal(new[] { OldThread, NewThread }, dispatcher.Sent.Select(x => x.Thread));
        Assert.Equal("原本", store.Read().Incidents[0].EvidenceDirectory);
    }

    [Theory]
    [InlineData(false, "failed")]
    [InlineData(true, "unknown")]
    public async Task 配達失敗と結果不明を保存し通知を自動再送しない(bool unknown, string state)
    {
        var dispatcher = new Dispatcher();
        var store = new BotAssistanceStore(Database);
        var coordinator = new BotAssistanceCoordinator(store, dispatcher);
        await coordinator.AttachAsync(OldThread, root, default);
        dispatcher.Failure = new("通信エラー", "配達できず", unknown);
        await Assert.ThrowsAsync<BotAssistanceDeliveryException>(() => coordinator.ReportAsync("根拠", Review("詰まり"), default));
        var incident = Assert.Single(store.Read().Incidents);
        Assert.Equal(state, incident.DeliveryState);
        Assert.Contains("通信エラー", incident.Error);
        Assert.Equal(incident.Id, await coordinator.ReportAsync("新しい観測", Review("継続"), default));
        Assert.Single(dispatcher.Sent);
    }

    [Fact]
    public async Task 新会話の検証失敗は既存担当と案件を変更しない()
    {
        var dispatcher = new Dispatcher();
        var store = new BotAssistanceStore(Database);
        var coordinator = new BotAssistanceCoordinator(store, dispatcher);
        await coordinator.AttachAsync(OldThread, root, default);
        dispatcher.Failure = new("parent-unverified", "会話を検証できず");
        await Assert.ThrowsAsync<BotAssistanceDeliveryException>(() => coordinator.AttachAsync(NewThread, root, default));
        Assert.Equal(OldThread, store.Read().Binding!.ThreadId);
    }

    [Fact]
    public void 未登録と未対応schemaを既定値へ置き換えない()
    {
        var store = new BotAssistanceStore(Database);
        Assert.Throws<InvalidOperationException>(() => store.Report("根拠", "詰まり"));
        File.WriteAllText(Path.Combine(store.DirectoryPath, "state.json"), """{"schemaVersion":999}""");
        Assert.Throws<InvalidDataException>(() => store.Read());
    }

    [Fact]
    public async Task AI支援登録時は人向け通知を使わず画面回復だけでは案件を閉じない()
    {
        var store = new BotAssistanceStore(Database);
        store.Bind(OldThread, root);
        var plan = store.Report("根拠", "詰まり");
        var notifier = VisualAssistReviewNotifier.Create("存在しない人向け設定", Database)!;
        Assert.Equal(Database, notifier.AssistanceDatabasePath);
        await notifier.ResolveAsync(new(VisualAssistNoticeKind.Assistant, plan.Incident.Id), default);
        var incident = Assert.Single(store.Read().Incidents);
        Assert.True(incident.ObservedCleared);
        Assert.Equal("open", incident.Status);
    }

    [Theory]
    [InlineData("壊れたJSON", 0)]
    [InlineData("null", 0)]
    [InlineData("{\"ok\":\"true\"}", 0)]
    [InlineData("{\"ok\":true}", 1)]
    public void 配達結果が不正なら送信結果不明として止める(string response, int exitCode)
    {
        var error = Assert.Throws<BotAssistanceDeliveryException>(() => SteerBotAssistanceDispatcher.ReadResponse(response, exitCode, true));
        Assert.True(error.OutcomeUnknown);
    }

    [Fact]
    public async Task Bot異常と通知失敗を両方表示し実行を回収する()
    {
        var gate = new DemonstrationRecordingGate();
        using var intents = new HostBotScriptIntents([new("test", "試験", "説明")], root, gate,
            (_, _, _, _) => throw new IOException("Nano応答なし"),
            (_, _) => throw new IOException("状態保存不可"));
        intents.Start("test");
        await intents.StopAsync();
        Assert.Contains("Nano応答なし", intents.Current().Detail);
        Assert.Contains("状態保存不可", intents.Current().Detail);
        Assert.Equal(DemonstrationGateState.Free, gate.State);
    }

    [Fact]
    public async Task 利用者停止はAIを起こさない()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notices = 0;
        using var intents = new HostBotScriptIntents([new("test", "試験", "説明")], root, new(),
            async (_, _, _, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return new(false, "終了"); },
            (_, _) => { notices++; return Task.CompletedTask; });
        intents.Start("test");
        await started.Task;
        await intents.StopAsync();
        Assert.Equal(0, notices);
    }

    private sealed class Dispatcher : IBotAssistanceDispatcher
    {
        public BotAssistanceDeliveryException? Failure { get; set; }
        public List<(string Thread, string Id, string Message)> Sent { get; } = [];
        public Task VerifyAsync(BotAssistantBinding binding, CancellationToken token)
        { if (Failure is not null) throw Failure; return Task.CompletedTask; }
        public Task<string?> SubmitAsync(BotAssistantBinding binding, string deliveryId, string message, CancellationToken token)
        {
            Sent.Add((binding.ThreadId, deliveryId, message));
            if (Failure is not null) throw Failure;
            return Task.FromResult<string?>("受付ID");
        }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
