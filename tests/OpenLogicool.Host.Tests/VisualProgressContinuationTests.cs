using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class VisualProgressContinuationTests
{
    [Fact]
    public void 確認通知の後も通常Spaceの周期を止めず停止画像は優先する()
    {
        var timer = new VisualKeyAssistSchedule(0, () => 10000);
        var unknown = new VisualProgressChoice(VisualProgressAction.Normal);
        Assert.Equal(VisualProgressAction.Normal, VisualProgressContinuation.AfterReview(unknown, true).Action);
        Assert.Equal(VisualKeyAssistDecision.Timed, timer.Decide(10000, false, false));
        timer.RecordInput(10000);
        Assert.Equal(VisualKeyAssistDecision.Wait, timer.Decide(19999, false, false));
        Assert.Equal(VisualKeyAssistDecision.Timed, timer.Decide(20000, false, false));
        Assert.Equal(VisualKeyAssistDecision.Hold, timer.Decide(20000, true, true));
        Assert.Equal(VisualProgressAction.Wait, VisualProgressContinuation.AfterReview(unknown, false).Action);
    }

    [Fact]
    public void 回答が必要な選択や同じ未確認操作は通知を理由に自動選択しない()
    {
        Assert.Equal(VisualProgressAction.Wait, VisualProgressContinuation.AfterReview(
            new(VisualProgressAction.Review, Detail: "ボーナスを選択してください"), true).Action);
        Assert.Equal(VisualProgressAction.Wait, VisualProgressContinuation.AfterReview(
            new(VisualProgressAction.Key, "dialogue", "同じ台詞", "Key:Space"), true).Action);
    }

    [Fact]
    public void 前の結果が未確認でも次の既知操作を実行できる()
    {
        var schedule = new VisualProgressSchedule(new(1, [], [], 5000, 10000));
        var first = new VisualProgressChoice(VisualProgressAction.Key, "first", "first", "Key:Space");
        schedule.RecordInput(0, first);
        _ = schedule.Decide(1000, first, false, false, true);
        _ = schedule.Decide(3000, first, false, false, true);
        Assert.Equal(VisualProgressAction.Review, schedule.Decide(5000, first, false, false, true).Action);
        var next = new VisualProgressChoice(VisualProgressAction.Click, "accept", "承諾|やめる");
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(5100, next, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Click, schedule.Decide(5700, next, false, false, true).Action);
    }

    [Fact]
    public void 通知された実画面のHUDが読める場合は画面差だけで再通知しない()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        using var stream = File.OpenRead(Path.Combine(fixture, "normal-gameplay-review.png"));
        var bitmap = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        var frame = new CapturedFrame(ContractSchemaVersions.Revision03, "gameplay-review", CaptureBackend.WindowsGraphicsCapture,
            1, 0, DateTimeOffset.UnixEpoch, bitmap.PixelWidth, bitmap.PixelHeight, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(bytes, bitmap.PixelWidth * 4));
        var observation = new VisualRecoveryRecognizer(VisualRecoveryProfile.Load(Path.Combine(fixture, "profile.json")))
            .Observe(frame, new(1, 31, frame.Width - 2, frame.Height - 32));
        Assert.True(observation.HudVisible);
        Assert.False(VisualProgressContinuation.RequiresUnchangedReview(true, observation.HudVisible));
        Assert.True(VisualProgressContinuation.RequiresUnchangedReview(false, observation.HudVisible));
        Assert.True(VisualProgressContinuation.RequiresUnchangedReview(true, false));
    }
}
