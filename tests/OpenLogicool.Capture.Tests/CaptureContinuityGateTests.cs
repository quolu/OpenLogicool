using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using Xunit;

namespace OpenLogicool.Capture.Tests;

public sealed class CaptureContinuityGateTests
{
    [Fact]
    public void Fresh_frame_requires_explicit_recalibration_before_automatic_input()
    {
        var gate = new CaptureContinuityGate();
        var frame = Frame();

        gate.Observe(CaptureRead.Available(frame), staleAfterMs: 100);

        Assert.False(gate.AllowsAutomaticInput);
        gate.Recalibrate(frame);
        Assert.True(gate.AllowsAutomaticInput);
    }

    [Fact]
    public void Static_unavailable_frame_preserves_calibrated_permission()
    {
        var gate = CalibratedGate();

        gate.Observe(CaptureRead.Unavailable("wgc frame はまだ到着していません。"), staleAfterMs: 100);

        Assert.True(gate.AllowsAutomaticInput);
        Assert.Null(gate.BlockingFault);
    }

    [Theory]
    [InlineData(CaptureFaultKind.Black)]
    [InlineData(CaptureFaultKind.Drop)]
    [InlineData(CaptureFaultKind.Resize)]
    [InlineData(CaptureFaultKind.DeviceLost)]
    [InlineData(CaptureFaultKind.Occluded)]
    [InlineData(CaptureFaultKind.Minimized)]
    public void Explicit_fault_blocks_automatic_input_until_recalibration(CaptureFaultKind kind)
    {
        var gate = CalibratedGate();
        var fresh = Frame();

        gate.Observe(CaptureRead.Unavailable(new CaptureFault(kind, kind.ToString())), staleAfterMs: 100);

        Assert.False(gate.AllowsAutomaticInput);
        Assert.Equal(kind, gate.BlockingFault?.Kind);
        gate.Observe(CaptureRead.Available(fresh), staleAfterMs: 100);
        Assert.False(gate.AllowsAutomaticInput);
        gate.Recalibrate(fresh);
        Assert.True(gate.AllowsAutomaticInput);
    }

    [Fact]
    public void Stale_backend_and_transform_changes_each_break_continuity()
    {
        var gate = CalibratedGate();

        gate.Observe(CaptureRead.Available(Frame() with { FreshnessMs = 101 }), staleAfterMs: 100);
        Assert.Equal(CaptureFaultKind.Stale, gate.BlockingFault?.Kind);

        gate.Recalibrate(Frame());
        gate.Observe(CaptureRead.Available(Frame() with { Backend = CaptureBackend.DesktopDuplication }), staleAfterMs: 100);
        Assert.Equal(CaptureFaultKind.BackendChanged, gate.BlockingFault?.Kind);

        gate.Recalibrate(Frame() with { Backend = CaptureBackend.DesktopDuplication });
        gate.Observe(CaptureRead.Available(Frame() with
        {
            Backend = CaptureBackend.DesktopDuplication,
            TransformRevision = 2,
        }), staleAfterMs: 100);
        Assert.Equal(CaptureFaultKind.Resize, gate.BlockingFault?.Kind);
    }

    private static CaptureContinuityGate CalibratedGate()
    {
        var gate = new CaptureContinuityGate();
        var frame = Frame();
        gate.Observe(CaptureRead.Available(frame), staleAfterMs: 100);
        gate.Recalibrate(frame);
        return gate;
    }

    private static CapturedFrame Frame() => new(
        "0.2.0", "capture-test", CaptureBackend.WindowsGraphicsCapture,
        Sequence: 1, MonotonicMs: 1, WallClockUtc: DateTimeOffset.UnixEpoch,
        Width: 10, Height: 10, PixelFormat: "B8G8R8A8_UNorm", DpiX: 96, DpiY: 96,
        TransformRevision: 1, FreshnessMs: 0, LastChangeMs: 1);
}
