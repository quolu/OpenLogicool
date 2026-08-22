using OpenLogicool.Devices.G600;
using Xunit;

namespace OpenLogicool.Devices.G600.Tests;

public sealed class G600OnboardImageTests
{
    private static byte[] CleanF3()
    {
        var report = new byte[G600SideRemap.ReportLength];
        for (var i = 0; i < report.Length; i++)
        {
            report[i] = (byte)(i & 0xFF);
        }

        report[0] = 0xF3;
        return report;
    }

    private static (byte MouseCode, byte Modifiers, byte HidKey) Cell(byte[] report, int button, bool shift)
    {
        var offset = (shift ? G600SideRemap.ShiftLayerBaseOffset : G600SideRemap.NormalLayerBaseOffset)
            + (button - 1) * G600SideRemap.BytesPerButton;
        return (report[offset], report[offset + 1], report[offset + 2]);
    }

    [Fact]
    public void Build_writes_cells_and_forces_g1_and_shift_selector()
    {
        var baseline = CleanF3();
        var cells = new[]
        {
            new G600OnboardCell(11, ShiftLayer: false, MouseCode: 0x00, Modifiers: 0x00, HidKey: 0x04), // G11 = A
            new G600OnboardCell(11, ShiftLayer: true, MouseCode: 0x00, Modifiers: 0x00, HidKey: 0x29),  // shift G11 = Esc
            new G600OnboardCell(12, ShiftLayer: false, MouseCode: 0x00, Modifiers: 0x01, HidKey: 0x06), // G12 = Ctrl+C
        };

        var payload = G600OnboardImage.Build(baseline, cells, shiftSelectorButton: 6);

        Assert.Equal((0x00, 0x00, 0x04), Cell(payload, 11, shift: false));
        Assert.Equal((0x00, 0x00, 0x29), Cell(payload, 11, shift: true));
        Assert.Equal((0x00, 0x01, 0x06), Cell(payload, 12, shift: false));
        Assert.Equal(((byte)G600OnboardImage.MouseCodeLeftClick, (byte)0x00, (byte)0x00), Cell(payload, 1, shift: false));
        Assert.Equal(((byte)G600OnboardImage.MouseCodeLeftClick, (byte)0x00, (byte)0x00), Cell(payload, 1, shift: true));
        Assert.Equal(((byte)G600OnboardImage.MouseCodeGShift, (byte)0x00, (byte)0x00), Cell(payload, 6, shift: false));
        Assert.Equal(((byte)G600OnboardImage.MouseCodeGShift, (byte)0x00, (byte)0x00), Cell(payload, 6, shift: true));
    }

    [Fact]
    public void Build_preserves_bytes_outside_button_cells_and_does_not_mutate_input()
    {
        var baseline = CleanF3();
        var original = baseline.ToArray();

        var payload = G600OnboardImage.Build(baseline, [], shiftSelectorButton: null);

        Assert.Equal(original, baseline); // 入力は非破壊
        // button map 以外（header 0〜30・層間 91〜93）は保持
        Assert.Equal(original[..G600SideRemap.NormalLayerBaseOffset], payload[..G600SideRemap.NormalLayerBaseOffset]);
        Assert.Equal(original[91..94], payload[91..94]);
        // G2〜G20（cell 指定なし）は baseline のまま
        Assert.Equal(Cell(original, 15, shift: false), Cell(payload, 15, shift: false));
    }

    [Fact]
    public void Build_rejects_cells_for_g1_or_selector_or_duplicates()
    {
        var baseline = CleanF3();

        Assert.Throws<ArgumentException>(() => G600OnboardImage.Build(
            baseline, [new G600OnboardCell(1, false, 0, 0, 0x04)], null));
        Assert.Throws<ArgumentException>(() => G600OnboardImage.Build(
            baseline, [new G600OnboardCell(6, false, 0, 0, 0x04)], shiftSelectorButton: 6));
        Assert.Throws<ArgumentException>(() => G600OnboardImage.Build(
            baseline,
            [new G600OnboardCell(9, false, 0, 0, 0x04), new G600OnboardCell(9, false, 0, 0, 0x05)],
            null));
        Assert.Throws<ArgumentException>(() => G600OnboardImage.Build(
            baseline, [], shiftSelectorButton: 1));
    }
}
