using Microsoft.Data.Sqlite;
using System.IO;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

// 一時的な原因再現試験。結果とソースを証拠へ保存してから試験projectから外す。
public sealed class DemonstrationStartupDiagnosticTests
{
    [Fact]
    public async Task Clicks_during_initial_vision_are_preserved_after_recording_stops()
    {
        var path = Path.Combine(Path.GetTempPath(), $"demo-start-diagnostic-{Guid.NewGuid():N}.db");
        var factory = new Factory();
        try
        {
            MacroTargetSettingsStore.ForDatabase(path).Save("game");
            using var intents = new HostDemonstrationRecordingIntents(path, factory, new DemonstrationRecordingGate());
            var starting = intents.StartAsync("往復を記録する");
            await factory.Runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // 初期画面が取得済みでもAI解析中に操作するとどうなるか。
            factory.Collector.Click(100);
            factory.Runtime.Resume.SetResult();
            await starting;
            factory.Collector.Click(200);
            var stopped = await intents.StopAsync();

            Assert.Equal(2, stopped.OperationCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete($"{path}.{MacroTargetSettingsStore.FileName}");
        }
    }

    private sealed class Factory : IDemonstrationLiveSessionFactory
    {
        public Collector Collector { get; } = new();
        public Runtime Runtime { get; } = new();
        public DemonstrationLiveSession Create(string targetProcessName) => new(
            @"C:\game\game.exe", "window:game", "game:live:diagnostic", Runtime, Collector, _ => [0.5, 0.5]);
    }

    private sealed class Collector : IDemonstrationInputCollector
    {
        private IDemonstrationInputSink? sink;
        public void Start(IDemonstrationInputSink value) => sink = value;
        public void Stop() => sink = null;
        public void Dispose() { }
        public void Click(long at)
        {
            var down = new DemonstrationInputEdge(
                ContractSchemaVersions.Revision03, DemonstrationInputSource.Mouse,
                DemonstrationInputEdgeKind.PointerDown, "left", "Mouse:Left", at,
                DateTimeOffset.UtcNow, new DemonstrationScreenPoint(10, 10));
            sink?.Observe(down);
            sink?.Observe(down with
            {
                Kind = DemonstrationInputEdgeKind.PointerUp,
                MonotonicMs = at + 1,
                OccurredUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    private sealed class Runtime : IDemonstrationObservationRuntime
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CapturedFrameReference frame = new(
            ContractSchemaVersions.Revision03, "window:game", CaptureBackend.WindowsGraphicsCapture,
            1, 100, DateTimeOffset.UtcNow, 1, 10, 300);
        public ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ObservationResult(
                ContractSchemaVersions.Revision03, "obs-before", frame, CaptureAvailability.Available,
                StateIdentityStatus.Novel, [], "recognizer-1", 0, null));
        public async ValueTask<ObservedScene> DiscoverTargetsAsync(
            ObservationResult observation, CancellationToken cancellationToken = default)
        {
            Entered.SetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            return Scene("obs-before");
        }
        public ValueTask<GameInteractionStabilityResult> WaitStableAsync(
            ObservedScene before, ExplorationWaitCondition condition, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GameInteractionStabilityResult(
                ContractSchemaVersions.Revision03, GameInteractionStabilityStatus.Stable,
                [Scene("obs-after")], Scene("obs-after"), 2, 1_000, 1_200, null));
        public GameTransitionComparison Compare(ObservedScene before, GameInteractionStabilityResult after) => new(
            ContractSchemaVersions.Revision03, before.ObservationId, "obs-after", GameTransitionJudgement.Moved,
            [], ["診断用の既知遷移"]);
        private ObservedScene Scene(string id) => new(
            ContractSchemaVersions.Revision03, $"scene-{id}", id, frame, CaptureAvailability.Available,
            StateIdentityStatus.Novel, null, [], [], "perception-1");
    }
}
