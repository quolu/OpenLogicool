using OpenLogicool.Contracts.Devices.Shared;
using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class OutputTokensTests
{
    [Theory]
    [InlineData("Key:A", 0x41, false)]
    [InlineData("Key:Z", 0x5A, false)]
    [InlineData("Key:0", 0x30, false)]
    [InlineData("Key:F1", 0x70, false)]
    [InlineData("Key:F13", 0x7C, false)]
    [InlineData("Key:F24", 0x87, false)]
    [InlineData("Key:LShift", 0xA0, false)]
    [InlineData("Key:RCtrl", 0xA3, true)]
    [InlineData("Key:Space", 0x20, false)]
    [InlineData("Key:Up", 0x26, true)]
    [InlineData("Key:Delete", 0x2E, true)]
    [InlineData("Vk:0x7C", 0x7C, false)]
    [InlineData("Vk:0xA5", 0xA5, true)]
    public void Key_tokens_resolve_to_virtual_keys(string token, int expectedVk, bool expectedExtended)
    {
        var resolved = OutputTokens.Parse(token);

        Assert.Equal(ResolvedOutputKind.Key, resolved.Kind);
        Assert.Equal((ushort)expectedVk, resolved.VirtualKey);
        Assert.Equal(expectedExtended, resolved.IsExtendedKey);
    }

    [Theory]
    [InlineData("Mouse:Left", MouseButton.Left)]
    [InlineData("Mouse:Right", MouseButton.Right)]
    [InlineData("Mouse:Middle", MouseButton.Middle)]
    [InlineData("Mouse:X1", MouseButton.X1)]
    [InlineData("Mouse:X2", MouseButton.X2)]
    public void Mouse_tokens_resolve_to_buttons(string token, MouseButton expected)
    {
        var resolved = OutputTokens.Parse(token);

        Assert.Equal(ResolvedOutputKind.MouseButton, resolved.Kind);
        Assert.Equal(expected, resolved.MouseButton);
    }

    [Theory]
    [InlineData("Key:NoSuchKey")]
    [InlineData("Pedal:1")]
    [InlineData("Key:")]
    [InlineData("KeyA")]
    [InlineData("Vk:0x00")]
    [InlineData("Vk:0xFF")]
    public void Unknown_tokens_are_rejected(string token)
    {
        Assert.ThrowsAny<ArgumentException>(() => OutputTokens.Parse(token));
    }

    [Fact]
    public void Mouse_token_with_unknown_button_is_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => OutputTokens.Parse("Mouse:Side"));
    }

    [Theory]
    [InlineData("Key:F13", PhysicalInputEdge.Down, "DOWN KEY 7C")]
    [InlineData("Key:F13", PhysicalInputEdge.Up, "UP KEY 7C")]
    [InlineData("Key:RCtrl", PhysicalInputEdge.Down, "DOWN KEY A3 EXT")]
    [InlineData("Mouse:Left", PhysicalInputEdge.Down, "DOWN MOUSE Left")]
    [InlineData("Mouse:X2", PhysicalInputEdge.Up, "UP MOUSE X2")]
    public void Watchdog_protocol_lines_encode_token_and_edge(string token, PhysicalInputEdge edge, string expected)
    {
        Assert.Equal(expected, WatchdogChannel.EncodeLine(token, edge));
    }
}
