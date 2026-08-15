using System.Text.Json.Serialization;

namespace OpenLogicool.Contracts.Playbooks;

public sealed record PlannerBudget(
    string SchemaVersion,
    [property: JsonPropertyName("proposals")]
    int RemainingProposals,
    [property: JsonPropertyName("costUsd")]
    decimal? RemainingCostUsd);

public sealed record PlannerContext(
    string SchemaVersion,
    string Goal,
    [property: JsonPropertyName("observationId")]
    string? CurrentObservationId,
    IReadOnlyList<string> AllowedActionIds,
    string HistorySummary,
    [property: JsonPropertyName("budgetRemaining")]
    PlannerBudget Budget);

public sealed record PlaybookNode(
    string SchemaVersion,
    string NodeId);

public sealed record PlaybookEdge(
    string SchemaVersion,
    string EdgeId,
    string FromNodeId,
    string ToNodeId);

public sealed record PlaybookVersion(
    string SchemaVersion,
    string VersionId,
    string? ParentVersionId,
    IReadOnlyList<PlaybookNode> Nodes,
    IReadOnlyList<PlaybookEdge> Edges,
    string ChangeReason);

public enum RunEventActorType
{
    User,
    Automation,
    System,
}

public sealed record RunEvent(
    string SchemaVersion,
    string EventId,
    string RunId,
    long RunSequence,
    string PlaybookId,
    string PlaybookVersionId,
    string? NodeOrTransitionId,
    string? CommandId,
    string? AttemptId,
    string CausationId,
    string CorrelationId,
    long ExecutorEpoch,
    RunEventActorType ActorType,
    DateTimeOffset OccurredUtc,
    DateTimeOffset PersistedUtc,
    string? ObservationId,
    string PayloadType,
    string PayloadJson);
