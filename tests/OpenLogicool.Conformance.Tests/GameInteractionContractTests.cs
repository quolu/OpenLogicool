using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Conformance.Tests;

public sealed class GameInteractionContractTests
{
    [Fact]
    public void Ten_foundation_operations_are_complete_and_unique()
    {
        Assert.Equal(10, GameInteractionOperations.All.Count);
        Assert.Equal(10, GameInteractionOperations.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            [
                "observe",
                "discover-targets",
                "hover",
                "click",
                "key-tap",
                "scroll",
                "drag",
                "wait-stable",
                "compare",
                "learn-transition",
            ],
            GameInteractionOperations.All);
        Assert.Equal(
            ["hover", "click", "key-tap", "scroll", "drag"],
            GameInteractionOperations.InputOperations);
    }

    [Fact]
    public void Target_binding_copies_the_observation_frame_transform_window_and_locator()
    {
        var candidate = new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            "candidate-1",
            "observation-7",
            42,
            3,
            "window:game",
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "ocr-text-region",
                [0.1, 0.2, 0.3, 0.4],
                "locator-9"),
            [new EvidenceRegion(
                ContractSchemaVersions.Revision03,
                "rect",
                [0.1, 0.2, 0.3, 0.4],
                "windows-ocr")],
            0.9,
            [GameInteractionOperations.Click]);

        var binding = GameInteractionTargetBinding.From(candidate);

        Assert.Equal("observation-7", binding.ObservationId);
        Assert.Equal(42, binding.FrameSequence);
        Assert.Equal(3, binding.TransformRevision);
        Assert.Equal("window:game", binding.TargetWindowSourceId);
        Assert.Equal("candidate-1", binding.CandidateId);
        Assert.Equal("locator-9", binding.LocatorRevision);
        Assert.Equal([0.1, 0.2, 0.3, 0.4], binding.NormalizedBounds);
        Assert.NotSame(candidate.Locator.NormalizedBounds, binding.NormalizedBounds);
    }

    [Fact]
    public void Dispatch_and_game_transition_are_independent_axes()
    {
        var dispatch = new GameInteractionDispatchReceipt(
            ContractSchemaVersions.Revision03,
            GameInteractionOperations.Click,
            GameInteractionDispatchStatus.Dispatched,
            "before-1",
            "window:game",
            "NanoSerialHid",
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMilliseconds(10),
            "candidate-1",
            "nano-ack-1",
            null);
        var comparison = new GameTransitionComparison(
            ContractSchemaVersions.Revision03,
            "before-1",
            "after-1",
            GameTransitionJudgement.Stayed,
            [],
            ["意味構造が同一"]);

        Assert.Equal(GameInteractionDispatchStatus.Dispatched, dispatch.Status);
        Assert.Equal(GameTransitionJudgement.Stayed, comparison.Judgement);
    }

    [Fact]
    public void Timeout_is_not_represented_as_stayed()
    {
        var stability = new GameInteractionStabilityResult(
            ContractSchemaVersions.Revision03,
            GameInteractionStabilityStatus.TimedOut,
            [],
            null,
            0,
            0,
            5_000,
            "意味構造が安定しなかった");
        var comparison = new GameTransitionComparison(
            ContractSchemaVersions.Revision03,
            "before-1",
            null,
            GameTransitionJudgement.Undetermined,
            [],
            ["wait timeout"]);

        Assert.Equal(GameInteractionStabilityStatus.TimedOut, stability.Status);
        Assert.Equal(GameTransitionJudgement.Undetermined, comparison.Judgement);
    }
}
