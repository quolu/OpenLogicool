using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Perception;
using Xunit;

namespace OpenLogicool.Perception.Tests;

public sealed class LiveObservationSourceTests
{
    [Theory]
    [InlineData(true, 1, null, ObservationStatus.Known, true)]
    [InlineData(true, 2, null, ObservationStatus.Ambiguous, false)]
    [InlineData(false, 1, null, ObservationStatus.Unknown, false)]
    [InlineData(true, 0, null, ObservationStatus.Unknown, false)]
    [InlineData(true, 1, "capture-stale", ObservationStatus.Unavailable, false)]
    public void Recorded_and_live_frames_share_four_state_normalization(
        bool calibrated,
        int count,
        string? unavailable,
        ObservationStatus expected,
        bool automatic)
    {
        var source = new LiveObservationSource(new Recognizer("recognizer-5", calibrated, count, unavailable));

        var observation = source.Observe(Frame());

        Assert.Equal(expected, observation.Status);
        Assert.Equal(automatic, LiveObservationSource.AllowsAutomaticExecution(observation));
    }

    [Fact]
    public void Observation_carries_frame_age_recognizer_version_and_candidate_evidence()
    {
        var source = new LiveObservationSource(new Recognizer("recognizer-5", calibrated: true, count: 1, unavailable: null));

        var observation = source.Observe(Frame(sequence: 7, monotonicMs: 120, freshnessMs: 18));

        Assert.Equal("recognizer-5", observation.RecognizerVersion);
        Assert.Equal(18, observation.FreshnessMs);
        Assert.Equal(7, observation.Frame.Sequence);
        Assert.Single(observation.StateCandidates);
        Assert.Single(observation.StateCandidates[0].EvidenceRegions);
    }

    [Fact]
    public void Invalid_recognizer_output_is_explicit_not_rounded()
    {
        var source = new LiveObservationSource(new InvalidRecognizer());

        Assert.Throws<InvalidOperationException>(() => source.Observe(Frame()));
    }

    [Fact]
    public void Stability_window_requires_a_time_sequence_of_the_same_known_state()
    {
        var window = new ObservationStabilityWindow(requiredStableMs: 100);

        Assert.False(window.Observe(Known(Frame(sequence: 1, monotonicMs: 100))));
        Assert.False(window.Observe(Known(Frame(sequence: 2, monotonicMs: 199))));
        Assert.True(window.Observe(Known(Frame(sequence: 3, monotonicMs: 200))));
    }

    [Fact]
    public void Stability_window_resets_for_ambiguous_state_and_transform_change()
    {
        var window = new ObservationStabilityWindow(requiredStableMs: 100);

        Assert.False(window.Observe(Known(Frame(sequence: 1, monotonicMs: 0))));
        Assert.False(window.Observe(Ambiguous(Frame(sequence: 2, monotonicMs: 100))));
        Assert.False(window.Observe(Known(Frame(sequence: 3, monotonicMs: 200))));
        Assert.False(window.Observe(Known(Frame(sequence: 4, monotonicMs: 300, transformRevision: 2))));
        Assert.True(window.Observe(Known(Frame(sequence: 5, monotonicMs: 400, transformRevision: 2))));
    }

    private static ObservationResult Known(CapturedFrame frame) =>
        new LiveObservationSource(new Recognizer("recognizer-5", calibrated: true, count: 1, unavailable: null)).Observe(frame);

    private static ObservationResult Ambiguous(CapturedFrame frame) =>
        new LiveObservationSource(new Recognizer("recognizer-5", calibrated: true, count: 2, unavailable: null)).Observe(frame);

    private static CapturedFrame Frame(
        long sequence = 1,
        double monotonicMs = 1,
        long freshnessMs = 1,
        long transformRevision = 1) =>
        new(
            "0.2.0",
            "source",
            CaptureBackend.WindowsGraphicsCapture,
            sequence,
            monotonicMs,
            DateTimeOffset.UnixEpoch.AddMilliseconds(monotonicMs),
            1920,
            1080,
            "BGRA8",
            96,
            96,
            transformRevision,
            freshnessMs,
            1,
            0,
            0);

    private sealed class Recognizer(string version, bool calibrated, int count, string? unavailable) : IFrameRecognizer
    {
        public RecognitionResult Recognize(CapturedFrame frame) => new(
            version,
            calibrated,
            Enumerable.Range(0, count)
                .Select(index => new StateCandidate(
                    "0.2.0",
                    $"state-{index}",
                    0.9,
                    [new EvidenceRegion("0.2.0", "rect", [0.25, 0.25, 0.5, 0.5], "recognizer")]))
                .ToArray(),
            unavailable);
    }

    private sealed class InvalidRecognizer : IFrameRecognizer
    {
        public RecognitionResult Recognize(CapturedFrame frame) => new(
            "recognizer-5",
            true,
            [new StateCandidate("0.2.0", "state-1", 0.9, [])]);
    }
}
