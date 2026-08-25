using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class WindowsGameExplorationCandidateRiskPolicyTests
{
    private readonly WindowsGameExplorationCandidateRiskPolicy policy = new(
        UnclassifiedExplorationCandidateRiskPolicy.Default);

    [Fact]
    public void Rejects_only_windows_non_client_region()
    {
        var title = policy.Evaluate(Candidate("NIKKE", [0.01, 0.008, 0.02, 0.008]));
        var hud = policy.Evaluate(Candidate("4,846+", [0.45, 0.04, 0.05, 0.02]));
        var objective = policy.Evaluate(Candidate("48-36をクリアする", [0.91, 0.11, 0.06, 0.02]));
        var content = policy.Evaluate(Candidate("ROOM", [0.40, 0.35, 0.08, 0.04]));

        Assert.Equal(ExplorationRiskLevel.Prohibited, title.Level);
        Assert.Contains("windows-non-client", title.RiskTags);
        Assert.Equal(ExplorationRiskLevel.Unknown, hud.Level);
        Assert.Equal(ExplorationRiskLevel.Unknown, objective.Level);
        Assert.Equal(ExplorationRiskLevel.Unknown, content.Level);
    }

    [Fact]
    public void Ocr_semantics_never_receive_rejection_authority()
    {
        var result = policy.Evaluate(Candidate("ショップ", [0.4, 0.4, 0.1, 0.05]));
        var activityStart = policy.Evaluate(Candidate("シミュレーション開始", [0.4, 0.4, 0.1, 0.05]));

        Assert.Equal(ExplorationRiskLevel.Unknown, result.Level);
        Assert.Empty(result.RiskTags);
        Assert.Equal(ExplorationRiskLevel.Unknown, activityStart.Level);
        Assert.Empty(activityStart.RiskTags);
    }

    [Fact]
    public void Exit_modal_text_does_not_block_either_candidate()
    {
        var ok = policy.Evaluate(Candidate(
            "OK",
            [0.55, 0.65, 0.2, 0.08],
            ["お知らせ", "ゲームを終了しますか？", "取消", "OK"]));
        var cancel = policy.Evaluate(Candidate(
            "取消",
            [0.35, 0.65, 0.2, 0.08],
            ["お知らせ", "ゲームを終了しますか？", "取消", "OK"]));

        Assert.Equal(ExplorationRiskLevel.Unknown, ok.Level);
        Assert.Equal(ExplorationRiskLevel.Unknown, cancel.Level);
    }

    private static AffordanceCandidate Candidate(
        string label,
        IReadOnlyList<double> bounds,
        IReadOnlyList<string>? contextTexts = null) =>
        new(
            ContractSchemaVersions.Revision03,
            $"candidate:{label}",
            "observation-1",
            1,
            1,
            "window:game",
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "ocr-text-region",
                bounds,
                "locator-1"),
            [new EvidenceRegion(
                ContractSchemaVersions.Revision03,
                "rect",
                bounds,
                "windows-ocr")],
            1,
            [GameInteractionOperations.Click],
            "text",
            label,
            ContextTexts: contextTexts);
}
