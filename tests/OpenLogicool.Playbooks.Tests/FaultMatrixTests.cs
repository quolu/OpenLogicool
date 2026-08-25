using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

/// <summary>
/// §10.2 crash matrix（t07・NFR-012）。各 fault 境界で journal を実際にその形まで積み、
/// 再起動復元（Recover）または live の fault 分類が §10.2 の不変条件を満たすことを確認する。
/// 実画面・実 process kill は使わない——crash は「journal がそこで途切れた」ことと等価であり、
/// その等価性こそが §6.7 契約2（journal の実 event だけを根拠にする）の主張である。
/// </summary>
public sealed class FaultMatrixTests
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

    private const string RunId = "run-1";
    private const string PlaybookId = "playbook-1";
    private const string Version1 = "ver-1";

    private static RunEvent Event(
        long sequence,
        string payloadType,
        string? attemptId = null,
        string? commandId = null,
        string? observationId = null,
        string? nodeOrTransitionId = null,
        RunEventActorType actorType = RunEventActorType.Automation) =>
        new(
            "0.1.0",
            $"event-{sequence}",
            RunId,
            sequence,
            PlaybookId,
            Version1,
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

    private static PlaybookVersion Version(string versionId) => new(
        ContractSchemaVersions.Revision01,
        versionId,
        versionId == Version1 ? null : Version1,
        [
            new PlaybookNode(ContractSchemaVersions.Revision01, "menu", true, "state:menu", [], null, []),
            new PlaybookNode(ContractSchemaVersions.Revision01, "step", false, "state:menu", ["state:menu"], "action.step", ["state:done"]),
        ],
        [new PlaybookEdge(ContractSchemaVersions.Revision01, "menu-to-step", "menu", "step", null)],
        versionId == Version1 ? "initial" : "revised");

    private static (AttemptDispatchGate Gate, RecordingStore Store) NewGate()
    {
        var store = new RecordingStore();
        return (new AttemptDispatchGate(new RunJournal(store, new NullSink())), store);
    }

    private static AttemptDispatchGate RecoverFrom(RecordingStore store) =>
        AttemptDispatchGate.Recover(store, RunJournal.Restore(store, new NullSink()));

    private static long PrepareAttempt(AttemptDispatchGate gate, string attemptId, long firstSequence, string? commandId = null)
    {
        gate.CommitProposed(Event(firstSequence, RunEventPayloadTypes.Proposal, attemptId, commandId));
        gate.CommitAuthorized(Event(firstSequence + 1, RunEventPayloadTypes.Authorization, attemptId));
        gate.MarkPrepared(attemptId);
        return firstSequence + 2;
    }

    // ---- 境界1・2: Prepared 前後（DispatchArmed commit 前）の crash ----

    [Fact]
    public void Boundary_1_and_2_crash_before_arm_restores_cancelled()
    {
        var (gate, store) = NewGate();
        PrepareAttempt(gate, "attempt-1", 1);
        // crash: journal は proposal＋approval で途切れる（Prepared は journal event を持たない——両境界は同じ観測形）。

        var recovered = RecoverFrom(store);

        // §6.7 契約2: 外部入力呼出前が確定している crash は Cancelled。
        Assert.Equal(AttemptState.Cancelled, recovered.Get("attempt-1").State);
    }

    // ---- 境界3・5・6: DispatchArmed commit 後〜DispatchReported commit 前の crash ----

    [Fact]
    public void Boundaries_3_5_6_crash_after_arm_restore_outcome_unknown_and_block_dispatch()
    {
        var (gate, store) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch, "attempt-1", "command-1"), () => { });
        // crash: input call 前（境界3）・外部効果後 return 前（境界5）・return 後 report 前（境界6）は
        // いずれも journal が dispatch で途切れた形であり、区別できない＝全て OutcomeUnknown。

        var recovered = RecoverFrom(store);

        Assert.Equal(AttemptState.OutcomeUnknown, recovered.Get("attempt-1").State);

        // 不変条件: 未解決 DispatchArmed（の復元）中に次の dispatch を出さない。
        var next = PrepareAttempt(recovered, "attempt-2", sequence + 1, "command-2");
        Assert.Throws<InvalidOperationException>(() => recovered.ArmThenDispatch(
            Event(next, RunEventPayloadTypes.Dispatch, "attempt-2", "command-2"), () => { }));
    }

    [Fact]
    public void Boundary_3_handled_stop_before_input_call_disarms_with_a_journal_record()
    {
        var (gate, store) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch, "attempt-1", "command-1"), () => { });

        // handled stop を入力呼出前に検出＝未呼出を保証できる→分類は Disarmed（§6.7）。
        var disposition = AttemptFaultClassifier.Classify(
            AttemptFaultPoint.HandledStop, ExternalInputCallState.ProvablyNotCalled);
        Assert.Equal(AttemptState.Disarmed, disposition);
        gate.CommitDisarmed(Event(sequence + 1, RunEventPayloadTypes.Disarm, "attempt-1", actorType: RunEventActorType.System));

        Assert.Equal(AttemptState.Disarmed, gate.Get("attempt-1").State);
        Assert.Single(store.Events, e => e.PayloadType == RunEventPayloadTypes.Disarm);

        // 保証付き終端は復元で OutcomeUnknown へ劣化しない。次の dispatch も塞がれない（解決済み）。
        var recovered = RecoverFrom(store);
        Assert.Equal(AttemptState.Disarmed, recovered.Get("attempt-1").State);
        var next = PrepareAttempt(recovered, "attempt-2", sequence + 2, "command-2");
        recovered.ArmThenDispatch(Event(next, RunEventPayloadTypes.Dispatch, "attempt-2", "command-2"), () => { });
        Assert.Equal(AttemptState.DispatchArmed, recovered.Get("attempt-2").State);
    }

    [Fact]
    public void Disarm_is_only_reachable_from_dispatch_armed()
    {
        var (gate, store) = NewGate();
        PrepareAttempt(gate, "attempt-1", 1);
        var eventCount = store.Events.Count;

        // Prepared からの disarm は §6.7 に無い（dispatch 前の中止は Cancelled）。
        Assert.Throws<InvalidOperationException>(() => gate.CommitDisarmed(
            Event(3, RunEventPayloadTypes.Disarm, "attempt-1", actorType: RunEventActorType.System)));
        Assert.Equal(eventCount, store.Events.Count);
    }

    // ---- 境界4: key down 後 key up 前（partial SendInput） ----

    [Fact]
    public void Boundary_4_partial_send_input_leaves_outcome_unknown_without_resend()
    {
        var (gate, store) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        var calls = 0;
        Assert.Throws<InvalidOperationException>(() => gate.ArmThenDispatch(
            Event(sequence, RunEventPayloadTypes.Dispatch, "attempt-1", "command-1"),
            () => { calls++; throw new InvalidOperationException("SendInput 部分成功"); }));

        // 部分成功は「呼ばれた」事実そのもの——Disarmed では表現できず、OutcomeUnknown だけが正しい。
        var disposition = AttemptFaultClassifier.Classify(
            AttemptFaultPoint.PartialSendInput, ExternalInputCallState.CalledOrUnknown);
        Assert.Equal(AttemptState.OutcomeUnknown, disposition);
        gate.ResolveLocally("attempt-1", disposition);

        // 不変条件: 自動再送しない（呼出は1回だけ）。外部効果回数を1回と仮定しない——
        // 0回保証=Disarmed／報告あり=DispatchReported／partial・unknown=OutcomeUnknown で表現する。
        Assert.Equal(1, calls);
        Assert.Equal(AttemptState.OutcomeUnknown, gate.Get("attempt-1").State);
        // OutcomeUnknown は journal event を持たない（記録なき解決は復元で同じ分類に戻る）。
        Assert.DoesNotContain(store.Events, e => e.PayloadType == RunEventPayloadTypes.Disarm);
        Assert.Equal(AttemptState.OutcomeUnknown, RecoverFrom(store).Get("attempt-1").State);
    }

    // ---- 境界7・8: capture 後〜Confirmed transaction 前の crash ----

    [Fact]
    public void Boundaries_7_8_crash_before_confirmation_never_restores_confirmed()
    {
        var (gate, store) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch, "attempt-1", "command-1"), () => { });
        gate.CommitReported(Event(sequence + 1, RunEventPayloadTypes.DispatchResult, "attempt-1"));
        gate.CommitObserving(Event(sequence + 2, RunEventPayloadTypes.Observation, "attempt-1", observationId: "observation-1"));
        // crash: confirmation の commit 前（境界8。境界7は observation も無い、より弱い形）。

        var recovered = RecoverFrom(store);

        // 不変条件: OutcomeUnknown を Confirmed として扱わない。observation event が存在しても、
        // AttemptId＋ObservationId 併記の confirmation（§6.7 契約4）が無い限り Confirmed へ戻らない。
        Assert.Equal(AttemptState.OutcomeUnknown, recovered.Get("attempt-1").State);
        Assert.Null(recovered.Get("attempt-1").ObservationId);
    }

    // ---- 境界9: 新 Playbook version 作成後、Run 切替前 ----

    [Fact]
    public void Boundary_9_a_failed_switch_commit_leaves_the_pin_unchanged()
    {
        var store = new RecordingStore();
        var journal = new RunJournal(store, new NullSink());
        var gate = new AttemptDispatchGate(journal);
        var controls = new RunControls(journal, gate, RunId,
            PlaybookRun.Start(PlaybookId, PlaybookMaterializer.ToGraph(Version(Version1))));
        controls.Pause();
        controls.RecordObservation(Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1", actorType: RunEventActorType.User));

        store.FailNextAppendWith = new InvalidOperationException("journal 停止");
        Assert.Throws<InvalidOperationException>(() => controls.SwitchVersion(
            Event(2, RunEventPayloadTypes.VersionSwitch, actorType: RunEventActorType.User) with { PlaybookVersionId = "ver-2" },
            Version("ver-2"),
            "step"));

        // 不変条件: active version が勝手に変わらない。switch event が commit されない限り pin は動かず、
        // journal replay の projection も旧 pin を返す。
        Assert.Equal(Version1, controls.Run.PinnedVersionId);
        Assert.Equal(Version1, RunProjection.Replay(store.ReadRun(RunId)).PinnedPlaybookVersionId);
    }

    // ---- 境界10: manual intervention 中、reconcile 前 ----

    [Fact]
    public void Boundary_10_no_observation_can_enter_the_journal_during_an_intervention()
    {
        var store = new RecordingStore();
        var journal = new RunJournal(store, new NullSink());
        var gate = new AttemptDispatchGate(journal);
        var controls = new RunControls(journal, gate, RunId,
            PlaybookRun.Start(PlaybookId, PlaybookMaterializer.ToGraph(Version(Version1))));
        var sequence = PrepareAttempt(gate, "attempt-1", 1);
        gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch, "attempt-1", "command-1"), () => { });
        gate.CommitReported(Event(sequence + 1, RunEventPayloadTypes.DispatchResult, "attempt-1"));
        controls.BeginManualIntervention(Event(sequence + 2, RunEventPayloadTypes.ManualIntervention, actorType: RunEventActorType.User));

        // 介入中は attempt 束縛の観測も journal へ入らない（run-level は t05 で拒否済み・ここは gate 経路の閉鎖）。
        Assert.Throws<InvalidOperationException>(() => controls.CommitAttemptObserving(
            Event(sequence + 3, RunEventPayloadTypes.Observation, "attempt-1", observationId: "observation-1")));

        // 介入終了後も、run-level の再照合が済むまで attempt の観測は進められない（§6.8）。
        controls.EndManualIntervention(Event(sequence + 3, RunEventPayloadTypes.ManualIntervention, actorType: RunEventActorType.User));
        Assert.Throws<InvalidOperationException>(() => controls.CommitAttemptObserving(
            Event(sequence + 4, RunEventPayloadTypes.Observation, "attempt-1", observationId: "observation-1")));

        controls.RecordObservation(Event(sequence + 4, RunEventPayloadTypes.Observation, observationId: "observation-2", actorType: RunEventActorType.User));
        controls.CommitAttemptObserving(Event(sequence + 5, RunEventPayloadTypes.Observation, "attempt-1", observationId: "observation-3"));
        Assert.Equal(AttemptState.Observing, gate.Get("attempt-1").State);

        // crash（reconcile 前）: journal 途切れ→復元は未確定 dispatch を OutcomeUnknown へ。
        Assert.Equal(AttemptState.OutcomeUnknown, RecoverFrom(store).Get("attempt-1").State);
    }

    // ---- 不変条件: duplicate UI command は Attempt 生成前に排除 ----

    [Fact]
    public void A_duplicate_command_never_creates_a_second_attempt()
    {
        var (gate, store) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1, "command-1");

        // UI の二重 command（同じ CommandId の再 proposal）は Attempt 生成前に排除される。
        Assert.Throws<InvalidOperationException>(() => gate.CommitProposed(
            Event(sequence, RunEventPayloadTypes.Proposal, "attempt-dup", "command-1")));
        Assert.Single(gate.Attempts);

        // 他 Attempt が同じ command を dispatch する経路も無い。
        var next = PrepareAttempt(gate, "attempt-2", sequence);
        Assert.Throws<InvalidOperationException>(() => gate.ArmThenDispatch(
            Event(next, RunEventPayloadTypes.Dispatch, "attempt-2", "command-1"), () => { }));

        // 復元後も重複排除は journal の実 event から再構築される。
        var recovered = RecoverFrom(store);
        Assert.Throws<InvalidOperationException>(() => recovered.CommitProposed(
            Event(next, RunEventPayloadTypes.Proposal, "attempt-dup", "command-1")));
    }

    // ---- 不変条件: journal replay projection が保存 projection と一致 ----

    [Fact]
    public void Replay_matches_the_incrementally_built_projection_across_all_fault_event_types()
    {
        var (gate, store) = NewGate();
        var sequence = PrepareAttempt(gate, "attempt-1", 1, "command-1");
        gate.ArmThenDispatch(Event(sequence, RunEventPayloadTypes.Dispatch, "attempt-1", "command-1"), () => { });
        gate.CommitDisarmed(Event(sequence + 1, RunEventPayloadTypes.Disarm, "attempt-1", actorType: RunEventActorType.System));

        var events = store.ReadRun(RunId);
        var incremental = events.Skip(1).Aggregate(RunProjection.FromFirstEvent(events[0]), (p, e) => p.Apply(e));
        var replayed = RunProjection.Replay(events);

        Assert.Equal(incremental, replayed);
        Assert.Equal(1, replayed.Tally.Disarms);
    }
}
