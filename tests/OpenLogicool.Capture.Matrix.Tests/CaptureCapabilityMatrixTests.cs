using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using Xunit;

namespace OpenLogicool.Capture.Matrix.Tests;

public sealed class CaptureCapabilityMatrixTests
{
    private readonly CaptureCapabilityMatrix matrix = CaptureCapabilityMatrix.CreateReferenceMatrix();

    [Fact]
    public void Select_WgcWindowed_ReturnsConfirmedAvailableRoute()
    {
        var decision = matrix.Select(
            CaptureBackend.WindowsGraphicsCapture,
            CaptureTargetKind.Window,
            CaptureCondition.Windowed);

        Assert.Equal(CaptureEvidenceLevel.Confirmed, decision.Evidence);
        Assert.True(decision.CanCapture);
    }

    [Fact]
    public void Select_MinimizedWindow_ReturnsExplicitUnsupportedReason()
    {
        var decision = matrix.Select(
            CaptureBackend.WindowsGraphicsCapture,
            CaptureTargetKind.Window,
            CaptureCondition.Minimized);

        Assert.Equal(CaptureEvidenceLevel.Unsupported, decision.Evidence);
        Assert.False(decision.CanCapture);
        Assert.Contains("frame", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Select_UnverifiedCondition_DoesNotFallbackToWindowedWgc()
    {
        var decision = matrix.Select(
            CaptureBackend.WindowsGraphicsCapture,
            CaptureTargetKind.Window,
            CaptureCondition.Hdr);

        Assert.Equal(CaptureEvidenceLevel.Unverified, decision.Evidence);
        Assert.Equal(CaptureRouteAvailability.Unavailable, decision.Availability);
        Assert.False(decision.CanCapture);
    }

    [Fact]
    public void Select_ProbedAlternateBackend_RecordsItAsProbedOnly()
    {
        var decision = matrix.Select(
            CaptureBackend.DesktopDuplication,
            CaptureTargetKind.Display,
            CaptureCondition.Windowed);

        Assert.Equal(CaptureEvidenceLevel.Confirmed, decision.Evidence);
        Assert.Equal(CaptureRouteAvailability.ProbedOnly, decision.Availability);
        Assert.False(decision.CanCapture);
    }

    [Fact]
    public void Select_MissingMatrixRow_ReturnsUnverifiedExplicitFailure()
    {
        var decision = matrix.Select(
            CaptureBackend.GdiBitBlt,
            CaptureTargetKind.Window,
            CaptureCondition.Fullscreen);

        Assert.Equal(CaptureEvidenceLevel.Unverified, decision.Evidence);
        Assert.Equal(CaptureRouteAvailability.Unavailable, decision.Availability);
    }
}
