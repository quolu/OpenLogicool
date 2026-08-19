using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Domain.Tests;

public sealed class RunControlStateTests
{
    [Fact]
    public void Start_runs_and_allows_neither_step_nor_switch()
    {
        var state = RunControlState.Start();

        Assert.Equal(RunControlPhase.Running, state.Phase);
        Assert.True(state.CanDispatch);
        Assert.False(state.CanStep);
        Assert.False(state.CanSwitchVersion);
    }

    [Fact]
    public void Pause_allows_step_but_not_switch_until_reobserved()
    {
        var paused = RunControlState.Start().Pause();

        Assert.Equal(RunControlPhase.Paused, paused.Phase);
        Assert.False(paused.CanDispatch);
        Assert.True(paused.CanStep);
        // §6.8: version 切替は「Paused かつ現在 state 再照合後」だけ——停止しただけでは切替不可。
        Assert.False(paused.CanSwitchVersion);

        Assert.True(paused.ObservationRecorded().CanSwitchVersion);
    }

    [Fact]
    public void Resume_discards_the_reverification_of_the_previous_hold()
    {
        var verified = RunControlState.Start().Pause().ObservationRecorded();

        var pausedAgain = verified.Resume().Pause();

        // 再照合は停止位置ごと。前回停止で取った観測を次の停止へ持ち越さない。
        Assert.False(pausedAgain.CanSwitchVersion);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Manual_intervention_stops_progress_from_running_and_paused(bool fromPaused)
    {
        var before = fromPaused ? RunControlState.Start().Pause() : RunControlState.Start();

        var intervening = before.BeginManualIntervention();

        Assert.Equal(RunControlPhase.ManualIntervention, intervening.Phase);
        Assert.False(intervening.CanDispatch);
        Assert.False(intervening.CanStep);
        Assert.False(intervening.CanSwitchVersion);
    }

    [Fact]
    public void Intervention_end_blocks_all_progress_until_a_fresh_observation()
    {
        var ended = RunControlState.Start().BeginManualIntervention().EndManualIntervention();

        Assert.Equal(RunControlPhase.Paused, ended.Phase);
        Assert.True(ended.NeedsReobservation);
        Assert.False(ended.CanStep);
        Assert.False(ended.CanSwitchVersion);
        Assert.Throws<InvalidOperationException>(() => ended.Resume());

        var reobserved = ended.ObservationRecorded();
        Assert.False(reobserved.NeedsReobservation);
        Assert.True(reobserved.CanStep);
        Assert.True(reobserved.CanSwitchVersion);
        Assert.Equal(RunControlPhase.Running, reobserved.Resume().Phase);
    }

    [Fact]
    public void Observations_are_not_recordable_during_an_intervention()
    {
        var intervening = RunControlState.Start().BeginManualIntervention();

        // journal 上「介入開始と終了の間に observation event は現れない」が再開照合（t10）の前提。
        Assert.Throws<InvalidOperationException>(() => intervening.ObservationRecorded());
    }

    [Fact]
    public void Abandon_is_terminal_from_every_phase_and_accepts_nothing_afterwards()
    {
        foreach (var state in new[]
        {
            RunControlState.Start(),
            RunControlState.Start().Pause(),
            RunControlState.Start().BeginManualIntervention(),
        })
        {
            var abandoned = state.Abandon();
            Assert.Equal(RunControlPhase.Abandoned, abandoned.Phase);
            Assert.False(abandoned.CanDispatch);
            Assert.False(abandoned.CanStep);
            Assert.False(abandoned.CanSwitchVersion);
            Assert.Throws<InvalidOperationException>(() => abandoned.Pause());
            Assert.Throws<InvalidOperationException>(() => abandoned.Resume());
            Assert.Throws<InvalidOperationException>(() => abandoned.BeginManualIntervention());
            Assert.Throws<InvalidOperationException>(() => abandoned.EndManualIntervention());
            Assert.Throws<InvalidOperationException>(() => abandoned.ObservationRecorded());
            Assert.Throws<InvalidOperationException>(() => abandoned.Abandon());
        }
    }

    [Fact]
    public void Transitions_outside_the_machine_are_rejected()
    {
        var running = RunControlState.Start();
        Assert.Throws<InvalidOperationException>(() => running.Resume());
        Assert.Throws<InvalidOperationException>(() => running.EndManualIntervention());

        var paused = running.Pause();
        Assert.Throws<InvalidOperationException>(() => paused.Pause());
        Assert.Throws<InvalidOperationException>(() => paused.EndManualIntervention());
    }

    [Fact]
    public void From_journal_does_not_resume_running_and_keeps_reobservation()
    {
        Assert.Equal(RunControlPhase.Running, RunControlState.FromJournal([]).Phase);

        var recorded = RunControlState.FromJournal([Event(1, RunEventPayloadTypes.Proposal)]);
        Assert.Equal(RunControlPhase.Paused, recorded.Phase);
        Assert.False(recorded.CanDispatch);
        Assert.True(recorded.CanStep);

        var intervening = RunControlState.FromJournal([Event(1, RunEventPayloadTypes.ManualIntervention)]);
        Assert.Equal(RunControlPhase.ManualIntervention, intervening.Phase);
        Assert.False(intervening.CanStep);

        var waiting = RunControlState.FromJournal(
        [
            Event(1, RunEventPayloadTypes.ManualIntervention),
            Event(2, RunEventPayloadTypes.ManualIntervention),
        ]);
        Assert.Equal(RunControlPhase.Paused, waiting.Phase);
        Assert.True(waiting.NeedsReobservation);
        Assert.False(waiting.CanStep);

        var reobserved = RunControlState.FromJournal(
        [
            Event(1, RunEventPayloadTypes.ManualIntervention),
            Event(2, RunEventPayloadTypes.ManualIntervention),
            Event(3, RunEventPayloadTypes.Observation, observationId: "observation-1"),
        ]);
        Assert.False(reobserved.NeedsReobservation);
        Assert.True(reobserved.CanStep);
        Assert.True(reobserved.ObservedInCurrentHold);

        var abandoned = RunControlState.FromJournal([Event(1, RunEventPayloadTypes.Abandon)]);
        Assert.Equal(RunControlPhase.Abandoned, abandoned.Phase);
    }

    private static RunEvent Event(long sequence, string payloadType, string? observationId = null) =>
        new(
            "0.1.0",
            $"event-{sequence}",
            "run-1",
            sequence,
            "playbook-1",
            "version-1",
            null,
            null,
            null,
            "cause-1",
            $"correlation-{sequence}",
            1,
            RunEventActorType.User,
            new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 0, 0, 1, TimeSpan.Zero),
            observationId,
            payloadType,
            "{}");
}
