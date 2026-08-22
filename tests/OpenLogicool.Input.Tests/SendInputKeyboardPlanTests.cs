using OpenLogicool.Contracts.Devices.Shared;
using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class SendInputKeyboardPlanTests
{
    private const uint KeyEventfExtendedKey = 0x0001;
    private const uint KeyEventfKeyUp = 0x0002;
    private const uint KeyEventfScanCode = 0x0008;

    [Theory]
    [InlineData("Key:A", 0x1E)]
    [InlineData("Key:Esc", 0x01)]
    [InlineData("Key:1", 0x02)]
    [InlineData("Key:Space", 0x39)]
    [InlineData("Key:F13", 0x64)]
    public void Down_carries_scancode_with_scancode_flag(string token, int expectedScan)
    {
        var plan = SendInputEmitter.BuildKeyboardPlan(OutputTokens.Parse(token), PhysicalInputEdge.Down);

        Assert.Equal((ushort)expectedScan, plan.ScanCode);
        Assert.Equal(KeyEventfScanCode, plan.Flags);
        Assert.Equal(OutputTokens.Parse(token).VirtualKey, plan.VirtualKey);
    }

    [Fact]
    public void Up_carries_keyup_and_scancode_flags()
    {
        var plan = SendInputEmitter.BuildKeyboardPlan(OutputTokens.Parse("Key:A"), PhysicalInputEdge.Up);

        Assert.Equal((ushort)0x1E, plan.ScanCode);
        Assert.Equal(KeyEventfKeyUp | KeyEventfScanCode, plan.Flags);
    }

    [Theory]
    [InlineData("Key:Up")]
    [InlineData("Key:Delete")]
    [InlineData("Key:RCtrl")]
    public void Extended_keys_keep_extended_flag_alongside_scancode(string token)
    {
        var plan = SendInputEmitter.BuildKeyboardPlan(OutputTokens.Parse(token), PhysicalInputEdge.Down);

        Assert.NotEqual(0, plan.ScanCode);
        Assert.Equal(KeyEventfExtendedKey | KeyEventfScanCode, plan.Flags);
    }

    [Fact]
    public void Vk_without_scancode_falls_back_to_virtual_key_only()
    {
        // 0xE8 は未割当 VK で MapVirtualKeyW が 0 を返す＝scancode が定義されない。
        var plan = SendInputEmitter.BuildKeyboardPlan(OutputTokens.Parse("Vk:0xE8"), PhysicalInputEdge.Down);

        Assert.Equal((ushort)0, plan.ScanCode);
        Assert.Equal(0u, plan.Flags);
        Assert.Equal((ushort)0xE8, plan.VirtualKey);
    }
}
