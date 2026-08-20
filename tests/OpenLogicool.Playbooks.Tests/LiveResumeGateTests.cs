using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class LiveResumeGateTests
{
    [Fact]
    public void Unique_live_observation_for_the_same_targets_is_the_only_dispatchable_case()
    {
        var decision = LiveResumeGate.Judge(Binding(), Context(Observation()), [], "state-menu", 100, 500);

        Assert.True(decision.DispatchAllowed);
        Assert.Equal(StateMatchResult.UniqueMatch, decision.StateMatch);
        Assert.Empty(decision.LiveBlockReasons);
    }

    [Theory]
    [InlineData(ObservationStatus.Ambiguous, StateMatchResult.AmbiguousMatch)]
    [InlineData(ObservationStatus.Unknown, StateMatchResult.InsufficientEvidence)]
    [InlineData(ObservationStatus.Unavailable, StateMatchResult.InsufficientEvidence)]
    public void Non_unique_live_observation_never_allows_dispatch(ObservationStatus status, StateMatchResult expectedMatch)
    {
        var decision = LiveResumeGate.Judge(Binding(), Context(Observation(status: status)), [], "state-menu", 100, 500);

        Assert.False(decision.DispatchAllowed);
        Assert.Equal(expectedMatch, decision.StateMatch);
        Assert.Contains(ResumeBlockReason.StateNotUniqueMatch, decision.ResumeDecision.BlockReasons);
    }

    [Fact]
    public void Old_or_unstable_known_observation_never_allows_dispatch()
    {
        var stale = LiveResumeGate.Judge(Binding(), Context(Observation(freshnessMs: 101)), [], "state-menu", 100, 500);
        var unstable = LiveResumeGate.Judge(Binding(), Context(Observation(lastChangeMs: 499)), [], "state-menu", 100, 500);

        Assert.Equal(StateMatchResult.StaleObservation, stale.StateMatch);
        Assert.False(stale.DispatchAllowed);
        Assert.Equal(StateMatchResult.InsufficientEvidence, unstable.StateMatch);
        Assert.False(unstable.DispatchAllowed);
    }

    [Fact]
    public void Target_window_capture_source_and_input_target_mismatches_stop_before_dispatch()
    {
        var context = Context(
            Observation(sourceId: "source-other"),
            targetWindowId: "window-other",
            captureSourceId: "source-recorded",
            inputTargetWindowId: "window-input-other");

        var decision = LiveResumeGate.Judge(Binding(), context, [], "state-menu", 100, 500);

        Assert.False(decision.DispatchAllowed);
        Assert.Equal(
            [
                LiveResumeBlockReason.TargetWindowMismatch,
                LiveResumeBlockReason.CaptureSourceMismatch,
                LiveResumeBlockReason.InputTargetMismatch,
            ],
            decision.LiveBlockReasons);
    }

    [Fact]
    public void Reobservation_requirement_is_preserved_when_live_observation_was_not_committed_after_intervention()
    {
        var events = new[]
        {
            Event(1, RunEventPayloadTypes.Observation, "observation-before"),
            Event(2, RunEventPayloadTypes.ManualIntervention, null),
        };

        var decision = LiveResumeGate.Judge(Binding(), Context(Observation()), events, "state-menu", 100, 500);

        Assert.False(decision.DispatchAllowed);
        Assert.Contains(ResumeBlockReason.ReobservationRequired, decision.ResumeDecision.BlockReasons);
    }

    private static LiveResumeBinding Binding() => new(
        "c:\\games\\nikke.exe",
        "window-recorded",
        "source-recorded",
        "version-1",
        "version-1");

    private static LiveResumeContext Context(
        ObservationResult observation,
        string? targetWindowId = "window-recorded",
        string? captureSourceId = "source-recorded",
        string? inputTargetWindowId = "window-recorded") =>
        new("c:\\games\\nikke.exe", targetWindowId, captureSourceId, inputTargetWindowId, observation);

    private static ObservationResult Observation(
        ObservationStatus status = ObservationStatus.Known,
        string sourceId = "source-recorded",
        long freshnessMs = 10,
        long lastChangeMs = 500) =>
        new(
            "0.2.0",
            "observation-live",
            new CapturedFrameReference(
                "0.2.0", sourceId, CaptureBackend.WindowsGraphicsCapture, 1, 1_000,
                DateTimeOffset.UnixEpoch, 1, freshnessMs, lastChangeMs),
            status,
            status is ObservationStatus.Known ? [new StateCandidate("0.2.0", "state-menu", 0.95, [])]
                : status == ObservationStatus.Ambiguous
                    ? [new StateCandidate("0.2.0", "state-menu", 0.51, []), new StateCandidate("0.2.0", "state-other", 0.49, [])]
                    : [],
            "recognizer-live-1",
            freshnessMs,
            status == ObservationStatus.Unavailable ? "capture-unavailable" : null);

    private static RunEvent Event(long sequence, string payloadType, string? observationId) =>
        new(
            "0.1.0", $"event-{sequence}", "run-1", sequence, "playbook-1", "version-1", null, null, null,
            "cause-1", $"correlation-{sequence}", 1, RunEventActorType.Automation,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, observationId, payloadType, "{}");
}
