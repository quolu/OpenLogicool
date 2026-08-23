namespace OpenLogicool.Devices.G13;

public enum G13LcdBitmapFit
{
    Stretch,
    Contain,
}

public sealed record G13LcdBitmapConversionOptions(
    G13LcdBitmapFit Fit = G13LcdBitmapFit.Stretch,
    byte PixelThreshold = 180,
    byte ContentThreshold = 180,
    bool AutoCrop = true,
    bool Invert = false);

public sealed record G13LcdBitmapConversionResult(
    byte[] Framebuffer,
    int SourceCropX,
    int SourceCropY,
    int SourceCropWidth,
    int SourceCropHeight,
    int DestinationX,
    int DestinationY,
    int DestinationWidth,
    int DestinationHeight);

/// <summary>BGRA32画像をG13の160×43 1-bit framebufferへ変換するpure実装。</summary>
public static class G13LcdBitmapConverter
{
    public static G13LcdBitmapConversionResult ConvertBgra32(
        ReadOnlySpan<byte> pixels,
        int sourceWidth,
        int sourceHeight,
        int stride,
        G13LcdBitmapConversionOptions? options = null)
    {
        if (sourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        }

        if (sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        }

        if (stride < sourceWidth * 4 || pixels.Length < stride * sourceHeight)
        {
            throw new ArgumentException("BGRA32画像のstrideまたはbuffer長が不足しています。", nameof(pixels));
        }

        options ??= new G13LcdBitmapConversionOptions();
        var crop = options.AutoCrop
            ? FindContentBounds(pixels, sourceWidth, sourceHeight, stride, options.ContentThreshold)
            : new Rectangle(0, 0, sourceWidth, sourceHeight);
        var destination = DestinationRectangle(crop.Width, crop.Height, options.Fit);
        var framebuffer = new byte[G13LcdFrame.FramebufferLength];

        // 画像外のletterboxは元画像の白背景と同じ白にする。
        if (options.Fit == G13LcdBitmapFit.Contain)
        {
            framebuffer.AsSpan().Fill(options.Invert ? (byte)0x00 : (byte)0xFF);
            ClearInvisibleTail(framebuffer);
        }

        for (var y = 0; y < destination.Height; y++)
        {
            var sourceY = crop.Y + (y * crop.Height / destination.Height);
            for (var x = 0; x < destination.Width; x++)
            {
                var sourceX = crop.X + (x * crop.Width / destination.Width);
                var luminance = Luminance(pixels, stride, sourceX, sourceY);
                var pixelOn = luminance >= options.PixelThreshold;
                if (options.Invert)
                {
                    pixelOn = !pixelOn;
                }

                if (pixelOn)
                {
                    G13LcdFrame.SetPixel(framebuffer, destination.X + x, destination.Y + y);
                }
                else
                {
                    ClearPixel(framebuffer, destination.X + x, destination.Y + y);
                }
            }
        }

        return new G13LcdBitmapConversionResult(
            framebuffer,
            crop.X,
            crop.Y,
            crop.Width,
            crop.Height,
            destination.X,
            destination.Y,
            destination.Width,
            destination.Height);
    }

    private static Rectangle FindContentBounds(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int stride,
        byte contentThreshold)
    {
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (Luminance(pixels, stride, x, y) >= contentThreshold)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return new Rectangle(0, 0, width, height);
        }

        const int margin = 2;
        minX = Math.Max(0, minX - margin);
        minY = Math.Max(0, minY - margin);
        maxX = Math.Min(width - 1, maxX + margin);
        maxY = Math.Min(height - 1, maxY + margin);
        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Rectangle DestinationRectangle(int width, int height, G13LcdBitmapFit fit)
    {
        if (fit == G13LcdBitmapFit.Stretch)
        {
            return new Rectangle(0, 0, G13LcdFrame.Width, G13LcdFrame.Height);
        }

        var scale = Math.Min(
            G13LcdFrame.Width / (double)width,
            G13LcdFrame.Height / (double)height);
        var destinationWidth = Math.Max(1, (int)Math.Round(width * scale));
        var destinationHeight = Math.Max(1, (int)Math.Round(height * scale));
        return new Rectangle(
            (G13LcdFrame.Width - destinationWidth) / 2,
            (G13LcdFrame.Height - destinationHeight) / 2,
            destinationWidth,
            destinationHeight);
    }

    private static byte Luminance(ReadOnlySpan<byte> pixels, int stride, int x, int y)
    {
        var offset = (y * stride) + (x * 4);
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        var alpha = pixels[offset + 3];
        var compositedRed = CompositeOnWhite(red, alpha);
        var compositedGreen = CompositeOnWhite(green, alpha);
        var compositedBlue = CompositeOnWhite(blue, alpha);
        return (byte)((compositedRed * 299 + compositedGreen * 587 + compositedBlue * 114) / 1000);
    }

    private static int CompositeOnWhite(byte channel, byte alpha) =>
        ((channel * alpha) + (255 * (255 - alpha))) / 255;

    private static void ClearPixel(Span<byte> framebuffer, int x, int y)
    {
        var offset = (y / 8 * G13LcdFrame.Width) + x;
        framebuffer[offset] &= (byte)~(1 << (y & 7));
    }

    private static void ClearInvisibleTail(Span<byte> framebuffer)
    {
        for (var y = G13LcdFrame.Height; y < G13LcdFrame.BandCount * 8; y++)
        {
            for (var x = 0; x < G13LcdFrame.Width; x++)
            {
                ClearPixel(framebuffer, x, y);
            }
        }
    }

    private readonly record struct Rectangle(int X, int Y, int Width, int Height);
}
