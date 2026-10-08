using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class VisualProgressReviewMonitorTests
{
    [Fact]
    public void 同じ未知画面や同じ未確認操作では監視を続け再送を許可しない()
    {
        var monitor = new VisualProgressReviewMonitor();
        var blocked = new VisualProgressChoice(VisualProgressAction.Key, "会話", "同じ台詞", "Key:Space");
        monitor.Hold(blocked);
        for (var i = 0; i < 100; i++)
        {
            Assert.False(monitor.TryResume(i * 250, blocked, false, false));
            Assert.False(monitor.TryResume(i * 250, new(VisualProgressAction.Normal), false, false));
        }
        Assert.True(monitor.IsHolding);
        Assert.False(monitor.TryResume(100000, new(VisualProgressAction.Review, Detail: "選択してください"), false, false));
        monitor.Hold(new(VisualProgressAction.Normal), hudVisible: true);
        Assert.False(monitor.TryResume(110000, new(VisualProgressAction.Normal), false, true));
        Assert.False(monitor.TryResume(120000, new(VisualProgressAction.Normal), false, true));
    }

    [Fact]
    public void 既知の別画面が安定すると再開し一瞬の認識では再開しない()
    {
        var monitor = new VisualProgressReviewMonitor();
        monitor.Hold(new(VisualProgressAction.Normal));
        var known = new VisualProgressChoice(VisualProgressAction.Click, "承諾", "承諾|やめる");
        Assert.False(monitor.TryResume(10000, known, false, false));
        Assert.False(monitor.TryResume(10200, new(VisualProgressAction.Normal), false, false));
        Assert.False(monitor.TryResume(11000, known, false, false));
        Assert.False(monitor.TryResume(11599, known, false, false));
        Assert.True(monitor.TryResume(11600, known, false, false));
        Assert.False(monitor.IsHolding);
    }

    [Fact]
    public void HUDや停止表示への復帰でも進行状態を回復できる()
    {
        var monitor = new VisualProgressReviewMonitor();
        monitor.Hold(new(VisualProgressAction.Normal));
        Assert.False(monitor.TryResume(0, new(VisualProgressAction.Normal), false, true));
        Assert.True(monitor.TryResume(600, new(VisualProgressAction.Normal), false, true));
        monitor.Hold(new(VisualProgressAction.Normal));
        Assert.False(monitor.TryResume(1000, new(VisualProgressAction.Normal), true, false));
        Assert.True(monitor.TryResume(1600, new(VisualProgressAction.Normal), true, false));
    }

    [Fact]
    public async Task 確認待ちの間も回復が動作し利用者の停止で両方を回収する()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progressEnded = false;
        var recoveryEnded = false;
        var count = 0;
        var running = VisualKeyAssistWorkers.RunAsync(async token =>
        {
            await entered.Task.WaitAsync(token);
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                while (await timer.WaitForNextTickAsync(token))
                    if (++count == 4) observed.SetResult();
            }
            finally { recoveryEnded = true; }
            return 0;
        }, async token =>
        {
            var monitor = new VisualProgressReviewMonitor();
            monitor.Hold(new(VisualProgressAction.Review));
            entered.SetResult();
            try
            {
                while (true)
                {
                    Assert.False(monitor.TryResume(count * 250, new(VisualProgressAction.Normal), false, false));
                    await Task.Delay(250, token);
                }
            }
            finally { progressEnded = true; }
        }, stop.Token);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.False(running.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(recoveryEnded && progressEnded);
    }
}
