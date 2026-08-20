using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Perception;

public sealed record FrozenMetricCase(
    CorpusArtifact Artifact,
    ObservationStatus ExpectedStatus,
    bool ExpectedDispatchAllowed,
    ObservationStatus ActualStatus,
    bool ActualDispatchAllowed);

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
    public static FrozenMetricReport Evaluate(AcceptanceCorpus acceptance, IReadOnlyList<FrozenMetricCase> cases)
    {
        ArgumentNullException.ThrowIfNull(acceptance);
        ArgumentNullException.ThrowIfNull(cases);
        var accepted = acceptance.Artifacts.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (cases.Count != accepted.Count || cases.Any(item => !accepted.Contains(item.Artifact.Id)))
        {
            throw new ArgumentException("評価 case は acceptance corpus の各 artifact に一件ずつ必要です。", nameof(cases));
        }

        return new FrozenMetricReport(
            cases.Count(item => item.ExpectedStatus != ObservationStatus.Known && item.ActualStatus == ObservationStatus.Known),
            cases.Count(item => item.ExpectedStatus == ObservationStatus.Unknown && item.ActualStatus == ObservationStatus.Known),
            cases.Count(item => !item.ExpectedDispatchAllowed && item.ActualDispatchAllowed));
    }
}
