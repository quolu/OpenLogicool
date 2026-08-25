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
    public void Ocr_keys_at_the_same_position_use_light_text_distance()
    {
        Assert.True(GameSceneSemanticComparer.AffordanceKeySimilar(
            "ocr-text|前哨%地|1|2",
            "ocr-text|前哨基地|1|2"));
        Assert.False(GameSceneSemanticComparer.AffordanceKeySimilar(
            "ocr-text|前哨基地|1|2",
            "ocr-text|隊員募集|1|2"));
    }

    [Fact]
    public void Known_state_stability_does_not_break_on_ocr_affordance_flutter()
    {
        var left = new GameSceneSemanticSignature(
            StateIdentityStatus.Known,
            ["known-screen:a"],
            ["ocr-text|OpenEvent|1|3"]);
        var right = new GameSceneSemanticSignature(
            StateIdentityStatus.Known,
            ["known-screen:a"],
            []);

        Assert.True(GameSceneSemanticComparer.StableEquivalent(left, right));
        Assert.False(GameSceneSemanticComparer.Equivalent(left, right));
    }

    [Fact]
    public void Exact_equivalence_does_not_use_fuzzy_ocr_distance()
    {
        var left = new GameSceneSemanticSignature(
            StateIdentityStatus.Novel,
            ["state:a"],
            ["ocr-text|前哨%地|1|2"]);
        var right = new GameSceneSemanticSignature(
            StateIdentityStatus.Novel,
            ["state:a"],
            ["ocr-text|前哨基地|1|2"]);

        Assert.False(GameSceneSemanticComparer.Equivalent(left, right));
        Assert.True(GameSceneSemanticComparer.StableEquivalent(left, right));
    }

    [Fact]
    public void One_changing_banner_among_stable_page_structure_is_stayed()
    {
        var before = SceneWithLabels("before", 1, ["ロビー", "部隊", "TRAILMARKER"]);
        var after = SceneWithLabels("after", 2, ["ロビー", "部隊", "MISSIONPASS"]);

        var comparison = new GameTransitionJudge().Compare(before, Stable(after));

        Assert.Equal(GameTransitionJudgement.Stayed, comparison.Judgement);
    }

    [Fact]
    public void Scroll_uses_smaller_visual_threshold_than_click_page_transition()
    {
        var before = SceneWithLabels("before", 1, ["A", "B", "C"]);
        var after = SceneWithLabels("after", 2, ["D", "E", "F"]);
        before = before with
        {
            Affordances = before.Affordances.Select(candidate => candidate with
            {
                AllowedPrimitives = [GameInteractionOperations.Scroll],
            }).ToArray(),
            SceneVisualPatch = Patch(40),
        };
        after = after with { SceneVisualPatch = Patch(42) };

        var scroll = new GameTransitionJudge().Compare(before, Stable(after));
        var click = new GameTransitionJudge().Compare(
            before with
            {
                Affordances = before.Affordances.Select(candidate => candidate with
                {
                    AllowedPrimitives = [GameInteractionOperations.Click],
                }).ToArray(),
            },
            Stable(after));

        Assert.Equal(GameTransitionJudgement.Moved, scroll.Judgement);
        Assert.Equal(GameTransitionJudgement.Stayed, click.Judgement);
    }

    [Fact]
    public void Visual_stability_keeps_the_same_page_when_ocr_labels_churn()
    {
        var left = SceneWithLabels("left", 1, ["イベントフィールド", "ショップ"]) with
        {
            SceneVisualPatch = Patch(40),
        };
        var right = SceneWithLabels("right", 2, ["フイィベーンルトド", "シヨップ"]) with
        {
            SceneVisualPatch = Patch(42),
        };

        Assert.True(GameSceneSemanticComparer.StableEquivalent(left, right));
    }

    [Fact]
    public void Stability_window_accumulates_visual_same_page_across_ocr_churn()
    {
        var window = new GameSceneStabilityWindow(new ExplorationWaitCondition(
            ContractSchemaVersions.Revision03, 3, 300, 2_000));
        var first = SceneWithLabels("first", 1, ["イベントフィールド", "ショップ"]) with { SceneVisualPatch = Patch(40) };
        var second = SceneWithLabels("second", 2, ["フイィベーンルトド", "シヨップ"]) with { SceneVisualPatch = Patch(42) };
        var third = SceneWithLabels("third", 3, ["イベントフイールド", "ショップ0"]) with { SceneVisualPatch = Patch(43) };

        Assert.False(window.Observe(first, 0));
        Assert.False(window.Observe(second, 150));
        Assert.True(window.Observe(third, 300));
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
    public void Ambiguous_state_with_the_same_actionable_structure_is_stayed()
    {
        var before = Scene("before", 1, "部隊", 0.1) with
        {
            StateIdentity = StateIdentityStatus.Ambiguous,
        };
        var after = Scene("after", 2, "部隊", 0.1);

        var comparison = new GameTransitionJudge().Compare(before, Stable(after));

        Assert.Equal(GameTransitionJudgement.Stayed, comparison.Judgement);
    }

    [Fact]
    public void Ambiguous_destination_with_changed_actionable_structure_is_moved()
    {
        var before = Scene("before", 1, "部隊", 0.1);
        var after = Scene("after", 2, "自動編成", 0.7) with
        {
            StateIdentity = StateIdentityStatus.Ambiguous,
        };

        var comparison = new GameTransitionJudge().Compare(before, Stable(after));

        Assert.Equal(GameTransitionJudgement.Moved, comparison.Judgement);
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

    private static ObservedScene SceneWithLabels(
        string observationId,
        long sequence,
        IReadOnlyList<string> labels)
    {
        var scene = Scene(observationId, sequence, labels[0], 0.1);
        return scene with
        {
            Affordances = labels.Select((label, index) => scene.Affordances[0] with
            {
                CandidateId = $"candidate:{observationId}:{index}",
                SemanticKind = "ocr-text",
                SemanticLabel = label,
                Locator = scene.Affordances[0].Locator with
                {
                    NormalizedBounds = [0.1 + index * 0.25, 0.2, 0.1, 0.1],
                },
            }).ToArray(),
        };
    }

    private static VisualPatchSignature Patch(byte value) => new(
        ContractSchemaVersions.Revision03,
        8,
        8,
        Convert.ToBase64String(Enumerable.Repeat(value, 64).ToArray()),
        $"patch-{value}");
}
