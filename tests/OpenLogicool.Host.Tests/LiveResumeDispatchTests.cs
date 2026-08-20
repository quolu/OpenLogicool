using System.Drawing;
using System.Security.Cryptography;
using System.Windows.Forms;
using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Host;
using OpenLogicool.Perception;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class LiveResumeDispatchTests
{
    [Theory]
    [InlineData(ObservationStatus.Ambiguous)]
    [InlineData(ObservationStatus.Unknown)]
    [InlineData(ObservationStatus.Unavailable)]
    public void Non_unique_or_unavailable_observation_never_reaches_external_input(ObservationStatus status)
    {
        var frame = Frame();
        var (dispatch, gate) = NewDispatch(frame);
        var called = 0;

        var allowed = dispatch.TryResumeStepOnce(
            Binding(frame), Context(Observation(frame, status)), [], "state-menu", 100, 100,
            Event(3), () => called++);

        Assert.False(allowed);
        Assert.Equal(0, called);
        Assert.Equal(AttemptState.Prepared, gate.Get("attempt-1").State);
    }

    [Fact]
    [Trait("Category", "WindowsNative")]
    public void Live_self_window_unique_match_dispatches_once_and_target_mismatch_does_not()
    {
        var live = CaptureRepaintedSelfWindow() with { LastChangeMs = 100 };
        var recognizer = new FixtureFrameRecognizer("self-window-1", [Rule(live)]);
        var observation = new LiveObservationSource(recognizer).Observe(live);
        var (dispatch, gate) = NewDispatch(live);
        var called = 0;

        var allowed = dispatch.TryResumeStepOnce(
            Binding(live), Context(observation), [], "self-window", 100, 100,
            Event(3), () => called++);
        var rejected = dispatch.TryResumeStepOnce(
            Binding(live), Context(observation, inputTarget: "window:other"), [], "self-window", 100, 100,
            Event(4), () => called++);

        Assert.True(allowed);
        Assert.False(rejected);
        Assert.Equal(1, called);
        Assert.Equal(AttemptState.DispatchArmed, gate.Get("attempt-1").State);
    }

    private static (CaptureContinuityDispatch Dispatch, AttemptDispatchGate Gate) NewDispatch(CapturedFrame frame)
    {
        var store = new MemoryJournalStore();
        var journal = new RunJournal(store, new NullSink());
        var gate = new AttemptDispatchGate(journal);
        var controls = new RunControls(journal, gate, "run-1", PlaybookRun.Start("playbook-1", Graph()));
        gate.CommitProposed(Event(1, RunEventPayloadTypes.Proposal, "attempt-1"));
        gate.CommitAuthorized(Event(2, RunEventPayloadTypes.Approval, "attempt-1", RunEventActorType.User));
        gate.MarkPrepared("attempt-1");
        controls.Pause();
        var continuity = new CaptureContinuityGate();
        continuity.Observe(CaptureRead.Available(frame), 100);
        continuity.Recalibrate(frame);
        return (new CaptureContinuityDispatch(controls, continuity), gate);
    }

    private static LiveResumeBinding Binding(CapturedFrame frame) => new("self-window.exe", "window:self", frame.SourceId, "version-1", "version-1");

    private static LiveResumeContext Context(ObservationResult observation, string? inputTarget = "window:self") =>
        new("self-window.exe", "window:self", observation.Frame.SourceId, inputTarget, observation);

    private static ObservationResult Observation(CapturedFrame frame, ObservationStatus status) => new(
        "0.2.0", "observation-test", new CapturedFrameReference("0.2.0", frame.SourceId, frame.Backend, 1, 1_000, DateTimeOffset.UnixEpoch, 1, 0, 100),
        status,
        status == ObservationStatus.Ambiguous ? [Candidate("state-menu"), Candidate("other")] : [],
        "test", 0, status == ObservationStatus.Unavailable ? "capture-unavailable" : null);

    private static FixtureFrameRule Rule(CapturedFrame frame) => new(frame.SourceId, frame.Width, frame.Height, frame.PixelFormat,
        Convert.ToHexString(SHA256.HashData(frame.Pixels!.Bgra8.Span)), true, [Candidate("self-window")]);

    private static StateCandidate Candidate(string state) => new("0.2.0", state, 0.95, [new EvidenceRegion("0.2.0", "rect", [0.25, 0.25, 0.5, 0.5], "self-window-1")]);

    private static CapturedFrame Frame() => new("0.2.0", "source:test", CaptureBackend.WindowsGraphicsCapture, 1, 1_000, DateTimeOffset.UnixEpoch, 10, 10, "B8G8R8A8_UNorm", 96, 96, 1, 0, 100, Pixels: new FramePixels(new byte[] { 1, 2, 3, 4 }, 4));

    private static CapturedFrame CaptureRepaintedSelfWindow()
    {
        CapturedFrame? captured = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new Form { ClientSize = new Size(160, 90), BackColor = Color.Navy, Text = "OpenLogicool live resume" };
                window.Show();
                Application.DoEvents();
                using var source = WgcFrameSource.CreateForWindow(window.Handle, "window:self-live-resume");
                for (var attempt = 0; attempt < 20 && captured is null; attempt++)
                {
                    window.BackColor = attempt % 2 == 0 ? Color.Navy : Color.Teal;
                    window.Invalidate(); window.Update(); Application.DoEvents();
                    if (source.Pull() is FrameAvailable available) captured = available.Frame;
                    Thread.Sleep(50);
                }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        Assert.Null(failure);
        return Assert.IsType<CapturedFrame>(captured);
    }

    private static PlaybookGraph Graph() => PlaybookMaterializer.ToGraph(new PlaybookVersion(ContractSchemaVersions.Revision01, "version-1", null, [new PlaybookNode(ContractSchemaVersions.Revision01, "entry", true, "state:entry", [], null, [])], [], "test"));
    private static RunEvent Event(long sequence, string payload = RunEventPayloadTypes.Dispatch, string? attempt = "attempt-1", RunEventActorType actor = RunEventActorType.Automation) => new("0.1.0", $"event-{sequence}", "run-1", sequence, "playbook-1", "version-1", null, "command-1", attempt, "cause", $"correlation-{sequence}", 1, actor, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, payload, "{}");
    private sealed class MemoryJournalStore : IRunJournalStore { private readonly List<RunEvent> events=[]; public void Append(RunEvent e)=>events.Add(e); public IReadOnlyList<RunEvent> ReadRun(string id)=>events.Where(e=>e.RunId==id).ToArray(); public IReadOnlyList<string> ListRunIds()=>events.Select(e=>e.RunId).Distinct().ToArray(); public IReadOnlyList<ExpiredRunPreview> PreviewExpiredRuns(DateTimeOffset _, int __)=>[]; public void DeleteRun(string id)=>events.RemoveAll(e=>e.RunId==id); }
    private sealed class NullSink : IEngineeringLogSink { public void Record(EngineeringLogEntry _) { } }
}
