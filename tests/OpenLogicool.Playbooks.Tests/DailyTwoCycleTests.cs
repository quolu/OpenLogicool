using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class DailyTwoCycleTests
{
    [Fact]
    public void 翌日の別sessionでknown_pathをreplayしても初日はVerifiedにならない()
    {
        var report = DailyTwoCycle.Record(
            new VerifiedEnvScope("gamelab:daily-pilot"),
            Session("day1-session", 12, ["OpenRewards", "SelectReward", "Confirm"]),
            Session("day2-session", 13, ["OpenRewards", "SelectReward", "Confirm"]));

        Assert.False(report.DayOneVerified);
        Assert.Equal("day1-session", report.DayOne.SessionId);
        Assert.Equal("day2-session", report.DayTwo.SessionId);
    }

    [Fact]
    public void 同じsessionまたは異なるpathを二日cycleとして拒否する()
    {
        var scope = new VerifiedEnvScope("gamelab:daily-pilot");
        var dayOne = Session("session-1", 12, ["OpenRewards", "SelectReward", "Confirm"]);

        Assert.Throws<ArgumentException>(() => DailyTwoCycle.Record(
            scope,
            dayOne,
            Session("session-1", 13, ["OpenRewards", "SelectReward", "Confirm"])));
        Assert.Throws<ArgumentException>(() => DailyTwoCycle.Record(
            scope,
            dayOne,
            Session("session-2", 13, ["OpenRewards", "Cancel"])));
    }

    private static DailyCycleSession Session(string sessionId, long virtualDay, IReadOnlyList<string> path) => new(
        sessionId,
        "gamelab:daily-pilot",
        virtualDay,
        path);
}
