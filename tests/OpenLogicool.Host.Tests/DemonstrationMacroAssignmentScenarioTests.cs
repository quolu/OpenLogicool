using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Domain;
using OpenLogicool.Input;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;
using OpenLogicool.Profiles;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>
/// t06: デモ由来macroが、AI由来macroと同じ既存経路（catalog／合成／G13・G600割当／
/// button queue／SQLite再open）をそのまま通ることを、実SQLiteで一巡して確認する。
///
/// 新しいtokenも新しいpublic intentsも作らない。ここで使うのは既存の
/// <see cref="MacroAssignment"/>／<see cref="MacroInvocationTokens"/>／
/// <see cref="HostWorkspaceEditorIntents"/>／<see cref="MacroInvocationQueue"/>／
/// <see cref="HostMacroCatalog"/> だけである。
/// </summary>
public sealed class DemonstrationMacroAssignmentScenarioTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"openlogicool-demo-macro-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        File.Delete(path);
        File.Delete($"{path}.{MacroTargetSettingsStore.FileName}");
    }

    [Fact]
    public async Task A_demonstration_macro_is_listed_assigned_to_both_devices_and_survives_reopen()
    {
        var demo = await RecordAndCreateMacroAsync("アークを開く");

        using (var intents = new HostMacroAutomationIntents(
            path, new UnusedEngine(), () => [new MacroTargetOption("game", "Game")]))
        {
            // 対象game profileは記録時と同じ選択（MacroTargetSettingsStore）を読むので、
            // デモ由来macroもAI由来macroと同じcatalogへ出る。
            var listed = Assert.Single(intents.ListMacros(), item => item.RouteId == demo.RouteId);
            Assert.Equal("アークを開く", listed.Goal);
            Assert.Equal("game", listed.GameId);
        }

        var token = AssignToBothDevices(demo, MacroPlaybackMode.AiMonitored);

        using var reopened = Open();
        var saved = Assert.Single(new SqliteWorkspaceRevisionStore(reopened).ListRevisions(WorkspaceId)).Document;
        Assert.Equal(token, Assert.Single(saved.Actions).Outputs.Single());
        Assert.Equal(["G1", "G9"], saved.Bindings.Select(binding => binding.ControlId).Order().ToArray());

        // 再open後のprofileにも同じtokenが入っている（device設定として残る）。
        var profiles = new SqliteMappingProfileStore(reopened).ListAll()
            .ToDictionary(document => document.ProfileId, MappingProfileMaterializer.ToProfile, StringComparer.Ordinal);
        Assert.Equal([token], profiles[$"{WorkspaceId}-G13"].Bindings[("G1", "base")]);
        Assert.Equal([token], profiles[$"{WorkspaceId}-G600"].Bindings[("G9", "base")]);
    }

    [Fact]
    public async Task Pressing_the_assigned_button_puts_the_demonstration_macro_into_the_button_queue()
    {
        var demo = await RecordAndCreateMacroAsync("アークを開く");
        var token = AssignToBothDevices(demo, MacroPlaybackMode.AiFree);

        using var reopened = Open();
        var runtimes = LoadRuntimes(reopened);
        var source = new FakeDeviceInputSource(
            [Device(G13DeviceId), Device(G600DeviceId)],
            [
                Input(G13DeviceId, "G1", PhysicalInputEdge.Down, 1),
                Input(G13DeviceId, "G1", PhysicalInputEdge.Up, 2),
                Input(G600DeviceId, "G9", PhysicalInputEdge.Down, 3),
                Input(G600DeviceId, "G9", PhysicalInputEdge.Up, 4),
            ]);
        var emitter = new RecordingEmitter();
        using var queue = new MacroInvocationQueue();
        using var pump = new FastPathPump([new FastPathSource(source)], runtimes, emitter, macroInvocations: queue);

        Assert.Equal(4, pump.RunOnce());

        // 押下1回につき1件、両deviceから同じmacroが入る。物理emitterへは何も出ない。
        Assert.Equal(2, pump.AcceptedMacroInvocations);
        Assert.Equal(0, pump.RejectedMacroInvocations);
        Assert.Empty(emitter.Emitted);

        var expected = MacroInvocationTokens.Parse(token);
        Assert.True(queue.TryDequeue(out var first));
        Assert.True(queue.TryDequeue(out var second));
        Assert.Equal(expected, first);
        Assert.Equal(expected, second);
        Assert.False(queue.TryDequeue(out _));

        // queueから出た参照が、デモ原本から作ったroute そのものを指す。
        var resolved = new HostMacroCatalog(reopened).Resolve(first);
        Assert.Equal(demo.RouteId, resolved.RouteId);
        Assert.Equal("アークを開く", resolved.Goal);
        Assert.NotEmpty(resolved.EdgeIds);
    }

    [Fact]
    public async Task A_demonstration_macro_can_be_composed_with_another_route_without_rebuilding_it()
    {
        var demo = await RecordAndCreateMacroAsync("アークを開く");
        var before = Snapshot(demo.RouteId);

        // デモが着いた画面から続く区間を、既存のAI由来routeと同じ形で用意する。
        // これが無いとStructure edge列が連続せず、合成は（デモ由来かどうかに関係なく）成立しない。
        var continuation = AppendContinuationRoute(demo);

        using var connection = Open();
        var composed = new HostMacroCatalog(connection).Compose(new MacroCompositionRequest("日課をまとめる",
        [
            new MacroVersionReference(demo.RouteId, demo.VersionId, MacroPlaybackMode.AiMonitored),
            new MacroVersionReference(continuation.RouteId, continuation.VersionId, MacroPlaybackMode.AiMonitored),
        ]));

        Assert.Equal("日課をまとめる", composed.Goal);
        Assert.NotEqual(demo.RouteId, composed.RouteId);

        var routes = new SqliteLearningRouteStore(connection);
        var composedRoute = routes.LoadLatest(composed.RouteId)!;
        Assert.Equal(
            [.. routes.LoadLatest(demo.RouteId)!.EdgeIds, .. continuation.EdgeIds],
            composedRoute.EdgeIds);

        // デモ由来routeは作り直されない（revisionが増えず、edge列も変わらない）。
        Assert.Equal(before, Snapshot(demo.RouteId));
    }

    [Fact]
    public async Task Assigning_a_demonstration_macro_does_not_rebuild_the_route_or_the_existing_binding()
    {
        var demo = await RecordAndCreateMacroAsync("アークを開く");
        var before = Snapshot(demo.RouteId);

        // 既にキー割当を持つworkspaceへ、後からデモ由来macroを足す。
        using (var connection = Open())
        {
            var editor = new HostWorkspaceEditorIntents(connection);
            var existing = WorkspaceDocumentEditor.CreateDraft(WorkspaceId);
            existing = WorkspaceDocumentEditor.AddAction(existing, "keep", "元からある操作", ["Key:F13"]);
            existing = WorkspaceDocumentEditor.SetBinding(existing, "keep", "G13", "G2", "base");
            _ = editor.Save(existing, "*");
        }

        var token = AssignToBothDevices(demo, MacroPlaybackMode.AiMonitored, keepExistingAction: true);

        using var reopened = Open();
        var saved = new SqliteWorkspaceRevisionStore(reopened).ListRevisions(WorkspaceId)[^1].Document;

        // 元の操作と割当がそのまま残っている。
        Assert.Contains(saved.Actions, action => action.ActionId == "keep" && action.Outputs.Single() == "Key:F13");
        Assert.Contains(saved.Bindings, binding => binding.ActionId == "keep" && binding.ControlId == "G2");
        Assert.Contains(saved.Actions, action => action.Outputs.Single() == token);

        // route側も作り直されていない。
        Assert.Equal(before, Snapshot(demo.RouteId));
    }

    [Fact]
    public async Task A_queued_button_press_is_refused_while_a_demonstration_is_recording()
    {
        var demo = await RecordAndCreateMacroAsync("アークを開く");
        var invocation = MacroInvocationTokens.Parse(AssignToBothDevices(demo, MacroPlaybackMode.AiFree));

        // 記録器と再生intentsが同じgateを持つと、button queueから来た再生も止まる。
        var gate = new DemonstrationRecordingGate();
        var engine = new CountingEngine();
        using var macros = new HostMacroAutomationIntents(
            path, engine, () => [new MacroTargetOption("game", "Game")], gate);
        _ = macros.SelectTarget("game");
        using var recording = new HostDemonstrationRecordingIntents(path, new FakeLiveSessionFactory(), gate);
        _ = await recording.StartAsync("記録中");

        // MacroAutomationWorkerがqueueから取り出したときに呼ぶ経路そのもの。
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((IMacroInvocationRunner)macros).RunQueuedAsync(invocation, Progress(), default));
        Assert.Equal(0, engine.Executions);

        _ = await recording.StopAsync();
        _ = await ((IMacroInvocationRunner)macros).RunQueuedAsync(invocation, Progress(), default);
        Assert.Equal(1, engine.Executions);
        Assert.NotNull(refused);
    }

    private const string WorkspaceId = "ws-demo-macro";
    private const string G13DeviceId = "g13-1";
    private const string G600DeviceId = "g600-1";

    /// <summary>
    /// デモ由来routeの終点から続く区間を、既存のAI由来routeと同じ形（structure edge＋Learning Route）で足す。
    /// 合成はStructure edge列が連続していることを要求するので、続きが無ければ何由来でも合成できない。
    /// </summary>
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
            StructureEventPayloadTypes.MutationBatch, JsonSerializer.Serialize(batch), null, DateTimeOffset.UnixEpoch),
            structure.RevisionId, DateTimeOffset.UnixEpoch);
        var updated = structures.LoadRevision(demoRoute.GameId, demoRoute.EnvironmentScope);

        return routes.Append(new LearningRouteDraft(
            ContractSchemaVersions.Revision03, "route:continuation", null,
            demoRoute.GameId, demoRoute.EnvironmentScope, updated.RevisionId,
            "続きを開く", [nextEdge.EdgeId], LearningRouteAuthor.Ai, null, "create",
            LearningRouteStatus.Compiled, DateTimeOffset.UtcNow));
    }

    private (long Revisions, long LatestRevisionNumber, string Edges) Snapshot(string routeId)
    {
        using var connection = Open();
        var revisions = new SqliteLearningRouteStore(connection).ReadRevisions(routeId);
        return (revisions.Count, revisions[^1].RevisionNumber, string.Join(",", revisions[^1].EdgeIds));
    }

    /// <summary>既存のassignment経路（Input Studioと同じ）でG13とG600の両方へ割り当てる。</summary>
    private string AssignToBothDevices(
        MacroCatalogItem macro, MacroPlaybackMode mode, bool keepExistingAction = false)
    {
        using var connection = Open();
        var editor = new HostWorkspaceEditorIntents(connection);
        var token = MacroAssignment.CreateToken(macro, mode);
        var document = keepExistingAction
            ? new SqliteWorkspaceRevisionStore(connection).ListRevisions(WorkspaceId)[^1].Document
            : WorkspaceDocumentEditor.CreateDraft(WorkspaceId);
        document = WorkspaceDocumentEditor.AddAction(document, "demo", "デモから作った操作", [token]);
        document = WorkspaceDocumentEditor.SetBinding(document, "demo", "G13", "G1", "base");
        document = WorkspaceDocumentEditor.SetBinding(document, "demo", "G600", "G9", "base");

        var compiled = editor.Compile(document);
        Assert.True(compiled.IsValid, compiled.ErrorMessage);
        Assert.Equal(2, compiled.ProfileCount);
        _ = editor.Save(document, "*");
        return token;
    }

    private static Dictionary<string, DeviceMappingRuntime> LoadRuntimes(SqliteConnection connection)
    {
        var profiles = new SqliteMappingProfileStore(connection).ListAll()
            .ToDictionary(document => document.ProfileId, MappingProfileMaterializer.ToProfile, StringComparer.Ordinal);
        return new Dictionary<string, DeviceMappingRuntime>(StringComparer.Ordinal)
        {
            [G13DeviceId] = new(G13DeviceId, profiles[$"{WorkspaceId}-G13"]),
            [G600DeviceId] = new(G600DeviceId, profiles[$"{WorkspaceId}-G600"]),
        };
    }

    private async Task<MacroCatalogItem> RecordAndCreateMacroAsync(
        string goal, string fromScene = "scene-a", string toScene = "scene-b")
    {
        MacroTargetSettingsStore.ForDatabase(path).Save("game");
        var factory = new FakeLiveSessionFactory(fromScene, toScene);
        using var intents = new HostDemonstrationRecordingIntents(path, factory, new DemonstrationRecordingGate());
        var summary = await intents.StartAsync(goal);
        var occurredUtc = DateTimeOffset.UtcNow;
        factory.Sink!.Observe(new DemonstrationInputEdge(
            ContractSchemaVersions.Revision03, DemonstrationInputSource.Mouse, DemonstrationInputEdgeKind.PointerDown,
            "left", "Mouse:Left", 100, occurredUtc, new DemonstrationScreenPoint(10, 10)));
        factory.Sink!.Observe(new DemonstrationInputEdge(
            ContractSchemaVersions.Revision03, DemonstrationInputSource.Mouse, DemonstrationInputEdgeKind.PointerUp,
            "left", "Mouse:Left", 130, occurredUtc.AddMilliseconds(30), new DemonstrationScreenPoint(10, 10)));
        _ = await intents.StopAsync();
        return intents.CreateMacroFromSession(summary.SessionId);
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

    private static DeviceInstance Device(string id) =>
        new(ContractSchemaVersions.Revision01, id, 0x046D, 0xC24A, id, "{00000000-0000-0000-0000-000000000000}", 1, []);

    private static PhysicalInput Input(string deviceId, string controlId, PhysicalInputEdge edge, long sequence) =>
        new(ContractSchemaVersions.Revision01, deviceId, controlId, edge, MonotonicMs: 0, ReportSequence: sequence);

    private sealed class FakeDeviceInputSource(
        IReadOnlyList<DeviceInstance> devices, IEnumerable<PhysicalInput> inputs) : IDeviceInputSource
    {
        private readonly ConcurrentQueue<PhysicalInput> pending = new(inputs);

        public IReadOnlyList<DeviceInstance> EnumerateDevices() => devices;

        public bool TryPull(out PhysicalInput input) => pending.TryDequeue(out input!);
    }

    private sealed class RecordingEmitter : IOutputEmitter
    {
        public List<MappedOutputEdge> Emitted { get; } = [];

        public void Emit(IReadOnlyList<MappedOutputEdge> edges) => Emitted.AddRange(edges);
    }

    private static IProgress<MacroRunSnapshot> Progress() => new Progress<MacroRunSnapshot>();

    /// <summary>実行回数だけを数えるengine（gateで止まったかどうかを見るため）。</summary>
    private sealed class CountingEngine : IProductMacroExecutionEngine
    {
        public int Executions { get; private set; }

        public Task<MacroRunSnapshot> ExecuteAsync(
            ProductMacroExecutionRequest request,
            IProgress<MacroRunSnapshot> progress,
            CancellationToken cancellationToken = default)
        {
            Executions++;
            return Task.FromResult(new MacroRunSnapshot(
                MacroRunPhase.Completed, request.Goal, request.TargetProcessName, 1,
                "保存済み", "test", "Moved", 0, 1, "完了", true, false));
        }
    }

    /// <summary>catalog列挙だけを見るtestでは実行engineを使わない。</summary>
    private sealed class UnusedEngine : IProductMacroExecutionEngine
    {
        public Task<MacroRunSnapshot> ExecuteAsync(
            ProductMacroExecutionRequest request,
            IProgress<MacroRunSnapshot> progress,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("このtestはmacroを実行しません。");
    }

    private sealed class FakeLiveSessionFactory(string fromScene = "scene-a", string toScene = "scene-b")
        : IDemonstrationLiveSessionFactory
    {
        public FakeDemonstrationInputCollector? Sink { get; private set; }

        public DemonstrationLiveSession Create(string targetProcessName)
        {
            Sink = new FakeDemonstrationInputCollector();
            return new DemonstrationLiveSession(
                @"C:\game\game.exe",
                "window:game",
                "game:live:test",
                new FakeObservationRuntime(Scene(fromScene, $"btn-from-{fromScene}"), Scene(toScene, $"btn-from-{toScene}")),
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
