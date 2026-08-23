using OpenLogicool.Contracts.Devices.G13;

namespace OpenLogicool.Devices.G13;

/// <summary>G13の160×43 monochrome LCDに渡すframebufferとwire reportを組み立てるpure実装。</summary>
public static class G13LcdFrame
{
    public const int Width = G13LcdContract.Width;
    public const int Height = G13LcdContract.Height;
    public const int BytesPerBand = Width;
    public const int BandCount = G13LcdContract.BandCount;
    public const int FramebufferLength = G13LcdContract.FramebufferLength;
    public const int HeaderLength = 32;
    public const int ReportLength = HeaderLength + FramebufferLength;
    public const byte ReportId = 0x03;

    public static byte[] CreateIdentificationPattern()
    {
        var framebuffer = new byte[FramebufferLength];

        for (var x = 0; x < Width; x++)
        {
            SetPixel(framebuffer, x, 0);
            SetPixel(framebuffer, x, Height - 1);
        }

        for (var y = 0; y < Height; y++)
        {
            SetPixel(framebuffer, 0, y);
            SetPixel(framebuffer, Width - 1, y);

            var diagonalX = y * (Width - 1) / (Height - 1);
            SetPixel(framebuffer, diagonalX, y);
            SetPixel(framebuffer, Width - 1 - diagonalX, y);
            SetPixel(framebuffer, Width / 2, y);
        }

        for (var x = 0; x < Width; x++)
        {
            SetPixel(framebuffer, x, Height / 2);
        }

        return framebuffer;
    }

    public static byte[] BuildWireReport(ReadOnlySpan<byte> framebuffer)
    {
        if (framebuffer.Length != FramebufferLength)
        {
            throw new ArgumentException(
                $"G13 LCD framebufferは{FramebufferLength} bytesでなければなりません。実際: {framebuffer.Length}",
                nameof(framebuffer));
        }

        var report = new byte[ReportLength];
        report[0] = ReportId;
        framebuffer.CopyTo(report.AsSpan(HeaderLength));
        return report;
    }

    public static void SetPixel(Span<byte> framebuffer, int x, int y)
    {
        if (framebuffer.Length != FramebufferLength)
        {
            throw new ArgumentException(
                $"G13 LCD framebufferは{FramebufferLength} bytesでなければなりません。実際: {framebuffer.Length}",
                nameof(framebuffer));
        }

        if ((uint)x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        var offset = (y / 8) * BytesPerBand + x;
        framebuffer[offset] |= (byte)(1 << (y & 7));
    }
}
