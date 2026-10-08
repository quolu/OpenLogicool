using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class CompassSpaceTests
{
    private static string Fixture
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
            return Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        }
    }

    private static VisualProgressProfile Profile() => VisualProgressProfile.Load(Path.Combine(Fixture, "progress.json"));

    [Fact]
    public void 保存実画面の右下コンパスをOCRなしで検出する()
    {
        var frame = Read("compass-screen.png");
        var choice = new VisualProgressRecognizer(Profile()).RecognizeImmediateImage(frame,
            new(1, 31, frame.Width - 2, frame.Height - 32));
        Assert.NotNull(choice);
        Assert.Equal("compass-space", choice.RuleId);
        Assert.Equal("Key:Space", choice.Key);
        Assert.True(choice.Immediate);
    }

    [Theory]
    [InlineData(0, 1.0, 0)]
    [InlineData(37, 0.65, 5)]
    [InlineData(83, 0.85, -4)]
    [InlineData(137, 1.15, 3)]
    [InlineData(213, 0.55, -5)]
    [InlineData(299, 1.25, 4)]
    public void 針の回転と揺れと窓倍率が変わっても検出する(double angle, double scale, int jitter)
    {
        var (frame, viewport) = Placed(angle, scale, jitter);
        var choice = new VisualProgressRecognizer(Profile()).RecognizeImmediateImage(frame, viewport);
        Assert.NotNull(choice);
        Assert.Equal("compass-space", choice.RuleId);
    }

    [Fact]
    public void HUDや停止四角やSpaceだけではコンパスと扱わない()
    {
        var recognizer = new VisualProgressRecognizer(Profile());
        foreach (var name in new[] { "normal-gameplay-review.png", "before.png", "after.png", "combat.png", "window-small.png",
            "window-short.png", "window-narrow.png", "bonus-three.png", "class-level.png", "space-confirm.png", "dungeon-clear.png" })
        {
            var frame = Read(name);
            Assert.Null(recognizer.RecognizeImmediateImage(frame, new(1, 31, frame.Width - 2, frame.Height - 32)));
        }
        var (withoutLabel, viewport) = Placed(0, 1, 0, removeLabel: true);
        Assert.Null(recognizer.RecognizeImmediateImage(withoutLabel, viewport));
        var stop = Read("stop.png");
        Assert.Null(recognizer.RecognizeImmediateImage(stop, new(0, 0, stop.Width, stop.Height)));
    }

    [Fact]
    public void 初回は即送出し同じ表示へ連打せず消失や停止表示の後に再開する()
    {
        var profile = Profile();
        var schedule = new VisualProgressSchedule(profile);
        var cue = new VisualProgressChoice(VisualProgressAction.Key, "compass-space", "compass-space", "Key:Space", Immediate: true);
        Assert.Equal(VisualProgressAction.Key, schedule.Decide(0, cue, false, false, due: false).Action);
        schedule.RecordInput(0, cue);
        foreach (var time in new[] { 250, 500, 1000, 10000 })
            Assert.Equal(VisualProgressAction.Wait, schedule.Decide(time, cue, false, false, true, sceneChanged: true).Action);
        var absent = new VisualProgressChoice(VisualProgressAction.Normal);
        _ = schedule.Decide(11000, absent, false, true, false);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(11250, cue, false, false, true).Action);
        _ = schedule.Decide(12000, absent, false, true, false);
        _ = schedule.Decide(12600, absent, false, true, false);
        Assert.Equal(VisualProgressAction.Key, schedule.Decide(12601, cue, false, false, false).Action);
        schedule.RecordInput(12601, cue);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(13000, cue, true, true, true).Action);
        Assert.Equal(VisualProgressAction.Key, schedule.Decide(13250, cue, false, false, false).Action);
    }

    private static CapturedFrame Read(string name)
    {
        using var file = File.OpenRead(Path.Combine(Fixture, name));
        var bitmap = new FormatConvertedBitmap(BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return new(ContractSchemaVersions.Revision03, "compass-test", CaptureBackend.WindowsGraphicsCapture, 1, 0,
            DateTimeOffset.UnixEpoch, bitmap.PixelWidth, bitmap.PixelHeight, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(bytes, bitmap.PixelWidth * 4));
    }

    private static (CapturedFrame Frame, FrameRect Viewport) Placed(double angle, double scale, int jitter, bool removeLabel = false)
    {
        var source = Read("compass-space.png");
        var viewport = new FrameRect(17, 31, (int)(1712 * scale), (int)(1084 * scale));
        var width = (int)viewport.Width + 40;
        var height = (int)viewport.Height + 60;
        var bytes = new byte[width * height * 4];
        for (var i = 0; i < bytes.Length; i += 4) { bytes[i] = 41; bytes[i + 1] = 29; bytes[i + 2] = 62; bytes[i + 3] = 255; }
        var radians = angle * Math.PI / 180;
        var w = (int)Math.Round(source.Width * scale);
        var h = (int)Math.Round(source.Height * scale);
        var rotated = source.Pixels!.Bgra8.ToArray();
        for (var sy = 0; sy < source.Height; sy++)
            for (var sx = 0; sx < source.Width; sx++)
            {
                var dx = sx - 76; var dy = sy - 80;
                if (dx * dx + dy * dy >= 44 * 44 || (sx > 85 && sy > 108 && sy < 130)) continue;
                var rx = (int)(76 + Math.Cos(radians) * dx + Math.Sin(radians) * dy);
                var ry = (int)(80 - Math.Sin(radians) * dx + Math.Cos(radians) * dy);
                source.Pixels.Bgra8.Span.Slice((ry * source.Width + rx) * 4, 4).CopyTo(rotated.AsSpan((sy * source.Width + sx) * 4));
            }
        if (removeLabel)
            for (var sy = 109; sy < 130; sy++)
                for (var sx = 86; sx < 130; sx++)
                    for (var c = 0; c < 3; c++) rotated[(sy * source.Width + sx) * 4 + c] = 50;
        var image = BitmapSource.Create(source.Width, source.Height, 96, 96, PixelFormats.Bgra32, null, rotated, source.Width * 4);
        var resized = new TransformedBitmap(image, new ScaleTransform(w / (double)source.Width, h / (double)source.Height));
        var scaled = new byte[w * h * 4];
        resized.CopyPixels(scaled, w * 4, 0);
        var left = (int)(viewport.X + viewport.Width) - w - 12 + jitter;
        var top = (int)(viewport.Y + viewport.Height) - h - 12 - jitter;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var offset = ((top + y) * width + left + x) * 4;
                scaled.AsSpan((y * w + x) * 4, 4).CopyTo(bytes.AsSpan(offset));
            }
        return (source with { Width = width, Height = height, Pixels = new FramePixels(bytes, width * 4) }, viewport);
    }
}
