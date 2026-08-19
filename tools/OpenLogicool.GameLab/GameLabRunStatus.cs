using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.GameLab;

/// <summary>UX-003 の常時表示9状態。表示は常にこのうち1つに定まる（欠落なし）。</summary>
public enum GameLabRunStatus
{
    /// <summary>提案待ち</summary>
    AwaitingProposal,

    /// <summary>承認待ち</summary>
    AwaitingApproval,

    /// <summary>入力中</summary>
    Dispatching,

    /// <summary>結果確認中</summary>
    ConfirmingResult,

    /// <summary>利用者停止</summary>
    UserStopped,

    /// <summary>対象不一致</summary>
    TargetMismatch,

    /// <summary>認識不能</summary>
    Unrecognized,

    /// <summary>完了</summary>
    Completed,

    /// <summary>失敗</summary>
    Failed,
}

/// <summary>Run の終端。完了と失敗が同時に立つ入力は型として作れない。</summary>
public enum GameLabRunOutcome
{
    Completed,
    Failed,
}

/// <summary>
/// 状態表示の入力。現在 state の根拠は GameLab oracle と fake Observation だけ（Phase 4）——
/// 実画面 capture を表す field は存在しない。
/// </summary>
public sealed record GameLabStatusInput(
    bool Paused,
    bool EmergencyStopped,
    bool TargetMismatch,
    AttemptState? ActiveAttempt,
    ObservationStatus? LatestObservation,
    GameLabRunOutcome? Outcome);

/// <summary>
/// UX-003 の pure 写像。どの入力組合せでも必ず1状態を返す（常時表示＝全域）。
/// 優先順: 利用者停止 ＞ 対象不一致 ＞ 認識不能 ＞ 終端 ＞ Attempt 進行。
/// </summary>
public static class GameLabStatusProjector
{
    public static GameLabRunStatus Project(GameLabStatusInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 利用者の停止操作は他のどの状態よりも先に見える（UX-004 の操作が効いたことの表示）。
        if (input.Paused || input.EmergencyStopped)
        {
            return GameLabRunStatus.UserStopped;
        }

        if (input.TargetMismatch)
        {
            return GameLabRunStatus.TargetMismatch;
        }

        if (input.LatestObservation is ObservationStatus.Unknown or ObservationStatus.Unavailable)
        {
            return GameLabRunStatus.Unrecognized;
        }

        if (input.Outcome is GameLabRunOutcome.Failed)
        {
            return GameLabRunStatus.Failed;
        }

        if (input.Outcome is GameLabRunOutcome.Completed)
        {
            return GameLabRunStatus.Completed;
        }

        return input.ActiveAttempt switch
        {
            AttemptState.Proposed => GameLabRunStatus.AwaitingApproval,
            AttemptState.Authorized or AttemptState.Prepared or AttemptState.DispatchArmed
                => GameLabRunStatus.Dispatching,
            AttemptState.DispatchReported or AttemptState.Observing or AttemptState.OutcomeUnknown
                or AttemptState.Reconciling or AttemptState.NeedsUserDecision
                => GameLabRunStatus.ConfirmingResult,
            // Attempt が無い・終端済み → 次の提案待ち。
            _ => GameLabRunStatus.AwaitingProposal,
        };
    }
}
