using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Host;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class CaptureContinuityDispatchTests
{
    [Theory]
    [InlineData(CaptureFaultKind.Stale)]
    [InlineData(CaptureFaultKind.BackendChanged)]
    [InlineData(CaptureFaultKind.Resize)]
    public void Discontinuity_does_not_arm_or_invoke_external_input(CaptureFaultKind kind)
    {
        var (dispatch, gate, continuity) = NewDispatch();
        BreakContinuity(continuity, kind);
        var called = 0;

        var allowed = dispatch.TryStepOnce(Event(3), () => called++);

        Assert.False(allowed);
        Assert.Equal(0, called);
        Assert.Equal(AttemptState.Prepared, gate.Get("attempt-1").State);
    }

    [Fact]
    public void Static_unavailable_capture_keeps_the_dispatch_path_open()
    {
        var (dispatch, gate, continuity) = NewDispatch();
        continuity.Observe(CaptureRead.Unavailable("WGC は変化待ちです。"), staleAfterMs: 100);
        var called = 0;

        var allowed = dispatch.TryStepOnce(Event(3), () => called++);

        Assert.True(allowed);
        Assert.Equal(1, called);
        Assert.Equal(AttemptState.DispatchArmed, gate.Get("attempt-1").State);
    }

    [Fact]
    public void Host_loop_observes_and_recalibrates_before_dispatch()
    {
        var (dispatch, gate, continuity) = NewDispatch();
        var loop = new CaptureContinuityDispatchLoop(dispatch, continuity);
        var called = 0;

        var allowed = loop.TryStepOnce(
            CaptureRead.Available(Frame()),
            staleAfterMs: 100,
            recalibrationFrame: Frame(),
            Event(3),
            () => called++);

        Assert.True(allowed);
        Assert.Equal(1, called);
        Assert.Equal(AttemptState.DispatchArmed, gate.Get("attempt-1").State);
    }

    private static void BreakContinuity(CaptureContinuityGate continuity, CaptureFaultKind kind)
    {
        var frame = Frame();
        switch (kind)
        {
            case CaptureFaultKind.Stale:
                continuity.Observe(CaptureRead.Available(frame with { FreshnessMs = 101 }), staleAfterMs: 100);
                break;
            case CaptureFaultKind.BackendChanged:
                continuity.Observe(CaptureRead.Available(frame with { Backend = CaptureBackend.DesktopDuplication }), staleAfterMs: 100);
                break;
            case CaptureFaultKind.Resize:
                continuity.Observe(CaptureRead.Available(frame with { TransformRevision = 2 }), staleAfterMs: 100);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static (CaptureContinuityDispatch Dispatch, AttemptDispatchGate Gate, CaptureContinuityGate Continuity) NewDispatch()
    {
        var store = new MemoryJournalStore();
        var journal = new RunJournal(store, new NullSink());
        var gate = new AttemptDispatchGate(journal);
        var controls = new RunControls(journal, gate, "run-1", PlaybookRun.Start("playbook-1", Graph()));
        gate.CommitProposed(Event(1, RunEventPayloadTypes.Proposal, "attempt-1"));
        gate.CommitAuthorized(Event(2, RunEventPayloadTypes.Approval, "attempt-1", actor: RunEventActorType.User));
        gate.MarkPrepared("attempt-1");
        controls.Pause();

        var continuity = new CaptureContinuityGate();
        var frame = Frame();
        continuity.Observe(CaptureRead.Available(frame), staleAfterMs: 100);
        continuity.Recalibrate(frame);
        return (new CaptureContinuityDispatch(controls, continuity), gate, continuity);
    }

    private static PlaybookGraph Graph() => PlaybookMaterializer.ToGraph(new PlaybookVersion(
        ContractSchemaVersions.Revision01,
        "version-1",
        null,
        [new PlaybookNode(ContractSchemaVersions.Revision01, "entry", true, "state:entry", [], null, [])],
        [],
        "test"));

    private static CapturedFrame Frame() => new(
        "0.2.0", "capture-test", CaptureBackend.WindowsGraphicsCapture,
        1, 1, DateTimeOffset.UnixEpoch, 10, 10, "B8G8R8A8_UNorm", 96, 96, 1, 0, 1);

    private static RunEvent Event(long sequence, string payloadType = RunEventPayloadTypes.Dispatch, string? attemptId = "attempt-1", RunEventActorType actor = RunEventActorType.Automation) => new(
        "0.1.0", $"event-{sequence}", "run-1", sequence, "playbook-1", "version-1", null,
        "command-1", attemptId, "cause-1", $"correlation-{sequence}", 1, actor,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, payloadType, "{}");

    private sealed class MemoryJournalStore : IRunJournalStore
    {
        private readonly List<RunEvent> events = [];

        public void Append(RunEvent runEvent) => events.Add(runEvent);

        public IReadOnlyList<RunEvent> ReadRun(string runId) => events.Where(item => item.RunId == runId).ToArray();

        public IReadOnlyList<string> ListRunIds() => events.Select(item => item.RunId).Distinct().ToArray();

        public IReadOnlyList<ExpiredRunPreview> PreviewExpiredRuns(DateTimeOffset asOfUtc, int retentionDays) => [];

        public void DeleteRun(string runId) => events.RemoveAll(item => item.RunId == runId);
    }

    private sealed class NullSink : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry)
        {
        }
    }
}
