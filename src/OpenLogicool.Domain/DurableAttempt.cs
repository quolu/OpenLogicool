using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Domain;

/// <summary>
/// Durable Attempt state machine（§6.7・PB-003/004）の pure model。
/// 許可される遷移は §6.7 の遷移図の写しだけで、終端からの遷移は存在しない。
/// Confirmed へは同じ Attempt を参照する Observation の ID が必須（契約4）。
/// Input API の戻り値を受け取る口は無く、成功戻り値で状態が進むことはない（契約3）。
/// </summary>
public sealed class DurableAttempt
{
    // §6.7 の遷移図の写し。ここに無い遷移は存在しない。
    private static readonly IReadOnlyDictionary<AttemptState, AttemptState[]> AllowedTransitions =
        new Dictionary<AttemptState, AttemptState[]>
        {
            [AttemptState.Proposed] = [AttemptState.Authorized, AttemptState.Cancelled],
            [AttemptState.Authorized] = [AttemptState.Prepared, AttemptState.Cancelled],
            [AttemptState.Prepared] = [AttemptState.DispatchArmed, AttemptState.Cancelled],
            [AttemptState.DispatchArmed] = [AttemptState.DispatchReported, AttemptState.Disarmed, AttemptState.OutcomeUnknown],
            [AttemptState.DispatchReported] = [AttemptState.Observing, AttemptState.OutcomeUnknown],
            [AttemptState.Observing] = [AttemptState.Confirmed, AttemptState.Rejected, AttemptState.OutcomeUnknown],
            [AttemptState.OutcomeUnknown] = [AttemptState.Reconciling],
            [AttemptState.Reconciling] = [AttemptState.Confirmed, AttemptState.Rejected, AttemptState.NeedsUserDecision, AttemptState.Abandoned],
            [AttemptState.NeedsUserDecision] = [AttemptState.UserResolvedSuccess, AttemptState.UserResolvedFailure, AttemptState.Abandoned],
            [AttemptState.Confirmed] = [],
            [AttemptState.Rejected] = [],
            [AttemptState.Cancelled] = [],
            [AttemptState.Disarmed] = [],
            [AttemptState.UserResolvedSuccess] = [],
            [AttemptState.UserResolvedFailure] = [],
            [AttemptState.Abandoned] = [],
        };

    private static readonly IReadOnlySet<AttemptState> TerminalStates = new HashSet<AttemptState>
    {
        AttemptState.Confirmed,
        AttemptState.Rejected,
        AttemptState.Cancelled,
        AttemptState.Disarmed,
        AttemptState.UserResolvedSuccess,
        AttemptState.UserResolvedFailure,
        AttemptState.Abandoned,
    };

    private DurableAttempt(string attemptId, AttemptState state, string? observationId)
    {
        AttemptId = attemptId;
        State = state;
        ObservationId = observationId;
    }

    public static DurableAttempt Propose(string attemptId)
    {
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            throw new ArgumentException("AttemptId が空です。", nameof(attemptId));
        }

        return new DurableAttempt(attemptId, AttemptState.Proposed, observationId: null);
    }

    /// <summary>
    /// 再起動復元（OPS-008・§6.7 契約2）で journal から取り出した状態をそのまま実体化する。
    /// 新規 Attempt は Propose から始める——この口は復元専用であり、遷移検証を迂回する近道にしない。
    /// </summary>
    public static DurableAttempt Restore(string attemptId, AttemptState state, string? observationId)
    {
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            throw new ArgumentException("AttemptId が空です。", nameof(attemptId));
        }

        if (state == AttemptState.Confirmed && observationId is null)
        {
            throw new ArgumentException("Confirmed の復元には ObservationId が必要です（§6.7 契約4）。", nameof(observationId));
        }

        return new DurableAttempt(attemptId, state, observationId);
    }

    public string AttemptId { get; }

    public AttemptState State { get; }

    /// <summary>Confirmed の根拠 Observation（契約4）。Confirmed 以外では遷移時に渡されたものを保持しない。</summary>
    public string? ObservationId { get; }

    public bool IsTerminal => TerminalStates.Contains(State);

    /// <summary>
    /// dispatch し得た後の未解決か（PB-004 の監視対象）。この間は次の dispatch を自動生成しない（契約5）。
    /// </summary>
    public bool IsUnresolvedAfterArm => !IsTerminal && State is not (AttemptState.Proposed or AttemptState.Authorized or AttemptState.Prepared);

    public DurableAttempt TransitionTo(AttemptState next, string? observationId = null)
    {
        if (!AllowedTransitions[State].Contains(next))
        {
            throw new InvalidOperationException($"Attempt '{AttemptId}' の {State} から {next} への遷移は §6.7 に存在しません。");
        }

        if (next == AttemptState.Confirmed && observationId is null)
        {
            throw new InvalidOperationException($"Attempt '{AttemptId}' を Confirmed にするには同じ Attempt を参照する Observation が必要です（§6.7 契約4）。");
        }

        if (next != AttemptState.Confirmed && observationId is not null)
        {
            throw new ArgumentException($"ObservationId は Confirmed への遷移だけが受け取ります（指定先: {next}）。", nameof(observationId));
        }

        return new DurableAttempt(AttemptId, next, next == AttemptState.Confirmed ? observationId : ObservationId);
    }

    /// <summary>
    /// crash 再開時の分類（§6.7 契約2）: DispatchArmed 以降の未解決は実際に未送信でも OutcomeUnknown、
    /// crash 境界が Prepared 以前（外部入力呼出前が確定）なら Cancelled、終端はそのまま。
    /// </summary>
    public static AttemptState RecoveryStateFor(AttemptState stateAtCrash)
    {
        if (TerminalStates.Contains(stateAtCrash))
        {
            return stateAtCrash;
        }

        return stateAtCrash is AttemptState.Proposed or AttemptState.Authorized or AttemptState.Prepared
            ? AttemptState.Cancelled
            : AttemptState.OutcomeUnknown;
    }
}
