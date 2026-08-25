using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Perception;
using Xunit;

namespace OpenLogicool.Perception.Tests;

public sealed class VisualPatchMatcherTests
{
    [Fact]
    public void Captured_patch_matches_same_region_and_rejects_changed_region()
    {
        var original = Frame(40);
        var signature = VisualPatchMatcher.Capture(original, [0.25, 0.25, 0.5, 0.5]);

        Assert.True(VisualPatchMatcher.Matches(signature, original, [0.25, 0.25, 0.5, 0.5]));
        Assert.False(VisualPatchMatcher.Matches(signature, Frame(220), [0.25, 0.25, 0.5, 0.5]));
        Assert.Equal(0, VisualPatchSignatureComparer.MeanAbsoluteDifference(signature, signature));
        Assert.True(VisualPatchSignatureComparer.MeanAbsoluteDifference(
            signature,
            VisualPatchMatcher.Capture(Frame(220), [0.25, 0.25, 0.5, 0.5])) > 100);
    }

    [Fact]
    public void Hover_change_uses_stricter_sensitivity_than_saved_image_identity()
    {
        var bounds = new[] { 0.25, 0.25, 0.5, 0.5 };
        var signature = VisualPatchMatcher.Capture(Frame(40), bounds);

        Assert.True(VisualPatchMatcher.Matches(signature, Frame(50), bounds));
        Assert.False(VisualPatchMatcher.Matches(
            signature,
            Frame(50),
            bounds,
            maximumMeanAbsoluteDifference: 0.5));
    }

    private static CapturedFrame Frame(byte value)
    {
        const int width = 16;
        const int height = 16;
        var pixels = Enumerable.Repeat(value, width * height * 4).ToArray();
        return new CapturedFrame(
            "0.3.0", "window:test", CaptureBackend.WindowsGraphicsCapture, 1, 0,
            DateTimeOffset.UnixEpoch, width, height, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(pixels, width * 4));
    }
}
