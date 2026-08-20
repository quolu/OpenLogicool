namespace OpenLogicool.Playbooks;

public enum DailyRecoveryTrigger
{
    Interrupted,
    ManualIntervention,
    ForegroundLost,
    CaptureLost,
    OutcomeUnknown,
}

/// <summary>既存 resume/fault 境界へ渡す、daily cycle の既知 path 再開候補。</summary>
public sealed record DailyRecoveryPlan(
    string ResumeSessionId,
    IReadOnlyList<string> KnownActionPath,
    DailyRecoveryTrigger Trigger);

public static class DailyRecovery
{
    public static DailyRecoveryPlan Plan(DailyTwoCycleReport cycle, DailyRecoveryTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(nameof(trigger));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(cycle.DayTwo.SessionId);
        ArgumentNullException.ThrowIfNull(cycle.DayTwo.SuccessfulActionPath);
        if (cycle.DayTwo.SuccessfulActionPath.Count == 0)
        {
            throw new ArgumentException("recovery には day2 の known action path が必要です。", nameof(cycle));
        }

        return new DailyRecoveryPlan(cycle.DayTwo.SessionId, cycle.DayTwo.SuccessfulActionPath, trigger);
    }
}
