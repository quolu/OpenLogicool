using System.IO;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class VisualKeyAssistWorkersTests
{
    [Fact]
    public async Task 回復監視は未完了の画面比較と同期OCRを待たない()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var releaseOcr = new ManualResetEventSlim();
        var enteredOcr = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastFood = DateTimeOffset.UtcNow;
        var profile = VisualRecoveryProfile.Load(FindProfile());
        var schedule = new VisualRecoverySchedule(profile, new(LastFood: lastFood));
        var ticks = 0;
        var sent = new List<VisualRecoveryAction>();
        try
        {
            var result = await VisualKeyAssistWorkers.RunAsync(async token =>
            {
                await enteredOcr.Task.WaitAsync(token);
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                while (await timer.WaitForNextTickAsync(token))
                {
                    ticks++;
                    var observation = new VisualRecoveryObservation(true, ticks < 2 ? 0.7 : 0.4,
                        198, VisualFoodState.Active, null, 0);
                    var choice = schedule.Decide(DateTimeOffset.UtcNow, observation, false);
                    if (choice.Action == VisualRecoveryAction.Potion)
                    {
                        schedule.RecordAttempt(choice.Action, DateTimeOffset.UtcNow, observation);
                        sent.Add(choice.Action);
                    }
                    if (ticks == 4) { releaseOcr.Set(); return "回復継続"; }
                }
                throw new InvalidOperationException("監視が終了しました。");
            }, async token =>
            {
                // OCRが同期的に止まる場合も、回復のタイマーを止めない。
                enteredOcr.SetResult();
                releaseOcr.Wait(token);
                await Task.Delay(Timeout.Infinite, token);
                return "進行終了";
            }, timeout.Token);
            Assert.Equal("回復継続", result);
            Assert.Equal(4, ticks);
            Assert.Equal([VisualRecoveryAction.Potion], sent);
        }
        finally { releaseOcr.Set(); }
    }

    [Fact]
    public async Task 回復の死亡終了は進行待ちを取り消して回収する()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = false;
        var result = await VisualKeyAssistWorkers.RunAsync(async token =>
        {
            await started.Task.WaitAsync(token);
            return "行動不能";
        }, async token =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { ended = true; }
            return "進行";
        }, CancellationToken.None);
        Assert.Equal("行動不能", result);
        Assert.True(ended);
    }

    [Fact]
    public async Task 進行側の障害は隠さず監視も終了する()
    {
        var ended = false;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => VisualKeyAssistWorkers.RunAsync(async token =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { ended = true; }
            return 0;
        }, async token =>
        {
            await started.Task.WaitAsync(token);
            return await Task.FromException<int>(new InvalidOperationException("Nano接続異常"));
        }, CancellationToken.None));
        Assert.Equal("Nano接続異常", error.Message);
        Assert.True(ended);
    }

    private static string FindProfile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "fixtures/visual-recovery/mabinogi-20261008/profile.json");
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("回復判定fixtureがありません。");
    }
}
