using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class KeyboardHidUsageTests
{
    [Theory]
    [InlineData("Key:A", 0x04)]
    [InlineData("Key:Z", 0x1D)]
    [InlineData("Key:1", 0x1E)]
    [InlineData("Key:0", 0x27)]
    [InlineData("Key:Esc", 0x29)]
    [InlineData("Key:Space", 0x2C)]
    [InlineData("Key:F1", 0x3A)]
    [InlineData("Key:F12", 0x45)]
    [InlineData("Key:F13", 0x68)]
    [InlineData("Key:F24", 0x73)]
    [InlineData("Key:Up", 0x52)]
    [InlineData("Key:Delete", 0x4C)]
    public void Known_keys_map_to_hid_usages(string token, int expectedUsage)
    {
        Assert.True(KeyboardHidUsage.TryGetUsage(OutputTokens.Parse(token).VirtualKey, out var usage));
        Assert.Equal((byte)expectedUsage, usage);
    }

    [Theory]
    [InlineData("Key:LCtrl", 0x01)]
    [InlineData("Key:LShift", 0x02)]
    [InlineData("Key:LAlt", 0x04)]
    [InlineData("Key:LWin", 0x08)]
    [InlineData("Key:RCtrl", 0x10)]
    [InlineData("Key:RShift", 0x20)]
    [InlineData("Key:RAlt", 0x40)]
    [InlineData("Key:RWin", 0x80)]
    public void Modifiers_map_to_hid_modifier_bits(string token, int expectedBit)
    {
        Assert.True(KeyboardHidUsage.TryGetModifierBit(OutputTokens.Parse(token).VirtualKey, out var bit));
        Assert.Equal((byte)expectedBit, bit);
    }

    [Fact]
    public void Unknown_virtual_keys_are_not_converted()
    {
        Assert.False(KeyboardHidUsage.TryGetUsage(0xE8, out _));
        Assert.False(KeyboardHidUsage.TryGetModifierBit(0x41, out _)); // 'A' は modifier ではない
        Assert.False(KeyboardHidUsage.TryGetUsage(0xA0, out _)); // LShift は modifier 表のみ
    }
}
