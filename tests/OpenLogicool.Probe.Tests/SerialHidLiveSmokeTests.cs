using System.Diagnostics;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Input;
using OpenLogicool.Probe;
using Xunit;

namespace OpenLogicool.Probe.Tests;

public sealed class SerialHidLiveSmokeTests
{
    [Fact]
    public void Latency_summary_uses_nearest_rank_percentiles_and_records_maximum()
    {
        var summary = LatencySummary.From(Enumerable.Range(1, 100).Select(value => (double)value));

        Assert.Equal(100, summary.Count);
        Assert.Equal(50, summary.P50Milliseconds);
        Assert.Equal(95, summary.P95Milliseconds);
        Assert.Equal(99, summary.P99Milliseconds);
        Assert.Equal(100, summary.MaximumMilliseconds);
    }

    [Fact]
    public void Event_balance_reports_wrong_release_and_stuck_outputs_separately()
    {
        var result = EventBalance.Analyze(
        [
            Event("key", "up", 0x70),
            Event("key", "down", 0x71),
            Event("mouse", "down", 0x04),
            Event("mouse", "up", 0x04),
        ]);

        Assert.Equal(1, result.WrongReleaseCount);
        Assert.Equal(["key:113=1"], result.StuckOutputs);
    }

    [Fact]
    public void Event_balance_treats_keyboard_typematic_down_as_one_held_key()
    {
        var result = EventBalance.Analyze(
        [
            Event("key", "down", 0x86),
            Event("key", "down", 0x86),
            Event("key", "down", 0x86),
            Event("key", "up", 0x86),
        ]);

        Assert.Equal(0, result.WrongReleaseCount);
        Assert.Empty(result.StuckOutputs);
    }

    [Fact]
    public void Unexpected_events_allow_expected_key_repeat_but_keep_other_code()
    {
        var actual = new[]
        {
            Event("key", "down", 0x82),
            Event("key", "down", 0x86),
            Event("key", "down", 0x86),
            Event("key", "down", 0x81),
            Event("key", "up", 0x82),
            Event("key", "up", 0x86),
        };
        IReadOnlyList<IReadOnlyList<ObservedHidEvent>> expected =
        [
            [Event("key", "down", 0x82), Event("key", "down", 0x86)],
            [Event("key", "up", 0x82), Event("key", "up", 0x86)],
        ];

        var unexpected = LiveActionObservation.UnexpectedEvents(actual, expected);

        Assert.Collection(unexpected, item => Assert.Equal(0x81, item.Code));
    }

    [Fact]
    public void Action_window_excludes_operator_input_but_keeps_nearby_legacy_leak()
    {
        var second = Stopwatch.Frequency;
        var actual = new[]
        {
            Event("key", "down", 0x4F, second / 10),
            Event("key", "up", 0x4F, second / 5),
            Event("key", "down", 0x80, second / 2),
            Event("key", "up", 0x80, second / 2 + second / 20),
            Event("key", "down", 0x7C, 2 * second - second / 200),
            Event("key", "down", 0x80, 2 * second),
            Event("key", "up", 0x7C, 2 * second + second / 20),
            Event("key", "up", 0x80, 2 * second + second / 10),
        };

        var window = LiveActionObservation.AroundExpected(
            actual,
            [[Event("key", "down", 0x80)], [Event("key", "up", 0x80)]],
            TimeSpan.FromMilliseconds(100));

        Assert.Equal([0x7C, 0x80, 0x7C, 0x80], window.Select(item => item.Code).ToArray());
    }

    [Fact]
    public void Expected_trace_excludes_unrelated_mouse_clicks_during_user_wait()
    {
        var entries = new[]
        {
            Trace("g600", "G1", "normal"),
            Trace("g600", "G9", "normal"),
            Trace("g13", "G1", "base"),
        };

        var relevant = LiveActionObservation.ExpectedTrace(entries, [("g600", "G9", "normal")]);

        Assert.Collection(relevant, entry => Assert.Equal("G9", entry.ControlId));
    }

    private static ObservedHidEvent Event(string kind, string edge, int code, long ticks = 0) =>
        new(kind, edge, code, false, ticks);

    private static InputTraceEntry Trace(string device, string control, string layer) =>
        new(device, control, PhysicalInputEdge.Down, layer, [], false, 0, 0, 0, 0);
}
