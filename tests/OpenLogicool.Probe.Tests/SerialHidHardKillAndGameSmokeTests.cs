using System.Diagnostics;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Input;
using OpenLogicool.Probe;
using Xunit;

namespace OpenLogicool.Probe.Tests;

public sealed class SerialHidHardKillAndGameSmokeTests
{
    [Fact]
    public void Hard_kill_release_uses_kill_request_as_budget_origin()
    {
        var frequency = Stopwatch.Frequency;
        var result = HardKillReleaseAnalysis.Analyze(
            downTicks: frequency,
            killRequestedTicks: 2 * frequency,
            killCompletedTicks: 2 * frequency + frequency / 100,
            releaseTicks: 2 * frequency + frequency / 10);

        Assert.True(result.ReleaseObserved);
        Assert.InRange(result.ReleaseLatencyMillisecondsFromKillRequest!.Value, 99, 101);
        Assert.InRange(result.ReleaseLatencyMillisecondsFromKillCompletion!.Value, 89, 91);
        Assert.True(result.Meets250MillisecondBudget);
    }

    [Fact]
    public void Hard_kill_release_rejects_release_before_kill()
    {
        var frequency = Stopwatch.Frequency;
        var result = HardKillReleaseAnalysis.Analyze(
            downTicks: frequency,
            killRequestedTicks: 3 * frequency,
            killCompletedTicks: 3 * frequency,
            releaseTicks: 2 * frequency);

        Assert.False(result.ReleaseObserved);
        Assert.False(result.Meets250MillisecondBudget);
    }

    [Fact]
    public void Game_observation_collapses_typematic_down_to_one_press()
    {
        var result = GameInputObservationAnalysis.Analyze(
        [
            Event("down"),
            Event("down"),
            Event("up"),
        ]);

        Assert.Equal(1, result.LogicalPressCount);
        Assert.Equal(0, result.WrongReleaseCount);
        Assert.False(result.Stuck);
        Assert.True(result.IsOneToOne);
    }

    [Fact]
    public void Game_observation_rejects_two_physical_press_cycles()
    {
        var result = GameInputObservationAnalysis.Analyze(
        [
            Event("down"), Event("up"),
            Event("down"), Event("up"),
        ]);

        Assert.Equal(2, result.LogicalPressCount);
        Assert.False(result.IsOneToOne);
    }

    [Fact]
    public void Game_trace_accepts_one_acked_g1_escape_pair()
    {
        var result = GameTraceObservationAnalysis.Analyze(Snapshot(
            Trace(PhysicalInputEdge.Down, true, "Key:Esc", 1),
            Trace(PhysicalInputEdge.Up, true, "Key:Esc", 2)));

        Assert.Equal(1, result.LogicalPressCount);
        Assert.False(result.Stuck);
        Assert.True(result.IsOneToOne);
    }

    [Theory]
    [InlineData(false, "Key:Esc")]
    [InlineData(true, "Key:F13")]
    public void Game_trace_rejects_unacked_or_wrong_output(bool emitted, string token)
    {
        var result = GameTraceObservationAnalysis.Analyze(Snapshot(
            Trace(PhysicalInputEdge.Down, emitted, token, 1),
            Trace(PhysicalInputEdge.Up, emitted, token, 2)));

        Assert.False(result.IsOneToOne);
    }

    [Fact]
    public void Game_trace_rejects_host_fault()
    {
        var snapshot = new ChildHostDiagnosticSnapshot
        {
            CapturedAtUtc = "2026-08-23T00:00:00.0000000Z",
            PumpProcessedCount = 2,
            PumpIsRunning = false,
            HostFailure = "fault",
            DroppedG13InputCount = 0,
            TraceEntries =
            [
                Trace(PhysicalInputEdge.Down, true, "Key:Esc", 1),
                Trace(PhysicalInputEdge.Up, true, "Key:Esc", 2),
            ],
        };

        Assert.False(GameTraceObservationAnalysis.Analyze(snapshot).IsOneToOne);
    }

    private static ObservedHidEvent Event(string edge) =>
        new("key", edge, 0x1B, false, 0);

    private static ChildHostDiagnosticSnapshot Snapshot(params InputTraceEntry[] entries) => new()
    {
        CapturedAtUtc = "2026-08-23T00:00:00.0000000Z",
        PumpProcessedCount = entries.Length,
        PumpIsRunning = true,
        DroppedG13InputCount = 0,
        TraceEntries = entries,
    };

    private static InputTraceEntry Trace(
        PhysicalInputEdge edge,
        bool emitted,
        string token,
        long sequence) => new(
            "g13-instance",
            "G1",
            edge,
            "base",
            [token],
            emitted,
            sequence,
            sequence + 0.1,
            0.1,
            sequence);
}
