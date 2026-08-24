using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Contracts.Exploration;

public sealed record ExplorationBudget(
    string SchemaVersion,
    int RemainingProbes,
    long RemainingInferenceMilliseconds,
    long RemainingElapsedMilliseconds);

public sealed record ExplorationStopPolicy(
    string SchemaVersion,
    long MaximumFrameFreshnessMilliseconds,
    int MaximumRepeatedProbeCount,
    int MaximumConsecutiveNoProgressCount,
    int MaximumOscillationCount);

public sealed record ExplorationPolicy(
    string SchemaVersion,
    string PolicyRevisionId,
    string ApplicationIdentity,
    string TargetWindowSourceId,
    string EnvironmentScope,
    string ExplorationScope,
    IReadOnlyList<string> AllowedPrimitives,
    IReadOnlyList<string> ProhibitedRiskTags,
    ExplorationBudget Budget,
    bool OneStepApprovalRequired,
    string ConsentRevisionId,
    string RecoveryBoundary,
    ExplorationStopPolicy StopPolicy,
    IReadOnlyList<string> StopConditions);

public sealed record ExplorationContext(
    string SchemaVersion,
    string ContextId,
    ExplorationPolicy Policy,
    ObservedScene CurrentScene,
    string StructureRevisionId,
    IReadOnlyList<string> FrontierIds,
    IReadOnlyList<string> KnownReturnPathEdgeIds,
    ExplorationBudget RemainingBudget);

public enum ExplorationOutcomeKind
{
    Destination,
    Novel,
    NoChange,
    Ambiguous,
    Unavailable,
    Fault,
    OutcomeUnknown,
}

public sealed record ExplorationWaitCondition(
    string SchemaVersion,
    int StableFrames,
    long MinimumStableMilliseconds,
    long TimeoutMilliseconds);

public sealed record TransitionEvidence(
    string SchemaVersion,
    string EvidenceId,
    string BeforeObservationId,
    string AfterObservationId,
    string AttemptId,
    string AffordanceCandidateId,
    string Primitive,
    ExplorationOutcomeKind Outcome,
    string EnvironmentScope,
    long DispatchMonotonicMilliseconds,
    long ObservationCompletedMonotonicMilliseconds,
    DateTimeOffset RecordedUtc);

public enum StructureVerificationState
{
    Candidate,
    Replayed,
    Verified,
    Retired,
}

public sealed record GameStateFact(
    string SchemaVersion,
    string FactId,
    string FactType,
    string Value,
    string ExtractorId,
    IReadOnlyList<string> EvidenceIds,
    double Confidence,
    string ValidityScope,
    string ResetScope,
    StructureVerificationState VerificationState,
    string? EnvironmentScope = null,
    string? CreatedRevisionId = null,
    string? UpdatedRevisionId = null,
    bool Retired = false);

public sealed record GameStructureRevision(
    string SchemaVersion,
    string RevisionId,
    string? ParentRevisionId,
    long ThroughEvidenceSequence,
    StructureScreenGraph ScreenGraph,
    IReadOnlyList<GameStateFact> StateFacts,
    IReadOnlyList<StructureDispatchProjection> Dispatches,
    string EnvironmentScope,
    DateTimeOffset CreatedUtc);
