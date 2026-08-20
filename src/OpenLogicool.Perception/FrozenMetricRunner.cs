using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Perception;

public sealed record FrozenMetricCase(
    CorpusArtifact Artifact,
    CapturedFrame Frame,
    ObservationStatus ExpectedStatus,
    bool ExpectedDispatchAllowed);

public sealed record FrozenMetricReport(
    int KnownMisclassifications,
    int UnknownPromotions,
    int SuccessFalsePositives)
{
    public bool Passed => KnownMisclassifications == 0 && UnknownPromotions == 0 && SuccessFalsePositives == 0;
}

/// <summary>凍結した acceptance corpus だけで、事前固定の認識／dispatch 基準を集計する。</summary>
public static class FrozenMetricRunner
{
    public static FrozenMetricReport Evaluate(AcceptanceCorpus acceptance, IReadOnlyList<FrozenMetricCase> cases, IFrameRecognizer recognizer)
    {
        ArgumentNullException.ThrowIfNull(acceptance);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(recognizer);
        var accepted = acceptance.Artifacts.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (cases.Count != accepted.Count || cases.Any(item => !accepted.Contains(item.Artifact.Id)))
        {
            throw new ArgumentException("評価 case は acceptance corpus の各 artifact に一件ずつ必要です。", nameof(cases));
        }

        var observations = new LiveObservationSource(recognizer);
        var measured = cases.Select(item => (Item: item, Actual: observations.Observe(item.Frame))).ToArray();
        return new FrozenMetricReport(
            measured.Count(item => item.Item.ExpectedStatus != ObservationStatus.Known && item.Actual.Status == ObservationStatus.Known),
            measured.Count(item => item.Item.ExpectedStatus == ObservationStatus.Unknown && item.Actual.Status == ObservationStatus.Known),
            measured.Count(item => !item.Item.ExpectedDispatchAllowed && LiveObservationSource.AllowsAutomaticExecution(item.Actual)));
    }
}
