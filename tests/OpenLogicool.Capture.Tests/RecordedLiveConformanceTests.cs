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
    [Fact]
    [Trait("Category", "WindowsNative")]
    public void Recorded_fixture_and_live_self_window_use_the_same_product_observation_path()
    {
        var recorded = RecordedFixtureFrame();
        var live = CaptureRepaintedSelfWindow();
        var recognizer = new FixtureFrameRecognizer(
            "fixture-recognizer-conformance-1",
            [Rule(recorded, "fixture.main-menu"), Rule(live, "self-window")]);
        var observations = new LiveObservationSource(recognizer);

        var recordedObservation = observations.Observe(recorded);
        var liveObservation = observations.Observe(live);

        AssertKnownConformance(recordedObservation, recorded, "fixture.main-menu");
        AssertKnownConformance(liveObservation, live, "self-window");
    }

    private static void AssertKnownConformance(
        ObservationResult observation,
        CapturedFrame frame,
        string expectedStateId)
    {
        Assert.Equal(ObservationStatus.Known, observation.Status);
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

    private static CapturedFrame CaptureRepaintedSelfWindow()
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
                    StartPosition = FormStartPosition.CenterScreen,
                    BackColor = Color.Navy,
                };
                window.Show();
                Application.DoEvents();

                using var source = WgcFrameSource.CreateForWindow(window.Handle, "window:self-conformance");
                for (var attempt = 0; attempt < 20 && captured is null; attempt++)
                {
                    window.BackColor = attempt % 2 == 0 ? Color.Navy : Color.Teal;
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
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("recorded frame fixture が見つかりません。", Path.Combine(relativePath));
    }
}
