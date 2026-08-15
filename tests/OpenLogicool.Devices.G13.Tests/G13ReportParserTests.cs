using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G13;
using Xunit;

namespace OpenLogicool.Devices.G13.Tests;

public sealed class G13ReportParserTests
{
    private static byte[] Report(byte b3 = 0, byte b4 = 0, byte b5 = 0, byte b6 = 0, byte b7 = 0, byte x = 143, byte y = 120) =>
        [G13DeviceIdentity.InputReportId, x, y, b3, b4, b5, b6, b7];

    private static List<(string ControlId, PhysicalInputEdge Edge)> Diff(byte[] previous, byte[] current)
    {
        var edges = new List<(string, PhysicalInputEdge)>();
        G13ReportParser.Diff(previous, current, edges);
        return edges;
    }

    [Fact]
    public void Single_button_press_and_release_produce_one_edge_each()
    {
        var idle = G13ReportParser.IdleReport();
        var g20Down = Report(b5: 0b0000_1000);

        Assert.Equal([("G20", PhysicalInputEdge.Down)], Diff(idle, g20Down));
        Assert.Equal([("G20", PhysicalInputEdge.Up)], Diff(g20Down, idle));
    }

    [Fact]
    public void Chord_bits_are_independent_within_one_report()
    {
        var idle = G13ReportParser.IdleReport();
        var chord = Report(b3: 0b0000_0011);

        Assert.Equal(
            [("G1", PhysicalInputEdge.Down), ("G2", PhysicalInputEdge.Down)],
            Diff(idle, chord));
    }

    [Fact]
    public void Unmapped_and_jitter_bits_produce_no_edges()
    {
        // byte5 bit6-7（未確認・idle bit）、byte7 bit1-2/4-6（未確認）、byte7 bit7（jitter）
        var idle = G13ReportParser.IdleReport();
        var noise = Report(b5: 0b1100_0000, b7: 0b1111_0110);

        Assert.Empty(Diff(idle, noise));
    }

    [Fact]
    public void Stick_movement_alone_produces_no_edges_and_reads_raw_values()
    {
        var idle = G13ReportParser.IdleReport();
        var moved = Report(x: 0x0B, y: 0x31);

        Assert.Empty(Diff(idle, moved));
        Assert.Equal(((byte)0x0B, (byte)0x31), G13ReportParser.ReadStick(moved));
    }

    [Fact]
    public void Every_mapped_control_id_is_in_the_contract_catalog()
    {
        var idle = G13ReportParser.IdleReport();
        var allBits = Report(b3: 0xFF, b4: 0xFF, b5: 0xFF, b6: 0xFF, b7: 0xFF);

        var edges = Diff(idle, allBits);
        Assert.Equal(G13Controls.Buttons.Count, edges.Count);
        Assert.All(edges, edge => Assert.Contains(edge.ControlId, G13Controls.Buttons));
    }

    [Fact]
    public void Wrong_length_or_report_id_is_rejected()
    {
        var idle = G13ReportParser.IdleReport();
        var edges = new List<(string, PhysicalInputEdge)>();

        Assert.Throws<ArgumentException>(() => G13ReportParser.Diff(idle, new byte[7], edges));
        Assert.Throws<ArgumentException>(() => G13ReportParser.Diff(idle, [0x02, 0, 0, 0, 0, 0, 0, 0], edges));
    }
}
