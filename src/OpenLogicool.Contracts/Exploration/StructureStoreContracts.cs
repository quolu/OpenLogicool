using System.Security.Cryptography;
using System.Text;

namespace OpenLogicool.Contracts.Exploration;

public enum StructureEventKind
{
    ObservationRecorded,
    ProbeProposed,
    ProbeApproved,
    DispatchArmed,
    OutcomeRecorded,
    DeltaAccepted,
    MutationApplied,
    VerificationAccepted,
    CorrectionApplied,
    ManualInterventionRecorded,
}

public enum StructureEventActor
{
    Controller,
    User,
    Automation,
}

public static class StructureEventPayloadTypes
{
    public const string None = "none";
    public const string Observation = "observed-scene";
    public const string ExplorationProposal = "exploration-proposal";
    public const string ExplorationApproval = "exploration-approval";
    public const string StructureDelta = "structure-delta-proposal";
    public const string MutationBatch = "structure-mutation-batch";
    public const string TransitionEvidence = "transition-evidence";
    public const string StructureVerification = "structure-verification";
}

public sealed record StructureEventDraft(
    string SchemaVersion,
    string EventId,
    string GameId,
    string EnvironmentScope,
    StructureEventKind Kind,
    StructureEventActor Actor,
    string CorrelationId,
    string CausationId,
    string? ObservationId,
    string? ProposalId,
    string? AttemptId,
    IReadOnlyList<string> EvidenceIds,
    string PayloadType,
    string PayloadJson,
    ExplorationOutcomeKind? Outcome,
    DateTimeOffset OccurredUtc);

public sealed record StructureEvent(
    string SchemaVersion,
    string EventId,
    string GameId,
    string EnvironmentScope,
    long Sequence,
    string? ParentStructureRevisionId,
    string ResultingStructureRevisionId,
    StructureEventKind Kind,
    StructureEventActor Actor,
    string CorrelationId,
    string CausationId,
    string? ObservationId,
    string? ProposalId,
    string? AttemptId,
    IReadOnlyList<string> EvidenceIds,
    string PayloadType,
    string PayloadJson,
    ExplorationOutcomeKind? Outcome,
    DateTimeOffset OccurredUtc,
    DateTimeOffset PersistedUtc);

public static class StructureRevisionIds
{
    public static string Next(string? parentRevisionId, string eventId, long sequence)
    {
        var material = $"{parentRevisionId ?? "root"}\n{sequence}\n{eventId}";
        return $"structure:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()}";
    }
}

public sealed record StructureScreenNode(
    string SchemaVersion,
    string StateId,
    string EnvironmentScope,
    IReadOnlyList<string> SceneSignatureIds,
    IReadOnlyList<string> VariantStateIds,
    IReadOnlyList<string> EvidenceIds,
    string? ProvisionalLabel,
    StructureVerificationState VerificationState,
    string? CreatedRevisionId = null,
    string? UpdatedRevisionId = null,
    bool Retired = false);

public sealed record StructureOutcomeCount(
    ExplorationOutcomeKind Outcome,
    int Count);

public sealed record StructureScreenEdge(
    string SchemaVersion,
    string EdgeId,
    string SourceStateId,
    string? DestinationStateId,
    string? SourceHypothesisId,
    string AffordanceCandidateId,
    string LocatorRevision,
    string Primitive,
    string Guard,
    IReadOnlyList<string> RiskTags,
    bool Reversible,
    string BeforeObservationId,
    string? AfterObservationId,
    ExplorationWaitCondition WaitCondition,
    IReadOnlyList<StructureOutcomeCount> OutcomeCounts,
    IReadOnlyList<string> EvidenceIds,
    StructureVerificationState VerificationState,
    string? CreatedRevisionId = null,
    string? UpdatedRevisionId = null,
    bool Retired = false,
    string? TargetSemanticKey = null,
    IReadOnlyList<double>? TargetNormalizedBounds = null);

public sealed record StructureContradiction(
    string SchemaVersion,
    string ContradictionId,
    IReadOnlyList<string> SubjectIds,
    IReadOnlyList<string> EvidenceIds,
    string Reason,
    string? ResolvedByEventId = null);

public sealed record StructureScreenGraph(
    string SchemaVersion,
    string GraphVersionId,
    IReadOnlyList<StructureScreenNode> Nodes,
    IReadOnlyList<StructureScreenEdge> Edges,
    IReadOnlyList<StructureContradiction> Contradictions,
    string EnvironmentScope);

public enum StructureMutationKind
{
    UpsertNode,
    UpsertEdge,
    UpsertFact,
    RelabelNode,
    MergeNodes,
    SplitNode,
    ReattributeEdge,
    RetireEntity,
    ChangeVerification,
    RecordContradiction,
}

public enum StructureEntityKind
{
    Node,
    Edge,
    Fact,
}

public sealed record StructureMutation(
    string SchemaVersion,
    StructureMutationKind Kind,
    StructureEntityKind EntityKind,
    string SubjectId,
    IReadOnlyList<string> RelatedIds,
    StructureScreenNode? Node,
    StructureScreenEdge? Edge,
    GameStateFact? Fact,
    string? Label,
    StructureVerificationState? VerificationState,
    StructureContradiction? Contradiction,
    IReadOnlyList<string> EvidenceIds,
    string Reason);

public sealed record StructureMutationBatch(
    string SchemaVersion,
    IReadOnlyList<StructureMutation> Mutations);

public sealed record StructureDispatchProjection(
    string AttemptId,
    string CorrelationId,
    ExplorationOutcomeKind Outcome,
    string? EvidenceId);

public sealed record StructureKnowledgePackExport(
    string SchemaVersion,
    string ExportId,
    string GameId,
    string EnvironmentScope,
    GameStructureRevision Revision,
    IReadOnlyList<StructureEvent> Events,
    DateTimeOffset CreatedUtc);

public interface IGameStructureStore
{
    StructureEvent Append(StructureEventDraft draft, string? expectedParentRevisionId, DateTimeOffset persistedUtc);

    IReadOnlyList<StructureEvent> ReadEvents(string gameId, string environmentScope);

    IReadOnlyList<string> ListGameIds();

    GameStructureRevision LoadRevision(string gameId, string environmentScope);

    StructureKnowledgePackExport Export(string gameId, string environmentScope, DateTimeOffset createdUtc);
}
