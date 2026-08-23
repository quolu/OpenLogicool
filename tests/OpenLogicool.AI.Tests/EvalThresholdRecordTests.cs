using OpenLogicool.AI;
using Xunit;

namespace OpenLogicool.AI.Tests;

public sealed class EvalThresholdRecordTests
{
    [Fact]
    public void 事前固定した入力と閾値に既存_eval_reportを結び付けて受理する()
    {
        var parameters = new Dictionary<string, string>
        {
            ["temperature"] = "0.1",
            ["max_tokens"] = "128",
        };
        var input = new EvalInputRecord(
            new EvalDatasetRecord("menu-frames", "2026-08-21", "sha256:dataset"),
            new EvalModelPromptRecord("candidate-model-v1", "planner-r3", "sha256:prompt"),
            parameters);
        parameters["temperature"] = "0.9";

        var assessment = EvalThresholdRecord.Assess(
            input,
            new EvalThreshold(1m, 1m, 18, 200),
            Report(knownAccuracy: 1, unknownRejections: 1, latencyMs: 18, workingSetBytes: 200));

        Assert.True(assessment.IsAccepted);
        Assert.Empty(assessment.Failures);
        Assert.Equal("0.1", assessment.Input.Parameters["temperature"]);
        Assert.Equal(["max_tokens", "temperature"], assessment.Input.Parameters.Keys);
    }

    [Fact]
    public void 未達と中断を個別に記録して受理しない()
    {
        var assessment = EvalThresholdRecord.Assess(
            Input(),
            new EvalThreshold(1m, 1m, 10, 100),
            Report(knownAccuracy: 0, unknownRejections: 0, latencyMs: 11, workingSetBytes: 200, cancelled: true));

        Assert.False(assessment.IsAccepted);
        Assert.Equal(
        [
            EvalThresholdFailure.Cancelled,
            EvalThresholdFailure.KnownActionAccuracyBelowThreshold,
            EvalThresholdFailure.UnknownRejectionRateBelowThreshold,
            EvalThresholdFailure.TotalLatencyAboveThreshold,
            EvalThresholdFailure.WorkingSetAboveThreshold,
        ],
        assessment.Failures);
    }

    [Fact]
    public void 閾値対象のcase欠落と不正な記録を拒否する()
    {
        var assessment = EvalThresholdRecord.Assess(
            Input(),
            new EvalThreshold(.5m, .5m, 1, 1),
            new FrozenEvaluationReport(0, 0, 0, 0, 0, 0, 0, false));

        Assert.Equal(
        [
            EvalThresholdFailure.MissingKnownActionCases,
            EvalThresholdFailure.MissingUnknownCases,
        ],
        assessment.Failures);
        Assert.Throws<ArgumentOutOfRangeException>(() => EvalThresholdRecord.Assess(
            Input(),
            new EvalThreshold(1.01m, 0m, 0, 0),
            Report(1, 1, 0, 0)));
        Assert.Throws<ArgumentException>(() => new EvalInputRecord(
            new EvalDatasetRecord("dataset", "v1", "digest"),
            new EvalModelPromptRecord("model", "prompt", "digest"),
            new Dictionary<string, string> { ["temperature"] = " " }));
    }

    private static EvalInputRecord Input() => new(
        new EvalDatasetRecord("dataset", "v1", "sha256:dataset"),
        new EvalModelPromptRecord("model-candidate", "prompt-v1", "sha256:prompt"),
        new Dictionary<string, string> { ["temperature"] = "0" });

    private static FrozenEvaluationReport Report(
        int knownAccuracy,
        int unknownRejections,
        long latencyMs,
        long workingSetBytes,
        bool cancelled = false) => new(
        ProcessedCases: 2,
        KnownCases: 1,
        CorrectKnownActions: knownAccuracy,
        UnknownCases: 1,
        CorrectUnknownRejections: unknownRejections,
        TotalLatencyMs: latencyMs,
        MaximumWorkingSetBytes: workingSetBytes,
        Cancelled: cancelled);
}
