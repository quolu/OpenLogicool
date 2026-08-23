using OpenLogicool.Devices.G13;
using Xunit;

namespace OpenLogicool.Devices.G13.Tests;

public sealed class G13LcdBitmapConverterTests
{
    [Fact]
    public void Stretch_maps_black_and_white_pixels_across_the_full_lcd()
    {
        var pixels = Bgra(
            (0, 0, 0, 255), (255, 255, 255, 255),
            (255, 255, 255, 255), (0, 0, 0, 255));

        var result = G13LcdBitmapConverter.ConvertBgra32(
            pixels,
            sourceWidth: 2,
            sourceHeight: 2,
            stride: 8,
            new G13LcdBitmapConversionOptions(AutoCrop: false));

        Assert.Equal((0, 0, 160, 43),
            (result.DestinationX, result.DestinationY, result.DestinationWidth, result.DestinationHeight));
        Assert.False(IsSet(result.Framebuffer, 0, 0));
        Assert.True(IsSet(result.Framebuffer, 159, 0));
        Assert.True(IsSet(result.Framebuffer, 0, 42));
        Assert.False(IsSet(result.Framebuffer, 159, 42));
    }

    [Fact]
    public void Auto_crop_finds_dark_content_and_preserves_a_small_margin()
    {
        var pixels = Enumerable.Repeat((byte)255, 10 * 10 * 4).ToArray();
        SetBgra(pixels, 10, 4, 5, 0, 0, 0, 255);

        var result = G13LcdBitmapConverter.ConvertBgra32(pixels, 10, 10, 40);

        Assert.Equal((2, 3, 5, 5),
            (result.SourceCropX, result.SourceCropY, result.SourceCropWidth, result.SourceCropHeight));
    }

    [Fact]
    public void Contain_centers_the_image_and_keeps_white_letterbox()
    {
        var pixels = Bgra((0, 0, 0, 255), (0, 0, 0, 255));

        var result = G13LcdBitmapConverter.ConvertBgra32(
            pixels,
            2,
            1,
            8,
            new G13LcdBitmapConversionOptions(Fit: G13LcdBitmapFit.Contain, AutoCrop: false));

        Assert.Equal(86, result.DestinationWidth);
        Assert.Equal(43, result.DestinationHeight);
        Assert.Equal(37, result.DestinationX);
        Assert.Equal(0, result.DestinationY);
        Assert.True(IsSet(result.Framebuffer, 0, 0));
        Assert.False(IsSet(result.Framebuffer, 37, 0));
        Assert.True(IsSet(result.Framebuffer, 159, 42));
    }

    [Fact]
    public void Transparent_pixels_are_composited_on_white()
    {
        var result = G13LcdBitmapConverter.ConvertBgra32(
            Bgra((0, 0, 0, 0)),
            1,
            1,
            4,
            new G13LcdBitmapConversionOptions(AutoCrop: false));

        Assert.True(IsSet(result.Framebuffer, 0, 0));
    }

    private static byte[] Bgra(params (byte R, byte G, byte B, byte A)[] pixels)
    {
        var result = new byte[pixels.Length * 4];
        for (var index = 0; index < pixels.Length; index++)
        {
            SetBgra(result, pixels.Length, index, 0, pixels[index].R, pixels[index].G, pixels[index].B, pixels[index].A);
        }

        return result;
    }

    private static void SetBgra(
        byte[] buffer,
        int width,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        var offset = ((y * width) + x) * 4;
        buffer[offset] = blue;
        buffer[offset + 1] = green;
        buffer[offset + 2] = red;
        buffer[offset + 3] = alpha;
    }

    private static bool IsSet(byte[] framebuffer, int x, int y)
    {
        var offset = (y / 8 * G13LcdFrame.Width) + x;
        return (framebuffer[offset] & (1 << (y & 7))) != 0;
    }
}
