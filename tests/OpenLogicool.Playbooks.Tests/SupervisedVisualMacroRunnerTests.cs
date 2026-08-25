using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class SupervisedVisualMacroRunnerTests
{
    [Fact]
    public void Before_mismatch_stops_without_dispatch()
    {
        var fixture = Fixture();
        var result = fixture.Runner.AuditBefore(Scene("state:other", "before:bad"));

        Assert.False(result.CanContinue);
        Assert.Equal(SupervisedMacroRunState.Stopped, fixture.Runner.Snapshot.State);
        Assert.Equal(SupervisedMacroStopReason.BeforeAuditFailed, fixture.Runner.Snapshot.StopReason);
        Assert.Single(fixture.Store.ReadRun("run:1"));
    }

    [Fact]
    public void Confirmed_before_dispatches_exactly_once_then_requires_after_audit()
    {
        var fixture = Fixture();
        var dispatches = 0;
        fixture.Runner.AuditBefore(Scene("state:source", "before:ok"));

        fixture.Runner.DispatchOnce(() => dispatches++);

        Assert.Equal(1, dispatches);
        Assert.Equal(SupervisedMacroRunState.AwaitingAfterAudit, fixture.Runner.Snapshot.State);
        Assert.Throws<InvalidOperationException>(() => fixture.Runner.DispatchOnce(() => dispatches++));
        Assert.Equal(1, dispatches);
        Assert.Equal(
            [RunEventPayloadTypes.Observation, RunEventPayloadTypes.Proposal, RunEventPayloadTypes.Authorization,
                RunEventPayloadTypes.Dispatch, RunEventPayloadTypes.DispatchResult],
            fixture.Store.ReadRun("run:1").Select(item => item.PayloadType));
    }

    [Fact]
    public void Dispatch_fault_is_outcome_unknown_and_is_not_retried()
    {
        var fixture = Fixture();
        var dispatches = 0;
        fixture.Runner.AuditBefore(Scene("state:source", "before:ok"));

        Assert.Throws<InvalidOperationException>(() => fixture.Runner.DispatchOnce(() =>
        {
            dispatches++;
            throw new InvalidOperationException("nano timeout");
        }));

        Assert.Equal(1, dispatches);
        Assert.Equal(SupervisedMacroRunState.OutcomeUnknown, fixture.Runner.Snapshot.State);
        Assert.Equal(SupervisedMacroStopReason.DispatchFault, fixture.Runner.Snapshot.StopReason);
        Assert.Throws<InvalidOperationException>(() => fixture.Runner.DispatchOnce(() => dispatches++));
        Assert.Equal(1, dispatches);
    }

    [Fact]
    public void Proven_not_started_dispatch_is_disarmed_instead_of_outcome_unknown()
    {
        var fixture = Fixture();
        fixture.Runner.AuditBefore(Scene("state:source", "before:ok"));

        Assert.Throws<SupervisedMacroDispatchNotStartedException>(() => fixture.Runner.DispatchOnce(() =>
            throw new SupervisedMacroDispatchNotStartedException("target missing")));

        Assert.Equal(SupervisedMacroRunState.Stopped, fixture.Runner.Snapshot.State);
        Assert.Equal(SupervisedMacroStopReason.DispatchNotStarted, fixture.Runner.Snapshot.StopReason);
        Assert.Equal(AttemptState.Disarmed, Assert.Single(fixture.Gate.Attempts).State);
        Assert.Equal(RunEventPayloadTypes.Disarm, fixture.Store.ReadRun("run:1")[^1].PayloadType);
    }

    [Fact]
    public void Runtime_unavailable_before_dispatch_is_a_system_terminal_event_not_user_abandon()
    {
        var fixture = Fixture();

        fixture.Runner.StopBeforeDispatchUnavailable("capture unavailable");

        Assert.Equal(SupervisedMacroRunState.Stopped, fixture.Runner.Snapshot.State);
        Assert.Equal(SupervisedMacroStopReason.RuntimeUnavailable, fixture.Runner.Snapshot.StopReason);
        var terminal = Assert.Single(fixture.Store.ReadRun("run:1"));
        Assert.Equal(RunEventPayloadTypes.RuntimeUnavailable, terminal.PayloadType);
        Assert.Equal(RunEventActorType.System, terminal.ActorType);
        Assert.DoesNotContain(fixture.Store.ReadRun("run:1"), item =>
            item.PayloadType == RunEventPayloadTypes.Abandon);
    }

    [Fact]
    public void Moved_transition_completes_even_when_destination_id_is_diagnostic_mismatch()
    {
        var fixture = Fixture();
        var before = Scene("state:source", "before:ok");
        fixture.Runner.AuditBefore(before);
        fixture.Runner.DispatchOnce(() => { });
        var after = Scene("state:ocr-variant", "after:moved");

        var result = fixture.Runner.AuditAfterTransition(Transition(
            before,
            after,
            GameTransitionJudgement.Moved,
            destinationMatched: false));

        Assert.True(result.CanContinue);
        Assert.Equal(SupervisedMacroRunState.Completed, fixture.Runner.Snapshot.State);
        Assert.Contains("診断上不一致", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Moved_transition_advances_to_the_next_pinned_step()
    {
        var fixture = Fixture(steps: 2);
        var before = Scene("state:source", "before:ok");
        fixture.Runner.AuditBefore(before);
        fixture.Runner.DispatchOnce(() => { });

        fixture.Runner.AuditAfterTransition(Transition(
            before,
            Scene("state:any-moved-destination", "after:moved"),
            GameTransitionJudgement.Moved,
            destinationMatched: false));

        Assert.Equal(SupervisedMacroRunState.AwaitingBeforeAudit, fixture.Runner.Snapshot.State);
        Assert.Equal(2, fixture.Runner.CurrentStep.Sequence);
        Assert.Single(fixture.Runner.Snapshot.History);
    }

    [Fact]
    public void Stayed_transition_stops_after_ten_second_result_without_retry()
    {
        var fixture = Fixture();
        var before = Scene("state:source", "before:ok");
        fixture.Runner.AuditBefore(before);
        fixture.Runner.DispatchOnce(() => { });

        var result = fixture.Runner.AuditAfterTransition(Transition(
            before,
            before with { ObservationId = "after:stayed" },
            GameTransitionJudgement.Stayed,
            destinationMatched: false));

        Assert.False(result.CanContinue);
        Assert.Equal(SupervisedMacroRunState.Stopped, fixture.Runner.Snapshot.State);
        Assert.Equal(SupervisedMacroStopReason.AfterAuditFailed, fixture.Runner.Snapshot.StopReason);
        Assert.Single(fixture.Gate.Attempts);
    }

    private static FixtureState Fixture(int steps = 1)
    {
        var store = new MemoryStore();
        var journal = new RunJournal(store, new NullLog());
        var gate = new AttemptDispatchGate(journal);
        var serial = 0;
        var runner = new SupervisedVisualMacroRunner(
            Program(steps),
            "run:1",
            journal,
            gate,
            prefix => $"{prefix}:{++serial}",
            new FixedTimeProvider());
        return new FixtureState(runner, gate, store);
    }

    private static VisualMacroProgram Program(int steps) => new(
        "0.3.0",
        "macro:1",
        "route:1",
        "route:1:version:1",
        "game",
        "env",
        "structure:1",
        VisualMacroExecutionMode.Supervised,
        Enumerable.Range(1, steps).Select(sequence => new VisualMacroStep(
            sequence,
            $"edge:{sequence}",
            "state:source",
            ["sig:source"],
            $"affordance:{sequence}",
            $"locator:{sequence}",
            "click",
            "state:destination",
            ["sig:destination"],
            new ExplorationWaitCondition("0.3.0", 3, 300, 5_000),
            [],
            StructureVerificationState.Replayed)).ToArray());

    private static ObservedScene Scene(string stateId, string observationId) => new(
        "0.3.0",
        $"scene:{observationId}",
        observationId,
        new CapturedFrameReference(
            "0.3.0", "window:game", CaptureBackend.WindowsGraphicsCapture, 1, 1_000,
            DateTimeOffset.Parse("2026-08-24T00:00:00Z"), 1, 0, 400),
        CaptureAvailability.Available,
        StateIdentityStatus.Known,
        stateId,
        [new StateCandidate("0.3.0", stateId, 1, [new EvidenceRegion("0.3.0", "rect", [0d, 0d, 1d, 1d], "test")])],
        [new AffordanceCandidate(
            "0.3.0",
            "affordance:1",
            observationId,
            1,
            1,
            "window:game",
            new AffordanceLocator("0.3.0", "ocr-normalized-rect", [0.1, 0.1, 0.1, 0.1], "locator:1"),
            [],
            1,
            ["click"])],
        "test");

    private static SupervisedMacroTransitionObservation Transition(
        ObservedScene before,
        ObservedScene after,
        GameTransitionJudgement judgement,
        bool destinationMatched) => new(
        new GameInteractionStabilityResult(
            "0.3.0",
            GameInteractionStabilityStatus.Stable,
            [after],
            after,
            3,
            1_000,
            10_000,
            null),
        new GameTransitionComparison(
            "0.3.0",
            before.ObservationId,
            after.ObservationId,
            judgement,
            [],
            ["test"]),
        after,
        destinationMatched);

    private sealed record FixtureState(
        SupervisedVisualMacroRunner Runner,
        AttemptDispatchGate Gate,
        MemoryStore Store);

    private sealed class MemoryStore : IRunJournalStore
    {
        private readonly List<RunEvent> events = [];
        public void Append(RunEvent runEvent) => events.Add(runEvent);
        public IReadOnlyList<RunEvent> ReadRun(string runId) => events.Where(item => item.RunId == runId).OrderBy(item => item.RunSequence).ToArray();
        public IReadOnlyList<string> ListRunIds() => events.Select(item => item.RunId).Distinct().ToArray();
        public IReadOnlyList<ExpiredRunPreview> PreviewExpiredRuns(DateTimeOffset asOfUtc, int retentionDays) => [];
        public void DeleteRun(string runId) => events.RemoveAll(item => item.RunId == runId);
    }

    private sealed class NullLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry) { }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-08-24T00:00:00Z");
    }
}
