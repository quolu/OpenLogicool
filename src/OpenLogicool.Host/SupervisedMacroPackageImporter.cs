using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

public static class SupervisedMacroPackageImporter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SupervisedMacroPackageDocument Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var document = JsonSerializer.Deserialize<SupervisedMacroPackageDocument>(json, Json)
            ?? throw new ArgumentException("教師付きマクロpackageがnullです。", nameof(json));
        Validate(document);
        return document;
    }

    public static SupervisedMacroPackageImportResult Import(
        SqliteConnection connection,
        SupervisedMacroPackageDocument document)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Validate(document);
        var structures = new SqliteGameStructureStore(connection);
        var mutations = document.Nodes.Select(node => new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertNode, StructureEntityKind.Node,
                node.StateId, [], node, null, null, null, null, null, node.EvidenceIds, "package import"))
            .Concat(document.Edges.Select(edge => new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertEdge, StructureEntityKind.Edge,
                edge.EdgeId, [edge.SourceStateId, edge.DestinationStateId!], null, edge, null, null, null, null,
                edge.EvidenceIds, "package import")))
            .ToArray();
        using var transaction = connection.BeginTransaction();
        var structureEvent = structures.Append(new StructureEventDraft(
            ContractSchemaVersions.Revision03,
            $"{document.PackageId}:structure",
            document.GameId,
            document.EnvironmentScope,
            StructureEventKind.MutationApplied,
            StructureEventActor.User,
            document.PackageId,
            document.PackageId,
            null, null, null,
            document.EvidenceIds,
            StructureEventPayloadTypes.MutationBatch,
            JsonSerializer.Serialize(new StructureMutationBatch(ContractSchemaVersions.Revision03, mutations)),
            null,
            document.CreatedUtc), null, DateTimeOffset.UtcNow, transaction);
        new SqliteLearnedSceneProfileStore(connection).Upsert(document.SceneProfile, transaction);
        var route = new SqliteLearningRouteStore(connection).Append(new LearningRouteDraft(
            ContractSchemaVersions.Revision03,
            document.RouteId,
            null,
            document.GameId,
            document.EnvironmentScope,
            structureEvent.ResultingStructureRevisionId,
            document.Goal,
            document.RouteEdgeIds,
            LearningRouteAuthor.Import,
            "既存の実ゲーム証拠から教師付きで確認",
            $"package {document.PackageId}をimport",
            LearningRouteStatus.Compiled,
            document.CreatedUtc), transaction);
        var structure = GameStructureProjector.Replay(
            document.GameId,
            document.EnvironmentScope,
            [structureEvent]);
        _ = VisualMacroCompiler.Compile(route, structure,
            document.ProhibitedRiskTags);
        transaction.Commit();
        return new SupervisedMacroPackageImportResult(
            document.PackageId,
            structureEvent.ResultingStructureRevisionId,
            route.VersionId,
            document.SceneProfile.ProfileVersion);
    }

    private static void Validate(SupervisedMacroPackageDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        LearnedSceneProfileValidator.Validate(document.SceneProfile);
        if (document.SchemaVersion != ContractSchemaVersions.Revision03
            || string.IsNullOrWhiteSpace(document.PackageId)
            || string.IsNullOrWhiteSpace(document.GameId)
            || string.IsNullOrWhiteSpace(document.EnvironmentScope)
            || document.SceneProfile.GameId != document.GameId
            || document.SceneProfile.EnvironmentScope != document.EnvironmentScope
            || document.Nodes is null || document.Nodes.Count < 2
            || document.Edges is null || document.Edges.Count == 0
            || document.Nodes.Any(node => node.EnvironmentScope != document.EnvironmentScope)
            || string.IsNullOrWhiteSpace(document.RouteId)
            || string.IsNullOrWhiteSpace(document.Goal)
            || document.RouteEdgeIds is null || document.RouteEdgeIds.Count == 0
            || document.RouteEdgeIds.Any(id => document.Edges.All(edge => edge.EdgeId != id))
            || document.ProhibitedRiskTags is null
            || document.EvidenceIds is null || document.EvidenceIds.Count == 0)
        {
            throw new ArgumentException("教師付きマクロpackageの必須fieldまたは参照整合性が不正です。", nameof(document));
        }
    }
}
