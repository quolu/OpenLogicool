using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>
/// low-level hookのmessage→生入力edge翻訳（t02）。実foregroundを要さない純関数の検証。
/// </summary>
public sealed class WindowsDemonstrationInputEdgeFactoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddHours(1);

    [Theory]
    [InlineData(WindowsDemonstrationInputEdgeFactory.WmKeyDown, DemonstrationInputEdgeKind.KeyDown)]
    [InlineData(WindowsDemonstrationInputEdgeFactory.WmSysKeyDown, DemonstrationInputEdgeKind.KeyDown)]
    [InlineData(WindowsDemonstrationInputEdgeFactory.WmKeyUp, DemonstrationInputEdgeKind.KeyUp)]
    [InlineData(WindowsDemonstrationInputEdgeFactory.WmSysKeyUp, DemonstrationInputEdgeKind.KeyUp)]
    public void Key_messages_become_key_edges_without_a_pointer_position(int message, DemonstrationInputEdgeKind kind)
    {
        var edge = WindowsDemonstrationInputEdgeFactory.FromKeyboardMessage(message, 0x1B, 0, 1234, Now);

        Assert.NotNull(edge);
        Assert.Equal(kind, edge.Kind);
        Assert.Equal(DemonstrationInputSource.Keyboard, edge.Source);
        Assert.Equal("Key:Esc", edge.OutputToken);
        Assert.Null(edge.ScreenPoint);
        Assert.Equal(1234, edge.MonotonicMs);
        Assert.Equal(Now, edge.OccurredUtc);
    }

    [Fact]
    public void A_key_outside_the_name_table_keeps_its_raw_virtual_key_instead_of_being_rounded()
    {
        var edge = WindowsDemonstrationInputEdgeFactory.FromKeyboardMessage(
            WindowsDemonstrationInputEdgeFactory.WmKeyDown, 0xF0, 0, 0, Now);

        Assert.Equal("Vk:0xF0", edge!.OutputToken);
    }

    [Fact]
    public void The_extended_flag_takes_part_in_the_key_name_lookup()
    {
        // RCtrl は拡張 flag 付きでだけ名前を持つ。flag を捨てると別 key の名前へ丸まる。
        Assert.Equal("Key:RCtrl", WindowsDemonstrationInputEdgeFactory.KeyToken(0xA3, isExtendedKey: true));
        Assert.Equal("Vk:0xA3", WindowsDemonstrationInputEdgeFactory.KeyToken(0xA3, isExtendedKey: false));
        Assert.Equal("Key:LCtrl", WindowsDemonstrationInputEdgeFactory.KeyToken(0xA2, isExtendedKey: false));
    }

    [Fact]
    public void Messages_that_are_not_key_edges_are_not_recorded()
    {
        Assert.Null(WindowsDemonstrationInputEdgeFactory.FromKeyboardMessage(0x0000, 0x1B, 0, 0, Now));
    }

    [Theory]
    [InlineData(WindowsDemonstrationInputEdgeFactory.WmLButtonDown, DemonstrationInputEdgeKind.PointerDown, "Mouse:Left")]
    [InlineData(WindowsDemonstrationInputEdgeFactory.WmLButtonUp, DemonstrationInputEdgeKind.PointerUp, "Mouse:Left")]
    [InlineData(WindowsDemonstrationInputEdgeFactory.WmRButtonDown, DemonstrationInputEdgeKind.PointerDown, "Mouse:Right")]
    [InlineData(WindowsDemonstrationInputEdgeFactory.WmRButtonUp, DemonstrationInputEdgeKind.PointerUp, "Mouse:Right")]
    [InlineData(WindowsDemonstrationInputEdgeFactory.WmMButtonDown, DemonstrationInputEdgeKind.PointerDown, "Mouse:Middle")]
    [InlineData(WindowsDemonstrationInputEdgeFactory.WmMButtonUp, DemonstrationInputEdgeKind.PointerUp, "Mouse:Middle")]
    public void Mouse_button_messages_carry_the_screen_point_of_the_event(
        int message,
        DemonstrationInputEdgeKind kind,
        string token)
    {
        var edge = WindowsDemonstrationInputEdgeFactory.FromMouseMessage(message, 0, 640, 480, 99, Now);

        Assert.NotNull(edge);
        Assert.Equal(kind, edge.Kind);
        Assert.Equal(DemonstrationInputSource.Mouse, edge.Source);
        Assert.Equal(token, edge.OutputToken);
        Assert.Equal(640, edge.ScreenPoint!.X);
        Assert.Equal(480, edge.ScreenPoint.Y);
        Assert.Equal(0, edge.WheelVerticalSteps);
        Assert.Equal(0, edge.WheelHorizontalSteps);
    }

    [Theory]
    [InlineData(1u, "Mouse:X1")]
    [InlineData(2u, "Mouse:X2")]
    public void The_x_button_number_comes_from_the_high_word_of_mouse_data(uint button, string token)
    {
        var edge = WindowsDemonstrationInputEdgeFactory.FromMouseMessage(
            WindowsDemonstrationInputEdgeFactory.WmXButtonDown, button << 16, 10, 20, 0, Now);

        Assert.Equal(token, edge!.OutputToken);
    }

    [Theory]
    [InlineData(120, 1, 0)]
    [InlineData(240, 2, 0)]
    [InlineData(-360, -3, 0)]
    public void Vertical_wheel_deltas_become_step_counts(int delta, int expectedVertical, int expectedHorizontal)
    {
        var edge = WindowsDemonstrationInputEdgeFactory.FromMouseMessage(
            WindowsDemonstrationInputEdgeFactory.WmMouseWheel,
            (uint)(delta << 16),
            10,
            20,
            0,
            Now);

        Assert.Equal(DemonstrationInputEdgeKind.Wheel, edge!.Kind);
        Assert.Equal(expectedVertical, edge.WheelVerticalSteps);
        Assert.Equal(expectedHorizontal, edge.WheelHorizontalSteps);
    }

    [Fact]
    public void A_horizontal_wheel_is_recorded_on_its_own_axis()
    {
        var edge = WindowsDemonstrationInputEdgeFactory.FromMouseMessage(
            WindowsDemonstrationInputEdgeFactory.WmMouseHWheel, unchecked((uint)(-120 << 16)), 10, 20, 0, Now);

        Assert.Equal(0, edge!.WheelVerticalSteps);
        Assert.Equal(-1, edge.WheelHorizontalSteps);
    }

    [Fact]
    public void A_wheel_delta_smaller_than_one_step_is_not_recorded_as_an_operation()
    {
        Assert.Null(WindowsDemonstrationInputEdgeFactory.FromMouseMessage(
            WindowsDemonstrationInputEdgeFactory.WmMouseWheel, 60u << 16, 10, 20, 0, Now));
    }

    [Fact]
    public void Mouse_movement_alone_is_not_an_operation()
    {
        Assert.Null(WindowsDemonstrationInputEdgeFactory.FromMouseMessage(0x0200, 0, 10, 20, 0, Now));
    }
}
