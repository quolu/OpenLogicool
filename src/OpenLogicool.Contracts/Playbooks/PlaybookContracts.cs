using System.Text.Json.Serialization;

namespace OpenLogicool.Contracts.Playbooks;

public sealed record PlannerBudget(
    string SchemaVersion,
    [property: JsonPropertyName("proposals")]
    int RemainingProposals,
    [property: JsonPropertyName("inferenceMs")]
    long? RemainingInferenceMilliseconds);

public sealed record PlannerContext(
    string SchemaVersion,
    string Goal,
    [property: JsonPropertyName("observationId")]
    string? CurrentObservationId,
    IReadOnlyList<string> AllowedActionIds,
    string HistorySummary,
    [property: JsonPropertyName("budgetRemaining")]
    PlannerBudget Budget);

/// <summary>
/// Playbook graph の node（PB-001）。前提・状態・Semantic Action・期待結果を持つ。
/// IsEntry が入口。SemanticActionId は無い node を許すが、あるなら空にしない。
/// </summary>
public sealed record PlaybookNode(
    string SchemaVersion,
    string NodeId,
    bool IsEntry,
    string? StateId,
    IReadOnlyList<string> Preconditions,
    string? SemanticActionId,
    IReadOnlyList<string> ExpectedOutcomes);

/// <summary>node 間の分岐（PB-001）。BranchCondition が無ければ無条件遷移。</summary>
public sealed record PlaybookEdge(
    string SchemaVersion,
    string EdgeId,
    string FromNodeId,
    string ToNodeId,
    string? BranchCondition);

/// <summary>
/// immutable な Playbook 1版（PB-002／008）。訂正は ParentVersionId 付きの新版を作り、この record を書き換えない。
/// </summary>
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
