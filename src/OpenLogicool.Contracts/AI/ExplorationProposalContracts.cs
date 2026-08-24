using OpenLogicool.Contracts.Exploration;

namespace OpenLogicool.Contracts.AI;

public sealed record ExplorationProposal(
    string SchemaVersion,
    string ProposalId,
    string SourceObservationId,
    string SourceStructureRevisionId,
    string AffordanceCandidateId,
    string Primitive,
    string ProbeHypothesis,
    IReadOnlyList<ExplorationOutcomeKind> AllowedOutcomes,
    ExplorationWaitCondition WaitCondition,
    IReadOnlyList<string> StopConditions);

public enum StructureDeltaKind
{
    CreateNode,
    AttributeEdge,
    ExtractFact,
    MergeNodes,
    SplitNode,
    Relabel,
    Retire,
}

public sealed record StructureDeltaOperation(
    string SchemaVersion,
    StructureDeltaKind Kind,
    string SubjectId,
    string? RelatedId,
    string? ProposedLabel,
    string? FactType,
    string? FactValue);

public sealed record StructureDeltaProposal(
    string SchemaVersion,
    string ProposalId,
    string SourceStructureRevisionId,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<StructureDeltaOperation> Operations);
