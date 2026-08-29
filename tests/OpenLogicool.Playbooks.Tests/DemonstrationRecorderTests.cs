using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class DemonstrationRecorderTests
{
    private const string Schema = ContractSchemaVersions.Revision03;
    private const string GamePath = @"C:\games\nikke\nikke.exe";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddHours(1);

    [Fact]
    public async Task A_click_is_bound_to_the_observation_that_was_current_when_the_button_went_down()
    {
        var fixture = new Fixture();
        await fixture.StartAsync();
        // 押下より後に画面が動いても、beforeは押下時点のsceneのままでなければならない。
        var sceneAtPress = fixture.Runtime.CurrentSceneId;

        await fixture.PressAsync("Mouse:Left", 300, 200);
        await fixture.Runtime.AdvanceSceneAsync();
        await fixture.ReleaseAsync("Mouse:Left", 300, 200);

        var operation = Assert.Single(fixture.Operations);
        Assert.Equal(GameInteractionOperations.Click, operation.Operation);
        Assert.Equal(sceneAtPress, operation.Before.ObservationId);
        Assert.Equal(sceneAtPress, operation.Target.ObservationId);
        Assert.Equal(operation.Before.Frame.Sequence, operation.Target.FrameSequence);
        Assert.Equal(operation.Before.Frame.TransformRevision, operation.Target.TransformRevision);
        Assert.Equal([0.3, 0.2], operation.Target.NormalizedPoint!);
        Assert.Equal(GameTransitionJudgement.Moved, operation.Comparison.Judgement);
        Assert.Equal(1, fixture.Runtime.WaitStableCalls);
    }

    [Fact]
    public async Task Only_a_press_paired_with_its_release_becomes_a_finite_operation()
    {
        var fixture = new Fixture();
        await fixture.StartAsync();

        await fixture.KeyDownAsync("Key:Escape");
        Assert.Empty(fixture.Operations);
        Assert.Equal(1, fixture.Recorder.HeldPressCount);

        await fixture.KeyUpAsync("Key:Escape");
        var operation = Assert.Single(fixture.Operations);
        Assert.Equal(GameInteractionOperations.KeyTap, operation.Operation);
        Assert.Equal(["Key:Escape"], operation.KeyTokens!);
        Assert.Null(operation.Target.NormalizedPoint);
        Assert.Equal(0, fixture.Recorder.HeldPressCount);
    }

    [Fact]
    public async Task A_press_still_held_at_stop_is_not_written_as_an_operation()
    {
        var fixture = new Fixture();
        await fixture.StartAsync();
        await fixture.KeyDownAsync("Key:A");

        var record = fixture.Recorder.StopAsync("利用者が停止", fixture.Now());

        Assert.Empty(fixture.Operations);
        Assert.Equal(1, fixture.Recorder.Counters.DiscardedHeldPresses);
        Assert.Equal(DemonstrationSessionState.Stopped, record.State);
        Assert.Equal(DemonstrationGateState.Free, fixture.Gate.State);
    }

    [Fact]
    public async Task A_release_at_a_different_point_is_recorded_as_a_drag_not_a_click()
    {
        var fixture = new Fixture();
        await fixture.StartAsync();

        await fixture.PressAsync("Mouse:Left", 100, 100);
        await fixture.ReleaseAsync("Mouse:Left", 800, 600);

        var operation = Assert.Single(fixture.Operations);
        Assert.Equal(GameInteractionOperations.Drag, operation.Operation);
        Assert.Equal([0.1, 0.1], operation.Target.NormalizedPoint!);
        Assert.Equal([0.8, 0.6], operation.DragDestinationNormalized!);
    }

    [Fact]
    public async Task A_wheel_edge_is_recorded_as_a_scroll_with_its_own_step_count()
    {
        var fixture = new Fixture();
        await fixture.StartAsync();

        await fixture.WheelAsync(500, 500, verticalSteps: -3);

        var operation = Assert.Single(fixture.Operations);
        Assert.Equal(GameInteractionOperations.Scroll, operation.Operation);
        Assert.Equal(-3, operation.VerticalScrollSteps);
        Assert.Null(operation.HorizontalScrollSteps);
        Assert.Null(operation.KeyTokens);
    }

    [Fact]
    public async Task Losing_focus_pauses_recording_and_regaining_it_resumes_from_a_new_observation()
    {
        var fixture = new Fixture();
        await fixture.StartAsync();
        await fixture.KeyDownAsync("Key:A");

        await fixture.Recorder.ObserveForegroundAsync(@"C:\other\chat.exe", fixture.Now());
        Assert.Equal(DemonstrationRecorderStatus.Paused, fixture.Recorder.Status);
        Assert.Equal(1, fixture.Recorder.Counters.DiscardedHeldPresses);

        // pause中の入力は原本へ入らない。他appのkey文字も座標も保存されない。
        await fixture.KeyDownAsync("Key:B");
        await fixture.KeyUpAsync("Key:B");
        await fixture.PressAsync("Mouse:Left", 300, 200);
        await fixture.ReleaseAsync("Mouse:Left", 300, 200);
        Assert.Empty(fixture.Operations);
        Assert.Equal(4, fixture.Recorder.Counters.IgnoredWhilePaused);

        var beforeResume = fixture.Runtime.CurrentSceneId;
        await fixture.Recorder.ObserveForegroundAsync(GamePath, fixture.Now());
        Assert.Equal(DemonstrationRecorderStatus.Recording, fixture.Recorder.Status);

        var focusEvents = fixture.Store.Events
            .Where(item => item.Kind is DemonstrationEventKind.FocusLost or DemonstrationEventKind.FocusRegained)
            .ToArray();
        Assert.Equal(2, focusEvents.Length);
        Assert.Equal(@"C:\other\chat.exe", focusEvents[0].FocusChange!.ForegroundApplicationPath);
        Assert.Null(focusEvents[0].FocusChange!.ResumedObservationId);
        Assert.Equal(GamePath, focusEvents[1].FocusChange!.ForegroundApplicationPath);
        var resumedObservationId = focusEvents[1].FocusChange!.ResumedObservationId;
        Assert.NotNull(resumedObservationId);
        Assert.NotEqual(beforeResume, resumedObservationId);

        // 復帰後の押下は、復帰時に取り直した新しいObservationへ束縛される。
        await fixture.PressAsync("Mouse:Left", 300, 200);
        await fixture.ReleaseAsync("Mouse:Left", 300, 200);
        Assert.Equal(resumedObservationId, Assert.Single(fixture.Operations).Before.ObservationId);
    }

    [Fact]
    public async Task An_unidentifiable_foreground_is_recorded_as_a_pause_with_no_path()
    {
        var fixture = new Fixture();
        await fixture.StartAsync();

        await fixture.Recorder.ObserveForegroundAsync(null, fixture.Now());

        Assert.Equal(DemonstrationRecorderStatus.Paused, fixture.Recorder.Status);
        var focusLost = Assert.Single(fixture.Store.Events, item => item.Kind == DemonstrationEventKind.FocusLost);
        Assert.Null(focusLost.FocusChange!.ForegroundApplicationPath);
    }

    [Fact]
    public async Task Presses_outside_the_client_frame_are_counted_and_not_recorded()
    {
        var fixture = new Fixture();
        await fixture.StartAsync();

        await fixture.PressAsync("Mouse:Left", -5, 200);
        await fixture.ReleaseAsync("Mouse:Left", -5, 200);

        Assert.Empty(fixture.Operations);
        Assert.Equal(1, fixture.Recorder.Counters.IgnoredOutsideClientFrame);
        Assert.Equal(1, fixture.Recorder.Counters.UnpairedReleases);
    }

    [Fact]
    public async Task Recording_and_playback_cannot_hold_the_gate_at_the_same_time()
    {
        var fixture = new Fixture();
        await fixture.StartAsync();

        Assert.False(fixture.Gate.TryBeginPlayback(out var playbackRefusal));
        Assert.Equal("記録中は再生を開始できません。", playbackRefusal);

        fixture.Recorder.StopAsync("利用者が停止", fixture.Now());
        Assert.True(fixture.Gate.TryBeginPlayback(out _));

        var second = new Fixture(fixture.Gate);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => second.StartAsync());
        Assert.Equal("再生中は記録を開始できません。", error.Message);
    }

    [Fact]
    public async Task A_refused_start_does_not_leave_the_gate_held()
    {
        var fixture = new Fixture();
        fixture.Store.FailNextStart = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.StartAsync());

        Assert.Equal(DemonstrationGateState.Free, fixture.Gate.State);
        Assert.Equal(DemonstrationRecorderStatus.Idle, fixture.Recorder.Status);
    }

    private sealed class Fixture
    {
        public Fixture(DemonstrationRecordingGate? gate = null)
        {
            Gate = gate ?? new DemonstrationRecordingGate();
            Recorder = new DemonstrationRecorder(
                Store,
                Runtime,
                point => point.X is < 0 or > 1000 || point.Y is < 0 or > 1000
                    ? null
                    : [point.X / 1000.0, point.Y / 1000.0],
                Gate,
                new ExplorationWaitCondition(Schema, 3, 500, 10_000));
        }

        private int tick;

        public FakeStore Store { get; } = new();

        public FakeObservationRuntime Runtime { get; } = new();

        public DemonstrationRecordingGate Gate { get; }

        public DemonstrationRecorder Recorder { get; }

        public IReadOnlyList<DemonstrationOperation> Operations =>
            Store.Events
                .Where(item => item.Kind == DemonstrationEventKind.Operation)
                .Select(item => item.Operation!)
                .ToArray();

        public Task<DemonstrationSessionRecord> StartAsync() =>
            Recorder.StartAsync(new DemonstrationSessionDraft(
                Schema,
                "demo-session-1",
                "nikke",
                "env-1",
                "アークを開く",
                GamePath,
                "window-nikke",
                "windows-demonstration-recorder-v1",
                Start));

        public Task PressAsync(string control, int x, int y) =>
            Recorder.HandleAsync(Edge(DemonstrationInputEdgeKind.PointerDown, control, x, y));

        public Task ReleaseAsync(string control, int x, int y) =>
            Recorder.HandleAsync(Edge(DemonstrationInputEdgeKind.PointerUp, control, x, y));

        public Task KeyDownAsync(string token) =>
            Recorder.HandleAsync(Edge(DemonstrationInputEdgeKind.KeyDown, token, null, null));

        public Task KeyUpAsync(string token) =>
            Recorder.HandleAsync(Edge(DemonstrationInputEdgeKind.KeyUp, token, null, null));

        public Task WheelAsync(int x, int y, int verticalSteps) =>
            Recorder.HandleAsync(new DemonstrationInputEdge(
                Schema,
                DemonstrationInputSource.Mouse,
                DemonstrationInputEdgeKind.Wheel,
                "Mouse:Wheel",
                "Mouse:Wheel",
                Tick() * 100,
                Start.AddSeconds(tick),
                new DemonstrationScreenPoint(x, y),
                verticalSteps));

        /// <summary>入力・focus観測の順に進む時計。pauseで捨てたedgeでも時間は進める。</summary>
        public DateTimeOffset Now() => Start.AddSeconds(Tick());

        private int Tick() => ++tick;

        private DemonstrationInputEdge Edge(
            DemonstrationInputEdgeKind kind,
            string control,
            int? x,
            int? y) =>
            new(
                Schema,
                kind is DemonstrationInputEdgeKind.KeyDown or DemonstrationInputEdgeKind.KeyUp
                    ? DemonstrationInputSource.Keyboard
                    : DemonstrationInputSource.Mouse,
                kind,
                control,
                control,
                Tick() * 100,
                Start.AddSeconds(tick),
                x is null || y is null ? null : new DemonstrationScreenPoint(x.Value, y.Value));
    }

    /// <summary>受入規則は製品と同じvalidatorを通す、in-memoryの原本。</summary>
    private sealed class FakeStore : IDemonstrationSessionStore
    {
        private readonly List<DemonstrationEvent> events = [];
        private DemonstrationSessionDraft? session;

        public bool FailNextStart { get; set; }

        public IReadOnlyList<DemonstrationEvent> Events => events;

        public DemonstrationSessionRecord Start(DemonstrationSessionDraft draft)
        {
            if (FailNextStart)
            {
                FailNextStart = false;
                throw new InvalidOperationException("原本を開始できませんでした。");
            }

            DemonstrationSessionValidator.ValidateSession(draft);
            session = draft;
            return new DemonstrationSessionRecord(draft, DemonstrationSessionState.Recording, null, []);
        }

        public DemonstrationEvent Append(DemonstrationEventDraft draft)
        {
            var active = session ?? throw new InvalidOperationException("原本がありません。");
            DemonstrationSessionValidator.ValidateAppend(active, events, draft);
            var sequence = events.Count + 1;
            var stored = new DemonstrationEvent(
                draft.SchemaVersion,
                draft.SessionId,
                sequence,
                $"demo-event:{sequence}",
                sequence == 1 ? null : $"demo:{sequence - 1}",
                $"demo:{sequence}",
                draft.Kind,
                draft.OccurredUtc,
                draft.OccurredUtc,
                draft.Operation,
                draft.FocusChange,
                draft.Stop);
            events.Add(stored);
            return stored;
        }

        public DemonstrationSessionRecord? Load(string sessionId) =>
            session is null
                ? null
                : new DemonstrationSessionRecord(
                    session,
                    events.Count > 0 && events[^1].Kind == DemonstrationEventKind.Stopped
                        ? DemonstrationSessionState.Stopped
                        : DemonstrationSessionState.Recording,
                    events.Count == 0 ? null : events[^1].ResultingRevisionId,
                    events.ToArray());

        public IReadOnlyList<string> ListSessionIds(string gameId, string environmentScope) =>
            session is null ? [] : [session.SessionId];
    }

    /// <summary>
    /// 観測面だけを実装する。dispatchのmethodが存在しないので、記録器が入力を出せないことは
    /// この型がcompileできること自体で示される。
    /// </summary>
    private sealed class FakeObservationRuntime : IDemonstrationObservationRuntime
    {
        private long sequence;

        public string CurrentSceneId { get; private set; } = string.Empty;

        public int WaitStableCalls { get; private set; }

        public async Task AdvanceSceneAsync() => _ = await NextSceneAsync();

        public ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            sequence++;
            return ValueTask.FromResult(new ObservationResult(
                Schema,
                $"obs-{sequence}",
                Frame(sequence),
                CaptureAvailability.Available,
                StateIdentityStatus.Novel,
                [],
                "local-target-tracking-v1",
                12,
                null));
        }

        public ValueTask<ObservedScene> DiscoverTargetsAsync(
            ObservationResult observation,
            CancellationToken cancellationToken = default)
        {
            CurrentSceneId = observation.ObservationId;
            return ValueTask.FromResult(Scene(observation));
        }

        public ValueTask<GameInteractionStabilityResult> WaitStableAsync(
            ObservedScene before,
            ExplorationWaitCondition condition,
            CancellationToken cancellationToken = default)
        {
            WaitStableCalls++;
            var stable = NextSceneAsync().GetAwaiter().GetResult();
            return ValueTask.FromResult(new GameInteractionStabilityResult(
                Schema,
                GameInteractionStabilityStatus.Stable,
                [stable],
                stable,
                17,
                8_500,
                10_012,
                null));
        }

        public GameTransitionComparison Compare(ObservedScene before, GameInteractionStabilityResult after) =>
            new(
                Schema,
                before.ObservationId,
                after.StableScene?.ObservationId,
                GameTransitionJudgement.Moved,
                [],
                ["意味構造が変化した"]);

        private async Task<ObservedScene> NextSceneAsync()
        {
            var observation = await ObserveAsync();
            return await DiscoverTargetsAsync(observation);
        }

        private static ObservedScene Scene(ObservationResult observation) =>
            new(
                Schema,
                $"scene-{observation.ObservationId}",
                observation.ObservationId,
                observation.Frame,
                CaptureAvailability.Available,
                StateIdentityStatus.Novel,
                null,
                [],
                [],
                "local-target-tracking-v1");

        private static CapturedFrameReference Frame(long sequence) =>
            new(
                Schema,
                "window-nikke",
                CaptureBackend.WindowsGraphicsCapture,
                sequence,
                sequence * 16.0,
                Start.AddMilliseconds(sequence * 16),
                7,
                12,
                8);
    }
}
