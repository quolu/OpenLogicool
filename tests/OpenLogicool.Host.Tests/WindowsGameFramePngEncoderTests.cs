using OpenLogicool.Contracts.Capture;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class WindowsGameFramePngEncoderTests
{
    [Fact]
    public void Vision_copy_is_downscaled_while_full_evidence_keeps_original_size()
    {
        const int width = 1920;
        const int height = 1080;
        var frame = new CapturedFrame(
            "0.3.0",
            "window:game",
            CaptureBackend.WindowsGraphicsCapture,
            1,
            0,
            DateTimeOffset.UnixEpoch,
            width,
            height,
            "BGRA8",
            96,
            96,
            0,
            0,
            0,
            Pixels: new FramePixels(new byte[width * height * 4], width * 4));
        var encoder = new WindowsGameFramePngEncoder();

        var full = encoder.Encode(frame);
        var vision = encoder.Encode(frame, 640);

        Assert.Equal((1920, 1080), (full.Width, full.Height));
        Assert.Equal((640, 360), (vision.Width, vision.Height));
        Assert.False(full.Bytes.IsEmpty);
        Assert.False(vision.Bytes.IsEmpty);
    }
}
