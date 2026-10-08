using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Host;

/// <summary>単色の回転する印を連結画素へ分け、向きを変えた形と照合する。</summary>
internal sealed class VisualRotatingTemplate
{
    private const int SampleSize = 24;
    private readonly int[] color;
    private readonly bool[][] rotations;
    private readonly int diameter;

    public VisualRotatingTemplate(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var w = bitmap.PixelWidth;
        var h = bitmap.PixelHeight;
        var bytes = new byte[w * h * 4];
        bitmap.CopyPixels(bytes, w * 4, 0);
        // 明るい背景の単色アイコンを対象とする。背景や色が曖昧な入力画像は拒否する。
        var foreground = Enumerable.Range(0, w * h).Where(i =>
            Math.Max(bytes[i * 4], Math.Max(bytes[i * 4 + 1], bytes[i * 4 + 2])) < 220).ToArray();
        if (w < 8 || h < 8 || foreground.Length < 8 || foreground.Length > w * h * 0.8)
            throw new InvalidDataException("回転画像には明るい背景と単色の印が必要です。");
        color = Enumerable.Range(0, 3).Select(c => (int)foreground.Select(i => bytes[i * 4 + c])
            .Order().ElementAt(foreground.Length / 2)).ToArray();
        var mask = Enumerable.Range(0, w * h).Select(i => IsForeground(bytes, i * 4)).ToArray();
        var points = Enumerable.Range(0, mask.Length).Where(i => mask[i]).Select(i => (X: i % w, Y: i / w)).ToArray();
        var left = points.Min(p => p.X);
        var top = points.Min(p => p.Y);
        var shapeWidth = points.Max(p => p.X) - left + 1;
        var shapeHeight = points.Max(p => p.Y) - top + 1;
        diameter = Math.Max(shapeWidth, shapeHeight);
        rotations = Enumerable.Range(0, 36).Select(index =>
        {
            var angle = index * Math.PI / 18;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            // 拡大した作業面に回転を描き、余白を除いてから共通の大きさへ揃える。
            var side = diameter * 4;
            var rotated = new bool[side * side];
            for (var y = 0; y < side; y++)
                for (var x = 0; x < side; x++)
                {
                    var dx = (x + 0.5 - side / 2.0) / 2;
                    var dy = (y + 0.5 - side / 2.0) / 2;
                    var sx = (int)Math.Floor(cos * dx + sin * dy + shapeWidth / 2.0);
                    var sy = (int)Math.Floor(-sin * dx + cos * dy + shapeHeight / 2.0);
                    rotated[y * side + x] = sx >= 0 && sx < shapeWidth && sy >= 0 && sy < shapeHeight
                        && mask[(top + sy) * w + left + sx];
                }
            var occupied = Enumerable.Range(0, rotated.Length).Where(i => rotated[i]).ToArray();
            var x0 = occupied.Min(i => i % side);
            var y0 = occupied.Min(i => i / side);
            return Sample(rotated, side, x0, y0, occupied.Max(i => i % side) - x0 + 1,
                occupied.Max(i => i / side) - y0 + 1);
        }).ToArray();
    }

    public VisualKeyTemplateMatch Find(CapturedFrame frame, IReadOnlyList<double> area, double scale)
    {
        var pixels = frame.Pixels ?? throw new InvalidOperationException("回転画像の照合にpixelsがありません。");
        var bytes = pixels.Bgra8.Span;
        var left = (int)(area[0] * frame.Width);
        var top = (int)(area[1] * frame.Height);
        var w = (int)(area[2] * frame.Width);
        var h = (int)(area[3] * frame.Height);
        var mask = new bool[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                mask[y * w + x] = IsForeground(bytes, (top + y) * pixels.Stride + (left + x) * 4);
        var seen = new bool[mask.Length];
        var stack = new Stack<int>();
        var best = 0.0;
        double[] bounds = [];
        for (var i = 0; i < mask.Length; i++)
        {
            if (!mask[i] || seen[i]) continue;
            stack.Push(i);
            seen[i] = true;
            var x0 = i % w; var x1 = x0; var y0 = i / w; var y1 = y0;
            while (stack.TryPop(out var p))
            {
                var x = p % w; var y = p / w;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x);
                y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                if (x > 0) Visit(p - 1);
                if (x + 1 < w) Visit(p + 1);
                if (y > 0) Visit(p - w);
                if (y + 1 < h) Visit(p + w);
            }
            var cw = x1 - x0 + 1; var ch = y1 - y0 + 1;
            if (Math.Min(cw, ch) < diameter * scale * 0.65 || Math.Max(cw, ch) > diameter * scale * 1.6) continue;
            var candidate = Sample(mask, w, x0, y0, cw, ch);
            foreach (var reference in rotations)
            {
                var intersection = 0; var union = 0;
                for (var j = 0; j < candidate.Length; j++)
                {
                    if (candidate[j] && reference[j]) intersection++;
                    if (candidate[j] || reference[j]) union++;
                }
                var score = intersection / (double)union;
                if (score <= best) continue;
                best = score;
                bounds = [(left + x0) / (double)frame.Width, (top + y0) / (double)frame.Height,
                    cw / (double)frame.Width, ch / (double)frame.Height];
            }
        }
        // 元のRGB画像とは別尺度。形の重なり75％以上だけを一致とする。
        return new(best >= 0.75 ? 0 : 255, bounds);

        void Visit(int p)
        {
            if (!seen[p] && mask[p]) { seen[p] = true; stack.Push(p); }
        }
    }

    private bool IsForeground(ReadOnlySpan<byte> bytes, int offset) =>
        Math.Abs(bytes[offset] - color[0]) < 22 && Math.Abs(bytes[offset + 1] - color[1]) < 22
        && Math.Abs(bytes[offset + 2] - color[2]) < 22;

    private static bool[] Sample(bool[] mask, int stride, int left, int top, int width, int height)
    {
        var sampled = new bool[SampleSize * SampleSize];
        for (var y = 0; y < SampleSize; y++)
            for (var x = 0; x < SampleSize; x++)
                sampled[y * SampleSize + x] = mask[(top + (int)((y + 0.5) * height / SampleSize)) * stride
                    + left + (int)((x + 0.5) * width / SampleSize)];
        return sampled;
    }
}
