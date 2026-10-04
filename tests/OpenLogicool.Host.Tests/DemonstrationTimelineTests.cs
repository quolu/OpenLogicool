using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class DemonstrationTimelineTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"demo-timeline-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Origin = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public async Task Saved_pixels_are_read_back_and_a_changed_artifact_cannot_reuse_the_previous_frame()
    {
        var frame = Frame(1, 0).Frame with { Pixels = new FramePixels(new byte[] { 10, 20, 30, 255 }, 4) };
        var evidence = new LocalPngGameFrameEvidenceSink(directory, new WindowsGameFramePngEncoder());
        var artifact = await evidence.SaveAsync(frame);
        var source = new DemonstrationRecordedFrameSource();
        var snapshot = new DemonstrationTimelineFrame(frame with { Pixels = null }, artifact);
        await source.SelectAsync(snapshot, CancellationToken.None);
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, (await source.CaptureAsync()).Pixels!.Bgra8.ToArray());
        await File.AppendAllTextAsync(artifact.LocalPath!, "変更");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.SelectAsync(snapshot, CancellationToken.None));
        Assert.Contains("SHA-256", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.CaptureAsync().AsTask());
    }

    [Fact]
    public async Task Recording_two_clicks_is_independent_of_blocked_vision_and_preserves_the_raw_journal()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var clock = new FixedTime(Origin);
        using var recording = new DemonstrationTimelineRecording(new FixedCapture(), new FakeEvidence(), _ => [0.5, 0.5],
            async (frame, token) =>
            {
                Interlocked.Increment(ref calls);
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                return Scene(frame, "ロビー");
            }, directory, clock);
        await recording.StartAsync(Draft());
        Click(recording, Origin.AddSeconds(1), 100);
        Click(recording, Origin.AddSeconds(2), 200);
        Assert.Equal(0, calls);
        Assert.Equal(2, recording.Status().TotalOperations);
        clock.Now = Origin.AddSeconds(3);
        using var connection = Open();
        var stopped = recording.StopAndAnalyzeAsync(new SqliteDemonstrationSessionStore(connection));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DemonstrationRecorderStatus.Analyzing, recording.Status().Status);
        Assert.Equal(4, File.ReadAllLines(Path.Combine(directory, "timeline-inputs.jsonl")).Length);
        Assert.DoesNotContain("ScreenPoint", File.ReadAllText(Path.Combine(directory, "timeline-inputs.jsonl")));
        Assert.True(File.Exists(Path.Combine(directory, "timeline-stopped.json")));
        release.SetResult();
        var result = await stopped;
        Assert.Equal(2, result.Events.Count(item => item.Kind == DemonstrationEventKind.Operation));
        Assert.All(result.Events.Where(item => item.Operation is not null), item =>
            Assert.Equal(GameTransitionJudgement.Undetermined, item.Operation!.Comparison.Judgement));
    }

    [Fact]
    public async Task Each_click_uses_its_captured_before_and_the_next_click_closes_its_after_interval()
    {
        var frames = Enumerable.Range(0, 9).Select(index => Frame(index + 1, index)).ToArray();
        var inputs = Inputs(1, 5);
        using var connection = Open();
        var result = await DemonstrationTimelineAnalyzer.AnalyzeAsync(Draft(), frames, inputs, Origin.AddSeconds(9),
            new SqliteDemonstrationSessionStore(connection),
            (frame, _) => ValueTask.FromResult(Scene(frame, frame.Frame.Sequence is >= 3 and <= 6 ? "アーク" : "ロビー")),
            _ => { });
        var operations = result.Events.Where(item => item.Operation is not null).Select(item => item.Operation!).ToArray();
        Assert.Equal(2, operations.Length);
        Assert.Equal(2, operations[0].Before.Frame.Sequence);
        Assert.Equal(6, operations[1].Before.Frame.Sequence);
        Assert.All(operations, item => Assert.Equal(GameTransitionJudgement.Moved, item.Comparison.Judgement));
        Assert.All(operations[0].After.Observations, scene => Assert.True(scene.Frame.WallClockUtc < Origin.AddSeconds(5)));
    }

    [Fact]
    public async Task An_unchanged_WGC_frame_keeps_its_identity_and_uses_distinct_observation_times_for_stability()
    {
        var before = Frame(1, 0);
        var after = Frame(2, 2);
        var frames = new[] { before, after, after with { ObservedUtc = Origin.AddSeconds(3) },
            after with { ObservedUtc = Origin.AddSeconds(4) } };
        var inputs = Inputs(1).Select(item => item with { FrameSequence = 1 }).ToArray();
        using var connection = Open();
        var result = await DemonstrationTimelineAnalyzer.AnalyzeAsync(Draft(), frames, inputs, Origin.AddSeconds(5),
            new SqliteDemonstrationSessionStore(connection),
            (frame, _) => ValueTask.FromResult(Scene(frame, frame.Frame.Sequence == 1 ? "ロビー" : "アーク")), _ => { });
        var operation = Assert.Single(result.Events, item => item.Operation is not null).Operation!;
        Assert.Equal(GameTransitionJudgement.Moved, operation.Comparison.Judgement);
        Assert.Equal(2, operation.After.StableScene!.Frame.Sequence);
        Assert.Equal(Origin.AddSeconds(2), operation.After.StableScene.Frame.WallClockUtc);
    }

    [Fact]
    public async Task A_loading_transition_keeps_the_saved_stable_interval_between_sparse_analysis_samples()
    {
        // 実記録と同じ取得間隔。1秒間隔への間引きでは安定区間1.44秒が0.74秒になる。
        var milliseconds = new[] { 1_260, 1_584, 1_926, 2_285, 2_657, 3_028 };
        var frames = new[] { Frame(1, 0) }.Concat(milliseconds.Select((at, index) =>
            Frame(index + 2, 0) with { ObservedUtc = Origin.AddMilliseconds(1_010 + at) })).ToArray();
        var inputs = Inputs(1).Select(item => item with { FrameSequence = 1 }).ToArray();
        using var connection = Open();
        var result = await DemonstrationTimelineAnalyzer.AnalyzeAsync(Draft(), frames, inputs, Origin.AddMilliseconds(4_370),
            new SqliteDemonstrationSessionStore(connection),
            (frame, _) => ValueTask.FromResult(Scene(frame, frame.Frame.Sequence == 1 ? "ロビー"
                : frame.Frame.Sequence == 2 ? "読み込み" : "アーク")), _ => { });
        var operation = Assert.Single(result.Events, item => item.Operation is not null).Operation!;
        Assert.Equal(GameTransitionJudgement.Moved, operation.Comparison.Judgement);
        Assert.True(operation.After.StableMillisecondsObserved >= 1_000);
    }

    [Fact]
    public async Task A_late_divergence_in_the_same_causal_interval_is_not_reported_as_stable()
    {
        var frames = Enumerable.Range(0, 6).Select(index => Frame(index + 1, index)).ToArray();
        using var connection = Open();
        var result = await DemonstrationTimelineAnalyzer.AnalyzeAsync(Draft(), frames, Inputs(1), Origin.AddSeconds(6),
            new SqliteDemonstrationSessionStore(connection),
            (frame, _) => ValueTask.FromResult(Scene(frame, frame.Frame.Sequence < 6 ? "アーク" : "ロビー")), _ => { });
        var operation = Assert.Single(result.Events, item => item.Operation is not null).Operation!;
        Assert.Equal(GameTransitionJudgement.Moved, operation.Comparison.Judgement);
        Assert.Equal(GameInteractionStabilityStatus.TimedOut, operation.After.Status);
        Assert.Null(operation.After.StableScene);
    }

    [Fact]
    public async Task Normal_speed_clicks_use_the_whole_saved_interval_without_a_live_settling_delay()
    {
        var frames = new[] { Frame(1, 0), Frame(2, 0) with { ObservedUtc = Origin.AddMilliseconds(250) },
            Frame(3, 0) with { ObservedUtc = Origin.AddMilliseconds(500) },
            Frame(4, 0) with { ObservedUtc = Origin.AddMilliseconds(800) },
            Frame(5, 0) with { ObservedUtc = Origin.AddMilliseconds(1_050) } };
        var inputs = Inputs(0, 1).Select((item, index) => item with
        {
            OccurredUtc = Origin.AddMilliseconds(index < 2 ? 50 + index * 10 : 650 + (index - 2) * 10),
            FrameSequence = index < 2 ? 1 : 3,
        }).ToArray();
        using var connection = Open();
        var result = await DemonstrationTimelineAnalyzer.AnalyzeAsync(Draft(), frames, inputs,
            Origin.AddMilliseconds(1_200), new SqliteDemonstrationSessionStore(connection),
            (frame, _) => ValueTask.FromResult(Scene(frame, frame.Frame.Sequence == 1 ? "ロビー"
                : frame.Frame.Sequence <= 3 ? "アーク" : "ロビー")), _ => { });
        var operations = result.Events.Where(item => item.Operation is not null).Select(item => item.Operation!).ToArray();
        Assert.Equal(2, operations.Length);
        Assert.All(operations, operation => Assert.Equal(GameTransitionJudgement.Moved, operation.Comparison.Judgement));
        Assert.Equal(new long[] { 2, 3 }, operations[0].After.Observations.Select(scene => scene.Frame.Sequence));
        Assert.Equal(new long[] { 4, 5 }, operations[1].After.Observations.Select(scene => scene.Frame.Sequence));
        Assert.All(operations, operation => Assert.Equal(250, operation.After.StableMillisecondsObserved));
    }

    [Fact]
    public async Task One_after_image_is_compared_with_before_without_inventing_stability()
    {
        var frames = new[] { Frame(1, 0), Frame(2, 0) with { ObservedUtc = Origin.AddMilliseconds(250) } };
        using var connection = Open();
        var result = await DemonstrationTimelineAnalyzer.AnalyzeAsync(Draft(), frames, Inputs(0),
            Origin.AddMilliseconds(600), new SqliteDemonstrationSessionStore(connection),
            (frame, _) => ValueTask.FromResult(Scene(frame, frame.Frame.Sequence == 1 ? "ロビー" : "アーク")), _ => { });
        var operation = Assert.Single(result.Events, item => item.Operation is not null).Operation!;
        Assert.Equal(GameTransitionJudgement.Moved, operation.Comparison.Judgement);
        Assert.Equal(GameInteractionStabilityStatus.TimedOut, operation.After.Status);
        Assert.Null(operation.After.StableScene);
        Assert.Single(operation.After.Observations);
        Assert.Equal(1, operation.After.StableFramesObserved);
        Assert.Equal(0, operation.After.StableMillisecondsObserved);
    }

    [Fact]
    public async Task Missing_semantic_evidence_at_the_end_invalidates_an_earlier_stable_screen()
    {
        var frames = Enumerable.Range(0, 4).Select(index => Frame(index + 1, index)).ToArray();
        using var connection = Open();
        var result = await DemonstrationTimelineAnalyzer.AnalyzeAsync(Draft(), frames, Inputs(0), Origin.AddSeconds(4),
            new SqliteDemonstrationSessionStore(connection), (frame, _) => ValueTask.FromResult(
                frame.Frame.Sequence == 4 ? Scene(frame, "アーク") with { Affordances = [] }
                : Scene(frame, frame.Frame.Sequence == 1 ? "ロビー" : "アーク")), _ => { });
        var operation = Assert.Single(result.Events, item => item.Operation is not null).Operation!;
        Assert.Equal(GameTransitionJudgement.Undetermined, operation.Comparison.Judgement);
        Assert.Null(operation.After.StableScene);
        Assert.Equal(0, operation.After.StableFramesObserved);
    }

    [Fact]
    public async Task Losing_foreground_discards_a_held_press_and_other_app_input_is_not_saved()
    {
        var clock = new FixedTime(Origin);
        using var recording = new DemonstrationTimelineRecording(new FixedCapture(), new FakeEvidence(), _ => [0.5, 0.5],
            (frame, _) => ValueTask.FromResult(Scene(frame, "ロビー")), directory, clock);
        await recording.StartAsync(Draft());
        recording.Observe(Edge(DemonstrationInputEdgeKind.PointerDown, Origin.AddSeconds(1), 100));
        recording.ObserveForeground(null, Origin.AddSeconds(2));
        recording.Observe(Edge(DemonstrationInputEdgeKind.PointerUp, Origin.AddSeconds(3), 200));
        clock.Now = Origin.AddSeconds(4);
        using var connection = Open();
        var result = await recording.StopAndAnalyzeAsync(new SqliteDemonstrationSessionStore(connection));
        Assert.DoesNotContain(result.Events, item => item.Kind == DemonstrationEventKind.Operation);
        Assert.Equal(1, recording.Status().DiscardedHeldPresses);
        Assert.Equal(2, File.ReadAllLines(Path.Combine(directory, "timeline-inputs.jsonl")).Length);
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(directory);
        var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "demo.db")};Pooling=False");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static DemonstrationSessionDraft Draft() => new(ContractSchemaVersions.Revision03, "demo:test", "game", "test",
        "アークを開いてロビーへ戻る", @"C:\game\game.exe", "window:game", "recorder-2.0.0", Origin);
    private static void Click(DemonstrationTimelineRecording recording, DateTimeOffset at, long monotonic)
    {
        recording.Observe(Edge(DemonstrationInputEdgeKind.PointerDown, at, monotonic));
        recording.Observe(Edge(DemonstrationInputEdgeKind.PointerUp, at.AddMilliseconds(10), monotonic + 10));
    }
    private static DemonstrationInputEdge Edge(DemonstrationInputEdgeKind kind, DateTimeOffset at, long monotonic) => new(
        ContractSchemaVersions.Revision03, DemonstrationInputSource.Mouse, kind, "left", "Mouse:Left", monotonic, at,
        new DemonstrationScreenPoint(500, 500));
    private static DemonstrationTimelineInput[] Inputs(params int[] seconds) => seconds.SelectMany(second => new[]
    {
        new DemonstrationTimelineInput(Origin.AddSeconds(second), second + 1, DemonstrationInputSource.Mouse,
            DemonstrationInputEdgeKind.PointerDown, "left", "Mouse:Left", second * 1000, [0.5, 0.5], 0, 0, null),
        new DemonstrationTimelineInput(Origin.AddSeconds(second).AddMilliseconds(10), second + 1, DemonstrationInputSource.Mouse,
            DemonstrationInputEdgeKind.PointerUp, "left", "Mouse:Left", second * 1000 + 10, [0.5, 0.5], 0, 0, null),
    }).ToArray();
    private static DemonstrationTimelineFrame Frame(int sequence, int second) => new(
        new CapturedFrame(ContractSchemaVersions.Revision03, "window:game", CaptureBackend.WindowsGraphicsCapture, sequence,
            second * 1000, Origin.AddSeconds(second), 1, 1, "BGRA8", 96, 96, 1, 0, 0),
        new CapturedFrameArtifact($"frame:{sequence}", "image/png", "test", 1, 1, "test.png"));
    private static ObservedScene Scene(DemonstrationTimelineFrame snapshot, string label)
    {
        var frame = snapshot.Frame;
        var id = $"obs:{frame.Sequence}";
        var reference = new CapturedFrameReference(ContractSchemaVersions.Revision03, frame.SourceId, frame.Backend,
            frame.Sequence, frame.MonotonicMs, frame.WallClockUtc, 1, 0, 0, snapshot.Artifact);
        return new ObservedScene(ContractSchemaVersions.Revision03, $"scene:{id}", id, reference, CaptureAvailability.Available,
            StateIdentityStatus.Novel, null, [],
            [new AffordanceCandidate(ContractSchemaVersions.Revision03, $"button:{id}", id, frame.Sequence, 1, frame.SourceId,
                new AffordanceLocator(ContractSchemaVersions.Revision03, "ocr", [0.4, 0.4, 0.2, 0.2], "test"),
                [], 0.9, [GameInteractionOperations.Click], SemanticLabel: label)], "test");
    }
    private sealed class FixedCapture : IProductGameFrameSource
    {
        public ValueTask<CapturedFrame> CaptureAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Frame(1, 0).Frame);
    }
    private sealed class FakeEvidence : IProductGameFrameEvidenceSink
    {
        public ValueTask<CapturedFrameArtifact> SaveAsync(CapturedFrame frame, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Frame(1, 0).Artifact);
    }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
