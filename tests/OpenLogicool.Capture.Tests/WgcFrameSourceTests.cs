using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using Xunit;

namespace OpenLogicool.Capture.Tests;

public sealed class WgcFrameSourceTests
{
    [Fact]
    public void Wgc_metadata_projects_the_complete_uncropped_frame_contract()
    {
        var pixels = new FramePixels(new byte[32], Stride: 16);
        var captured = new WgcFrameMetadata(
            Sequence: 8,
            MonotonicMs: 1234.5,
            WallClockUtc: new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
            Width: 4,
            Height: 2,
            PixelFormat: "B8G8R8A8_UNorm",
            DpiX: 144,
            DpiY: 144,
            Pixels: pixels).ToCapturedFrame("window:42");

        Assert.Equal(CaptureBackend.WindowsGraphicsCapture, captured.Backend);
        Assert.Equal(8, captured.Sequence);
        Assert.Equal(1234.5, captured.MonotonicMs);
        Assert.Equal("B8G8R8A8_UNorm", captured.PixelFormat);
        Assert.Equal(FrameColorSpace.Unknown, captured.ColorSpace);
        Assert.Equal(FrameRotation.None, captured.Rotation);
        Assert.Equal(new FrameCrop(0, 0, 4, 2), captured.Crop);
        Assert.Equal(pixels, captured.Pixels);
    }
}
