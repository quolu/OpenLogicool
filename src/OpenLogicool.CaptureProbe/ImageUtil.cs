using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OpenLogicool.CaptureProbe;

// フレームの pixel buffer から PNG 保存と非黒検証用の平均輝度算出を行う。
// 全 backend (GDI / DXGI Desktop Duplication / WGC) が BGRA8 相当のバッファを
// この経路に流し込むことで、輝度計算とPNG保存のロジックを一本化する。
internal static class ImageUtil
{
    private const int SampleStep = 7;

    // 生の BGRA8 バッファ（行ごとの pitch 付き）から System.Drawing.Bitmap を組み立てる。
    public static Bitmap CreateBitmapFromBgra8(IntPtr srcPtr, int srcRowPitch, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBuffer = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(srcPtr + y * srcRowPitch, rowBuffer, 0, rowBuffer.Length);
                Marshal.Copy(rowBuffer, 0, data.Scan0 + y * data.Stride, rowBuffer.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    // sampleStep 間隔で pixel を間引きながら輝度平均を求め、bitmap を PNG として保存する。
    public static double ComputeAverageLuminanceAndSave(Bitmap bitmap, string pngPath)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        double sum = 0;
        long count = 0;
        try
        {
            var buffer = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            for (var y = 0; y < bitmap.Height; y += SampleStep)
            {
                var rowOffset = y * data.Stride;
                for (var x = 0; x < bitmap.Width; x += SampleStep)
                {
                    var idx = rowOffset + x * 4;
                    byte b = buffer[idx];
                    byte g = buffer[idx + 1];
                    byte r = buffer[idx + 2];
                    sum += 0.299 * r + 0.587 * g + 0.114 * b;
                    count++;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(pngPath, ImageFormat.Png);
        return count == 0 ? 0 : sum / count;
    }
}
