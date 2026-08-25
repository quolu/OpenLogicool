using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class KnownGoalActionSelectorTests
{
    [Fact]
    public void Confirmed_similar_saved_button_is_used_without_discovery()
    {
        var selection = KnownGoalActionSelector.Select(
            State(Action("前哨%地", "destination")),
            "前哨基地を開く",
            GameInteractionOperations.Click);

        Assert.Equal(KnownGoalActionSelectionKind.UseKnown, selection.Kind);
        Assert.Equal("action", selection.Action!.CandidateId);
    }

    [Fact]
    public void Missing_saved_button_is_the_first_discovery_condition()
    {
        var selection = KnownGoalActionSelector.Select(
            State(),
            "フレンドを開く",
            GameInteractionOperations.Click);

        Assert.Equal(KnownGoalActionSelectionKind.MissingSavedButton, selection.Kind);
    }

    [Fact]
    public void Unconfirmed_destination_is_the_second_discovery_condition()
    {
        var selection = KnownGoalActionSelector.Select(
            State(Action("フレンド", null)),
            "フレンドを開く",
            GameInteractionOperations.Click);

        Assert.Equal(KnownGoalActionSelectionKind.PreviousTransitionUnconfirmed, selection.Kind);
    }

    private static LearnedStateSceneSignature State(params LearnedAffordanceSignature[] actions) => new(
        "state", "signature",
        [
            new LearnedSceneAnchor("A", [0.1, 0.1, 0.1, 0.1], "e1"),
            new LearnedSceneAnchor("B", [0.8, 0.8, 0.1, 0.1], "e2"),
        ],
        actions,
        ["e1", "e2"]);

    private static LearnedAffordanceSignature Action(string text, string? destination) => new(
        "action", "locator", text, [0.4, 0.4, 0.1, 0.1], [GameInteractionOperations.Click], ["e3"], destination);
}
