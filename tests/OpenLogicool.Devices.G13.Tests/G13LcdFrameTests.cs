using OpenLogicool.Devices.G13;
using Xunit;

namespace OpenLogicool.Devices.G13.Tests;

public sealed class G13LcdFrameTests
{
    [Fact]
    public void Wire_shape_matches_the_observed_G13_protocol()
    {
        Assert.Equal(160, G13LcdFrame.Width);
        Assert.Equal(43, G13LcdFrame.Height);
        Assert.Equal(960, G13LcdFrame.FramebufferLength);
        Assert.Equal(32, G13LcdFrame.HeaderLength);
        Assert.Equal(992, G13LcdFrame.ReportLength);
        Assert.Equal(0x03, G13LcdFrame.ReportId);
    }

    [Theory]
    [InlineData(0, 0, 0, 0x01)]
    [InlineData(159, 7, 159, 0x80)]
    [InlineData(0, 8, 160, 0x01)]
    [InlineData(159, 42, 959, 0x04)]
    public void Pixel_layout_is_column_first_inside_eight_row_bands(int x, int y, int expectedOffset, byte expectedMask)
    {
        var framebuffer = new byte[G13LcdFrame.FramebufferLength];

        G13LcdFrame.SetPixel(framebuffer, x, y);

        Assert.Equal(expectedMask, framebuffer[expectedOffset]);
        Assert.Equal(1, framebuffer.Count(value => value != 0));
    }

    [Fact]
    public void Wire_report_contains_zeroed_header_and_framebuffer_at_offset_32()
    {
        var framebuffer = Enumerable.Range(0, G13LcdFrame.FramebufferLength)
            .Select(index => (byte)(index & 0xFF))
            .ToArray();

        var report = G13LcdFrame.BuildWireReport(framebuffer);

        Assert.Equal(G13LcdFrame.ReportId, report[0]);
        Assert.All(report[1..G13LcdFrame.HeaderLength], value => Assert.Equal(0, value));
        Assert.Equal(framebuffer, report[G13LcdFrame.HeaderLength..]);
    }

    [Fact]
    public void Identification_pattern_has_border_diagonals_and_center_cross()
    {
        var framebuffer = G13LcdFrame.CreateIdentificationPattern();

        Assert.True(IsSet(framebuffer, 0, 0));
        Assert.True(IsSet(framebuffer, 159, 42));
        Assert.True(IsSet(framebuffer, 80, 21));
        Assert.True(IsSet(framebuffer, 80, 10));
        Assert.True(IsSet(framebuffer, 20, 21));
        Assert.False(IsSet(framebuffer, 20, 10));
    }

    [Fact]
    public void Wrong_framebuffer_length_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => G13LcdFrame.BuildWireReport(new byte[959]));
    }

    private static bool IsSet(byte[] framebuffer, int x, int y)
    {
        var offset = (y / 8) * G13LcdFrame.Width + x;
        return (framebuffer[offset] & (1 << (y & 7))) != 0;
    }
}
