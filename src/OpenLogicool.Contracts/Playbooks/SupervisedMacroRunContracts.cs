using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Contracts.Playbooks;

public enum SupervisedMacroRunState
{
    AwaitingBeforeAudit,
    ReadyToDispatch,
    AwaitingAfterAudit,
    Completed,
    Stopped,
    OutcomeUnknown,
}

public enum SupervisedMacroStopReason
{
    None,
    BeforeAuditFailed,
    AfterAuditFailed,
    DispatchFault,
    DispatchNotStarted,
    RuntimeUnavailable,
    ObservationFault,
    UserStopped,
}

public sealed class SupervisedMacroDispatchNotStartedException(string message) : Exception(message);

public enum SupervisedMacroAuthorizationSource
{
    InteractiveUser,
    OwnerDelegatedAutomation,
}

public sealed record SupervisedMacroRunPin(
    string ProgramId,
    string RouteVersionId,
    string StructureRevisionId,
    string GameId,
    string EnvironmentScope);

public sealed record SupervisedMacroStepHistory(
    int StepSequence,
    VisualMacroAuditResult? BeforeAudit,
    string? AttemptId,
    bool DispatchArmed,
    bool DispatchReported,
    VisualMacroAuditResult? AfterAudit);

/// <summary>10の基盤機能が返した操作後10秒間の観測と遷移判定。</summary>
public sealed record SupervisedMacroTransitionObservation(
    GameInteractionStabilityResult Stability,
    GameTransitionComparison Comparison,
    ObservedScene? FinalScene,
    bool DestinationMatched);

/// <summary>教師付きVisual Macro実行の利用者向け投影。入力を許す状態をboolへ丸めず、状態機械をそのまま示す。</summary>
public sealed record SupervisedMacroRunSnapshot(
    string RunId,
    SupervisedMacroRunPin Pin,
    SupervisedMacroRunState State,
    SupervisedMacroStopReason StopReason,
    int CurrentStepSequence,
    int TotalSteps,
    string StatusMessage,
    IReadOnlyList<SupervisedMacroStepHistory> History)
{
    public bool CanAuditBefore => State == SupervisedMacroRunState.AwaitingBeforeAudit;
    public bool CanDispatch => State == SupervisedMacroRunState.ReadyToDispatch;
    public bool CanAuditAfter => State == SupervisedMacroRunState.AwaitingAfterAudit;
}
