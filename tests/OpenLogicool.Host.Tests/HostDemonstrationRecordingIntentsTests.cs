using Microsoft.Data.Sqlite;
using System.IO;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostDemonstrationRecordingIntentsTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"openlogicool-demo-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        File.Delete(path);
        File.Delete($"{path}.{MacroTargetSettingsStore.FileName}");
    }

    [Fact]
    public async Task Starting_a_recording_without_a_selected_target_is_refused()
    {
        var gate = new DemonstrationRecordingGate();
        using var intents = new HostDemonstrationRecordingIntents(path, new FakeLiveSessionFactory(), gate);

        await Assert.ThrowsAsync<InvalidOperationException>(() => intents.StartAsync("目的"));
    }

    [Fact]
    public async Task Start_records_the_session_and_status_reflects_recording()
    {
        SelectTarget();
        var factory = new FakeLiveSessionFactory();
        var gate = new DemonstrationRecordingGate();
        using var intents = new HostDemonstrationRecordingIntents(path, factory, gate);

        var summary = await intents.StartAsync("アークを開く");

        Assert.Equal(DemonstrationSessionState.Recording, summary.State);
        var status = intents.Status();
        Assert.Equal(DemonstrationRecorderStatus.Recording, status.Status);
        Assert.Equal(summary.SessionId, status.SessionId);
    }

    [Fact]
    public async Task Starting_twice_without_stopping_is_refused()
    {
        SelectTarget();
        var gate = new DemonstrationRecordingGate();
        using var intents = new HostDemonstrationRecordingIntents(path, new FakeLiveSessionFactory(), gate);
        _ = await intents.StartAsync("目的");

        await Assert.ThrowsAsync<InvalidOperationException>(() => intents.StartAsync("別の目的"));
    }

    [Fact]
    public async Task Stopping_without_starting_is_refused()
    {
        var gate = new DemonstrationRecordingGate();
        using var intents = new HostDemonstrationRecordingIntents(path, new FakeLiveSessionFactory(), gate);

        await Assert.ThrowsAsync<InvalidOperationException>(() => intents.StopAsync());
    }

    [Fact]
    public async Task A_full_click_operation_flows_through_the_pump_and_is_visible_after_stop_and_listing()
    {
        SelectTarget();
        var factory = new FakeLiveSessionFactory();
        var gate = new DemonstrationRecordingGate();
        using var intents = new HostDemonstrationRecordingIntents(path, factory, gate);
        var summary = await intents.StartAsync("アークを開く");

        var occurredUtc = DateTimeOffset.UtcNow;
        factory.Sink!.Observe(new DemonstrationInputEdge(
            ContractSchemaVersions.Revision03, DemonstrationInputSource.Mouse, DemonstrationInputEdgeKind.PointerDown,
            "left", "Mouse:Left", 100, occurredUtc, new DemonstrationScreenPoint(10, 10)));
        factory.Sink!.Observe(new DemonstrationInputEdge(
            ContractSchemaVersions.Revision03, DemonstrationInputSource.Mouse, DemonstrationInputEdgeKind.PointerUp,
            "left", "Mouse:Left", 130, occurredUtc.AddMilliseconds(30), new DemonstrationScreenPoint(10, 10)));

        var stopped = await intents.StopAsync();
        Assert.Equal(DemonstrationSessionState.Stopped, stopped.State);
        Assert.Equal(1, stopped.OperationCount);

        var sessions = intents.ListSessions();
        var listed = Assert.Single(sessions);
        Assert.Equal(summary.SessionId, listed.SessionId);
        Assert.Equal(1, listed.OperationCount);
    }

    [Fact]
    public async Task Recording_blocks_macro_playback_through_the_shared_gate_and_releases_it_on_stop()
    {
        SelectTarget();
        var gate = new DemonstrationRecordingGate();
        using var intents = new HostDemonstrationRecordingIntents(path, new FakeLiveSessionFactory(), gate);
        _ = await intents.StartAsync("目的");

        Assert.False(gate.TryBeginPlayback(out var refusal));
        Assert.NotNull(refusal);

        _ = await intents.StopAsync();

        Assert.True(gate.TryBeginPlayback(out _));
        gate.EndPlayback();
    }

    [Fact]
    public async Task Create_macro_from_a_stopped_session_compiles_a_playable_route()
    {
        SelectTarget();
        var factory = new FakeLiveSessionFactory();
        var gate = new DemonstrationRecordingGate();
        using var intents = new HostDemonstrationRecordingIntents(path, factory, gate);
        var summary = await intents.StartAsync("アークを開く");
        var occurredUtc = DateTimeOffset.UtcNow;
        factory.Sink!.Observe(new DemonstrationInputEdge(
            ContractSchemaVersions.Revision03, DemonstrationInputSource.Mouse, DemonstrationInputEdgeKind.PointerDown,
            "left", "Mouse:Left", 100, occurredUtc, new DemonstrationScreenPoint(10, 10)));
        factory.Sink!.Observe(new DemonstrationInputEdge(
            ContractSchemaVersions.Revision03, DemonstrationInputSource.Mouse, DemonstrationInputEdgeKind.PointerUp,
            "left", "Mouse:Left", 130, occurredUtc.AddMilliseconds(30), new DemonstrationScreenPoint(10, 10)));
        _ = await intents.StopAsync();

        var macro = intents.CreateMacroFromSession(summary.SessionId);

        Assert.Equal("game", macro.GameId);
        Assert.Equal(1, macro.StepCount);
        Assert.Equal("アークを開く", macro.Goal);
    }

    private void SelectTarget() => MacroTargetSettingsStore.ForDatabase(path).Save("game");

    private sealed class FakeLiveSessionFactory : IDemonstrationLiveSessionFactory
    {
        public FakeDemonstrationInputCollector? Sink { get; private set; }

        public DemonstrationLiveSession Create(string targetProcessName)
        {
            Sink = new FakeDemonstrationInputCollector();
            var sceneA = Scene("scene-a", "btn-to-b");
            var sceneB = Scene("scene-b", "btn-to-c");
            var runtime = new FakeObservationRuntime(sceneA, sceneB);
            return new DemonstrationLiveSession(
                @"C:\game\game.exe",
                "window:game",
                "game:live:test",
                runtime,
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
