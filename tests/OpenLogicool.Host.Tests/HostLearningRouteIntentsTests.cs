using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostLearningRouteIntentsTests
{
    [Fact]
    public void Real_sqlite_journey_loads_saves_compiles_revises_and_undoes()
    {
        using var connection = OpenMigrated();
        SeedGraph(connection);
        var intents = new HostLearningRouteIntents(connection);
        var scope = Assert.Single(intents.ListScopes());
        var initial = intents.Load(scope.GameId, scope.EnvironmentScope);

        Assert.Null(initial.RouteId);
        Assert.Equal(2, initial.AvailableEdges.Count);
        var first = intents.Save(Request(initial, ["edge:daily"]));
        var compiled = intents.Compile(scope.GameId, scope.EnvironmentScope, first.RouteId!, first.VersionId!);
        var second = intents.Save(Request(first, ["edge:daily", "edge:reward"]));
        var restored = intents.Undo(scope.GameId, scope.EnvironmentScope, second.RouteId!, second.VersionId!);

        Assert.Equal(1, first.RevisionNumber);
        Assert.Contains("教師付きとして生成", compiled.MacroStateLabel, StringComparison.Ordinal);
        Assert.Equal(2, second.Steps.Count);
        Assert.Equal(3, restored.RevisionNumber);
        Assert.Equal(["edge:daily"], restored.Steps.Select(step => step.Edge.EdgeId));
        Assert.True(restored.CanUndo);
    }

    [Fact]
    public void Invalid_discontinuous_edit_is_rejected_before_revision_is_appended()
    {
        using var connection = OpenMigrated();
        SeedGraph(connection);
        var intents = new HostLearningRouteIntents(connection);
        var initial = intents.Load("game-1", "env-1");
        var first = intents.Save(Request(initial, ["edge:daily"]));

        Assert.Throws<InvalidOperationException>(() =>
            intents.Save(Request(first, ["edge:daily", "edge:daily"])));

        Assert.Single(new SqliteLearningRouteStore(connection).ReadRevisions(first.RouteId!));
    }

    private static LearningRouteSaveRequest Request(
        LearningRouteScreenSnapshot snapshot,
        IReadOnlyList<string> edgeIds) => new(
        snapshot.GameId,
        snapshot.EnvironmentScope,
        snapshot.StructureRevisionId,
        snapshot.RouteId,
        snapshot.VersionId,
        "日課報酬を受け取る",
        edgeIds,
        "利用者が順序を確認");

    private static SqliteConnection OpenMigrated()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static void SeedGraph(SqliteConnection connection)
    {
        var nodes = new[]
        {
            Node("state:lobby", "ロビー"),
            Node("state:daily", "日課一覧"),
            Node("state:reward", "報酬受領"),
        };
        var edges = new[]
        {
            Edge("edge:daily", "state:lobby", "state:daily"),
            Edge("edge:reward", "state:daily", "state:reward"),
        };
        var mutations = nodes.Select(node => new StructureMutation(
                ContractSchemaVersions.Revision03,
                StructureMutationKind.UpsertNode,
                StructureEntityKind.Node,
                node.StateId,
                [],
                node,
                null,
                null,
                null,
                null,
                null,
                node.EvidenceIds,
                "test seed"))
            .Concat(edges.Select(edge => new StructureMutation(
                ContractSchemaVersions.Revision03,
                StructureMutationKind.UpsertEdge,
                StructureEntityKind.Edge,
                edge.EdgeId,
                [edge.SourceStateId, edge.DestinationStateId!],
                null,
                edge,
                null,
                null,
                null,
                null,
                edge.EvidenceIds,
                "test seed")))
            .ToArray();
        var batch = new StructureMutationBatch(ContractSchemaVersions.Revision03, mutations);
        _ = new SqliteGameStructureStore(connection).Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03,
                "event-route-seed",
                "game-1",
                "env-1",
                StructureEventKind.MutationApplied,
                StructureEventActor.Controller,
                "correlation-1",
                "observation-1",
                "observation-1",
                null,
                null,
                ["evidence-1"],
                StructureEventPayloadTypes.MutationBatch,
                JsonSerializer.Serialize(batch),
                null,
                DateTimeOffset.UnixEpoch),
            null,
            DateTimeOffset.UnixEpoch);
    }

    private static StructureScreenNode Node(string id, string label) => new(
        ContractSchemaVersions.Revision03,
        id,
        "env-1",
        [$"signature:{id}"],
        [],
        ["evidence-1"],
        label,
        StructureVerificationState.Replayed);

    private static StructureScreenEdge Edge(string id, string source, string destination) => new(
        ContractSchemaVersions.Revision03,
        id,
        source,
        destination,
        null,
        $"affordance:{id}",
        "locator:v1",
        "click",
        "supervised",
        [],
        true,
        $"before:{id}",
        $"after:{id}",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 300, 10000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 2)],
        ["evidence-1"],
        StructureVerificationState.Replayed);
}
