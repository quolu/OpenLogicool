using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Host;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostBotScriptIntentsTests
{
    [Fact]
    public async Task 停止は監視と入力の回収を待ち二重起動と録画を防ぐ()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new DemonstrationRecordingGate();
        using var intents = Create(gate, async (_, _, report, token) =>
        {
            report(JsonSerializer.SerializeToElement(new { Event = "recovery-sample", IntervalMs = (long?)250 }));
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled.SetResult(); await cleanup.Task; }
            return new(false, "終了");
        });
        intents.Start("test");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(BotScriptPhase.Running, intents.Current().Phase);
        Assert.Throws<InvalidOperationException>(() => intents.Start("test"));
        Assert.False(gate.TryBeginRecording(out _));
        var stopping = intents.StopAsync();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stopping.IsCompleted);
        Assert.Equal(BotScriptPhase.Stopping, intents.Current().Phase);
        cleanup.SetResult();
        await stopping;
        Assert.Equal(BotScriptPhase.Stopped, intents.Current().Phase);
        Assert.Equal(DemonstrationGateState.Free, gate.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 完了と判断待ちを区別し再起動できる(bool review)
    {
        using var intents = Create(new(), (_, _, _, _) => Task.FromResult(new BotScriptResult(review, "停止理由")));
        intents.Start("test");
        await intents.StopAsync();
        Assert.Equal(review ? BotScriptPhase.AwaitingReview : BotScriptPhase.Stopped, intents.Current().Phase);
        Assert.Equal("停止理由", intents.Current().Detail);
        intents.Start("test");
        await intents.StopAsync();
    }

    [Fact]
    public async Task 接続エラーを表示し記録中の開始を拒否する()
    {
        var gate = new DemonstrationRecordingGate();
        using var intents = Create(gate, (_, _, _, _) => throw new IOException("Nanoを接続してください。"));
        Assert.True(gate.TryBeginRecording(out _));
        Assert.Throws<InvalidOperationException>(() => intents.Start("test"));
        gate.EndRecording();
        intents.Start("test");
        await intents.StopAsync();
        Assert.Equal(BotScriptPhase.Faulted, intents.Current().Phase);
        Assert.Equal("Nanoを接続してください。", intents.Current().Detail);
        Assert.Equal(DemonstrationGateState.Free, gate.State);
    }

    [Fact]
    public void 配布物には既存の判定設定と参照画像が揃っている()
    {
        var package = BotScriptPackage.Load(Path.Combine(AppContext.BaseDirectory, "BotScripts", "MabinogiMobile", "bot.json"));
        Assert.False(package.TimedInputEnabled);
        var recovery = VisualRecoveryProfile.Load(package.File("profile.json"));
        // 進行設定は機能の組み合わせで、機能ごとのファイルも一緒に配る。
        Assert.Equal(51, VisualProgressProfile.Load(package.File("progress.json")).Rules.Length);
        Assert.Equal(10, VisualProgressProfile.ListFunctions(package.File("progress.json")).Count);
        Assert.Equal(0.7, recovery.PotionThreshold);
        Assert.Equal(0.2, recovery.BandageThreshold);
        Assert.Equal("MabinogiMobile", package.ProcessName);
        using var settings = JsonDocument.Parse(File.ReadAllText(package.File("bot.json")));
        Assert.False(settings.RootElement.TryGetProperty("DurationMs", out _));
        foreach (var file in new[] { "stop.png", "hud.png", "food-ready.png", "food-active.png", "dialogue-tail.png", "dialogue-cue.jpg", "compass-space.png", "quest-marker.png" })
            Assert.True(File.Exists(package.File(file)), file);
    }

    [Fact]
    public async Task 詰まりの一分待機は実行を終了せず回復観測も続ける()
    {
        Action<JsonElement>? report = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var intents = Create(new(), async (_, _, emit, token) =>
        {
            report = emit;
            emit(JsonSerializer.SerializeToElement(new { Event = "progress-review-grace", Detail = "移動待ち" }));
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new(false, "終了");
        });
        intents.Start("test");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(BotScriptPhase.Running, intents.Current().Phase);
        Assert.Contains("1分間は通知せず", intents.Current().Detail);
        report!(JsonSerializer.SerializeToElement(new { Event = "recovery-sample", IntervalMs = 250 }));
        Assert.Equal(1, intents.Current().ObservationCount);
        Assert.Equal(BotScriptPhase.Running, intents.Current().Phase);
        report(JsonSerializer.SerializeToElement(new { Event = "progress-resumed", Detail = "復帰" }));
        Assert.Equal(BotScriptPhase.Running, intents.Current().Phase);
        await intents.StopAsync();
        Assert.Equal(BotScriptPhase.Stopped, intents.Current().Phase);
    }

    [Fact]
    public async Task 確認待ちは実行中として扱い回復観測で理由を消さず再開できる()
    {
        Action<JsonElement>? report = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var intents = Create(new(), async (_, _, emit, token) =>
        {
            report = emit;
            emit(JsonSerializer.SerializeToElement(new { Event = "progress-review-monitoring", Detail = "選択を待っています。" }));
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new(false, "終了");
        });
        intents.Start("test");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(BotScriptPhase.ReviewMonitoring, intents.Current().Phase);
        Assert.Throws<InvalidOperationException>(() => intents.Start("test"));
        report!(JsonSerializer.SerializeToElement(new { Event = "recovery-sample", IntervalMs = 250 }));
        report(JsonSerializer.SerializeToElement(new { Event = "recovery", Observation = new { HealthFraction = 0.6 }, Detail = "回復監視中" }));
        Assert.Equal(1, intents.Current().ObservationCount);
        Assert.Contains("選択を待っています", intents.Current().Detail);
        report(JsonSerializer.SerializeToElement(new { Event = "progress-resumed", Detail = "再開しました。" }));
        Assert.Equal(BotScriptPhase.Running, intents.Current().Phase);
        await intents.StopAsync();
        Assert.Equal(BotScriptPhase.Stopped, intents.Current().Phase);
    }

    private static HostBotScriptIntents Create(DemonstrationRecordingGate gate,
        Func<string, string, Action<JsonElement>, CancellationToken, Task<BotScriptResult>> execute) =>
        new([new("test", "テスト", "説明")], Path.Combine(Path.GetTempPath(), "openlogicool-bot-tests", Guid.NewGuid().ToString("N")), gate, execute);

    [Fact]
    public async Task 手入力中も観測を表示し確認事項を保持したまま再開する()
    {
        Action<JsonElement>? report = null;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var intents = Create(new(), async (_, _, emit, token) =>
        {
            report = emit; started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new(false, "終了");
        });
        intents.Start("test"); await started.Task;
        report!(JsonSerializer.SerializeToElement(new { Event = "progress-review-monitoring", Detail = "確認事項", AutomaticRulesContinue = true }));
        report(JsonSerializer.SerializeToElement(new { Event = "user-input-paused", Detail = "手入力中" }));
        Assert.Equal(BotScriptPhase.UserPaused, intents.Current().Phase);
        report(JsonSerializer.SerializeToElement(new { Event = "recovery-sample", IntervalMs = 250 }));
        Assert.Equal(BotScriptPhase.UserPaused, intents.Current().Phase);
        Assert.Equal(1, intents.Current().ObservationCount);
        report(JsonSerializer.SerializeToElement(new { Event = "user-input-resumed", Detail = "再開" }));
        Assert.Equal(BotScriptPhase.ReviewMonitoring, intents.Current().Phase);
        Assert.Contains("確認事項", intents.Current().Detail);
        await intents.StopAsync();
    }

}
