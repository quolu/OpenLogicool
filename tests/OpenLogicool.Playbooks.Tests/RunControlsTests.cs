using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class RunControlsTests
{
    private sealed class RecordingStore : IRunJournalStore
    {
        public List<RunEvent> Events { get; } = [];

        public void Append(RunEvent runEvent) => Events.Add(runEvent);

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

    private const string RunId = "run-1";
    private const string PlaybookId = "playbook-1";
    private const string Version1 = "ver-1";
    private const string Version2 = "ver-2";

    private static PlaybookVersion Version(
        string versionId,
        string? parentVersionId = null,
        IReadOnlyList<string>? stepPreconditions = null,
        string stepNodeId = "step")
        => new(
            ContractSchemaVersions.Revision01,
            versionId,
            parentVersionId,
            [
                new PlaybookNode(ContractSchemaVersions.Revision01, "menu", true, "state:menu", [], null, []),
                new PlaybookNode(
                    ContractSchemaVersions.Revision01,
                    stepNodeId,
                    false,
                    "state:menu",
                    stepPreconditions ?? ["state:menu"],
                    "action.step",
                    ["state:done"]),
            ],
            [
                new PlaybookEdge(ContractSchemaVersions.Revision01, "menu-to-step", "menu", stepNodeId, null),
            ],
            versionId == Version1 ? "initial" : "revised");

    private static RunEvent Event(
        long sequence,
        string payloadType,
        string? attemptId = null,
        string? commandId = null,
        string? observationId = null,
        string? nodeOrTransitionId = null,
        string versionId = Version1,
        string runId = RunId,
        RunEventActorType actorType = RunEventActorType.User) =>
        new(
            "0.1.0",
            $"event-{sequence}",
            runId,
            sequence,
            PlaybookId,
            versionId,
            nodeOrTransitionId,
            commandId,
            attemptId,
            "cause-1",
            $"correlation-{sequence}",
            1,
            actorType,
            new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 0, 0, 1, TimeSpan.Zero),
            observationId,
            payloadType,
            "{}");

    private static (RunControls Controls, AttemptDispatchGate Gate, RecordingStore Store) NewControls()
    {
        var store = new RecordingStore();
        var journal = new RunJournal(store, new NullSink());
        var gate = new AttemptDispatchGate(journal);
        var run = PlaybookRun.Start(PlaybookId, PlaybookMaterializer.ToGraph(Version(Version1)));
        return (new RunControls(journal, gate, RunId, run), gate, store);
    }

    /// <summary>proposal→approval→Prepared まで進めた Attempt を作り、次の runSequence を返す。</summary>
    private static long PrepareAttempt(AttemptDispatchGate gate, string attemptId, long firstSequence)
    {
        gate.CommitProposed(Event(firstSequence, RunEventPayloadTypes.Proposal, attemptId, actorType: RunEventActorType.Automation));
        gate.CommitAuthorized(Event(firstSequence + 1, RunEventPayloadTypes.Approval, attemptId, actorType: RunEventActorType.User));
        gate.MarkPrepared(attemptId);
        return firstSequence + 2;
    }

    [Fact]
    public void Step_once_executes_a_single_dispatch_and_stays_paused()
    {
        var (controls, gate, _) = NewControls();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        controls.Pause();

        var dispatched = 0;
        controls.StepOnce(
            Event(sequence, RunEventPayloadTypes.Dispatch, "attempt-1", "command-1", actorType: RunEventActorType.Automation),
            () => dispatched++);

        // PB-007: 一手だけ。実行後も Paused のままで、自動継続する経路は無い。
        Assert.Equal(1, dispatched);
        Assert.Equal(RunControlPhase.Paused, controls.State.Phase);
        Assert.Equal(AttemptState.DispatchArmed, gate.Get("attempt-1").State);
    }

    [Fact]
    public void Step_once_is_rejected_while_running()
    {
        var (controls, gate, store) = NewControls();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        var eventCount = store.Events.Count;

        var dispatched = 0;
        Assert.Throws<InvalidOperationException>(() => controls.StepOnce(
            Event(sequence, RunEventPayloadTypes.Dispatch, "attempt-1", "command-1", actorType: RunEventActorType.Automation),
            () => dispatched++));

        Assert.Equal(0, dispatched);
        Assert.Equal(eventCount, store.Events.Count);
    }

    [Fact]
    public void Step_once_rejects_an_event_that_does_not_carry_the_pinned_version()
    {
        var (controls, gate, _) = NewControls();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        controls.Pause();

        Assert.Throws<ArgumentException>(() => controls.StepOnce(
            Event(sequence, RunEventPayloadTypes.Dispatch, "attempt-1", "command-1",
                versionId: Version2, actorType: RunEventActorType.Automation),
            () => { }));
    }

    [Fact]
    public void Skip_records_the_event_without_dispatch_or_attempt()
    {
        var (controls, gate, store) = NewControls();

        controls.Skip(Event(1, RunEventPayloadTypes.Skip, nodeOrTransitionId: "step"));

        var skipEvent = Assert.Single(store.Events);
        Assert.Equal(RunEventPayloadTypes.Skip, skipEvent.PayloadType);
        Assert.Equal("step", skipEvent.NodeOrTransitionId);
        Assert.Empty(gate.Attempts);
    }

    [Fact]
    public void Skip_rejects_a_target_missing_from_the_pinned_version()
    {
        var (controls, _, store) = NewControls();

        Assert.Throws<InvalidOperationException>(
            () => controls.Skip(Event(1, RunEventPayloadTypes.Skip, nodeOrTransitionId: "ghost")));
        Assert.Empty(store.Events);
    }

    [Fact]
    public void Control_operations_reject_events_not_recorded_as_the_user()
    {
        var (controls, _, store) = NewControls();

        // PB-013: 制御操作を自動化へ帰属させない。
        Assert.Throws<ArgumentException>(() => controls.Skip(
            Event(1, RunEventPayloadTypes.Skip, nodeOrTransitionId: "step", actorType: RunEventActorType.Automation)));
        Assert.Throws<ArgumentException>(() => controls.Abandon(
            Event(1, RunEventPayloadTypes.Abandon, actorType: RunEventActorType.System)));
        Assert.Empty(store.Events);
    }

    [Fact]
    public void Manual_intervention_requires_a_fresh_observation_before_resuming()
    {
        var (controls, _, store) = NewControls();

        controls.BeginManualIntervention(Event(1, RunEventPayloadTypes.ManualIntervention));
        Assert.Equal(RunControlPhase.ManualIntervention, controls.State.Phase);

        controls.EndManualIntervention(Event(2, RunEventPayloadTypes.ManualIntervention));
        Assert.Equal(RunControlPhase.Paused, controls.State.Phase);
        Assert.True(controls.State.NeedsReobservation);
        Assert.Throws<InvalidOperationException>(controls.Resume);

        // §6.8: 終了後は必ず新しい Observation から照合する。記録後にだけ進行が再開できる。
        controls.RecordObservation(Event(3, RunEventPayloadTypes.Observation, observationId: "observation-1"));
        controls.Resume();
        Assert.Equal(RunControlPhase.Running, controls.State.Phase);
        Assert.Equal(3, store.Events.Count);
    }

    [Fact]
    public void Observations_between_intervention_begin_and_end_are_refused()
    {
        var (controls, _, store) = NewControls();
        controls.BeginManualIntervention(Event(1, RunEventPayloadTypes.ManualIntervention));

        // 再開照合（PB-009・t10）は「介入開始と終了の間に observation event が現れない」journal を前提にする。
        Assert.Throws<InvalidOperationException>(() => controls.RecordObservation(
            Event(2, RunEventPayloadTypes.Observation, observationId: "observation-1")));
        Assert.Single(store.Events);
    }

    [Fact]
    public void Run_level_observations_reject_attempt_bound_events()
    {
        var (controls, _, store) = NewControls();
        controls.Pause();

        Assert.Throws<ArgumentException>(() => controls.RecordObservation(
            Event(1, RunEventPayloadTypes.Observation, attemptId: "attempt-1", observationId: "observation-1")));
        Assert.Empty(store.Events);
    }

    [Fact]
    public void Physical_input_on_a_bound_action_stops_the_executor_as_an_intervention()
    {
        var (controls, _, store) = NewControls();

        var arbitration = controls.OnPhysicalSemanticAction(
            "action.step", () => Event(1, RunEventPayloadTypes.ManualIntervention));

        // §6.5×PB-013: 同じ Semantic Action への物理入力は manual intervention として停止。Run へ合流しない。
        Assert.Equal(PhysicalInputArbitration.ExecutorStopped, arbitration);
        Assert.Equal(RunControlPhase.ManualIntervention, controls.State.Phase);
        var intervention = Assert.Single(store.Events);
        Assert.Equal(RunEventPayloadTypes.ManualIntervention, intervention.PayloadType);
    }

    [Fact]
    public void Physical_input_on_an_unbound_action_is_outside_the_run()
    {
        var (controls, _, store) = NewControls();

        var arbitration = controls.OnPhysicalSemanticAction(
            "action.other", () => Event(1, RunEventPayloadTypes.ManualIntervention));

        Assert.Equal(PhysicalInputArbitration.NotBoundToRun, arbitration);
        Assert.Equal(RunControlPhase.Running, controls.State.Phase);
        Assert.Empty(store.Events);
    }

    [Fact]
    public void Physical_input_during_an_intervention_adds_no_event()
    {
        var (controls, _, store) = NewControls();
        controls.OnPhysicalSemanticAction("action.step", () => Event(1, RunEventPayloadTypes.ManualIntervention));

        var arbitration = controls.OnPhysicalSemanticAction(
            "action.step", () => Event(2, RunEventPayloadTypes.ManualIntervention));

        Assert.Equal(PhysicalInputArbitration.AlreadyIntervening, arbitration);
        Assert.Single(store.Events);
    }

    [Fact]
    public void Abandon_terminates_attempts_along_the_legal_paths_only()
    {
        var (controls, gate, store) = NewControls();
        gate.CommitProposed(Event(1, RunEventPayloadTypes.Proposal, "attempt-pre", actorType: RunEventActorType.Automation));
        var sequence = PrepareAttempt(gate, "attempt-armed", 2);
        gate.ArmThenDispatch(
            Event(sequence, RunEventPayloadTypes.Dispatch, "attempt-armed", "command-1", actorType: RunEventActorType.Automation),
            () => { });

        controls.Abandon(Event(sequence + 1, RunEventPayloadTypes.Abandon));

        // §6.7: dispatch 前は Cancelled への写像（PB-007）、dispatch し得た後は
        // OutcomeUnknown→Reconciling→Abandoned の合法経路だけで終端へ。
        Assert.Equal(RunControlPhase.Abandoned, controls.State.Phase);
        Assert.Equal(AttemptState.Cancelled, gate.Get("attempt-pre").State);
        Assert.Equal(AttemptState.Abandoned, gate.Get("attempt-armed").State);
        Assert.Single(store.Events, e => e.PayloadType == RunEventPayloadTypes.Abandon);

        // Abandoned の Run は以後の制御を受けない。
        Assert.Throws<InvalidOperationException>(controls.Pause);
        Assert.Throws<InvalidOperationException>(() => controls.Skip(
            Event(sequence + 2, RunEventPayloadTypes.Skip, nodeOrTransitionId: "step")));
        Assert.Equal(
            PhysicalInputArbitration.RunClosed,
            controls.OnPhysicalSemanticAction("action.step", () => Event(sequence + 2, RunEventPayloadTypes.ManualIntervention)));
    }

    [Fact]
    public void Switch_version_requires_pause_and_reverification_then_repins()
    {
        var (controls, _, store) = NewControls();
        controls.Pause();
        controls.RecordObservation(Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"));

        controls.SwitchVersion(
            Event(2, RunEventPayloadTypes.VersionSwitch, versionId: Version2),
            Version(Version2, parentVersionId: Version1),
            progressNodeId: "step");

        // §6.8: 切替後も Paused のまま（切替は再開ではない）。以後の event は新 version を運ぶ。
        Assert.Equal(Version2, controls.Run.PinnedVersionId);
        Assert.Equal(RunControlPhase.Paused, controls.State.Phase);
        Assert.Single(store.Events, e => e.PayloadType == RunEventPayloadTypes.VersionSwitch);

        controls.Skip(Event(3, RunEventPayloadTypes.Skip, nodeOrTransitionId: "step", versionId: Version2));
        Assert.Throws<ArgumentException>(() => controls.Skip(
            Event(4, RunEventPayloadTypes.Skip, nodeOrTransitionId: "step", versionId: Version1)));
    }

    [Fact]
    public void Switch_version_is_rejected_while_running_or_without_reverification()
    {
        var (controls, _, store) = NewControls();
        var newVersion = Version(Version2, parentVersionId: Version1);

        // Running のまま——§6.8 は Paused を要求する。
        Assert.Throws<InvalidOperationException>(() => controls.SwitchVersion(
            Event(1, RunEventPayloadTypes.VersionSwitch, versionId: Version2), newVersion, "step"));

        // Paused だが停止位置での再照合（新しい Observation）が無い。
        controls.Pause();
        Assert.Throws<InvalidOperationException>(() => controls.SwitchVersion(
            Event(1, RunEventPayloadTypes.VersionSwitch, versionId: Version2), newVersion, "step"));

        Assert.Empty(store.Events);
    }

    [Fact]
    public void Switch_version_after_an_intervention_requires_the_fresh_observation_first()
    {
        var (controls, _, _) = NewControls();
        controls.BeginManualIntervention(Event(1, RunEventPayloadTypes.ManualIntervention));
        controls.EndManualIntervention(Event(2, RunEventPayloadTypes.ManualIntervention));

        Assert.Throws<InvalidOperationException>(() => controls.SwitchVersion(
            Event(3, RunEventPayloadTypes.VersionSwitch, versionId: Version2),
            Version(Version2, parentVersionId: Version1),
            "step"));
    }

    [Fact]
    public void Switch_version_rejects_uninheritable_progress()
    {
        var (controls, _, store) = NewControls();
        controls.Pause();
        controls.RecordObservation(Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"));
        var eventCount = store.Events.Count;

        // §6.8: 進捗継承は stable node ID と前後 condition が互換な node だけ。
        Assert.Throws<InvalidOperationException>(() => controls.SwitchVersion(
            Event(2, RunEventPayloadTypes.VersionSwitch, versionId: Version2),
            Version(Version2, parentVersionId: Version1, stepNodeId: "step-renamed"),
            "step"));
        Assert.Throws<InvalidOperationException>(() => controls.SwitchVersion(
            Event(2, RunEventPayloadTypes.VersionSwitch, versionId: Version2),
            Version(Version2, parentVersionId: Version1, stepPreconditions: ["state:other"]),
            "step"));
        Assert.Equal(eventCount, store.Events.Count);
    }

    [Fact]
    public void Switch_version_rejects_mismatched_event_and_target()
    {
        var (controls, _, store) = NewControls();
        controls.Pause();
        controls.RecordObservation(Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"));
        var eventCount = store.Events.Count;

        // event が旧 version を運ぶ——version-switch event は新 version を運ばなければならない。
        Assert.Throws<ArgumentException>(() => controls.SwitchVersion(
            Event(2, RunEventPayloadTypes.VersionSwitch, versionId: Version1),
            Version(Version2, parentVersionId: Version1),
            "step"));

        // pin と同一 version への切替は無意味であり拒否する。
        Assert.Throws<ArgumentException>(() => controls.SwitchVersion(
            Event(2, RunEventPayloadTypes.VersionSwitch, versionId: Version1),
            Version(Version1),
            "step"));

        Assert.Equal(eventCount, store.Events.Count);
    }

    [Fact]
    public void Control_events_of_another_run_are_rejected()
    {
        var (controls, _, store) = NewControls();

        Assert.Throws<ArgumentException>(() => controls.Skip(
            Event(1, RunEventPayloadTypes.Skip, nodeOrTransitionId: "step", runId: "run-2")));
        Assert.Empty(store.Events);
    }

    [Fact]
    public void Reconstructing_from_journal_does_not_drop_reobservation()
    {
        var (controls, _, store) = NewControls();
        controls.BeginManualIntervention(Event(1, RunEventPayloadTypes.ManualIntervention));
        controls.EndManualIntervention(Event(2, RunEventPayloadTypes.ManualIntervention));

        var restored = Reconstruct(store);

        Assert.Equal(RunControlPhase.Paused, restored.State.Phase);
        Assert.True(restored.State.NeedsReobservation);
        Assert.Throws<InvalidOperationException>(restored.Resume);
        Assert.Throws<InvalidOperationException>(() => restored.StepOnce(
            Event(3, RunEventPayloadTypes.Dispatch, "attempt-1", "command-1", actorType: RunEventActorType.Automation),
            () => { }));
    }

    [Fact]
    public void Reconstructing_mid_intervention_stays_intervening()
    {
        var (controls, _, store) = NewControls();
        controls.BeginManualIntervention(Event(1, RunEventPayloadTypes.ManualIntervention));

        var restored = Reconstruct(store);

        Assert.Equal(RunControlPhase.ManualIntervention, restored.State.Phase);
        Assert.Throws<InvalidOperationException>(() => restored.RecordObservation(
            Event(2, RunEventPayloadTypes.Observation, observationId: "observation-1")));
    }

    [Fact]
    public void Reconstructing_a_recorded_run_does_not_auto_dispatch()
    {
        var store = new RecordingStore();
        var journal = new RunJournal(store, new NullSink());
        journal.Append(Event(1, RunEventPayloadTypes.Proposal, "attempt-1", actorType: RunEventActorType.Automation));

        var restored = Reconstruct(store);

        Assert.Equal(RunControlPhase.Paused, restored.State.Phase);
        Assert.False(restored.State.CanDispatch);
        restored.Resume();
        Assert.Equal(RunControlPhase.Running, restored.State.Phase);
    }

    private static RunControls Reconstruct(RecordingStore store)
    {
        var journal = new RunJournal(store, new NullSink());
        var gate = AttemptDispatchGate.Recover(store, journal);
        var run = PlaybookRun.Start(PlaybookId, PlaybookMaterializer.ToGraph(Version(Version1)));
        return new RunControls(journal, gate, RunId, run);
    }
}
