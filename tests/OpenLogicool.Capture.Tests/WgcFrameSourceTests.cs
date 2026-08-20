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
        Assert.Equal(FrameRotation.None, captured.Rotation);
        Assert.Equal(new FrameCrop(0, 0, 4, 2), captured.Crop);
        Assert.Equal(pixels, captured.Pixels);
    }

    [Fact]
    [Trait("Category", "WindowsNative")]
    public void Wgc_source_captures_a_repainted_window()
    {
        CapturedFrame? captured = null;
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
                for (var attempt = 0; attempt < 20 && captured is null; attempt++)
                {
                    window.BackColor = attempt % 2 == 0 ? Color.Navy : Color.Teal;
                    window.Invalidate();
                    window.Update();
                    Application.DoEvents();

                    if (source.Pull() is FrameAvailable available)
                    {
                        captured = available.Frame;
                        break;
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
        Assert.NotNull(captured);
        Assert.Equal(CaptureBackend.WindowsGraphicsCapture, captured!.Backend);
        Assert.True(captured.Pixels!.Bgra8.Length > 0);
        Assert.True(captured.Pixels.Stride >= captured.Width * 4);
    }
}
