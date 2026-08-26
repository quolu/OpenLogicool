using System.Text.Json;
using OpenLogicool.Probe;
using Xunit;

namespace OpenLogicool.Probe.Tests;

public sealed class LiveDiscoveryNanoActionSmokeTests
{
    [Fact]
    public void PinnedObservationResolvesVlmTypoThroughSameFrameOcrText()
    {
        var path = WriteObservation([
            Candidate("タップして受けける", "タップして受け取る", 10, 20),
        ]);

        try
        {
            var result = LiveDiscoveryNanoActionSmoke.ValidatePinnedObservation(path, "タップして受け取る");

            Assert.Equal("タップして受け取る", result.Text);
            Assert.Equal(10, result.X);
            Assert.Equal(20, result.Y);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PinnedObservationRejectsTwoDifferentEquallySimilarTargets()
    {
        var path = WriteObservation([
            Candidate("受け取る", "受け取る", 10, 20),
            Candidate("受け取る", "受け取る", 100, 200),
        ]);

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                LiveDiscoveryNanoActionSmoke.ValidatePinnedObservation(path, "受け取る"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Explicit_ocr_label_requires_opt_in_and_resolves_an_exact_unique_operator_label()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openlogicool-pinned-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            GroundedCandidates = Array.Empty<object>(),
            Ocr = new
            {
                Text = "ゲームを終了しますか?",
                ElapsedMs = 1,
                RecognizerLanguage = "ja",
                MaxImageDimension = 10_000u,
                VisualLines = new[] { "ゲームを終了しますか?" },
                Words = new[]
                {
                    new { Text = "ゲームを終了しますか?", X = 100d, Y = 200d, Width = 180d, Height = 20d },
                },
            },
        }));

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                LiveDiscoveryNanoActionSmoke.ValidatePinnedObservation(path, "ゲームを終了しますか?"));

            var result = LiveDiscoveryNanoActionSmoke.ValidatePinnedObservation(
                path, "ゲームを終了しますか?", allowExplicitOcrLabel: true);

            Assert.Equal("ゲームを終了しますか?", result.Text);
            Assert.Equal(100, result.X);
            Assert.Equal(200, result.Y);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("before", "after", false, "Grounded", true)]
    [InlineData("same", "same", false, "Unknown", false)]
    [InlineData("before", "after", true, "Grounded", false)]
    [InlineData("before", "after", true, "Unknown", true)]
    public void OutcomeRequiresScreenTransitionAndOptionalLabelAbsence(
        string before,
        string after,
        bool expectLabelAbsent,
        string targetStatus,
        bool expected)
    {
        Assert.Equal(
            expected,
            LiveDiscoveryNanoActionSmoke.EvaluateOutcome(
                before,
                after,
                expectLabelAbsent,
                targetStatus));
    }

    private static string WriteObservation(object[] candidates)
    {
        var path = Path.Combine(Path.GetTempPath(), $"openlogicool-pinned-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { GroundedCandidates = candidates }));
        return path;
    }

    private static object Candidate(string label, string text, double x, double y) => new
    {
        Label = label,
        Status = "Grounded",
        Box = new { Text = text, X = x, Y = y, Width = 40d, Height = 12d },
    };
}
