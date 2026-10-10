using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G13;
using Xunit;

namespace OpenLogicool.Devices.G13.Tests;

public sealed class G13StickDirectionTrackerTests
{
    private const byte NeutralX = 143;
    private const byte NeutralY = 120;

    [Fact]
    public void Resting_at_the_measured_neutral_produces_no_edges()
    {
        var tracker = new G13StickDirectionTracker();

        Assert.Empty(Feed(tracker, NeutralX, NeutralY));
        Assert.Empty(Feed(tracker, 128, 128));
    }

    [Theory]
    [InlineData(0, NeutralY, G13Controls.StickLeft)]
    [InlineData(255, NeutralY, G13Controls.StickRight)]
    [InlineData(NeutralX, 0, G13Controls.StickUp)]
    [InlineData(NeutralX, 255, G13Controls.StickDown)]
    public void Full_tilt_presses_one_direction_and_returning_to_neutral_releases_it(byte x, byte y, string controlId)
    {
        var tracker = new G13StickDirectionTracker();

        Assert.Equal([(controlId, PhysicalInputEdge.Down)], Feed(tracker, x, y));
        Assert.Empty(Feed(tracker, x, y));
        Assert.Equal([(controlId, PhysicalInputEdge.Up)], Feed(tracker, NeutralX, NeutralY));
    }

    [Fact]
    public void Press_and_release_points_are_apart_so_wobble_at_the_edge_does_not_repeat_the_press()
    {
        var tracker = new G13StickDirectionTracker();

        Assert.Empty(Feed(tracker, 64, NeutralY));
        Assert.Equal([(G13Controls.StickLeft, PhysicalInputEdge.Down)], Feed(tracker, 63, NeutralY));
        Assert.Empty(Feed(tracker, 70, NeutralY));
        Assert.Empty(Feed(tracker, 60, NeutralY));
        Assert.Empty(Feed(tracker, 79, NeutralY));
        Assert.Equal([(G13Controls.StickLeft, PhysicalInputEdge.Up)], Feed(tracker, 80, NeutralY));

        Assert.Empty(Feed(tracker, NeutralX, 191));
        Assert.Equal([(G13Controls.StickDown, PhysicalInputEdge.Down)], Feed(tracker, NeutralX, 192));
        Assert.Empty(Feed(tracker, NeutralX, 176));
        Assert.Equal([(G13Controls.StickDown, PhysicalInputEdge.Up)], Feed(tracker, NeutralX, 175));
    }

    [Fact]
    public void Diagonal_holds_two_directions_and_each_axis_releases_on_its_own()
    {
        var tracker = new G13StickDirectionTracker();

        Assert.Equal(
            [(G13Controls.StickRight, PhysicalInputEdge.Down), (G13Controls.StickUp, PhysicalInputEdge.Down)],
            Feed(tracker, 250, 5));
        Assert.Equal([(G13Controls.StickUp, PhysicalInputEdge.Up)], Feed(tracker, 250, NeutralY));
        Assert.Equal([(G13Controls.StickRight, PhysicalInputEdge.Up)], Feed(tracker, NeutralX, NeutralY));
    }

    [Fact]
    public void Flicking_straight_to_the_opposite_side_releases_before_pressing()
    {
        var tracker = new G13StickDirectionTracker();
        Feed(tracker, 0, NeutralY);

        Assert.Equal(
            [(G13Controls.StickLeft, PhysicalInputEdge.Up), (G13Controls.StickRight, PhysicalInputEdge.Down)],
            Feed(tracker, 255, NeutralY));
    }

    private static List<(string ControlId, PhysicalInputEdge Edge)> Feed(G13StickDirectionTracker tracker, byte x, byte y)
    {
        var edges = new List<(string ControlId, PhysicalInputEdge Edge)>();
        tracker.Feed(x, y, edges);
        return edges;
    }
}
