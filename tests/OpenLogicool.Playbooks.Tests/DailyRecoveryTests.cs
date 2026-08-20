using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class DailyRecoveryTests
{
    [Theory]
    [InlineData(DailyRecoveryTrigger.Interrupted)]
    [InlineData(DailyRecoveryTrigger.ManualIntervention)]
    [InlineData(DailyRecoveryTrigger.ForegroundLost)]
    [InlineData(DailyRecoveryTrigger.CaptureLost)]
    [InlineData(DailyRecoveryTrigger.OutcomeUnknown)]
    public void 中断原因ごとにday2の既知pathを再開候補にする(DailyRecoveryTrigger trigger)
    {
        var cycle = Cycle();

        var plan = DailyRecovery.Plan(cycle, trigger);

        Assert.Equal("day2-session", plan.ResumeSessionId);
        Assert.Equal(["OpenRewards", "SelectReward", "Confirm"], plan.KnownActionPath);
        Assert.Equal(trigger, plan.Trigger);
        Assert.False(cycle.DayOneVerified);
    }

    private static DailyTwoCycleReport Cycle() => DailyTwoCycle.Record(
        new VerifiedEnvScope("gamelab:daily-pilot"),
        new DailyCycleSession("day1-session", "gamelab:daily-pilot", 12, ["OpenRewards", "SelectReward", "Confirm"]),
        new DailyCycleSession("day2-session", "gamelab:daily-pilot", 13, ["OpenRewards", "SelectReward", "Confirm"]));
}
