using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class DemonstrationReanalysisTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"demo-reanalysis-{Guid.NewGuid():N}");
    private string Database => Path.Combine(directory, "demo.db");
    private static readonly DateTimeOffset Origin = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public async Task Reanalysis_keeps_the_original_and_adds_a_reopenable_result_from_the_full_saved_timeline()
    {
        var original = await SeedAsync();
        var raw = Directory.GetFiles(directory, "timeline-*").ToDictionary(path => path, File.ReadAllText);
        var originalJson = JsonSerializer.Serialize(original);
        var gate = new DemonstrationRecordingGate();
        var analysis = new SavedAnalysis(gate);
        using var intents = new HostDemonstrationRecordingIntents(Database, new NoLiveFactory(), gate, reanalysis: analysis);
        var result = await intents.ReanalyzeAsync(original.Session.SessionId);
        Assert.Equal(2, result.OperationCount);
        Assert.Equal("再解析済み", result.StateLabel);
        Assert.All(intents.ListSteps(result.SessionId), step => Assert.Equal("画面が変わった", step.TransitionLabel));
        Assert.Equal(DemonstrationGateState.Free, gate.State);
        using (var db = Open())
        {
            var store = new SqliteDemonstrationSessionStore(db);
            Assert.Equal(originalJson, JsonSerializer.Serialize(store.Load(original.Session.SessionId)));
            var added = store.Load(result.SessionId)!;
            Assert.Equal(original.Session.SessionId, added.Session.SourceSessionId);
            Assert.Equal(original.Session.StartedUtc, added.Session.StartedUtc);
            Assert.Equal(original.Events.Where(item => item.Operation is not null).Select(item => item.Operation!.Target.NormalizedPoint),
                added.Events.Where(item => item.Operation is not null).Select(item => item.Operation!.Target.NormalizedPoint));
        }
        using var reopened = new HostDemonstrationRecordingIntents(Database, new NoLiveFactory(), gate, reanalysis: analysis);
        Assert.Equal(result.SessionId, reopened.ListSessions()[0].SessionId);
        var repeated = await reopened.ReanalyzeAsync(result.SessionId);
        Assert.NotEqual(result.SessionId, repeated.SessionId);
        Assert.Equal(original.Session.SessionId, analysis.LastSourceId);
        Assert.All(raw, item => Assert.Equal(item.Value, File.ReadAllText(item.Key)));
    }

    [Fact]
    public async Task A_different_archive_is_refused_and_the_gate_is_released_without_a_new_result()
    {
        var original = await SeedAsync();
        File.WriteAllText(Path.Combine(directory, "timeline-session.json"), JsonSerializer.Serialize(new
        {
            SchemaVersion = "0.4.0", Session = original.Session with { SessionId = "other" },
        }));
        var gate = new DemonstrationRecordingGate();
        using var intents = new HostDemonstrationRecordingIntents(Database, new NoLiveFactory(), gate, reanalysis: new SavedAnalysis(gate));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => intents.ReanalyzeAsync(original.Session.SessionId));
        Assert.Contains("一致しません", error.Message);
        Assert.Single(intents.ListSessions());
        Assert.Equal(DemonstrationGateState.Free, gate.State);
    }

    private async Task<DemonstrationSessionRecord> SeedAsync()
    {
        using var db = Open();
        var frames = Enumerable.Range(0, 9).Select(index => new DemonstrationTimelineFrame(
            new CapturedFrame(ContractSchemaVersions.Revision03, "window:game", CaptureBackend.WindowsGraphicsCapture,
                index + 1, index * 1000, Origin.AddSeconds(index), 1, 1, "BGRA8", 96, 96, 1, 0, 0),
            new CapturedFrameArtifact($"frame:{index + 1}", "image/png", "test", 1, 1, Path.Combine(directory, $"{index}.png")))).ToArray();
        var inputs = new[] { 1, 5 }.SelectMany(second => new[]
        {
            new DemonstrationTimelineInput(Origin.AddSeconds(second), second + 1, DemonstrationInputSource.Mouse,
                DemonstrationInputEdgeKind.PointerDown, "left", "Mouse:Left", second * 1000, [0.5, 0.5], 0, 0, null),
            new DemonstrationTimelineInput(Origin.AddSeconds(second).AddMilliseconds(10), second + 1, DemonstrationInputSource.Mouse,
                DemonstrationInputEdgeKind.PointerUp, "left", "Mouse:Left", second * 1000 + 10, [0.5, 0.5], 0, 0, null),
        }).ToArray();
        var draft = new DemonstrationSessionDraft(ContractSchemaVersions.Revision03, "demo:original", "game", "env",
            "アーク往復", @"C:\game\game.exe", "window:game", "recorder-2.0.0", Origin);
        File.WriteAllText(Path.Combine(directory, "timeline-session.json"), JsonSerializer.Serialize(new { SchemaVersion = "0.4.0", Session = draft }));
        File.WriteAllLines(Path.Combine(directory, "timeline-frames.jsonl"), frames.Select(item => JsonSerializer.Serialize(item)));
        File.WriteAllLines(Path.Combine(directory, "timeline-inputs.jsonl"), inputs.Select(item => JsonSerializer.Serialize(item)));
        File.WriteAllText(Path.Combine(directory, "timeline-stopped.json"), JsonSerializer.Serialize(new { SchemaVersion = "0.4.0", StoppedUtc = Origin.AddSeconds(9) }));
        MacroTargetSettingsStore.ForDatabase(Database).Save("game");
        // 旧解析は操作後観測を欠いた結果として保持する。原本のframe列は完全なまま。
        var original = await DemonstrationTimelineAnalyzer.AnalyzeAsync(draft,
            frames.Where(frame => frame.Frame.Sequence is 1 or 2 or 6).ToArray(), inputs,
            Origin.AddSeconds(9), new SqliteDemonstrationSessionStore(db), SceneAsync, _ => { });
        Assert.All(original.Events.Where(item => item.Operation is not null),
            item => Assert.Equal(GameTransitionJudgement.Undetermined, item.Operation!.Comparison.Judgement));
        return original;
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(directory);
        var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static ValueTask<ObservedScene> SceneAsync(DemonstrationTimelineFrame snapshot, CancellationToken cancellationToken)
    {
        var frame = snapshot.Frame;
        var id = $"obs:{frame.Sequence}";
        var reference = new CapturedFrameReference(ContractSchemaVersions.Revision03, frame.SourceId, frame.Backend,
            frame.Sequence, frame.MonotonicMs, frame.WallClockUtc, 1, 0, 0, snapshot.Artifact);
        return ValueTask.FromResult(new ObservedScene(ContractSchemaVersions.Revision03, $"scene:{id}", id, reference,
            CaptureAvailability.Available, StateIdentityStatus.Novel, null, [],
            [new AffordanceCandidate(ContractSchemaVersions.Revision03, $"button:{id}", id, frame.Sequence, 1, frame.SourceId,
                new AffordanceLocator(ContractSchemaVersions.Revision03, "ocr", [0.4, 0.4, 0.2, 0.2], "test"),
                [], 0.9, [GameInteractionOperations.Click], SemanticLabel: frame.Sequence is >= 3 and <= 6 ? "アーク" : "ロビー")], "test"));
    }

    private sealed class NoLiveFactory : IDemonstrationLiveSessionFactory
    {
        public DemonstrationLiveSession Create(string targetProcessName) => throw new Xunit.Sdk.XunitException("再解析はlive記録を開始しません。");
    }

    private sealed class SavedAnalysis(DemonstrationRecordingGate gate) : IDemonstrationTimelineReanalysis
    {
        public string? LastSourceId { get; private set; }
        public Task<DemonstrationSessionRecord> AnalyzeAsync(DemonstrationSessionRecord source, IDemonstrationSessionStore store,
            Action<int> progress, CancellationToken cancellationToken)
        {
            Assert.False(gate.TryBeginPlayback(out _));
            LastSourceId = source.Session.SessionId;
            return DemonstrationTimelineArchive.Load(source).AnalyzeAsync(store, SceneAsync, progress, cancellationToken);
        }
    }
}
