using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;

namespace OpenLogicool.Playbooks;

/// <summary>
/// Attempt の dispatch 統括（PB-003/004/005・§6.7。dispatch 依頼の統括は Playbooks——§6.3）。
///
/// - Attempt（proposal／approval）と DispatchArmed（dispatch）を journal へ commit してから
///   外部入力 delegate を呼ぶ。順序は ArmThenDispatch の構造が強制する（PB-003）。
/// - 外部入力の例外・戻り値で状態を変えない。DispatchReported へ進むのは CommitReported だけで、
///   Input API の成功は Confirmed の証拠にならない（§6.7 契約3）。失敗時の自動再送は存在しない（PB-004）。
/// - journal append と外部入力は別ステップであり、同一 transaction を共有する口が無い（PB-005・契約6/7）。
///   外部入力が失敗しても commit 済みの dispatch event は巻き戻らない。
/// - dispatch し得た未解決 Attempt が居る間、次の ArmThenDispatch を拒否する（契約5）。
/// - Recover は journal の実 event だけから Attempt を再分類する（OPS-008・契約2）:
///   confirmation 済み→Confirmed、dispatch 済み未確定→実際に未送信でも OutcomeUnknown、
///   dispatch 前（proposal／approval のみ）→Cancelled。journal に解決が記録されていない Attempt は
///   未解決として復元される——記録の無い解決を信じない安全側の分類である。
/// </summary>
public sealed class AttemptDispatchGate
{
    private readonly RunJournal _journal;
    private readonly Dictionary<string, DurableAttempt> _attempts = new(StringComparer.Ordinal);

    public AttemptDispatchGate(RunJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    public IReadOnlyCollection<DurableAttempt> Attempts => _attempts.Values;

    public DurableAttempt Get(string attemptId) =>
        _attempts.TryGetValue(attemptId, out var attempt)
            ? attempt
            : throw new InvalidOperationException($"Attempt '{attemptId}' はこの gate に登録されていません。");

    /// <summary>proposal event を journal へ commit し、Attempt を Proposed で登録する。</summary>
    public void CommitProposed(RunEvent proposalEvent)
    {
        var attemptId = RequireAttemptId(proposalEvent, RunEventPayloadTypes.Proposal);
        if (_attempts.ContainsKey(attemptId))
        {
            throw new InvalidOperationException($"Attempt '{attemptId}' は既に登録済みです。前提が変わった Attempt は再利用せず、新しい AttemptId で作り直します（§6.7 契約8）。");
        }

        _journal.Append(proposalEvent);
        _attempts[attemptId] = DurableAttempt.Propose(attemptId);
    }

    /// <summary>approval event を journal へ commit し、Proposed→Authorized。</summary>
    public void CommitAuthorized(RunEvent approvalEvent)
    {
        var attemptId = RequireAttemptId(approvalEvent, RunEventPayloadTypes.Approval);
        var next = Get(attemptId).TransitionTo(AttemptState.Authorized);
        _journal.Append(approvalEvent);
        _attempts[attemptId] = next;
    }

    /// <summary>Authorized→Prepared（gate 内遷移。§6.7 契約2 で Prepared 以前は Cancelled へ倒れるため、journal event は持たない）。</summary>
    public void MarkPrepared(string attemptId)
    {
        _attempts[attemptId] = Get(attemptId).TransitionTo(AttemptState.Prepared);
    }

    /// <summary>
    /// PB-003 の中核: dispatch event（DispatchArmed の commit）を journal へ append し、
    /// 成功した後にだけ外部入力 delegate を呼ぶ。外部入力の例外はそのまま伝播し、
    /// Attempt は DispatchArmed のまま＝未解決として残る（PB-004: 自動再送しない）。
    /// </summary>
    public void ArmThenDispatch(RunEvent dispatchEvent, Action externalInput)
    {
        ArgumentNullException.ThrowIfNull(externalInput);
        var attemptId = RequireAttemptId(dispatchEvent, RunEventPayloadTypes.Dispatch);
        var attempt = Get(attemptId);

        var unresolved = _attempts.Values.FirstOrDefault(candidate => candidate.IsUnresolvedAfterArm);
        if (unresolved is not null)
        {
            throw new InvalidOperationException(
                $"未解決の Attempt '{unresolved.AttemptId}'（{unresolved.State}）がある間、次の dispatch を生成できません（§6.7 契約5）。");
        }

        var armed = attempt.TransitionTo(AttemptState.DispatchArmed);
        _journal.Append(dispatchEvent);
        _attempts[attemptId] = armed;

        externalInput();
    }

    /// <summary>dispatch-result event を commit し、DispatchArmed→DispatchReported。報告だけが状態を進める（契約3）。</summary>
    public void CommitReported(RunEvent resultEvent)
    {
        var attemptId = RequireAttemptId(resultEvent, RunEventPayloadTypes.DispatchResult);
        var next = Get(attemptId).TransitionTo(AttemptState.DispatchReported);
        _journal.Append(resultEvent);
        _attempts[attemptId] = next;
    }

    /// <summary>observation event を commit し、DispatchReported→Observing。</summary>
    public void CommitObserving(RunEvent observationEvent)
    {
        var attemptId = RequireAttemptId(observationEvent, RunEventPayloadTypes.Observation);
        var next = Get(attemptId).TransitionTo(AttemptState.Observing);
        _journal.Append(observationEvent);
        _attempts[attemptId] = next;
    }

    /// <summary>
    /// confirmation event（AttemptId＋ObservationId 併記——journal が検証する）を commit し、
    /// Observing→Confirmed。Confirmed の根拠 Observation は event のものだけ（§6.7 契約4）。
    /// </summary>
    public void CommitConfirmed(RunEvent confirmationEvent)
    {
        var attemptId = RequireAttemptId(confirmationEvent, RunEventPayloadTypes.Confirmation);
        var next = Get(attemptId).TransitionTo(AttemptState.Confirmed, confirmationEvent.ObservationId);
        _journal.Append(confirmationEvent);
        _attempts[attemptId] = next;
    }

    /// <summary>
    /// dispatch の外部効果を伴わない解決・進行（Rejected／Disarmed／OutcomeUnknown／Reconciling 等）を
    /// 台帳へ反映する。遷移の可否は Domain がそのまま検証する。journal への解決 event 表現は
    /// run controls（t05）・fault matrix（t07)で確定するため、この口は journal を書かない——
    /// 記録されない解決は Recover で未解決（OutcomeUnknown）へ戻る。
    /// </summary>
    public void ResolveLocally(string attemptId, AttemptState next)
    {
        _attempts[attemptId] = Get(attemptId).TransitionTo(next);
    }

    /// <summary>
    /// 再起動復元（OPS-008・§6.7 契約2）: journal の実 event だけから Attempt を再分類して gate を作る。
    /// abandon event（PB-007・t05）のある Run の Attempt は、RunControls.Abandon と同じ分類で終端へ復元する:
    /// confirmation 済みは Confirmed のまま、dispatch し得た未確定は Abandoned、dispatch 前は Cancelled。
    /// </summary>
    public static AttemptDispatchGate Recover(IRunJournalStore store, RunJournal journal)
    {
        ArgumentNullException.ThrowIfNull(store);
        var gate = new AttemptDispatchGate(journal);

        var allEvents = store.ListRunIds().SelectMany(store.ReadRun).ToList();
        var abandonedRunIds = allEvents
            .Where(runEvent => runEvent.PayloadType == RunEventPayloadTypes.Abandon)
            .Select(runEvent => runEvent.RunId)
            .ToHashSet(StringComparer.Ordinal);

        var eventsByAttempt = allEvents
            .Where(runEvent => runEvent.AttemptId is not null)
            .GroupBy(runEvent => runEvent.AttemptId!, StringComparer.Ordinal);

        foreach (var attemptEvents in eventsByAttempt)
        {
            var confirmation = attemptEvents.FirstOrDefault(e => e.PayloadType == RunEventPayloadTypes.Confirmation);
            if (confirmation is not null)
            {
                gate._attempts[attemptEvents.Key] = DurableAttempt.Restore(
                    attemptEvents.Key, AttemptState.Confirmed, confirmation.ObservationId);
                continue;
            }

            var dispatched = attemptEvents.Any(e => e.PayloadType == RunEventPayloadTypes.Dispatch);
            var abandoned = attemptEvents.Any(e => abandonedRunIds.Contains(e.RunId));
            if (abandoned)
            {
                gate._attempts[attemptEvents.Key] = DurableAttempt.Restore(
                    attemptEvents.Key,
                    dispatched ? AttemptState.Abandoned : AttemptState.Cancelled,
                    observationId: null);
                continue;
            }

            gate._attempts[attemptEvents.Key] = DurableAttempt.Restore(
                attemptEvents.Key,
                dispatched
                    ? DurableAttempt.RecoveryStateFor(AttemptState.DispatchArmed)
                    : DurableAttempt.RecoveryStateFor(AttemptState.Proposed),
                observationId: null);
        }

        return gate;
    }

    private static string RequireAttemptId(RunEvent runEvent, string expectedPayloadType)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        if (runEvent.PayloadType != expectedPayloadType)
        {
            throw new ArgumentException(
                $"この操作は payload type '{expectedPayloadType}' の event だけを受け取ります（実際: '{runEvent.PayloadType}'）。", nameof(runEvent));
        }

        return runEvent.AttemptId
            ?? throw new ArgumentException("Attempt gate を通る event には AttemptId が必要です。", nameof(runEvent));
    }
}
