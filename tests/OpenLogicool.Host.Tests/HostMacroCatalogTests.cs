using Microsoft.Data.Sqlite;
using System.Text.Json;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostMacroCatalogTests
{
    [Fact]
    public void Catalog_lists_latest_versions_and_composition_survives_reopen()
    {
        using var connection = Open();
        SeedStructure(connection);
        var routes = new SqliteLearningRouteStore(connection);
        var first = routes.Append(Draft("macro-a", "A", ["edge-1"]));
        _ = routes.Append(Draft("macro-a", "A updated", ["edge-1"], first.VersionId));
        var second = routes.Append(Draft("macro-b", "B", ["edge-2"]));
        var catalog = new HostMacroCatalog(connection, new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var listed = catalog.ListMacros();
        var composed = catalog.Compose(new MacroCompositionRequest(
            "AからB", [
                new MacroVersionReference("macro-a", null, MacroPlaybackMode.AiFree),
                new MacroVersionReference("macro-b", second.VersionId, MacroPlaybackMode.AiMonitored),
            ]));

        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, item => item.RouteId == "macro-a" && item.RevisionNumber == 2);
        Assert.Equal(2, composed.StepCount);
        var restored = routes.LoadLatest(composed.RouteId)!;
        Assert.Equal(["edge-1", "edge-2"], restored.EdgeIds);
        Assert.Equal(2, routes.ReadRevisions("macro-a").Count);
    }

    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static LearningRouteDraft Draft(string id, string goal, IReadOnlyList<string> edges, string? parent = null) => new(
        ContractSchemaVersions.Revision03, id, parent, "game", "env", "structure:seed", goal, edges,
        LearningRouteAuthor.Ai, null, "seed", LearningRouteStatus.Compiled, DateTimeOffset.UnixEpoch);

    private static void SeedStructure(SqliteConnection connection)
    {
        var nodes = new[] { Node("state-1"), Node("state-2"), Node("state-3") };
        var edges = new[] { Edge("edge-1", "state-1", "state-2"), Edge("edge-2", "state-2", "state-3") };
        var mutations = nodes.Select(node => new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertNode, StructureEntityKind.Node,
                node.StateId, [], node, null, null, null, null, null, node.EvidenceIds, "test seed"))
            .Concat(edges.Select(edge => new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertEdge, StructureEntityKind.Edge,
                edge.EdgeId, [edge.SourceStateId, edge.DestinationStateId!], null, edge, null, null, null, null,
                edge.EvidenceIds, "test seed")))
            .ToArray();
        var batch = new StructureMutationBatch(ContractSchemaVersions.Revision03, mutations);
        _ = new SqliteGameStructureStore(connection).Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03, "event:macro-seed", "game", "env",
                StructureEventKind.MutationApplied, StructureEventActor.Controller,
                "correlation", "observation", "observation", null, null, ["evidence"],
                StructureEventPayloadTypes.MutationBatch, JsonSerializer.Serialize(batch), null, DateTimeOffset.UnixEpoch),
            null,
            DateTimeOffset.UnixEpoch);
    }

    private static StructureScreenNode Node(string id) => new(
        ContractSchemaVersions.Revision03, id, "env", [], [], ["evidence"], id,
        StructureVerificationState.Replayed);

    private static StructureScreenEdge Edge(string id, string source, string destination) => new(
        ContractSchemaVersions.Revision03, id, source, destination, null, $"candidate:{id}", $"locator:{id}",
        "click", "guard", [], true, "before", "after",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 1)], ["evidence"],
        StructureVerificationState.Replayed,
        TargetSemanticKey: $"text|{id}|0|0", TargetNormalizedBounds: [0.1, 0.1, 0.1, 0.1]);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
