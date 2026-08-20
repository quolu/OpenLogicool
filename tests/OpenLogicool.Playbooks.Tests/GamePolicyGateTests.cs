using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class GamePolicyGateTests
{
    [Theory]
    [InlineData(GamePolicyReviewStatus.Unverified, GamePolicyGateReason.Unverified)]
    [InlineData(GamePolicyReviewStatus.Changed, GamePolicyGateReason.Changed)]
    [InlineData(GamePolicyReviewStatus.InterpretationUnknown, GamePolicyGateReason.InterpretationUnknown)]
    public void 未確認状態はAssistとAutoを技術可否に関わらず無効にする(
        GamePolicyReviewStatus status,
        GamePolicyGateReason expectedReason)
    {
        var record = Record(status, [GameAutomationMode.Observe, GameAutomationMode.Assist, GameAutomationMode.Auto]);

        var observe = GamePolicyGate.Evaluate(record, GameAutomationMode.Observe);
        var assist = GamePolicyGate.Evaluate(record, GameAutomationMode.Assist);
        var auto = GamePolicyGate.Evaluate(record, GameAutomationMode.Auto);

        Assert.True(observe.IsAllowed);
        Assert.False(assist.IsAllowed);
        Assert.Equal(expectedReason, assist.Reason);
        Assert.False(auto.IsAllowed);
        Assert.Equal(expectedReason, auto.Reason);
    }

    [Fact]
    public void 確認済みでもrecordで許可されないmodeは拒否する()
    {
        var importedPlaybookRecord = Record(GamePolicyReviewStatus.Confirmed, [GameAutomationMode.Observe, GameAutomationMode.Assist]);

        var decision = GamePolicyGate.Evaluate(importedPlaybookRecord, GameAutomationMode.Auto);

        Assert.False(decision.IsAllowed);
        Assert.Equal(GamePolicyGateReason.ModeNotAllowed, decision.Reason);
    }

    private static GamePolicyRecord Record(GamePolicyReviewStatus status, IReadOnlyList<GameAutomationMode> modes) => new(
        ContractSchemaVersions.Revision01,
        "gamelab:daily-pilot",
        status,
        modes);
}
