using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using System.Drawing;
using System.Windows.Forms;
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
        Assert.Equal(FrameRotation.Unknown, captured.Rotation);
        Assert.Equal(new FrameCrop(0, 0, 4, 2), captured.Crop);
        Assert.Equal(pixels, captured.Pixels);
        Assert.Equal(0, captured.FreshnessMs);
        Assert.Equal(0, captured.LastChangeMs);
    }

    [Fact]
    public void Wgc_freshness_tracks_time_since_last_pixel_change()
    {
        var tracker = new FrameFreshnessTracker();

        Assert.Equal(new FrameFreshness(0, 100), tracker.Observe(100, [1, 2, 3]));
        Assert.Equal(new FrameFreshness(25, 100), tracker.Observe(125, [1, 2, 3]));
        Assert.Equal(new FrameFreshness(0, 150), tracker.Observe(150, [3, 2, 1]));
    }

    [Fact]
    [Trait("Category", "WindowsNative")]
    public void Wgc_source_captures_a_repainted_window()
    {
        CapturedFrame? initial = null;
        CapturedFrame? resized = null;
        var poolRecreated = false;
        CaptureFault? resizeFault = null;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                using var window = new Form
                {
                    Text = "OpenLogicool WGC t01",
                    ClientSize = new Size(160, 90),
                    StartPosition = FormStartPosition.CenterScreen,
                    BackColor = Color.Navy,
                };
                window.Show();
                Application.DoEvents();

                using var source = WgcFrameSource.CreateForWindow(window.Handle, "t01-wgc-window");
                for (var attempt = 0; attempt < 20 && initial is null; attempt++)
                {
                    window.BackColor = attempt % 2 == 0 ? Color.Navy : Color.Teal;
                    window.Invalidate();
                    window.Update();
                    Application.DoEvents();

                    if (source.Pull() is FrameAvailable available)
                    {
                        initial = available.Frame;
                        break;
                    }

                    Thread.Sleep(50);
                }

                if (initial is null)
                {
                    return;
                }

                window.ClientSize = new Size(320, 180);
                for (var attempt = 0; attempt < 40 && resized is null; attempt++)
                {
                    window.BackColor = attempt % 2 == 0 ? Color.Maroon : Color.Olive;
                    window.Invalidate();
                    window.Update();
                    Application.DoEvents();

                    var detailed = source.PullDetailed();
                    var result = detailed.Result;
                    if (result is FrameUnavailable { Reason: var reason }
                        && reason.Contains("frame pool", StringComparison.Ordinal))
                    {
                        poolRecreated = true;
                        resizeFault = detailed.Fault;
                    }
                    else if (result is FrameAvailable { Frame: var available }
                             && available.Width > initial.Width
                             && available.Height > initial.Height)
                    {
                        resized = available;
                    }

                    Thread.Sleep(50);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        worker.Join();

        Assert.Null(failure);
        Assert.NotNull(initial);
        Assert.Equal(CaptureBackend.WindowsGraphicsCapture, initial!.Backend);
        Assert.True(initial.TransformRevision >= 1);
        Assert.True(initial.Pixels!.Bgra8.Length > 0);
        Assert.True(initial.Pixels.Stride >= initial.Width * 4);
        Assert.True(poolRecreated);
        Assert.Equal(CaptureFaultKind.Resize, resizeFault?.Kind);
        Assert.NotNull(resized);
        Assert.True(resized!.TransformRevision > initial.TransformRevision);
        Assert.True(resized.Pixels!.Bgra8.Length > 0);
        Assert.True(resized.Pixels.Stride >= resized.Width * 4);
    }
}
