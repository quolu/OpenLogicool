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
        var recovery = VisualRecoveryProfile.Load(package.File("profile.json"));
        _ = VisualProgressProfile.Load(package.File("progress.json"));
        Assert.Equal(0.7, recovery.PotionThreshold);
        Assert.Equal(0.2, recovery.BandageThreshold);
        Assert.Equal(3600000, package.DurationMs);
        foreach (var file in new[] { "stop.png", "hud.png", "food-ready.png", "food-active.png", "dialogue-tail.png", "dialogue-cue.jpg" })
            Assert.True(File.Exists(package.File(file)), file);
    }

    private static HostBotScriptIntents Create(DemonstrationRecordingGate gate,
        Func<string, string, Action<JsonElement>, CancellationToken, Task<BotScriptResult>> execute) =>
        new([new("test", "テスト", "説明")], Path.Combine(Path.GetTempPath(), "openlogicool-bot-tests", Guid.NewGuid().ToString("N")), gate, execute);
}
