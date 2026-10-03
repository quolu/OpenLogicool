using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Domain;
using OpenLogicool.Exploration;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostMacroCatalogTests
{
    [Fact]
    public void Separate_live_ark_observations_join_without_changing_original_routes_or_events()
    {
        using var connection = Open();
        var scenes = LiveScenes();
        Assert.NotEqual(GameSceneSemanticComparer.SignatureId(scenes[0]), GameSceneSemanticComparer.SignatureId(scenes[1]));
        SeedDisconnectedStructure(connection, scenes[0], scenes[1]);
        var structures = new SqliteGameStructureStore(connection);
        var originalEvents = structures.ReadEvents("game", "env").ToArray();
        var originalStructure = structures.LoadRevision("game", "env");
        var routes = new SqliteLearningRouteStore(connection);
        var first = routes.Append(Draft("macro-a", "アークを開く", ["edge-1"]));
        var second = routes.Append(Draft("macro-b", "ロビーへ戻る", ["edge-2"]));

        var reused = StructureSceneIdentityResolver.FindExistingNode(originalStructure, originalEvents, scenes[1]);
        var composed = new HostMacroCatalog(connection).Compose(Request(first, second));

        Assert.Equal("state-2a", reused?.StateId);
        Assert.Equal(2, composed.StepCount);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(routes.LoadLatest(first.RouteId)));
        Assert.Equal(JsonSerializer.Serialize(second), JsonSerializer.Serialize(routes.LoadLatest(second.RouteId)));
        Assert.Equal(JsonSerializer.Serialize(originalEvents),
            JsonSerializer.Serialize(structures.ReadEvents("game", "env").Take(originalEvents.Length)));
        Assert.False(GameStructureProjector.Replay("game", "env", originalEvents).ScreenGraph.Nodes.Single(n => n.StateId == "state-2b").Retired);
        var updated = structures.LoadRevision("game", "env");
        Assert.True(updated.ScreenGraph.Nodes.Single(n => n.StateId == "state-2b").Retired);
        Assert.Equal(updated.ScreenGraph.Edges.Single(e => e.EdgeId == "edge-1").DestinationStateId,
            updated.ScreenGraph.Edges.Single(e => e.EdgeId == "edge-2").SourceStateId);
        LearningRouteValidator.Validate(routes.LoadLatest(composed.RouteId)!, updated);
        var reopened = new HostMacroCatalog(connection);
        Assert.Contains(reopened.ListMacros(), item => item.RouteId == composed.RouteId && item.StepCount == 2);
    }

    [Fact]
    public void Different_live_screen_with_same_label_and_known_state_does_not_join()
    {
        using var connection = Open();
        var scenes = LiveScenes();
        SeedDisconnectedStructure(connection, scenes[0], scenes[1] with { SceneVisualPatch = scenes[2].SceneVisualPatch });
        AssertRejectedWithoutChanges(connection);
    }

    [Fact]
    public void Missing_screen_evidence_does_not_join()
    {
        using var connection = Open();
        SeedDisconnectedStructure(connection, null, null);
        AssertRejectedWithoutChanges(connection);
    }

    [Fact]
    public void Reused_observation_id_after_edge_creation_does_not_replace_original_evidence()
    {
        using var connection = Open();
        var scenes = LiveScenes();
        SeedDisconnectedStructure(connection, scenes[0], scenes[1]);
        AppendScene(connection, scenes[1] with { SceneVisualPatch = scenes[2].SceneVisualPatch }, "reused-id");
        var routes = new SqliteLearningRouteStore(connection);
        var first = routes.Append(Draft("macro-a", "アークを開く", ["edge-1"]));
        var second = routes.Append(Draft("macro-b", "ロビーへ戻る", ["edge-2"]));
        Assert.Equal(2, new HostMacroCatalog(connection).Compose(Request(first, second)).StepCount);
    }

    private static void AssertRejectedWithoutChanges(SqliteConnection connection)
    {
        var structures = new SqliteGameStructureStore(connection);
        var before = structures.ReadEvents("game", "env").ToArray();
        var routes = new SqliteLearningRouteStore(connection);
        var first = routes.Append(Draft("macro-a", "アークを開く", ["edge-1"]));
        var second = routes.Append(Draft("macro-b", "ロビーへ戻る", ["edge-2"]));
        Assert.Throws<InvalidOperationException>(() => new HostMacroCatalog(connection).Compose(Request(first, second)));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(structures.ReadEvents("game", "env")));
        Assert.Equal(2, new HostMacroCatalog(connection).ListMacros().Count);
    }

    private static MacroCompositionRequest Request(LearningRouteRevision first, LearningRouteRevision second) => new(
        "アークを開いてロビーへ戻る", [
            new MacroVersionReference(first.RouteId, first.VersionId, MacroPlaybackMode.AiFree),
            new MacroVersionReference(second.RouteId, second.VersionId, MacroPlaybackMode.AiFree),
        ]);

    private static ObservedScene[] LiveScenes()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "fixtures"))) root = root.Parent;
        return JsonSerializer.Deserialize<ObservedScene[]>(File.ReadAllText(Path.Combine(root!.FullName,
            "fixtures", "game-structure", "nikke-ark-scene-identity.v1.json")))!;
    }

    private static void SeedDisconnectedStructure(SqliteConnection connection, ObservedScene? after, ObservedScene? before)
    {
        if (after is not null) AppendScene(connection, after, "arrival");
        if (before is not null) AppendScene(connection, before, "departure");
        var nodes = new[] { Node("state-1"), Node("state-2a"), Node("state-2b"), Node("state-3") };
        var edges = new[] {
            Edge("edge-1", "state-1", "state-2a") with { AfterObservationId = after?.ObservationId },
            Edge("edge-2", "state-2b", "state-3") with { BeforeObservationId = before?.ObservationId ?? "missing" },
        };
        SeedStructure(connection, nodes, edges);
    }

    private static void AppendScene(SqliteConnection connection, ObservedScene scene, string suffix)
    {
        var store = new SqliteGameStructureStore(connection);
        var revision = store.LoadRevision("game", "env").RevisionId;
        store.Append(new StructureEventDraft(
            ContractSchemaVersions.Revision03, $"event:{suffix}", "game", "env",
            StructureEventKind.ObservationRecorded, StructureEventActor.Controller,
            "correlation", "capture", scene.ObservationId, null, null, [scene.ObservationId],
            StructureEventPayloadTypes.Observation, JsonSerializer.Serialize(scene), null, DateTimeOffset.UnixEpoch),
            revision == "structure:root" ? null : revision, DateTimeOffset.UnixEpoch);
    }

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
        SeedStructure(connection, nodes, edges);
    }

    private static void SeedStructure(SqliteConnection connection, StructureScreenNode[] nodes, StructureScreenEdge[] edges)
    {
        var mutations = nodes.Select(node => new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertNode, StructureEntityKind.Node,
                node.StateId, [], node, null, null, null, null, null, node.EvidenceIds, "test seed"))
            .Concat(edges.Select(edge => new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertEdge, StructureEntityKind.Edge,
                edge.EdgeId, [edge.SourceStateId, edge.DestinationStateId!], null, edge, null, null, null, null,
                edge.EvidenceIds, "test seed")))
            .ToArray();
        var batch = new StructureMutationBatch(ContractSchemaVersions.Revision03, mutations);
        var store = new SqliteGameStructureStore(connection);
        var revision = store.LoadRevision("game", "env").RevisionId;
        _ = store.Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03, "event:macro-seed", "game", "env",
                StructureEventKind.MutationApplied, StructureEventActor.Controller,
                "correlation", "observation", "observation", null, null, ["evidence"],
                StructureEventPayloadTypes.MutationBatch, JsonSerializer.Serialize(batch), null, DateTimeOffset.UnixEpoch),
            revision == "structure:root" ? null : revision,
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
