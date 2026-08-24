using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Contracts.Exploration;

public enum ExplorationRiskLevel
{
    Unknown,
    Low,
    Elevated,
    Prohibited,
}

public sealed record ExplorationRiskAssessment(
    string SchemaVersion,
    string AffordanceCandidateId,
    ExplorationRiskLevel Level,
    IReadOnlyList<string> RiskTags,
    bool Reversible,
    bool SideEffectFree,
    IReadOnlyList<string> RecoveryEdgeIds,
    string AssessorRevisionId);

public sealed record ExplorationApproval(
    string SchemaVersion,
    string ApprovalId,
    string ProposalId,
    string ObservationId,
    string PolicyRevisionId,
    string StructureRevisionId,
    string ActorId,
    DateTimeOffset ApprovedUtc);

public enum ExplorationAdmissionStatus
{
    Allowed,
    NeedsApproval,
    Rejected,
    Stopped,
}

public enum ExplorationStopReason
{
    None,
    SchemaMismatch,
    PolicyMismatch,
    SourceRevisionMismatch,
    CaptureUnavailable,
    StaleFrame,
    TargetNotCurrent,
    TargetWindowMismatch,
    PrimitiveNotAllowed,
    RiskProhibited,
    ApprovalRequired,
    ApprovalMismatch,
    BudgetExhausted,
    RecoveryMissing,
    GamePolicyDisabled,
    ScopeViolation,
    RepeatedProbe,
    NoProgress,
    Oscillation,
    StabilityInsufficient,
    RecoveryLost,
}

public sealed record ExplorationAdmissionDecision(
    ExplorationAdmissionStatus Status,
    ExplorationStopReason Reason,
    string Detail,
    bool DispatchAllowed);

public sealed record ExplorationRunBinding(
    string SchemaVersion,
    string ExplorationRunId,
    string GameId,
    string EnvironmentScope,
    string PlaybookId,
    string PlaybookVersionId,
    long ExecutorEpoch);

public sealed record ExplorationProposalAdmission(
    ExplorationContext Context,
    ExplorationProposal Proposal,
    ExplorationRiskAssessment Risk,
    bool GamePolicyAllowsExplore,
    bool WithinExplorationScope,
    long ElapsedMilliseconds,
    long InferenceMilliseconds);

public sealed record ExplorationOutcomeReport(
    string SchemaVersion,
    string ProposalId,
    ObservedScene AfterScene,
    ExplorationOutcomeKind Outcome,
    int StableFramesObserved,
    long StableMillisecondsObserved,
    string TransitionEvidenceId,
    long DispatchMonotonicMilliseconds,
    long ObservationCompletedMonotonicMilliseconds,
    DateTimeOffset RecordedUtc);

public sealed record MaterializedStructureDeltaOperation(
    StructureDeltaOperation ProposalOperation,
    StructureMutation Mutation);

public sealed record StructureDeltaCommitRequest(
    string SchemaVersion,
    StructureDeltaProposal Proposal,
    IReadOnlyDictionary<string, string> StableIdByProposalAlias,
    IReadOnlyList<MaterializedStructureDeltaOperation> Operations,
    string CorrelationId,
    string CausationId,
    DateTimeOffset OccurredUtc,
    DateTimeOffset PersistedUtc);
