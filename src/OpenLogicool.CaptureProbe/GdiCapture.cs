using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OpenLogicool.CaptureProbe;

// GDI BitBlt backend: System.Drawing.Graphics.CopyFromScreen（内部で BitBlt を使う）で
// 仮想スクリーン全体を 2 フレーム取得する。read-only。fallback しない。
internal static class GdiCapture
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public static CaptureResult Run(string fileBase)
    {
        var result = ProbeOutput.NewResult("gdi", "GDI BitBlt (System.Drawing.Graphics.CopyFromScreen)");
        try
        {
            var bounds = new Rectangle(
                GetSystemMetrics(SM_XVIRTUALSCREEN),
                GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN),
                GetSystemMetrics(SM_CYVIRTUALSCREEN));

            result.Target = new
            {
                VirtualScreenX = bounds.X,
                VirtualScreenY = bounds.Y,
                VirtualScreenWidth = bounds.Width,
                VirtualScreenHeight = bounds.Height,
            };

            var clock = Stopwatch.StartNew();
            for (var i = 0; i < 2; i++)
            {
                try
                {
                    using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                    }

                    var pngPath = Path.Combine(ProbeOutput.OutputDirectory, $"{fileBase}-frame{i}.png");
                    var luminance = ImageUtil.ComputeAverageLuminanceAndSave(bitmap, pngPath);
                    result.Frames.Add(new FrameRecord
                    {
                        Sequence = i,
                        MonotonicMs = clock.Elapsed.TotalMilliseconds,
                        Width = bounds.Width,
                        Height = bounds.Height,
                        PixelFormat = "Format32bppArgb",
                        AverageLuminance = luminance,
                        PngFile = Path.GetFileName(pngPath),
                    });
                }
                catch (Exception ex)
                {
                    result.Frames.Add(ProbeOutput.FailedFrame(i, clock.Elapsed.TotalMilliseconds, ex));
                }
            }
        }
        catch (Exception ex)
        {
            result.Error = ErrorRecord.FromException(ex);
        }

        return result;
    }
}
