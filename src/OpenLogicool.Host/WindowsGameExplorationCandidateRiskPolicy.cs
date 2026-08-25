using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Host;

/// <summary>WGCの非クライアント領域と上端HUDを探索対象から除外するWindows専用policy。</summary>
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
        var label = candidate.SemanticLabel ?? string.Empty;
        if ((bounds[1] < 0.10 || bounds[0] > 0.80 && bounds[1] < 0.20)
            && bounds[3] < 0.06
            && label.Any(char.IsDigit))
        {
            return Prohibited("status-hud", "画面上端の数値HUD");
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
