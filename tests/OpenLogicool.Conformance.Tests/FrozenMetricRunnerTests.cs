using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
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

    [Fact]
    public void Tracked_png_acceptance_fixture_produces_zero_fixed_metrics()
    {
        var frame = RecordedFixtureFrame();
        var item = new FrozenMetricCase(
            new CorpusArtifact("fixture:gamelab-main-menu", "fixtures/frames/gamelab-main-menu-20260815.png", "fixture:phase5"),
            frame,
            ObservationStatus.Known,
            ExpectedDispatchAllowed: true);
        var recognizer = new FixtureFrameRecognizer(
            "fixture-v1",
            [new FixtureFrameRule(
                "fixture:gamelab-main-menu",
                706,
                473,
                "B8G8R8A8_UNorm",
                "89A84343CCB27E7338F3AD7EFD52B25B4D427B1AF25F3018D204B7F6BF913816",
                IsCalibrated: true,
                [Candidate("gamelab.main-menu")])]);

        var report = FrozenMetricRunner.Evaluate(new AcceptanceCorpus([item.Artifact]), [item], recognizer);

        Assert.True(report.Passed);
        Assert.Equal(0, report.KnownMisclassifications);
        Assert.Equal(0, report.UnknownPromotions);
        Assert.Equal(0, report.SuccessFalsePositives);
    }

    private static FrozenMetricCase Case(CapturedFrame frame, ObservationStatus expected, bool dispatch) => new(new CorpusArtifact(frame.SourceId, $"acceptance/{frame.SourceId}.png", "fixture:phase5"), frame, expected, dispatch);
    private static FixtureFrameRule Rule(CapturedFrame frame, params StateCandidate[] candidates) => new(frame.SourceId, frame.Width, frame.Height, frame.PixelFormat, Convert.ToHexString(SHA256.HashData(frame.Pixels!.Bgra8.Span)), true, candidates);
    private static StateCandidate Candidate(string state) => new("0.2.0", state, .9, [new EvidenceRegion("0.2.0", "rect", [0d, 0d, 1d, 1d], "fixture-v1")]);
    private static CapturedFrame Frame(string source, byte[] pixels) => new("0.2.0", source, CaptureBackend.WindowsGraphicsCapture, 1, 1, DateTimeOffset.UnixEpoch, 1, 1, "B8G8R8A8_UNorm", 96, 96, 1, 0, 1, Pixels: new FramePixels(pixels, pixels.Length));

    private static CapturedFrame RecordedFixtureFrame()
    {
        using var bitmap = new Bitmap(FindFixture("fixtures", "frames", "gamelab-main-menu-20260815.png"));
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var locked = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var byteCount = checked(Math.Abs(locked.Stride) * bitmap.Height);
            var pixels = new byte[byteCount];
            Marshal.Copy(locked.Scan0, pixels, 0, pixels.Length);
            return new CapturedFrame(
                "0.2.0",
                "fixture:gamelab-main-menu",
                CaptureBackend.WindowsGraphicsCapture,
                Sequence: 1,
                MonotonicMs: 1,
                WallClockUtc: DateTimeOffset.UnixEpoch,
                bitmap.Width,
                bitmap.Height,
                "B8G8R8A8_UNorm",
                DpiX: 96,
                DpiY: 96,
                TransformRevision: 1,
                FreshnessMs: 0,
                LastChangeMs: 0,
                Pixels: new FramePixels(pixels, Math.Abs(locked.Stride)));
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }
    }

    private static string FindFixture(params string[] relativePath)
    {
        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }.Distinct(StringComparer.Ordinal))
        {
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine([directory.FullName, .. relativePath]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("tracked PNG fixture が見つかりません。", Path.Combine(relativePath));
    }
}
