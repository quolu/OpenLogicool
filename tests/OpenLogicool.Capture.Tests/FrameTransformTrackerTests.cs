using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using Xunit;

namespace OpenLogicool.Capture.Tests;

public sealed class FrameTransformTrackerTests
{
    [Fact]
    public void Revision_advances_for_resize_dpi_format_and_letterbox_changes()
    {
        var tracker = new FrameTransformTracker();
        var frame = Frame();
        var full = new FrameRect(0, 0, 1920, 1080);

        Assert.Equal(1, tracker.Observe(frame, full));
        Assert.True(tracker.IsCurrent(1));
        Assert.Equal(1, tracker.Observe(frame, full));
        Assert.Equal(2, tracker.Observe(frame with { Width = 2560 }, new FrameRect(0, 0, 2560, 1080)));
        Assert.False(tracker.IsCurrent(1));
        Assert.True(tracker.IsCurrent(2));
        Assert.Equal(3, tracker.Observe(frame with { DpiX = 144, DpiY = 144 }, full));
        Assert.Equal(4, tracker.Observe(frame with { PixelFormat = "R16G16B16A16_Float" }, full));
        Assert.Equal(5, tracker.Observe(frame, new FrameRect(0, 120, 1920, 840)));
    }

    [Fact]
    public void Coordinate_transform_preserves_the_declared_coordinate_pipeline()
    {
        var transform = new FrameCoordinateTransform(Revision: 5, new FrameRect(0, 120, 1920, 840));

        var content = transform.SourceToContent(new FramePoint(960, 540));
        var normalized = transform.ContentToNormalized(content);
        var client = transform.NormalizedToClient(normalized, new FrameSize(1280, 720));
        var input = transform.ClientToInput(client, new FramePoint(100, 200));

        Assert.Equal(new FramePoint(960, 420), content);
        Assert.Equal(new NormalizedPoint(0.5, 0.5), normalized);
        Assert.Equal(new FramePoint(640, 360), client);
        Assert.Equal(new FramePoint(740, 560), input);
    }

    [Fact]
    public void Coordinate_transform_rejects_points_outside_content()
    {
        var transform = new FrameCoordinateTransform(Revision: 1, new FrameRect(0, 120, 1920, 840));

        Assert.Throws<ArgumentOutOfRangeException>(() => transform.SourceToNormalized(new FramePoint(0, 119)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            transform.NormalizedToClient(new NormalizedPoint(1.01, 0.5), new FrameSize(100, 100)));
    }

    private static CapturedFrame Frame() => new(
        "0.2.0",
        "frame-transform-test",
        CaptureBackend.WindowsGraphicsCapture,
        Sequence: 1,
        MonotonicMs: 1,
        WallClockUtc: DateTimeOffset.UnixEpoch,
        Width: 1920,
        Height: 1080,
        PixelFormat: "B8G8R8A8_UNorm",
        DpiX: 96,
        DpiY: 96,
        TransformRevision: 0,
        FreshnessMs: 0,
        LastChangeMs: 0);
}
