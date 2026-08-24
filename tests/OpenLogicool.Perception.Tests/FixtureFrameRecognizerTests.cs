using System.Security.Cryptography;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Perception;
using Xunit;

namespace OpenLogicool.Perception.Tests;

public sealed class FixtureFrameRecognizerTests
{
    [Fact]
    public void Exact_calibrated_fixture_becomes_known_through_the_product_observation_path()
    {
        var frame = Frame("fixture:gamelab-main-menu", [1, 2, 3, 4]);
        var recognizer = Recognizer(Rule(frame, calibrated: true, Candidate("main-menu")));

        var observation = new LiveObservationSource(recognizer).Observe(frame);

        Assert.Equal(CaptureAvailability.Available, observation.CaptureAvailability);
        Assert.Equal(StateIdentityStatus.Known, observation.StateIdentity);
        Assert.Equal("main-menu", Assert.Single(observation.StateCandidates).StateId);
    }

    [Fact]
    public void Uncalibrated_fixture_is_unknown_not_known()
    {
        var frame = Frame("fixture:gamelab-main-menu", [1, 2, 3, 4]);
        var observation = new LiveObservationSource(Recognizer(Rule(frame, calibrated: false, Candidate("main-menu")))).Observe(frame);

        Assert.Equal(CaptureAvailability.Available, observation.CaptureAvailability);
        Assert.Equal(StateIdentityStatus.InsufficientEvidence, observation.StateIdentity);
        Assert.Empty(observation.StateCandidates);
    }

    [Fact]
    public void Multiple_fixture_candidates_are_ambiguous()
    {
        var frame = Frame("window:self-gamelab", [5, 6, 7, 8]);
        var observation = new LiveObservationSource(Recognizer(
            Rule(frame, calibrated: true, Candidate("main-menu"), Candidate("settings")))).Observe(frame);

        Assert.Equal(CaptureAvailability.Available, observation.CaptureAvailability);
        Assert.Equal(StateIdentityStatus.Ambiguous, observation.StateIdentity);
        Assert.Equal(2, observation.StateCandidates.Count);
    }

    [Fact]
    public void Unlisted_pixels_for_an_allowed_source_are_unknown()
    {
        var enrolled = Frame("window:self-gamelab", [5, 6, 7, 8]);
        var unlisted = Frame("window:self-gamelab", [8, 7, 6, 5]);

        var observation = new LiveObservationSource(Recognizer(Rule(enrolled, calibrated: true, Candidate("main-menu")))).Observe(unlisted);

        Assert.Equal(CaptureAvailability.Available, observation.CaptureAvailability);
        Assert.Equal(StateIdentityStatus.InsufficientEvidence, observation.StateIdentity);
        Assert.Empty(observation.StateCandidates);
    }

    [Fact]
    public void Source_outside_the_fixture_and_self_window_contract_is_rejected()
    {
        var enrolled = Frame("fixture:gamelab-main-menu", [1, 2, 3, 4]);
        var external = Frame("game:nikke", [1, 2, 3, 4]);

        Assert.Throws<InvalidOperationException>(() => Recognizer(Rule(enrolled, calibrated: true, Candidate("main-menu"))).Recognize(external));
    }

    [Fact]
    public void Frame_without_pixels_is_rejected_explicitly()
    {
        var frame = Frame("fixture:gamelab-main-menu", [1, 2, 3, 4]);
        var noPixels = frame with { Pixels = null };

        Assert.Throws<InvalidOperationException>(() => Recognizer(Rule(frame, calibrated: true, Candidate("main-menu"))).Recognize(noPixels));
    }

    [Fact]
    public void Duplicate_fixture_rules_are_rejected()
    {
        var frame = Frame("fixture:gamelab-main-menu", [1, 2, 3, 4]);
        var rule = Rule(frame, calibrated: true, Candidate("main-menu"));

        Assert.Throws<ArgumentException>(() => new FixtureFrameRecognizer("fixture-recognizer-1", [rule, rule]));
    }

    private static FixtureFrameRecognizer Recognizer(params FixtureFrameRule[] rules) =>
        new("fixture-recognizer-1", rules);

    private static FixtureFrameRule Rule(CapturedFrame frame, bool calibrated, params StateCandidate[] candidates) =>
        new(
            frame.SourceId,
            frame.Width,
            frame.Height,
            frame.PixelFormat,
            Convert.ToHexString(SHA256.HashData(frame.Pixels!.Bgra8.Span)),
            calibrated,
            candidates);

    private static StateCandidate Candidate(string stateId) =>
        new(
            "0.2.0",
            stateId,
            0.95,
            [new EvidenceRegion("0.2.0", "rect", [0.25, 0.25, 0.5, 0.5], "fixture-recognizer-1")]);

    private static CapturedFrame Frame(string sourceId, byte[] pixels) =>
        new(
            "0.2.0",
            sourceId,
            CaptureBackend.WindowsGraphicsCapture,
            Sequence: 1,
            MonotonicMs: 1,
            WallClockUtc: DateTimeOffset.UnixEpoch,
            Width: 1,
            Height: 1,
            PixelFormat: "B8G8R8A8_UNorm",
            DpiX: 96,
            DpiY: 96,
            TransformRevision: 1,
            FreshnessMs: 1,
            LastChangeMs: 1,
            Pixels: new FramePixels(pixels, pixels.Length));
}
