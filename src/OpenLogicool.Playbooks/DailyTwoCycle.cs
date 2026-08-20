namespace OpenLogicool.Playbooks;

/// <summary>GameLab の一日分の成功経路を記録した、session 固有の結果。</summary>
public sealed record DailyCycleSession(
    string SessionId,
    string EnvironmentId,
    long VirtualDay,
    IReadOnlyList<string> SuccessfulActionPath);

/// <summary>初日の成功と、翌日相当の別 session における known path replay の記録。</summary>
public sealed record DailyTwoCycleReport(
    DailyCycleSession DayOne,
    DailyCycleSession DayTwo)
{
    /// <summary>初日の成功は replay 実証前に Verified へ昇格させない。</summary>
    public bool DayOneVerified => false;
}

public static class DailyTwoCycle
{
    public static DailyTwoCycleReport Record(
        VerifiedEnvScope scope,
        DailyCycleSession dayOne,
        DailyCycleSession dayTwo)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(dayOne);
        ArgumentNullException.ThrowIfNull(dayTwo);
        RequireSession(dayOne, nameof(dayOne));
        RequireSession(dayTwo, nameof(dayTwo));
        if (!scope.AppliesTo(dayOne.EnvironmentId) || !scope.AppliesTo(dayTwo.EnvironmentId))
        {
            throw new ArgumentException("daily cycle は同じ verified environment で記録する必要があります。", nameof(scope));
        }

        if (string.Equals(dayOne.SessionId, dayTwo.SessionId, StringComparison.Ordinal)
            || dayTwo.VirtualDay != dayOne.VirtualDay + 1)
        {
            throw new ArgumentException("day2 は翌 virtual day の別 session である必要があります。", nameof(dayTwo));
        }

        if (!dayOne.SuccessfulActionPath.SequenceEqual(dayTwo.SuccessfulActionPath, StringComparer.Ordinal))
        {
            throw new ArgumentException("day2 は day1 の known path を replay する必要があります。", nameof(dayTwo));
        }

        return new DailyTwoCycleReport(dayOne, dayTwo);
    }

    private static void RequireSession(DailyCycleSession session, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.EnvironmentId);
        ArgumentNullException.ThrowIfNull(session.SuccessfulActionPath);
        if (session.SuccessfulActionPath.Count == 0 || session.SuccessfulActionPath.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("daily session は空でない成功 action path を持つ必要があります。", parameterName);
        }
    }
}
