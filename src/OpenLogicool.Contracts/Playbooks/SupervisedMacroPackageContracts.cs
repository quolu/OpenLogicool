using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Contracts.Playbooks;

/// <summary>学習済み構造・画面照合data・初期routeを製品DBへ投入するgame非依存package。</summary>
public sealed record SupervisedMacroPackageDocument(
    string SchemaVersion,
    string PackageId,
    string GameId,
    string EnvironmentScope,
    LearnedSceneProfileDocument SceneProfile,
    IReadOnlyList<StructureScreenNode> Nodes,
    IReadOnlyList<StructureScreenEdge> Edges,
    string RouteId,
    string Goal,
    IReadOnlyList<string> RouteEdgeIds,
    IReadOnlyList<string> ProhibitedRiskTags,
    IReadOnlyList<string> EvidenceIds,
    DateTimeOffset CreatedUtc);

public sealed record SupervisedMacroPackageImportResult(
    string PackageId,
    string StructureRevisionId,
    string RouteVersionId,
    string SceneProfileVersion);
