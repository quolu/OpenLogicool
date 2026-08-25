using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using Xunit;

namespace OpenLogicool.Exploration.Tests;

public sealed class GameTransitionJudgeTests
{
    [Fact]
    public void Continuous_animation_with_the_same_semantic_structure_becomes_stable_and_stayed()
    {
        var condition = new ExplorationWaitCondition(
            ContractSchemaVersions.Revision03,
            3,
            300,
            1_000);
        var window = new GameSceneStabilityWindow(condition);
        var before = Scene("before", 1, "部隊", 0.10);
        var frame1 = Scene("after-1", 2, "部隊", 0.11);
        var frame2 = Scene("after-2", 3, "部隊", 0.12);
        var frame3 = Scene("after-3", 4, "部隊", 0.13);

        Assert.False(window.Observe(frame1, 0));
        Assert.False(window.Observe(frame2, 150));
        Assert.True(window.Observe(frame3, 300));
        var stability = new GameInteractionStabilityResult(
            ContractSchemaVersions.Revision03,
            GameInteractionStabilityStatus.Stable,
            [frame1, frame2, frame3],
            frame3,
            3,
            300,
            300,
            null);

        var comparison = new GameTransitionJudge().Compare(before, stability);

        Assert.Equal(GameTransitionJudgement.Stayed, comparison.Judgement);
        Assert.Empty(comparison.ChangedRegions);
    }

    [Fact]
    public void Changed_actionable_structure_is_moved()
    {
        var before = Scene("before", 1, "部隊", 0.1);
        var after = Scene("after", 2, "戦闘開始", 0.7);
        var stability = Stable(after);

        var comparison = new GameTransitionJudge().Compare(before, stability);

        Assert.Equal(GameTransitionJudgement.Moved, comparison.Judgement);
        Assert.NotEmpty(comparison.ChangedRegions);
    }

    [Fact]
    public void Width_and_spacing_variants_do_not_split_the_same_semantic_label()
    {
        var before = Scene("before", 1, "ゲームを終了しますか？", 0.1);
        var after = Scene("after", 2, " ゲームを終了しますか? ", 0.1);

        var comparison = new GameTransitionJudge().Compare(before, Stable(after));

        Assert.Equal(GameTransitionJudgement.Stayed, comparison.Judgement);
    }

    [Fact]
    public void Missing_terminal_punctuation_does_not_split_the_same_semantic_label()
    {
        var before = Scene("before", 1, "ゲームを終了しますか？", 0.1);
        var after = Scene("after", 2, "ゲームを終了しますか", 0.1);

        var comparison = new GameTransitionJudge().Compare(before, Stable(after));

        Assert.Equal(GameTransitionJudgement.Stayed, comparison.Judgement);
    }

    [Fact]
    public void Probe_target_animation_does_not_count_as_page_transition()
    {
        var beforeBase = Scene("before", 1, "ロビー", 0.2);
        var afterBase = Scene("after", 2, "ロビー", 0.2);
        var before = beforeBase with
        {
            Affordances =
            [
                .. beforeBase.Affordances,
                beforeBase.Affordances[0] with
                {
                    CandidateId = "probe-before",
                    SemanticKind = "probe-target",
                    SemanticLabel = "点滅前",
                },
            ],
        };
        var after = afterBase with
        {
            Affordances =
            [
                .. afterBase.Affordances,
                afterBase.Affordances[0] with
                {
                    CandidateId = "probe-after",
                    SemanticKind = "probe-target",
                    SemanticLabel = "点滅後",
                },
            ],
        };

        var comparison = new GameTransitionJudge().Compare(before, Stable(after));

        Assert.Equal(GameTransitionJudgement.Stayed, comparison.Judgement);
    }

    [Fact]
    public void Partial_detection_is_equivalent_when_the_smaller_semantic_set_is_mostly_contained()
    {
        var smaller = new GameSceneSemanticSignature(
            StateIdentityStatus.Novel,
            [],
            ["ARENA|2|2", "ROOM|2|2", "戻る|0|3", "NIKKE|0|0"]);
        var larger = new GameSceneSemanticSignature(
            StateIdentityStatus.Novel,
            [],
            ["ARENA|2|2", "ROOM|2|2", "戻る|0|3", "NIKKE|0|0", "迎撃戦|2|3"]);

        Assert.True(GameSceneSemanticComparer.StableEquivalent(smaller, larger));
        Assert.False(GameSceneSemanticComparer.Equivalent(smaller, larger));
    }

    [Fact]
    public void Shared_header_text_alone_does_not_make_different_screens_equivalent()
    {
        var left = new GameSceneSemanticSignature(
            StateIdentityStatus.Novel,
            [],
            ["NIKKE|0|0", "GEMS|1|0", "GOLD|2|0", "アーク|2|2"]);
        var right = new GameSceneSemanticSignature(
            StateIdentityStatus.Novel,
            [],
            ["NIKKE|0|0", "GEMS|1|0", "GOLD|2|0", "部隊|1|3"]);

        Assert.False(GameSceneSemanticComparer.StableEquivalent(left, right));
    }

    [Fact]
    public void Missing_semantic_detection_does_not_erase_prior_stability_evidence()
    {
        var condition = new ExplorationWaitCondition(
            ContractSchemaVersions.Revision03,
            3,
            300,
            2_000);
        var window = new GameSceneStabilityWindow(condition);

        Assert.False(window.Observe(Scene("valid-1", 1, "ランキング", 0.5), 0));
        Assert.False(window.Observe(EmptyScene("missing", 2), 100));
        Assert.False(window.Observe(Scene("valid-2", 3, "ランキング", 0.5), 200));
        Assert.True(window.Observe(Scene("valid-3", 4, "ランキング", 0.5), 300));
        Assert.Equal(3, window.StableFramesObserved);
    }

    [Theory]
    [InlineData(GameInteractionStabilityStatus.TimedOut)]
    [InlineData(GameInteractionStabilityStatus.Unavailable)]
    [InlineData(GameInteractionStabilityStatus.Fault)]
    public void Non_stable_result_is_undetermined(GameInteractionStabilityStatus status)
    {
        var before = Scene("before", 1, "部隊", 0.1);
        var result = new GameInteractionStabilityResult(
            ContractSchemaVersions.Revision03,
            status,
            [],
            null,
            0,
            0,
            1_000,
            "not stable");

        var comparison = new GameTransitionJudge().Compare(before, result);

        Assert.Equal(GameTransitionJudgement.Undetermined, comparison.Judgement);
    }

    [Fact]
    public void Ambiguous_state_is_undetermined_even_when_the_window_is_stable()
    {
        var before = Scene("before", 1, "部隊", 0.1) with
        {
            StateIdentity = StateIdentityStatus.Ambiguous,
        };
        var after = Scene("after", 2, "部隊", 0.1);

        var comparison = new GameTransitionJudge().Compare(before, Stable(after));

        Assert.Equal(GameTransitionJudgement.Undetermined, comparison.Judgement);
    }

    private static GameInteractionStabilityResult Stable(ObservedScene scene) => new(
        ContractSchemaVersions.Revision03,
        GameInteractionStabilityStatus.Stable,
        [scene],
        scene,
        3,
        300,
        300,
        null);

    private static ObservedScene Scene(
        string observationId,
        long sequence,
        string label,
        double x) => new(
        ContractSchemaVersions.Revision03,
        $"scene-{observationId}",
        observationId,
        new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            "window:game",
            CaptureBackend.WindowsGraphicsCapture,
            sequence,
            sequence * 100,
            DateTimeOffset.UnixEpoch.AddMilliseconds(sequence * 100),
            3,
            10,
            250),
        CaptureAvailability.Available,
        StateIdentityStatus.Novel,
        $"hypothesis:{observationId}",
        [],
        [new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            $"candidate-{observationId}",
            observationId,
            sequence,
            3,
            "window:game",
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "foundry-local-text-region",
                [x, 0.2, 0.1, 0.1],
                $"locator-{observationId}"),
            [new EvidenceRegion(
                ContractSchemaVersions.Revision03,
                "rect",
                [x, 0.2, 0.1, 0.1],
                "foundry-local")],
            0.5,
            [GameInteractionOperations.Click],
            "text",
            label)],
        "foundry-local-controls");

    private static ObservedScene EmptyScene(string observationId, long sequence) =>
        Scene(observationId, sequence, "placeholder", 0.1) with
        {
            Affordances = [],
            StateCandidates = [],
        };
}
