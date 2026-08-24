using OpenLogicool.Contracts.Exploration;

namespace OpenLogicool.Contracts.Playbooks;

public enum VisualMacroExecutionMode
{
    Supervised,
    Verified,
}

public sealed record VisualMacroStep(
    int Sequence,
    string StructureEdgeId,
    string SourceStateId,
    IReadOnlyList<string> SourceSceneSignatureIds,
    string AffordanceCandidateId,
    string LocatorRevision,
    string Primitive,
    string DestinationStateId,
    IReadOnlyList<string> DestinationSceneSignatureIds,
    ExplorationWaitCondition WaitCondition,
    IReadOnlyList<string> RiskTags,
    StructureVerificationState VerificationState);

/// <summary>学習ルートから生成する、通常動作でAIを必要としない画面監査つき有限マクロ。</summary>
public sealed record VisualMacroProgram(
    string SchemaVersion,
    string ProgramId,
    string RouteId,
    string RouteVersionId,
    string GameId,
    string EnvironmentScope,
    string StructureRevisionId,
    VisualMacroExecutionMode ExecutionMode,
    IReadOnlyList<VisualMacroStep> Steps);

public enum VisualMacroAuditPhase
{
    Before,
    After,
}

public enum VisualMacroAuditStatus
{
    Confirmed,
    UnexpectedState,
    Ambiguous,
    Unavailable,
    Stale,
}

public sealed record VisualMacroAuditResult(
    int StepSequence,
    VisualMacroAuditPhase Phase,
    string ExpectedStateId,
    string ObservationId,
    VisualMacroAuditStatus Status,
    string Message)
{
    public bool CanContinue => Status == VisualMacroAuditStatus.Confirmed;
}
