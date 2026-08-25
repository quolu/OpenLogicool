using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Host;

/// <summary>WGCの非クライアント領域だけを除外するWindows専用policy。</summary>
public sealed class WindowsGameExplorationCandidateRiskPolicy(
    IExplorationCandidateRiskPolicy contentPolicy) : IExplorationCandidateRiskPolicy
{
    public ExplorationCandidateRiskDecision Evaluate(AffordanceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var bounds = candidate.Locator.NormalizedBounds;
        if (bounds.Count != 4)
        {
            throw new ArgumentException("candidate boundsは4要素でなければなりません。", nameof(candidate));
        }
        if (bounds[1] < 0.03 && bounds[3] < 0.03)
        {
            return Prohibited("windows-non-client", "Windowsのタイトルバー領域");
        }
        return contentPolicy.Evaluate(candidate);
    }

    private static ExplorationCandidateRiskDecision Prohibited(string tag, string detail) =>
        new(
            ExplorationRiskLevel.Prohibited,
            [tag],
            false,
            false,
            [],
            detail);
}
