using System.IO;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using OpenLogicool.Perception;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class ProductSupervisedMacroRuntimeTests
{
    [Fact]
    public void Nano_open_failure_disposes_the_window_session_created_for_the_failed_pin()
    {
        var window = new FakeWindowSession(Frame(freshnessMs: 0));
        using var runtime = new ProductSupervisedMacroRuntime(
            new ProfileStore(Profile()),
            new WindowLocator(window),
            new OcrReader(Ocr()),
            new FailingNanoFactory());

        Assert.Throws<IOException>(() => runtime.Pin(Program()));

        Assert.Equal(1, window.DisposeCount);
    }

    [Fact]
    public void Stale_observation_is_rejected_before_nano_dispatch()
    {
        var nano = new FakeNanoSession();
        using var runtime = Runtime(Frame(freshnessMs: 501), nano);
        var program = Program();
        runtime.Pin(program);
        var observed = runtime.ObserveBefore(program.Steps[0]);

        Assert.Equal(CaptureAvailability.Stale, observed.CaptureAvailability);
        Assert.Throws<SupervisedMacroDispatchNotStartedException>(() =>
            runtime.DispatchNano(program.Steps[0], observed));
        Assert.Equal(0, nano.DispatchCount);
    }

    [Fact]
    public void Confirmed_fresh_click_activates_target_and_dispatches_nano_once()
    {
        var nano = new FakeNanoSession();
        var window = new FakeWindowSession(Frame(freshnessMs: 0));
        using var runtime = Runtime(window, nano);
        var program = Program();
        runtime.Pin(program);
        var observed = runtime.ObserveBefore(program.Steps[0]);

        runtime.DispatchNano(program.Steps[0], observed);

        Assert.Equal(1, window.ActivateCount);
        Assert.Equal(1, nano.DispatchCount);
        Assert.Equal("click", nano.LastPrimitive);
    }

    [Fact]
    public void Window_move_after_observation_is_rejected_before_nano_dispatch()
    {
        var nano = new FakeNanoSession();
        var window = new FakeWindowSession(Frame(freshnessMs: 0));
        using var runtime = Runtime(window, nano);
        var program = Program();
        runtime.Pin(program);
        var observed = runtime.ObserveBefore(program.Steps[0]);
        window.MoveWindow();

        Assert.Throws<SupervisedMacroDispatchNotStartedException>(() =>
            runtime.DispatchNano(program.Steps[0], observed));
        Assert.Equal(0, nano.DispatchCount);
    }

    [Fact]
    public void Ocr_processing_time_does_not_reject_an_available_current_observation()
    {
        var nano = new FakeNanoSession();
        using var runtime = Runtime(
            Frame(freshnessMs: 0, wallClockUtc: DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5)), nano);
        var program = Program();
        runtime.Pin(program);
        var observed = runtime.ObserveBefore(program.Steps[0]);

        Assert.Equal(CaptureAvailability.Available, observed.CaptureAvailability);
        runtime.DispatchNano(program.Steps[0], observed);
        Assert.Equal(1, nano.DispatchCount);
    }

    [Fact]
    public void Observe_before_uses_one_current_frame_without_duplicate_confirmation_loop()
    {
        var nano = new FakeNanoSession();
        var reader = new SequenceOcrReader([Ocr(), EmptyOcr(), Ocr(), Ocr()]);
        using var runtime = new ProductSupervisedMacroRuntime(
            new ProfileStore(Profile()),
            new WindowLocator(new FakeWindowSession(Frame(freshnessMs: 0))),
            reader,
            new NanoFactory(nano));
        var program = Program(stableFrames: 2);
        runtime.Pin(program);

        var observed = runtime.ObserveBefore(program.Steps[0]);

        Assert.Equal(StateIdentityStatus.Known, observed.StateIdentity);
        Assert.Equal("state:lobby", observed.StateHypothesisId);
        Assert.Equal(1, reader.CallCount);
        Assert.Equal(0, nano.DispatchCount);
    }

    [Fact]
    public void Transient_missing_click_target_is_observed_again_before_pointer_dispatch()
    {
        var nano = new FakeNanoSession();
        var reader = new SequenceOcrReader([OcrWithoutTarget(), Ocr()]);
        using var runtime = new ProductSupervisedMacroRuntime(
            new ProfileStore(Profile()),
            new WindowLocator(new FakeWindowSession(Frame(freshnessMs: 0))),
            reader,
            new NanoFactory(nano));
        var program = Program();
        runtime.Pin(program);

        var observed = runtime.ObserveBefore(program.Steps[0]);

        Assert.Equal(StateIdentityStatus.Known, observed.StateIdentity);
        Assert.Single(observed.Affordances);
        Assert.Equal(2, reader.CallCount);
        Assert.Equal(0, nano.DispatchCount);
    }

    [Fact]
    public void After_observation_waits_past_the_source_frame_until_destination_is_stable()
    {
        var nano = new FakeNanoSession();
        var reader = new SequenceOcrReader([Ocr(), OcrWithSpecificState(), OcrWithSpecificState()]);
        using var runtime = new ProductSupervisedMacroRuntime(
            new ProfileStore(Profile()),
            new WindowLocator(new FakeWindowSession(Frame(freshnessMs: 0))),
            reader,
            new NanoFactory(nano));
        var program = Program(stableFrames: 2);
        runtime.Pin(program);

        var before = runtime.ObserveBefore(program.Steps[0]);
        var observed = runtime.ObserveAfter(program.Steps[0], before);

        Assert.Equal(GameTransitionJudgement.Moved, observed.Comparison.Judgement);
        Assert.Equal(StateIdentityStatus.Known, observed.FinalScene!.StateIdentity);
        Assert.Equal("state:squad", observed.FinalScene.StateHypothesisId);
        Assert.True(reader.CallCount >= 3);
        Assert.Equal(0, nano.DispatchCount);
    }

    [Fact]
    public void Final_capture_cancellation_keeps_continuous_stability_in_product_adapter_path()
    {
        var nano = new FakeNanoSession();
        var window = new FakeWindowSession(Frame(freshnessMs: 0), hangAfterCalls: 3);
        var reader = new SequenceOcrReader([Ocr(), OcrWithSpecificState(), OcrWithSpecificState()]);
        using var runtime = new ProductSupervisedMacroRuntime(
            new ProfileStore(Profile()),
            new WindowLocator(window),
            reader,
            new NanoFactory(nano));
        var program = Program(stableFrames: 2, timeoutMilliseconds: 250);
        runtime.Pin(program);
        var before = runtime.ObserveBefore(program.Steps[0]);

        var result = runtime.ObserveAfter(program.Steps[0], before);

        Assert.Equal(GameInteractionStabilityStatus.Stable, result.Stability.Status);
        Assert.Equal(GameTransitionJudgement.Moved, result.Comparison.Judgement);
    }

    private static ProductSupervisedMacroRuntime Runtime(CapturedFrame frame, FakeNanoSession nano) =>
        Runtime(new FakeWindowSession(frame), nano);

    private static ProductSupervisedMacroRuntime Runtime(FakeWindowSession window, FakeNanoSession nano) => new(
        new ProfileStore(Profile()),
        new WindowLocator(window),
        new OcrReader(Ocr()),
        new NanoFactory(nano));

    private static VisualMacroProgram Program(
        int stableFrames = 1,
        int timeoutMilliseconds = 1_000) => new(
        "0.3.0", "macro:1", "route:1", "route-version:1", "game", "env", "structure:1",
        VisualMacroExecutionMode.Supervised,
        [new VisualMacroStep(
            1, "edge:1", "state:lobby", ["signature:lobby"], "affordance:squad", "locator:squad:v1",
            "click", "state:squad", ["signature:squad"],
            new ExplorationWaitCondition("0.3.0", stableFrames, 0, timeoutMilliseconds), [], StructureVerificationState.Replayed)]);

    private static LearnedSceneProfileDocument Profile() => new(
        "0.3.0", "profile:1", "profile:v1", "game", "env", "game", "Game", 500, 0.04,
        [
            new LearnedStateSceneSignature(
                "state:lobby", "signature:lobby",
                [
                    new LearnedSceneAnchor("ロビー", [0.10, 0.10, 0.10, 0.05], "e1"),
                    new LearnedSceneAnchor("隊員募集", [0.70, 0.10, 0.15, 0.05], "e2"),
                ],
                [new LearnedAffordanceSignature(
                    "affordance:squad", "locator:squad:v1", "部隊", [0.40, 0.80, 0.10, 0.05], ["click"], ["e3"])],
                ["e1", "e2"]),
            new LearnedStateSceneSignature(
                "state:squad", "signature:squad",
                [
                    new LearnedSceneAnchor("部隊編成", [0.10, 0.10, 0.10, 0.05], "e4"),
                    new LearnedSceneAnchor("CAMPAIGN", [0.70, 0.10, 0.15, 0.05], "e5"),
                ], [], ["e4", "e5"], ["state:lobby"]),
        ], ["profile-evidence"]);

    private static OcrFrameSnapshot Ocr() => new(
        "ocr:v1", "ja",
        [
            new OcrWordBox("ロビー", 100, 100, 100, 50),
            new OcrWordBox("隊員募集", 700, 100, 150, 50),
            new OcrWordBox("部隊", 400, 800, 100, 50),
        ]);

    private static OcrFrameSnapshot EmptyOcr() => new("ocr:v1", "ja", []);

    private static OcrFrameSnapshot OcrWithoutTarget() => new(
        "ocr:v1", "ja",
        [
            new OcrWordBox("ロビー", 100, 100, 100, 50),
            new OcrWordBox("隊員募集", 700, 100, 150, 50),
        ]);

    private static OcrFrameSnapshot OcrWithSpecificState() => new(
        "ocr:v1", "ja",
        [
            new OcrWordBox("ロビー", 100, 100, 100, 50),
            new OcrWordBox("隊員募集", 700, 100, 150, 50),
            new OcrWordBox("部隊", 400, 800, 100, 50),
            new OcrWordBox("部隊編成", 100, 100, 100, 50),
            new OcrWordBox("CAMPAIGN", 700, 100, 150, 50),
        ]);

    private static CapturedFrame Frame(long freshnessMs, DateTimeOffset? wallClockUtc = null) => new(
        "0.3.0", "window:game", CaptureBackend.WindowsGraphicsCapture, 1, 1_000,
        wallClockUtc ?? DateTimeOffset.UtcNow, 1_000, 1_000, "B8G8R8A8_UNorm", 96, 96, 1, freshnessMs, 0);

    private sealed class ProfileStore(LearnedSceneProfileDocument document) : ILearnedSceneProfileStore
    {
        public void Upsert(LearnedSceneProfileDocument value) => throw new NotSupportedException();
        public LearnedSceneProfileDocument? Load(string gameId, string environmentScope) => document;
    }

    private sealed class WindowLocator(FakeWindowSession session) : ISupervisedWindowLocator
    {
        public ISupervisedWindowSession Locate(LearnedSceneProfileDocument profile) => session;
    }

    private sealed class FakeWindowSession(
        CapturedFrame frame,
        int? hangAfterCalls = null) : ISupervisedWindowSession
    {
        public string SourceId => frame.SourceId;
        public SupervisedWindowBounds CaptureBounds { get; private set; } = new(0, 0, 1_000, 1_000);
        public int ActivateCount { get; private set; }
        public int DisposeCount { get; private set; }
        private int captureCalls;
        private long transformRevision = frame.TransformRevision;
        public CapturedFrame Capture(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            captureCalls++;
            if (hangAfterCalls is not null && captureCalls > hangAfterCalls)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(5);
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            return frame with { TransformRevision = transformRevision };
        }
        public SupervisedWindowBounds GetCaptureBounds(long expectedTransformRevision)
        {
            if (expectedTransformRevision != transformRevision)
            {
                throw new SupervisedMacroDispatchNotStartedException("window moved");
            }
            return CaptureBounds;
        }
        public void MoveWindow()
        {
            CaptureBounds = CaptureBounds with { Left = CaptureBounds.Left + 100 };
            transformRevision++;
        }
        public bool Activate() { ActivateCount++; return true; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class OcrReader(OcrFrameSnapshot snapshot) : ISupervisedOcrReader
    {
        public OcrFrameSnapshot Recognize(CapturedFrame frame) => snapshot;
    }

    private sealed class SequenceOcrReader(IReadOnlyList<OcrFrameSnapshot> snapshots) : ISupervisedOcrReader
    {
        private int index;
        public int CallCount { get; private set; }
        public OcrFrameSnapshot Recognize(CapturedFrame frame)
        {
            CallCount++;
            return snapshots[Math.Min(index++, snapshots.Count - 1)];
        }
    }

    private sealed class NanoFactory(FakeNanoSession session) : ISupervisedNanoSessionFactory
    {
        public ISupervisedNanoSession Open() => session;
    }

    private sealed class FailingNanoFactory : ISupervisedNanoSessionFactory
    {
        public ISupervisedNanoSession Open() => throw new IOException("Nano unavailable");
    }

    private sealed class FakeNanoSession : ISupervisedNanoSession
    {
        public int DispatchCount { get; private set; }
        public string? LastPrimitive { get; private set; }
        public GameInteractionDispatchReceipt Dispatch(
            string primitive,
            AffordanceCandidate? target,
            SupervisedWindowBounds captureBounds,
            ObservedScene beforeScene)
        {
            DispatchCount++;
            LastPrimitive = primitive;
            return new GameInteractionDispatchReceipt(
                ContractSchemaVersions.Revision03,
                primitive,
                GameInteractionDispatchStatus.Dispatched,
                beforeScene.ObservationId,
                beforeScene.Frame.SourceId,
                "NanoSerialHid",
                1,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                target?.CandidateId,
                "fake-receipt",
                null);
        }
        public void Dispose() { }
    }
}
