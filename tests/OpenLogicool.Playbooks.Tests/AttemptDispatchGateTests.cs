using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class AttemptDispatchGateTests
{
    private sealed class RecordingStore : IRunJournalStore
    {
        public List<RunEvent> Events { get; } = [];

        public Exception? FailNextAppendWith { get; set; }

        public void Append(RunEvent runEvent)
        {
            if (FailNextAppendWith is not null)
            {
                var failure = FailNextAppendWith;
                FailNextAppendWith = null;
                throw failure;
            }

            Events.Add(runEvent);
        }

        public IReadOnlyList<RunEvent> ReadRun(string runId) =>
            Events.Where(e => e.RunId == runId).OrderBy(e => e.RunSequence).ToArray();

        public IReadOnlyList<string> ListRunIds() =>
            Events.Select(e => e.RunId).Distinct(StringComparer.Ordinal).Order().ToArray();

        public IReadOnlyList<ExpiredRunPreview> PreviewExpiredRuns(DateTimeOffset asOfUtc, int retentionDays) => [];

        public void DeleteRun(string runId) => Events.RemoveAll(e => e.RunId == runId);
    }

    private sealed class NullSink : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry)
        {
        }
    }

    private static RunEvent Event(
        long sequence,
        string payloadType,
        string attemptId = "attempt-1",
        string? commandId = null,
        string? observationId = null) =>
        new(
            "0.1.0",
            $"event-{sequence}",
            "run-1",
            sequence,
            "playbook-1",
            "playbook-version-1",
            null,
            commandId ?? (payloadType == RunEventPayloadTypes.Dispatch ? "command-1" : null),
            attemptId,
            "cause-1",
            $"correlation-{sequence}",
            1,
            RunEventActorType.Automation,
            new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 0, 0, 1, TimeSpan.Zero),
            observationId ?? (payloadType is "observation" or "confirmation" ? "observation-1" : null),
            payloadType,
            "{}");

    private static (AttemptDispatchGate Gate, RecordingStore Store) NewGate()
    {
        var store = new RecordingStore();
        return (new AttemptDispatchGate(new RunJournal(store, new NullSink())), store);
    }

    private static long PrepareAttempt(AttemptDispatchGate gate, string attemptId, long firstSequence)
    {
        gate.CommitProposed(Event(firstSequence, RunEventPayloadTypes.Proposal, attemptId));
        gate.CommitAuthorized(Event(firstSequence + 1, RunEventPayloadTypes.Approval, attemptId));
        gate.MarkPrepared(attemptId);
        return firstSequence + 2;
    }

    [Fact]
    public void Full_path_commits_each_step_and_binds_the_observation()
    {
        var (gate, store) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);

        var dispatched = false;
        gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch), () => dispatched = true);
        gate.CommitReported(Event(sequence + 1, RunEventPayloadTypes.DispatchResult));
        gate.CommitObserving(Event(sequence + 2, RunEventPayloadTypes.Observation));
        gate.CommitConfirmed(Event(sequence + 3, RunEventPayloadTypes.Confirmation));

        Assert.True(dispatched);
        Assert.Equal(6, store.Events.Count);
        var attempt = gate.Get("attempt-1");
        Assert.Equal(AttemptState.Confirmed, attempt.State);
        Assert.Equal("observation-1", attempt.ObservationId);
    }

    [Fact]
    public void Arm_commits_the_dispatch_event_before_calling_external_input()
    {
        var (gate, store) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);

        var journalHadDispatchEventAtCall = false;
        gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch), () =>
            journalHadDispatchEventAtCall = store.Events.Any(e => e.PayloadType == RunEventPayloadTypes.Dispatch));

        // PB-003: 外部入力が呼ばれた時点で DispatchArmed は commit 済み。
        Assert.True(journalHadDispatchEventAtCall);
    }

    [Fact]
    public void A_failed_journal_commit_never_reaches_the_external_input()
    {
        var (gate, store) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        store.FailNextAppendWith = new InvalidOperationException("journal 停止");

        var dispatched = false;
        Assert.Throws<InvalidOperationException>(
            () => gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch), () => dispatched = true));

        Assert.False(dispatched);
        Assert.Equal(AttemptState.Prepared, gate.Get("attempt-1").State);
    }

    [Fact]
    public void External_input_failure_leaves_the_armed_attempt_unresolved_without_resend()
    {
        var (gate, store) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);

        var calls = 0;
        Assert.Throws<TimeoutException>(() => gate.ArmThenDispatch(
            Event(sequence, RunEventPayloadTypes.Dispatch),
            () => { calls++; throw new TimeoutException("SendInput 失敗"); }));

        // PB-004: 自動再送しない。PB-005: commit 済み dispatch event は外部失敗で巻き戻らない。
        Assert.Equal(1, calls);
        Assert.Equal(AttemptState.DispatchArmed, gate.Get("attempt-1").State);
        Assert.Single(store.Events, e => e.PayloadType == RunEventPayloadTypes.Dispatch);
        Assert.Equal(3, store.Events.Count);
    }

    [Fact]
    public void External_input_success_does_not_advance_the_state()
    {
        var (gate, _) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);

        gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch), () => { });

        // §6.7 契約3: Input API 成功は証拠でない。DispatchReported へ進めるのは CommitReported だけ。
        Assert.Equal(AttemptState.DispatchArmed, gate.Get("attempt-1").State);
    }

    [Fact]
    public void An_unresolved_dispatch_blocks_the_next_dispatch_until_resolved()
    {
        var (gate, _) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch), () => { });
        var next = PrepareAttempt(gate, "attempt-2", sequence + 1);

        Assert.Throws<InvalidOperationException>(() => gate.ArmThenDispatch(
            Event(next, RunEventPayloadTypes.Dispatch, "attempt-2"), () => { }));

        // §6.7 契約5: 終端だけが解決。Disarmed へ解決した後は次の dispatch が通る。
        gate.ResolveLocally("attempt-1", AttemptState.Disarmed);
        gate.ArmThenDispatch(Event(next, RunEventPayloadTypes.Dispatch, "attempt-2"), () => { });
        Assert.Equal(AttemptState.DispatchArmed, gate.Get("attempt-2").State);
    }

    [Fact]
    public void Confirmation_outside_observing_is_rejected()
    {
        var (gate, _) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch), () => { });
        gate.CommitReported(Event(sequence + 1, RunEventPayloadTypes.DispatchResult));

        Assert.Throws<InvalidOperationException>(
            () => gate.CommitConfirmed(Event(sequence + 2, RunEventPayloadTypes.Confirmation)));
    }

    [Fact]
    public void A_registered_attempt_id_cannot_be_reused()
    {
        var (gate, _) = NewGate();
        gate.CommitProposed(Event(1, RunEventPayloadTypes.Proposal));

        Assert.Throws<InvalidOperationException>(
            () => gate.CommitProposed(Event(2, RunEventPayloadTypes.Proposal)));
    }

    [Fact]
    public void Gate_operations_reject_mismatched_payload_types_and_missing_attempt_ids()
    {
        var (gate, store) = NewGate();

        Assert.Throws<ArgumentException>(
            () => gate.CommitProposed(Event(1, RunEventPayloadTypes.Dispatch)));
        Assert.Throws<ArgumentException>(
            () => gate.CommitProposed(Event(1, RunEventPayloadTypes.Proposal) with { AttemptId = null }));
        Assert.Empty(store.Events);
    }

    [Fact]
    public void Resolve_locally_writes_no_journal_event()
    {
        var (gate, store) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch), () => { });
        var eventCount = store.Events.Count;

        gate.ResolveLocally("attempt-1", AttemptState.OutcomeUnknown);

        Assert.Equal(eventCount, store.Events.Count);
    }

    [Fact]
    public void Recover_classifies_attempts_from_journal_events_only()
    {
        var store = new RecordingStore();
        var journal = new RunJournal(store, new NullSink());
        // pre-dispatch の attempt / dispatch 済み未確定の attempt / confirmation 済みの attempt を journal に置く。
        journal.Append(Event(1, RunEventPayloadTypes.Proposal, "attempt-pre"));
        journal.Append(Event(2, RunEventPayloadTypes.Proposal, "attempt-armed"));
        journal.Append(Event(3, RunEventPayloadTypes.Approval, "attempt-armed"));
        journal.Append(Event(4, RunEventPayloadTypes.Dispatch, "attempt-armed"));
        journal.Append(Event(5, RunEventPayloadTypes.DispatchResult, "attempt-armed"));
        journal.Append(Event(6, RunEventPayloadTypes.Proposal, "attempt-done"));
        journal.Append(Event(7, RunEventPayloadTypes.Approval, "attempt-done"));
        journal.Append(Event(8, RunEventPayloadTypes.Dispatch, "attempt-done"));
        journal.Append(Event(9, RunEventPayloadTypes.Observation, "attempt-done"));
        journal.Append(Event(10, RunEventPayloadTypes.Confirmation, "attempt-done", observationId: "observation-done"));

        var recovered = AttemptDispatchGate.Recover(store, RunJournal.Restore(store, new NullSink()));

        // §6.7 契約2: dispatch 前は Cancelled、dispatch 以降の未確定は実際の送信有無に関わらず OutcomeUnknown。
        Assert.Equal(AttemptState.Cancelled, recovered.Get("attempt-pre").State);
        Assert.Equal(AttemptState.OutcomeUnknown, recovered.Get("attempt-armed").State);
        Assert.Equal(AttemptState.Confirmed, recovered.Get("attempt-done").State);
        Assert.Equal("observation-done", recovered.Get("attempt-done").ObservationId);
    }

    [Fact]
    public void Recovered_unknown_outcome_still_blocks_the_next_dispatch()
    {
        var store = new RecordingStore();
        var journal = new RunJournal(store, new NullSink());
        journal.Append(Event(1, RunEventPayloadTypes.Proposal, "attempt-armed"));
        journal.Append(Event(2, RunEventPayloadTypes.Dispatch, "attempt-armed"));

        var recovered = AttemptDispatchGate.Recover(store, RunJournal.Restore(store, new NullSink()));
        var sequence = PrepareAttempt(recovered, "attempt-next", 3);

        // OPS-008×契約5: 復元された OutcomeUnknown も未解決であり、次の dispatch を自動生成できない。
        Assert.Throws<InvalidOperationException>(() => recovered.ArmThenDispatch(
            Event(sequence, RunEventPayloadTypes.Dispatch, "attempt-next"), () => { }));
    }
}
