using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class CodexProductGameRuntimeTests
{
    [Fact]
    public void Scroll_region_ignores_new_text_outside_the_information_area()
    {
        var result = Result(
            Scene(("MISSION A", 0.45, 0.45)),
            Scene(("MISSION A", 0.45, 0.45), ("rotating banner", 0.95, 0.05)));

        Assert.False(WindowsInformationScrollComparisonNormalizer.HasScrollRegionProgress(
            result.Before!, result.Stability!, [0.499, 0.599, 0.002, 0.002]));
    }

    [Fact]
    public void Scroll_region_accepts_new_information_text_near_the_scroll_target()
    {
        var result = Result(
            Scene(("MISSION A", 0.45, 0.45)),
            Scene(("MISSION A", 0.45, 0.45), ("MISSION B", 0.45, 0.75)));

        Assert.True(WindowsInformationScrollComparisonNormalizer.HasScrollRegionProgress(
            result.Before!, result.Stability!, [0.499, 0.599, 0.002, 0.002]));
    }

    [Fact]
    public void Missing_local_regions_preserves_the_foundation_comparison()
    {
        var before = Scene(("MISSION A", 0.45, 0.45)) with { DiscoveryEvidence = null };
        var result = Result(before, Scene(("MISSION A", 0.45, 0.45)));

        Assert.True(WindowsInformationScrollComparisonNormalizer.HasScrollRegionProgress(
            result.Before!, result.Stability!, [0.499, 0.599, 0.002, 0.002]));
    }

    [Fact]
    public void Scroll_region_accepts_vertical_movement_of_an_existing_information_text()
    {
        var result = Result(
            Scene(("派遣を3回実行する", 0.45, 0.72)),
            Scene(("派遣を3回実行する", 0.45, 0.52)));

        Assert.True(WindowsInformationScrollComparisonNormalizer.HasScrollRegionProgress(
            result.Before!, result.Stability!, [0.499, 0.599, 0.002, 0.002]));
    }

    [Fact]
    public void Ai_free_saved_scroll_promotes_real_ocr_movement_over_foundation_stayed()
    {
        var result = Result(
            Scene(("MISSION A", 0.45, 0.45)),
            Scene(("MISSION A", 0.45, 0.45), ("MISSION B", 0.45, 0.75))) with
        {
            Comparison = Comparison(GameTransitionJudgement.Stayed),
        };
        var normalizer = new WindowsInformationScrollComparisonNormalizer();
        var actual = normalizer.Normalize(
            GameInteractionOperations.Scroll,
            ScrollEdge(),
            result.Before!,
            result.Stability!,
            result.Comparison!);

        Assert.Equal(GameTransitionJudgement.Moved, actual.Judgement);
    }

    [Fact]
    public void Ai_free_saved_scroll_demotes_unrelated_global_movement_without_new_region_text()
    {
        var result = Result(
            Scene(("MISSION A", 0.45, 0.45)),
            Scene(("MISSION A", 0.45, 0.45), ("banner", 0.95, 0.05))) with
        {
            Comparison = Comparison(GameTransitionJudgement.Moved),
        };
        var normalizer = new WindowsInformationScrollComparisonNormalizer();
        var actual = normalizer.Normalize(
            GameInteractionOperations.Scroll,
            ScrollEdge(),
            result.Before!,
            result.Stability!,
            result.Comparison!);

        Assert.Equal(GameTransitionJudgement.Stayed, actual.Judgement);
    }

    private static ProductGameExplorerStepResult Result(ObservedScene before, ObservedScene after) => new(
        ProductGameExplorerStepStatus.Learned,
        before,
        null,
        null,
        new GameInteractionStabilityResult(
            ContractSchemaVersions.Revision03,
            GameInteractionStabilityStatus.Stable,
            [after],
            after,
            2,
            1_000,
            1_000,
            null),
        null,
        null,
        "structure",
        "result");

    private static ObservedScene Scene(params (string Text, double X, double Y)[] texts)
    {
        var frame = new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            "window:test",
            CaptureBackend.WindowsGraphicsCapture,
            1,
            1,
            DateTimeOffset.UnixEpoch,
            1,
            0,
            0);
        return new ObservedScene(
            ContractSchemaVersions.Revision03,
            "scene",
            "observation",
            frame,
            CaptureAvailability.Available,
            StateIdentityStatus.Novel,
            null,
            [],
            [],
            "test",
            new SceneDiscoveryEvidence(
                "local",
                "ocr",
                "test",
                "sha",
                "ok",
                "none",
                null,
                "",
                1,
                0,
                0,
                LocalGroundingRegions: texts.Select(item => new SceneGroundingRegion(
                    item.Text,
                    new EvidenceRegion(
                        ContractSchemaVersions.Revision03,
                        "rect",
                        [item.X, item.Y, 0.1, 0.04],
                        "ocr"))).ToArray()));
    }

    private static GameTransitionComparison Comparison(GameTransitionJudgement judgement) => new(
        ContractSchemaVersions.Revision03,
        "before",
        "after",
        judgement,
        [],
        ["foundation"]);

    private static StructureScreenEdge ScrollEdge() => new(
        ContractSchemaVersions.Revision03,
        "edge",
        "source",
        "destination",
        null,
        "candidate",
        "locator",
        GameInteractionOperations.Scroll,
        "goal",
        [],
        false,
        "before",
        "after",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
        [],
        [],
        StructureVerificationState.Candidate,
        TargetNormalizedBounds: [0.499, 0.599, 0.002, 0.002],
        VerticalScrollSteps: -8,
        HorizontalScrollSteps: 0);

}
