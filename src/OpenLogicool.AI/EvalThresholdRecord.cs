using System.Collections.ObjectModel;

namespace OpenLogicool.AI;

/// <summary>AI eval の前に固定する acceptance threshold。</summary>
public sealed record EvalThreshold(
    decimal MinimumKnownActionAccuracy,
    decimal MinimumUnknownRejectionRate,
    long MaximumTotalLatencyMs,
    long MaximumWorkingSetBytes)
{
    public void Validate()
    {
        ValidateRate(MinimumKnownActionAccuracy, nameof(MinimumKnownActionAccuracy));
        ValidateRate(MinimumUnknownRejectionRate, nameof(MinimumUnknownRejectionRate));
        if (MaximumTotalLatencyMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumTotalLatencyMs));
        }

        if (MaximumWorkingSetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumWorkingSetBytes));
        }
    }

    private static void ValidateRate(decimal value, string parameterName)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

/// <summary>凍結 frame corpus の識別子・版・内容 digest。</summary>
public sealed record EvalDatasetRecord(string DatasetId, string DatasetVersion, string CorpusDigest)
{
    public void Validate()
    {
        RequireValue(DatasetId, nameof(DatasetId));
        RequireValue(DatasetVersion, nameof(DatasetVersion));
        RequireValue(CorpusDigest, nameof(CorpusDigest));
    }

    private static void RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("値は空白にできません。", parameterName);
        }
    }
}

/// <summary>provider を選定せずに残す model と prompt の記録。</summary>
public sealed record EvalModelPromptRecord(
    string ModelIdentifier,
    string PromptIdentifier,
    string PromptDigest)
{
    public void Validate()
    {
        RequireValue(ModelIdentifier, nameof(ModelIdentifier));
        RequireValue(PromptIdentifier, nameof(PromptIdentifier));
        RequireValue(PromptDigest, nameof(PromptDigest));
    }

    private static void RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("値は空白にできません。", parameterName);
        }
    }
}

/// <summary>
/// eval 実行前に凍結する入力来歴。provider を表す field や選定口は持たない。
/// </summary>
public sealed record EvalInputRecord
{
    public EvalInputRecord(
        EvalDatasetRecord dataset,
        EvalModelPromptRecord modelPrompt,
        IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(modelPrompt);
        ArgumentNullException.ThrowIfNull(parameters);
        dataset.Validate();
        modelPrompt.Validate();

        var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("parameter の名前と値は空白にできません。", nameof(parameters));
            }

            copy.Add(key, value);
        }

        Dataset = dataset;
        ModelPrompt = modelPrompt;
        Parameters = new ReadOnlyDictionary<string, string>(copy);
    }

    public EvalDatasetRecord Dataset { get; }

    public EvalModelPromptRecord ModelPrompt { get; }

    public IReadOnlyDictionary<string, string> Parameters { get; }
}

public enum EvalThresholdFailure
{
    Cancelled,
    MissingKnownActionCases,
    KnownActionAccuracyBelowThreshold,
    MissingUnknownCases,
    UnknownRejectionRateBelowThreshold,
    TotalLatencyAboveThreshold,
    WorkingSetAboveThreshold,
}

public sealed record EvalThresholdAssessment(
    EvalInputRecord Input,
    EvalThreshold Threshold,
    FrozenEvaluationReport Report,
    IReadOnlyList<EvalThresholdFailure> Failures)
{
    public bool IsAccepted => Failures.Count == 0;
}

/// <summary>
/// 既存 EvalHarness の集計結果を、事前に固定した閾値と入力来歴へ結び付ける。
/// corpus や prompt の調整、provider 選定、評価実行は行わない。
/// </summary>
public static class EvalThresholdRecord
{
    public static EvalThresholdAssessment Assess(
        EvalInputRecord input,
        EvalThreshold threshold,
        FrozenEvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(threshold);
        ArgumentNullException.ThrowIfNull(report);
        threshold.Validate();

        var failures = new List<EvalThresholdFailure>();
        if (report.Cancelled)
        {
            failures.Add(EvalThresholdFailure.Cancelled);
        }

        AddRateFailure(
            report.KnownActionAccuracy,
            threshold.MinimumKnownActionAccuracy,
            EvalThresholdFailure.MissingKnownActionCases,
            EvalThresholdFailure.KnownActionAccuracyBelowThreshold,
            failures);
        AddRateFailure(
            report.UnknownRejectionRate,
            threshold.MinimumUnknownRejectionRate,
            EvalThresholdFailure.MissingUnknownCases,
            EvalThresholdFailure.UnknownRejectionRateBelowThreshold,
            failures);

        if (report.TotalLatencyMs > threshold.MaximumTotalLatencyMs)
        {
            failures.Add(EvalThresholdFailure.TotalLatencyAboveThreshold);
        }

        if (report.MaximumWorkingSetBytes > threshold.MaximumWorkingSetBytes)
        {
            failures.Add(EvalThresholdFailure.WorkingSetAboveThreshold);
        }

        return new EvalThresholdAssessment(input, threshold, report, failures.AsReadOnly());
    }

    private static void AddRateFailure(
        decimal? actual,
        decimal minimum,
        EvalThresholdFailure missingFailure,
        EvalThresholdFailure belowThresholdFailure,
        ICollection<EvalThresholdFailure> failures)
    {
        if (actual is null)
        {
            failures.Add(missingFailure);
        }
        else if (actual < minimum)
        {
            failures.Add(belowThresholdFailure);
        }
    }
}
