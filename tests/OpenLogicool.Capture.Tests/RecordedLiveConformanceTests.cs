using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;
using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Perception;
using Xunit;

namespace OpenLogicool.Capture.Tests;

public sealed class RecordedLiveConformanceTests
{
    private const string Navy160x90BgraSha256 = "0AC9C45530F214837F723798B3D61FE38357DC03CA6F723E5ED741B815890B5B";

    [Fact]
    [Trait("Category", "WindowsNative")]
    public void Recorded_fixture_and_live_self_window_use_the_same_product_observation_path()
    {
        var recorded = RecordedFixtureFrame();
        var catalog = SelfWindowCatalogRule();
        var recognizer = new FixtureFrameRecognizer(
            "fixture-recognizer-conformance-1",
            [Rule(recorded, "fixture.main-menu"), catalog]);
        var observations = new LiveObservationSource(recognizer);
        var live = CaptureRepaintedSelfWindow(Color.Navy);
        var mismatched = CaptureRepaintedSelfWindow(Color.Teal);

        var recordedObservation = observations.Observe(recorded);
        var liveObservation = observations.Observe(live);
        var mismatchedObservation = observations.Observe(mismatched);

        AssertKnownConformance(recordedObservation, recorded, "fixture.main-menu");
        AssertKnownConformance(liveObservation, live, "self-window");
        Assert.Equal(CaptureAvailability.Available, mismatchedObservation.CaptureAvailability);
        Assert.Equal(StateIdentityStatus.InsufficientEvidence, mismatchedObservation.StateIdentity);
        Assert.Empty(mismatchedObservation.StateCandidates);
    }

    private static void AssertKnownConformance(
        ObservationResult observation,
        CapturedFrame frame,
        string expectedStateId)
    {
        Assert.Equal(CaptureAvailability.Available, observation.CaptureAvailability);
        Assert.Equal(StateIdentityStatus.Known, observation.StateIdentity);
        Assert.Equal(frame.SourceId, observation.Frame.SourceId);
        Assert.Equal(frame.Backend, observation.Frame.Backend);
        Assert.Equal(frame.Sequence, observation.Frame.Sequence);
        Assert.Equal(frame.FreshnessMs, observation.FreshnessMs);
        Assert.Equal("fixture-recognizer-conformance-1", observation.RecognizerVersion);

        var candidate = Assert.Single(observation.StateCandidates);
        Assert.Equal(expectedStateId, candidate.StateId);
        Assert.InRange(candidate.Confidence, 0, 1);
        Assert.NotEmpty(candidate.EvidenceRegions);
    }

    private static FixtureFrameRule Rule(CapturedFrame frame, string stateId) =>
        new(
            frame.SourceId,
            frame.Width,
            frame.Height,
            frame.PixelFormat,
            Convert.ToHexString(SHA256.HashData(frame.Pixels!.Bgra8.Span)),
            IsCalibrated: true,
            [new StateCandidate(
                "0.2.0",
                stateId,
                Confidence: 0.95,
                [new EvidenceRegion("0.2.0", "rect", [0.25, 0.25, 0.5, 0.5], "fixture-recognizer-conformance-1")])]);

    private static FixtureFrameRule SelfWindowCatalogRule() =>
        new(
            "window:self-conformance",
            Width: 160,
            Height: 90,
            PixelFormat: "B8G8R8A8_UNorm",
            PixelSha256: Navy160x90BgraSha256,
            IsCalibrated: true,
            [new StateCandidate(
                "0.2.0",
                "self-window",
                Confidence: 0.95,
                [new EvidenceRegion("0.2.0", "rect", [0.25, 0.25, 0.5, 0.5], "fixture-recognizer-conformance-1")])]);

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

    private static CapturedFrame CaptureRepaintedSelfWindow(Color color)
    {
        CapturedFrame? captured = null;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                using var window = new Form
                {
                    Text = "OpenLogicool recorded/live conformance",
                    ClientSize = new Size(160, 90),
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.CenterScreen,
                    BackColor = color,
                };
                window.Show();
                Application.DoEvents();

                using var source = WgcFrameSource.CreateForWindow(window.Handle, "window:self-conformance");
                for (var attempt = 0; attempt < 20 && captured is null; attempt++)
                {
                    window.Invalidate();
                    window.Update();
                    Application.DoEvents();

                    if (source.Pull() is FrameAvailable available)
                    {
                        captured = available.Frame;
                    }

                    Thread.Sleep(50);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        worker.Join();

        Assert.Null(failure);
        return Assert.IsType<CapturedFrame>(captured);
    }

    private static string FindFixture(params string[] relativePath)
    {
        var roots = new[] { AppContext.BaseDirectory, Environment.GetEnvironmentVariable("OPENLOGICOOL_SOURCE_ROOT") }
            .OfType<string>()
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.Ordinal);
        foreach (var root in roots)
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

        throw new FileNotFoundException("recorded frame fixture が見つかりません。", Path.Combine(relativePath));
    }
}
