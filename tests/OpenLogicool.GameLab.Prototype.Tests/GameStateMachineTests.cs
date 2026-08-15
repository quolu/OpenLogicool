using OpenLogicool.GameLab.Prototype;
using Xunit;

namespace OpenLogicool.GameLab.Prototype.Tests;

public class GameStateMachineTests
{
    private static readonly string[] FixedOperations =
    {
        "OpenEvent", "ClosePopup", "OpenRewards", "SelectReward", "Confirm",
    };

    private static IReadOnlyList<(string StateId, string Cause)> RunSequence(int seed, IEnumerable<string> operations)
    {
        var machine = new GameStateMachine(seed);
        var trace = machine.History.Select(h => (h.StateId, h.Cause)).ToList();

        foreach (var op in operations)
        {
            machine.TryButton(op);
        }

        return machine.History.Select(h => (h.StateId, h.Cause)).ToList();
    }

    [Fact]
    public void SameSeedAndSameOperations_ProduceIdenticalStateSequence()
    {
        // seed=5 は固定操作列 OpenEvent→ClosePopup→OpenRewards→SelectReward→Confirm で
        // 途中 glitch を挟まずに claim-done まで到達する（scratchpad の全 seed 走査で確認済み）。
        const int seed = 5;

        var runA = RunSequence(seed, FixedOperations);
        var runB = RunSequence(seed, FixedOperations);

        Assert.Equal(runA, runB);
    }

    [Fact]
    public void DifferentSeeds_CanProduceDifferentGlitchAndPopupSequence()
    {
        // seed=1: 初回 popup なしで main-menu から直接 unknown-glitch へ落ちる。
        // seed=2: 初回 popup が発生し、ClosePopup 試行時に unknown-glitch へ落ちる。
        // どちらも scratchpad の全 seed 走査（1..50）で実測済みの固定ペア。
        var runSeed1 = RunSequence(1, FixedOperations);
        var runSeed2 = RunSequence(2, FixedOperations);

        Assert.NotEqual(runSeed1, runSeed2);
    }

    [Fact]
    public void ClaimDone_HasNoReverseTransition()
    {
        // seed=5 で claim-done まで到達させ、以後どのボタン名を渡しても
        // 遷移表に claim-done を From とする entry が存在しないため false になる
        // （state は claim-done のまま変化しない＝逆遷移 API が呼べないことの検証）。
        const int seed = 5;
        var machine = new GameStateMachine(seed);
        foreach (var op in FixedOperations)
        {
            machine.TryButton(op);
        }

        Assert.Equal(GameStateId.ClaimDone, machine.CurrentState);

        var allKnownButtons = new[] { "OpenEvent", "ClosePopup", "OpenRewards", "SelectReward", "Confirm", "Cancel" };
        foreach (var button in allKnownButtons)
        {
            var accepted = machine.TryButton(button);
            Assert.False(accepted);
            Assert.Equal(GameStateId.ClaimDone, machine.CurrentState);
        }
    }
}
