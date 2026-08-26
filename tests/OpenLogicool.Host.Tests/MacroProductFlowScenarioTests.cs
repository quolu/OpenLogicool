using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class MacroProductFlowScenarioTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"openlogicool-macro-scenario-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        File.Delete(path);
        File.Delete($"{path}.{MacroTargetSettingsStore.FileName}");
    }

    [Fact]
    public async Task Create_assign_replay_repair_compose_and_reopen_are_one_product_flow()
    {
        SeedStructure();
        var engine = new ScenarioEngine(path);
        using (var intents = new HostMacroAutomationIntents(
            path, engine, () => [new MacroTargetOption("game", "Game")]))
        {
            _ = intents.SelectTarget("game");
            var target = intents.ListTargets().Single();
            _ = await intents.CreateAsync(new MacroCreateRequest(target.ProcessName, "前半"), Progress());
            _ = await intents.CreateAsync(new MacroCreateRequest(target.ProcessName, "後半"), Progress());
            var initial = intents.ListMacros().Single(item => item.Goal == "前半");
            var tail = intents.ListMacros().Single(item => item.Goal == "後半");

            _ = await intents.PlayAsync(new MacroPlaybackRequest("game",
                new MacroVersionReference(initial.RouteId, initial.VersionId, MacroPlaybackMode.AiFree)), Progress());
            Assert.Single(ReadRoutes(initial.RouteId));

            _ = await intents.PlayAsync(new MacroPlaybackRequest("game",
                new MacroVersionReference(initial.RouteId, initial.VersionId, MacroPlaybackMode.AiMonitored)), Progress());
            var repaired = intents.ListMacros().Single(item => item.RouteId == initial.RouteId);
            Assert.Equal(2, repaired.RevisionNumber);

            var composed = intents.Compose(new MacroCompositionRequest("全部",
            [
                new MacroVersionReference(repaired.RouteId, repaired.VersionId, MacroPlaybackMode.AiMonitored),
                new MacroVersionReference(tail.RouteId, tail.VersionId, MacroPlaybackMode.AiMonitored),
            ]));

            using var connection = Open();
            var editor = new HostWorkspaceEditorIntents(connection);
            var token = MacroAssignment.CreateToken(composed, MacroPlaybackMode.AiFree);
            var document = WorkspaceDocumentEditor.CreateDraft("ws-macro-scenario");
            document = WorkspaceDocumentEditor.AddAction(document, "daily", "全部の日課", [token]);
            document = WorkspaceDocumentEditor.SetBinding(document, "daily", "G13", "G1", "base");
            document = WorkspaceDocumentEditor.SetBinding(document, "daily", "G600", "G9", "base");
            Assert.True(editor.Compile(document).IsValid);
            Assert.Equal(1, editor.Save(document, "*").RevisionNumber);
        }

        using (var reopened = Open())
        {
            var catalog = new HostMacroCatalog(reopened).ListMacros();
            var composed = catalog.Single(item => item.Goal == "全部");
            var composedRoute = new SqliteLearningRouteStore(reopened).LoadLatest(composed.RouteId)!;
            Assert.Equal(["edge-1b", "edge-2", "edge-3"], composedRoute.EdgeIds);
            Assert.Equal(["edge-1", "edge-2"], ReadRoutes("macro:front")[0].EdgeIds);
            Assert.Equal(["edge-1b", "edge-2"], ReadRoutes("macro:front")[1].EdgeIds);

            var saved = Assert.Single(new SqliteWorkspaceRevisionStore(reopened)
                .ListRevisions("ws-macro-scenario")).Document;
            var invocation = MacroInvocationTokens.Parse(Assert.Single(saved.Actions).Outputs.Single());
            Assert.Equal(composed.RouteId, invocation.RouteId);
            Assert.Null(invocation.VersionId);
            Assert.Equal(MacroPlaybackMode.AiFree, invocation.PlaybackMode);
            Assert.Equal(["G1", "G9"], saved.Bindings.Select(binding => binding.ControlId).Order().ToArray());
        }
    }

    private IReadOnlyList<LearningRouteRevision> ReadRoutes(string routeId)
    {
        using var connection = Open();
        return new SqliteLearningRouteStore(connection).ReadRevisions(routeId);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private void SeedStructure()
    {
        using var connection = Open();
        var nodes = new[] { Node("state-1"), Node("state-2"), Node("state-3"), Node("state-4") };
        var edges = new[]
        {
            Edge("edge-1", "state-1", "state-2"),
            Edge("edge-1b", "state-1", "state-2"),
            Edge("edge-2", "state-2", "state-3"),
            Edge("edge-3", "state-3", "state-4"),
        };
        var mutations = nodes.Select(node => new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertNode, StructureEntityKind.Node,
                node.StateId, [], node, null, null, null, null, null, node.EvidenceIds, "scenario"))
            .Concat(edges.Select(edge => new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertEdge, StructureEntityKind.Edge,
                edge.EdgeId, [edge.SourceStateId, edge.DestinationStateId!], null, edge, null, null, null, null,
                edge.EvidenceIds, "scenario")))
            .ToArray();
        var batch = new StructureMutationBatch(ContractSchemaVersions.Revision03, mutations);
        _ = new SqliteGameStructureStore(connection).Append(new StructureEventDraft(
            ContractSchemaVersions.Revision03, "event:scenario", "game", "env",
            StructureEventKind.MutationApplied, StructureEventActor.Controller,
            "correlation", "causation", "observation", null, null, ["evidence"],
            StructureEventPayloadTypes.MutationBatch, JsonSerializer.Serialize(batch), null, DateTimeOffset.UnixEpoch),
            null, DateTimeOffset.UnixEpoch);
    }

    private static StructureScreenNode Node(string id) => new(
        ContractSchemaVersions.Revision03, id, "env", [], [], ["evidence"], id,
        StructureVerificationState.Replayed);

    private static StructureScreenEdge Edge(string id, string source, string destination) => new(
        ContractSchemaVersions.Revision03, id, source, destination, null, $"candidate:{id}", $"locator:{id}",
        GameInteractionOperations.Click, "goal", [], true, "before", "after",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 1)], ["evidence"],
        StructureVerificationState.Replayed,
        TargetSemanticKey: $"text|{id}|0|0", TargetNormalizedBounds: [0.1, 0.1, 0.1, 0.1]);

    private static IProgress<MacroRunSnapshot> Progress() => new Progress<MacroRunSnapshot>();

    private sealed class ScenarioEngine(string path) : IProductMacroExecutionEngine
    {
        private int created;

        public Task<MacroRunSnapshot> ExecuteAsync(
            ProductMacroExecutionRequest request,
            IProgress<MacroRunSnapshot> progress,
            CancellationToken cancellationToken)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
            }.ToString());
            connection.Open();
            new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
            var routes = new SqliteLearningRouteStore(connection);
            LearningRouteRevision route;
            if (request.InitialRoute is null)
            {
                created++;
                var front = created == 1;
                route = routes.Append(Draft(
                    front ? "macro:front" : "macro:tail",
                    request.Goal,
                    front ? ["edge-1", "edge-2"] : ["edge-3"]));
            }
            else if (request.PlaybackMode == MacroPlaybackMode.AiMonitored)
            {
                route = routes.Append(Draft(
                    request.InitialRoute.RouteId,
                    request.InitialRoute.Goal,
                    ["edge-1b", "edge-2"],
                    request.InitialRoute.VersionId));
            }
            else
            {
                route = request.InitialRoute;
            }
            var snapshot = new MacroRunSnapshot(
                MacroRunPhase.Completed, request.Goal, request.TargetProcessName, route.EdgeIds.Count,
                request.InitialRoute is null ? "AI探索" : "保存済み", "scenario", "Moved",
                request.PlaybackMode == MacroPlaybackMode.AiMonitored ? 1 : 0,
                route.RevisionNumber, "完了", true, false);
            progress.Report(snapshot);
            return Task.FromResult(snapshot);
        }

        private static LearningRouteDraft Draft(
            string routeId, string goal, IReadOnlyList<string> edges, string? parent = null) => new(
            ContractSchemaVersions.Revision03, routeId, parent, "game", "env", "structure:scenario",
            goal, edges, LearningRouteAuthor.Ai, null, parent is null ? "create" : "repair",
            LearningRouteStatus.Compiled, DateTimeOffset.UtcNow);
    }
}
