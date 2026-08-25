using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class GamePolicyGateTests
{
    [Theory]
    [InlineData(GamePolicyReviewStatus.Unverified)]
    [InlineData(GamePolicyReviewStatus.Changed)]
    [InlineData(GamePolicyReviewStatus.InterpretationUnknown)]
    public void review表示状態は明示された許可modeを無効化しない(GamePolicyReviewStatus status)
    {
        var record = Record(status, [GameAutomationMode.Observe, GameAutomationMode.Assist, GameAutomationMode.Explore, GameAutomationMode.Auto]);

        var observe = GamePolicyGate.Evaluate(record, GameAutomationMode.Observe);
        var assist = GamePolicyGate.Evaluate(record, GameAutomationMode.Assist);
        var explore = GamePolicyGate.Evaluate(record, GameAutomationMode.Explore);
        var auto = GamePolicyGate.Evaluate(record, GameAutomationMode.Auto);

        Assert.True(observe.IsAllowed);
        Assert.True(assist.IsAllowed);
        Assert.True(explore.IsAllowed);
        Assert.True(auto.IsAllowed);
    }

    [Fact]
    public void 確認済みでもrecordで許可されないmodeは拒否する()
    {
        var importedPlaybookRecord = Record(GamePolicyReviewStatus.Confirmed, [GameAutomationMode.Observe, GameAutomationMode.Assist]);

        var decision = GamePolicyGate.Evaluate(importedPlaybookRecord, GameAutomationMode.Auto);

        Assert.False(decision.IsAllowed);
        Assert.Equal(GamePolicyGateReason.ModeNotAllowed, decision.Reason);
    }

    [Fact]
    public void 旧schemaへExploreを混ぜたrecordは拒否する()
    {
        var record = new GamePolicyRecord(
            ContractSchemaVersions.Revision01,
            "gamelab:legacy",
            GamePolicyReviewStatus.Confirmed,
            [GameAutomationMode.Observe, GameAutomationMode.Explore]);

        Assert.Throws<ArgumentException>(() => GamePolicyGate.Evaluate(record, GameAutomationMode.Explore));
    }

    private static GamePolicyRecord Record(GamePolicyReviewStatus status, IReadOnlyList<GameAutomationMode> modes) => new(
        ContractSchemaVersions.Revision02,
        "gamelab:daily-pilot",
        status,
        modes);
}
