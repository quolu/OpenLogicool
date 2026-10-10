using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class VisualProgressReviewMonitorTests
{
    [Fact]
    public void 停止表示の静止は同じ停止表示を復帰と扱わず一分後に通知する()
    {
        var monitor = new VisualProgressReviewMonitor();
        var blocked = new VisualProgressChoice(VisualProgressAction.Normal);
        monitor.Hold(blocked, hudVisible: true, now: 10000, inhibited: true);
        foreach (var now in new[] { 10000, 10600, 69999 })
        {
            Assert.False(monitor.TryResume(now, blocked, true, true));
            Assert.False(monitor.TryTakeNotification(now));
        }
        Assert.True(monitor.TryTakeNotification(70000));
        Assert.False(monitor.TryResume(71000, blocked, false, true));
        Assert.True(monitor.TryResume(71600, blocked, false, true));
    }

    [Fact]
    public void 停止表示があっても画面が動き出したら未通知の詰まりを破棄する()
    {
        var monitor = new VisualProgressReviewMonitor();
        var blocked = new VisualProgressChoice(VisualProgressAction.Normal);
        monitor.Hold(blocked, now: 0, inhibited: true);
        Assert.True(monitor.TryResume(59000, blocked, true, true, sceneChanged: true));
        Assert.False(monitor.TryTakeNotification(60000));
        Assert.False(monitor.IsHolding);
    }

    [Fact]
    public void 詰まり検出から一分未満は通知せず一分後も残る時だけ一度通知する()
    {
        var monitor = new VisualProgressReviewMonitor();
        var blocked = new VisualProgressChoice(VisualProgressAction.Review, Detail: "移動待ち");
        monitor.Hold(blocked, now: 120_000);
        Assert.False(monitor.TryTakeNotification(120_000));
        Assert.False(monitor.TryTakeNotification(179_999));
        Assert.True(monitor.TryTakeNotification(180_000));
        Assert.False(monitor.TryTakeNotification(240_000));
    }

    [Fact]
    public void 利用者へ直接申請できた表示は一分後の通知を出さず申請できなかった時だけ通知する()
    {
        var monitor = new VisualProgressReviewMonitor();
        monitor.Hold(new(VisualProgressAction.Review, Detail: "選択", AskUserImmediately: true), now: 1000);
        monitor.MarkNotified();
        Assert.False(monitor.TryTakeNotification(61_000));
        Assert.True(monitor.IsHolding);
        monitor.Hold(new(VisualProgressAction.Review, Detail: "選択", AskUserImmediately: true), now: 100_000);
        Assert.False(monitor.TryTakeNotification(159_999));
        Assert.True(monitor.TryTakeNotification(160_000));
    }

    [Fact]
    public void 一分以内に復帰したら通知を破棄し次の詰まりを検出した時から数え直す()
    {
        var monitor = new VisualProgressReviewMonitor();
        monitor.Hold(new(VisualProgressAction.Normal), now: 10_000);
        Assert.False(monitor.TryTakeNotification(69_399));
        Assert.False(monitor.TryResume(69_400, new(VisualProgressAction.Normal), false, true));
        Assert.True(monitor.TryResume(70_000, new(VisualProgressAction.Normal), false, true));
        Assert.False(monitor.TryTakeNotification(70_000));
        Assert.False(monitor.TryTakeNotification(100_000));
        monitor.Hold(new(VisualProgressAction.Review), now: 110_000);
        Assert.False(monitor.TryTakeNotification(169_999));
        Assert.True(monitor.TryTakeNotification(170_000));
    }

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
