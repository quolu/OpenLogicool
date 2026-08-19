namespace OpenLogicool.Contracts.Playbooks;

/// <summary>
/// Durable Attempt の状態（§6.7）。遷移の許可は Domain の DurableAttempt が持ち、
/// この enum は wire・journal payload・表示のための語彙だけを提供する。
/// </summary>
public enum AttemptState
{
    Proposed,
    Authorized,
    Prepared,
    DispatchArmed,
    DispatchReported,
    Observing,
    Confirmed,
    Rejected,
    OutcomeUnknown,
    Cancelled,
    Disarmed,
    Reconciling,
    NeedsUserDecision,
    UserResolvedSuccess,
    UserResolvedFailure,
    Abandoned,
}
