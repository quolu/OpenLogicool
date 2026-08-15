using OpenLogicool.Contracts.Devices.G600;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G600;
using Xunit;

namespace OpenLogicool.Devices.G600.Tests;

/// <summary>
/// pure parser の focused test。bit 対応の正は docs/probes/g600-input-map-2026-08-15.md。
/// </summary>
public sealed class G600ReportParserTests
{
    public static TheoryData<int, int, string> ButtonBitCases()
    {
        var data = new TheoryData<int, int, string>();
        for (var g = 1; g <= 8; g++)
        {
            data.Add(1, g - 1, $"G{g}");
        }

        for (var g = 9; g <= 16; g++)
        {
            data.Add(2, g - 9, $"G{g}");
        }

        for (var g = 17; g <= 20; g++)
        {
            data.Add(3, g - 17, $"G{g}");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ButtonBitCases))]
    public void Each_button_bit_maps_to_its_g_number(int byteIndex, int bit, string controlId)
    {
        var report = G600ReportParser.IdleReport();
        report[byteIndex] = (byte)(1 << bit);

        var edges = new List<(string ControlId, PhysicalInputEdge Edge)>();
        G600ReportParser.Diff(G600ReportParser.IdleReport(), report, edges);

        Assert.Equal([(controlId, PhysicalInputEdge.Down)], edges);
    }

    [Fact]
    public void Diff_covers_all_twenty_controls()
    {
        var allDown = G600ReportParser.IdleReport();
        allDown[1] = 0xFF;
        allDown[2] = 0xFF;
        allDown[3] = 0x0F;

        var edges = new List<(string ControlId, PhysicalInputEdge Edge)>();
        G600ReportParser.Diff(G600ReportParser.IdleReport(), allDown, edges);

        Assert.Equal(G600Controls.Buttons, edges.Select(edge => edge.ControlId));
        Assert.All(edges, edge => Assert.Equal(PhysicalInputEdge.Down, edge.Edge));
    }

    [Fact]
    public void Wheel_byte_is_read_as_signed()
    {
        var up = G600ReportParser.IdleReport();
        up[4] = 0x01;
        Assert.Equal(1, G600ReportParser.ReadWheelByte(up));

        var down = G600ReportParser.IdleReport();
        down[4] = 0xFF;
        Assert.Equal(-1, G600ReportParser.ReadWheelByte(down));
    }

    [Fact]
    public void Wrong_length_or_report_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => G600ReportParser.ValidateReport(new byte[16]));

        var wrongId = G600ReportParser.IdleReport();
        wrongId[0] = 0x01;
        Assert.Throws<ArgumentException>(() => G600ReportParser.ValidateReport(wrongId));
    }
}

/// <summary>
/// stream のホイール判定（byte4 latch 実測挙動）の focused test。
/// </summary>
public sealed class G600ReportStreamTests
{
    [Fact]
    public void Button_report_with_latched_wheel_byte_emits_no_wheel_tick()
    {
        var stream = new G600ReportStream("test-device");
        var inputs = new List<PhysicalInput>();

        // ホイール下1ノッチ → byte4=0xFF が latch される
        var wheelDown = G600ReportParser.IdleReport();
        wheelDown[4] = 0xFF;
        stream.Feed(wheelDown, 1.0, inputs, out var tick1);
        Assert.NotNull(tick1);
        Assert.Equal(-1, tick1.Delta);
        Assert.Empty(inputs);

        // G1 押下 report は latch 残存の 0xFF を保持するが、回転 event ではない
        var g1Down = G600ReportParser.IdleReport();
        g1Down[1] = 0x01;
        g1Down[4] = 0xFF;
        stream.Feed(g1Down, 2.0, inputs, out var tick2);
        Assert.Null(tick2);
        Assert.Equal([("G1", PhysicalInputEdge.Down)], inputs.Select(input => (input.ControlId, input.Edge)));
    }

    [Fact]
    public void Identical_consecutive_wheel_reports_are_one_tick_each()
    {
        var stream = new G600ReportStream("test-device");
        var inputs = new List<PhysicalInput>();
        var wheelUp = G600ReportParser.IdleReport();
        wheelUp[4] = 0x01;

        var ticks = new List<G600WheelTick>();
        for (var i = 0; i < 3; i++)
        {
            stream.Feed(wheelUp, i, inputs, out var tick);
            Assert.NotNull(tick);
            ticks.Add(tick);
        }

        Assert.Empty(inputs);
        Assert.Equal([1, 1, 1], ticks.Select(tick => tick.Delta));
        Assert.Equal([1L, 2L, 3L], ticks.Select(tick => tick.ReportSequence));
    }
}
