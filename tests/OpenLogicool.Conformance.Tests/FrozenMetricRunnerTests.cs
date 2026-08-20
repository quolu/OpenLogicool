using System.Security.Cryptography;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Perception;
using Xunit;

namespace OpenLogicool.Conformance.Tests;

public sealed class FrozenMetricRunnerTests
{
    [Fact]
    public void Acceptance_fixtures_run_through_recognizer_and_observation()
    {
        var known = Frame("fixture:known", [1, 2, 3, 4]);
        var unknown = Frame("fixture:unknown", [5, 6, 7, 8]);
        var cases = new[] { Case(known, ObservationStatus.Known, true), Case(unknown, ObservationStatus.Unknown, false) };
        var recognizer = new FixtureFrameRecognizer("fixture-v1", [Rule(known, Candidate("menu")), Rule(unknown)]);

        var report = FrozenMetricRunner.Evaluate(new AcceptanceCorpus(cases.Select(item => item.Artifact).ToArray()), cases, recognizer);

        Assert.True(report.Passed);
    }

    [Fact]
    public void Actual_fixture_misclassification_fails_all_relevant_fixed_metrics()
    {
        var frame = Frame("fixture:unknown", [9, 9, 9, 9]);
        var item = Case(frame, ObservationStatus.Unknown, false);
        var report = FrozenMetricRunner.Evaluate(new AcceptanceCorpus([item.Artifact]), [item], new FixtureFrameRecognizer("fixture-v1", [Rule(frame, Candidate("wrong"))]));

        Assert.False(report.Passed);
        Assert.Equal(1, report.KnownMisclassifications);
        Assert.Equal(1, report.UnknownPromotions);
        Assert.Equal(1, report.SuccessFalsePositives);
    }

    private static FrozenMetricCase Case(CapturedFrame frame, ObservationStatus expected, bool dispatch) => new(new CorpusArtifact(frame.SourceId, $"acceptance/{frame.SourceId}.png", "fixture:phase5"), frame, expected, dispatch);
    private static FixtureFrameRule Rule(CapturedFrame frame, params StateCandidate[] candidates) => new(frame.SourceId, frame.Width, frame.Height, frame.PixelFormat, Convert.ToHexString(SHA256.HashData(frame.Pixels!.Bgra8.Span)), true, candidates);
    private static StateCandidate Candidate(string state) => new("0.2.0", state, .9, [new EvidenceRegion("0.2.0", "rect", [0d, 0d, 1d, 1d], "fixture-v1")]);
    private static CapturedFrame Frame(string source, byte[] pixels) => new("0.2.0", source, CaptureBackend.WindowsGraphicsCapture, 1, 1, DateTimeOffset.UnixEpoch, 1, 1, "B8G8R8A8_UNorm", 96, 96, 1, 0, 1, Pixels: new FramePixels(pixels, pixels.Length));
}
