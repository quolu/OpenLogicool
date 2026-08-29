using Microsoft.Data.Sqlite;
using System.IO;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;
using OpenLogicool.Profiles;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>
/// t07: 利用者の一巡を、製品のpublic intentsだけで通す受入。
///
/// 記録 → route導出 → AI監視修復 → AI 0再生 → 統合 → G13／G600割当 → SQLite再open。
/// engineとOS境界だけをfakeにし、保存と再openは実SQLiteで行う。
/// SendInput・Computer Use・外部AI APIは1回も使わない。
/// </summary>
public sealed class DemonstrationProductJourneyTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"openlogicool-journey-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        File.Delete(path);
        File.Delete($"{path}.{MacroTargetSettingsStore.FileName}");
    }

    [Fact]
    public async Task Record_derive_repair_replay_compose_assign_and_reopen_are_one_product_journey()
    {
        var gate = new DemonstrationRecordingGate();
        var engine = new JourneyEngine(path);
        MacroTargetSettingsStore.ForDatabase(path).Save("game");

        MacroCatalogItem demo;
        MacroCatalogItem repaired;
        MacroCatalogItem composed;
        string token;

        using (var macros = new HostMacroAutomationIntents(
            path, engine, () => [new MacroTargetOption("game", "Game")], gate))
        {
            _ = macros.SelectTarget("game");

            // 1. 記録（利用者が実際に操作した1手）
            var factory = new FakeLiveSessionFactory();
            using var recording = new HostDemonstrationRecordingIntents(path, factory, gate);
            var session = await recording.StartAsync("アークを開く");

            // 記録中は同じgateを持つ再生が拒否される（自己記録を構造で防ぐ）。
            await Assert.ThrowsAsync<InvalidOperationException>(() => macros.PlayAsync(
                new MacroPlaybackRequest("game", new MacroVersionReference("route:none", null, MacroPlaybackMode.AiFree)),
                Progress()));

            Click(factory);
            var stopped = await recording.StopAsync();
            Assert.Equal(1, stopped.OperationCount);

            // 2. route導出（原本→Learning Route。原本は変えない）
            demo = recording.CreateMacroFromSession(session.SessionId);
            Assert.Equal("アークを開く", demo.Goal);
            Assert.Equal(1, demo.RevisionNumber);

            // 3. AI監視修復（route revisionが1本増える）
            var monitored = await macros.PlayAsync(new MacroPlaybackRequest("game",
                new MacroVersionReference(demo.RouteId, demo.VersionId, MacroPlaybackMode.AiMonitored)), Progress());
            Assert.Equal(1, monitored.AiCallCount);
            repaired = macros.ListMacros().Single(item => item.RouteId == demo.RouteId);
            Assert.Equal(2, repaired.RevisionNumber);

            // 4. AI 0再生（保存済みrouteをそのまま使い、revisionを増やさない）
            var free = await macros.PlayAsync(new MacroPlaybackRequest("game",
                new MacroVersionReference(repaired.RouteId, repaired.VersionId, MacroPlaybackMode.AiFree)), Progress());
            Assert.Equal(0, free.AiCallCount);
            Assert.Equal(MacroRunPhase.Completed, free.Phase);
            Assert.Equal(2, macros.ListMacros().Single(item => item.RouteId == demo.RouteId).RevisionNumber);

            // 5. 統合（続く区間と1本にまとめる）
            var continuation = AppendContinuationRoute(repaired);
            composed = macros.Compose(new MacroCompositionRequest("日課をまとめる",
            [
                new MacroVersionReference(repaired.RouteId, repaired.VersionId, MacroPlaybackMode.AiMonitored),
                new MacroVersionReference(continuation.RouteId, continuation.VersionId, MacroPlaybackMode.AiMonitored),
            ]));

            // 6. G13／G600割当（Input Studioと同じassignment経路）
            token = Assign(composed);
        }

        // 7. SQLite再open（別connectionで、保存したものが全部戻る）
        using var reopened = Open();
        var routes = new SqliteLearningRouteStore(reopened);
        Assert.Equal(2, routes.ReadRevisions(demo.RouteId).Count);
        Assert.Equal(
            [.. routes.LoadLatest(repaired.RouteId)!.EdgeIds, "edge:continuation"],
            routes.LoadLatest(composed.RouteId)!.EdgeIds);

        var savedWorkspace = Assert.Single(new SqliteWorkspaceRevisionStore(reopened).ListRevisions(WorkspaceId)).Document;
        Assert.Equal(token, Assert.Single(savedWorkspace.Actions).Outputs.Single());
        Assert.Equal(["G1", "G9"], savedWorkspace.Bindings.Select(binding => binding.ControlId).Order().ToArray());

        var profiles = new SqliteMappingProfileStore(reopened).ListAll()
            .ToDictionary(document => document.ProfileId, MappingProfileMaterializer.ToProfile, StringComparer.Ordinal);
        Assert.Equal([token], profiles[$"{WorkspaceId}-G13"].Bindings[("G1", "base")]);
        Assert.Equal([token], profiles[$"{WorkspaceId}-G600"].Bindings[("G9", "base")]);

        // 記録の原本も残っている（route導出でも合成でも消えない）。
        var demonstration = new SqliteDemonstrationSessionStore(reopened).ListSessionIds("game", "game:live:test");
        Assert.Single(demonstration);

        // 割当tokenは合成macroを指す。
        var invocation = MacroInvocationTokens.Parse(token);
        Assert.Equal(composed.RouteId, invocation.RouteId);
        Assert.Equal(composed.RouteId, new HostMacroCatalog(reopened).Resolve(invocation).RouteId);
    }

    private const string WorkspaceId = "ws-journey";

    private string Assign(MacroCatalogItem macro)
    {
        using var connection = Open();
        var editor = new HostWorkspaceEditorIntents(connection);
        var token = MacroAssignment.CreateToken(macro, MacroPlaybackMode.AiFree);
        var document = WorkspaceDocumentEditor.CreateDraft(WorkspaceId);
        document = WorkspaceDocumentEditor.AddAction(document, "daily", "日課をまとめる", [token]);
        document = WorkspaceDocumentEditor.SetBinding(document, "daily", "G13", "G1", "base");
        document = WorkspaceDocumentEditor.SetBinding(document, "daily", "G600", "G9", "base");
        var compiled = editor.Compile(document);
        Assert.True(compiled.IsValid, compiled.ErrorMessage);
        _ = editor.Save(document, "*");
        return token;
    }

    /// <summary>合成できるように、demo routeの終点から続く区間を既存形式で足す。</summary>
    private LearningRouteRevision AppendContinuationRoute(MacroCatalogItem demo)
    {
        using var connection = Open();
        var routes = new SqliteLearningRouteStore(connection);
        var demoRoute = routes.LoadLatest(demo.RouteId)!;
        var structures = new SqliteGameStructureStore(connection);
        var structure = structures.LoadRevision(demoRoute.GameId, demoRoute.EnvironmentScope);
        var lastEdge = structure.ScreenGraph.Edges.Single(edge => edge.EdgeId == demoRoute.EdgeIds[^1]);
        var nextState = new StructureScreenNode(
            ContractSchemaVersions.Revision03, "state:continuation", demoRoute.EnvironmentScope,
            [], [], ["evidence"], "continuation", StructureVerificationState.Replayed);
        var nextEdge = new StructureScreenEdge(
            ContractSchemaVersions.Revision03, "edge:continuation", lastEdge.DestinationStateId!, nextState.StateId,
            null, "candidate:continuation", "locator:continuation", GameInteractionOperations.Click, "goal", [],
            true, "before", "after",
            new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
            [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 1)], ["evidence"],
            StructureVerificationState.Replayed,
            TargetSemanticKey: "text|continuation|0|0", TargetNormalizedBounds: [0.1, 0.1, 0.1, 0.1]);
        var batch = new StructureMutationBatch(ContractSchemaVersions.Revision03,
        [
            new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertNode, StructureEntityKind.Node,
                nextState.StateId, [], nextState, null, null, null, null, null, nextState.EvidenceIds, "continuation"),
            new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertEdge, StructureEntityKind.Edge,
                nextEdge.EdgeId, [nextEdge.SourceStateId, nextEdge.DestinationStateId!], null, nextEdge,
                null, null, null, null, nextEdge.EvidenceIds, "continuation"),
        ]);
        _ = structures.Append(new StructureEventDraft(
            ContractSchemaVersions.Revision03, "event:continuation", demoRoute.GameId, demoRoute.EnvironmentScope,
            StructureEventKind.MutationApplied, StructureEventActor.Controller,
            "correlation:continuation", "causation:continuation", "observation:continuation", null, null, ["evidence"],
            StructureEventPayloadTypes.MutationBatch, System.Text.Json.JsonSerializer.Serialize(batch), null,
            DateTimeOffset.UnixEpoch),
            structure.RevisionId, DateTimeOffset.UnixEpoch);
        var updated = structures.LoadRevision(demoRoute.GameId, demoRoute.EnvironmentScope);
        return routes.Append(new LearningRouteDraft(
            ContractSchemaVersions.Revision03, "route:continuation", null,
            demoRoute.GameId, demoRoute.EnvironmentScope, updated.RevisionId,
            "続きを開く", ["edge:continuation"], LearningRouteAuthor.Ai, null, "create",
            LearningRouteStatus.Compiled, DateTimeOffset.UtcNow));
    }

    private static void Click(FakeLiveSessionFactory factory)
    {
        var occurredUtc = DateTimeOffset.UtcNow;
        factory.Sink!.Observe(new DemonstrationInputEdge(
            ContractSchemaVersions.Revision03, DemonstrationInputSource.Mouse, DemonstrationInputEdgeKind.PointerDown,
            "left", "Mouse:Left", 100, occurredUtc, new DemonstrationScreenPoint(10, 10)));
        factory.Sink!.Observe(new DemonstrationInputEdge(
            ContractSchemaVersions.Revision03, DemonstrationInputSource.Mouse, DemonstrationInputEdgeKind.PointerUp,
            "left", "Mouse:Left", 130, occurredUtc.AddMilliseconds(30), new DemonstrationScreenPoint(10, 10)));
    }

    private static IProgress<MacroRunSnapshot> Progress() => new Progress<MacroRunSnapshot>();

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

    /// <summary>AI監視は1回だけAIを使ってrevisionを足し、AI 0は保存済みrouteをそのまま使う。</summary>
    private sealed class JourneyEngine(string path) : IProductMacroExecutionEngine
    {
        public Task<MacroRunSnapshot> ExecuteAsync(
            ProductMacroExecutionRequest request,
            IProgress<MacroRunSnapshot> progress,
            CancellationToken cancellationToken = default)
        {
            var route = request.InitialRoute
                ?? throw new InvalidOperationException("このjourneyはデモ由来routeからしか始まらない。");
            var aiCalls = 0;
            if (request.PlaybackMode == MacroPlaybackMode.AiMonitored)
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Pooling = false,
                }.ToString());
                connection.Open();
                new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
                var routes = new SqliteLearningRouteStore(connection);
                route = routes.Append(new LearningRouteDraft(
                    ContractSchemaVersions.Revision03, route.RouteId, route.VersionId,
                    route.GameId, route.EnvironmentScope, route.StructureRevisionId,
                    route.Goal, route.EdgeIds, LearningRouteAuthor.Ai, null, "repair",
                    LearningRouteStatus.Compiled, DateTimeOffset.UtcNow));
                aiCalls = 1;
            }

            var snapshot = new MacroRunSnapshot(
                MacroRunPhase.Completed, request.Goal, request.TargetProcessName, route.EdgeIds.Count,
                "保存済み", "journey", "Moved", aiCalls, route.RevisionNumber, "完了", true, false);
            progress.Report(snapshot);
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FakeLiveSessionFactory : IDemonstrationLiveSessionFactory
    {
        public FakeDemonstrationInputCollector? Sink { get; private set; }

        public DemonstrationLiveSession Create(string targetProcessName)
        {
            Sink = new FakeDemonstrationInputCollector();
            return new DemonstrationLiveSession(
                @"C:\game\game.exe",
                "window:game",
                "game:live:test",
                new FakeObservationRuntime(Scene("scene-a", "btn-to-b"), Scene("scene-b", "btn-to-c")),
                Sink,
                _ => [0.5, 0.5]);
        }

        private static ObservedScene Scene(string observationId, string label)
        {
            var frame = new CapturedFrameReference(
                ContractSchemaVersions.Revision03, "window:game", CaptureBackend.WindowsGraphicsCapture,
                1, 100, DateTimeOffset.UnixEpoch, 1, 10, 300);
            return new ObservedScene(
                ContractSchemaVersions.Revision03, $"scene-{observationId}", observationId, frame,
                CaptureAvailability.Available, StateIdentityStatus.Novel, null, [],
                [new AffordanceCandidate(
                    ContractSchemaVersions.Revision03, $"affordance-{observationId}", observationId, 1, 1,
                    "window:game",
                    new AffordanceLocator(ContractSchemaVersions.Revision03, "ocr", [0.4, 0.4, 0.2, 0.2], "locator-1"),
                    [], 0.9, [GameInteractionOperations.Click], SemanticLabel: label)],
                "perception-1");
        }
    }

    private sealed class FakeDemonstrationInputCollector : IDemonstrationInputCollector
    {
        private IDemonstrationInputSink? sink;

        public void Start(IDemonstrationInputSink sink) => this.sink = sink;

        public void Stop() => sink = null;

        public void Observe(DemonstrationInputEdge edge) => sink?.Observe(edge);

        public void Dispose()
        {
        }
    }

    private sealed class FakeObservationRuntime(ObservedScene initial, ObservedScene after) : IDemonstrationObservationRuntime
    {
        public ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ObservationResult(
                ContractSchemaVersions.Revision03, initial.ObservationId, initial.Frame,
                CaptureAvailability.Available, StateIdentityStatus.Novel, [], "recognizer-1", 0, null));

        public ValueTask<ObservedScene> DiscoverTargetsAsync(
            ObservationResult observation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(initial);

        public ValueTask<GameInteractionStabilityResult> WaitStableAsync(
            ObservedScene before, ExplorationWaitCondition condition, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GameInteractionStabilityResult(
                ContractSchemaVersions.Revision03, GameInteractionStabilityStatus.Stable,
                [after], after, 2, 1_000, 1_200, null));

        public GameTransitionComparison Compare(ObservedScene before, GameInteractionStabilityResult stability) =>
            new(
                ContractSchemaVersions.Revision03, before.ObservationId, stability.StableScene!.ObservationId,
                GameTransitionJudgement.Moved, [], ["test"]);
    }
}
