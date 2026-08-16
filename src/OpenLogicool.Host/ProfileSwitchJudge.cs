using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// foreground identity の変化に対する profile 切替判断を構造化する pure function（APP-005）。
/// 切替規則そのもの（package→path→既定）は AppProfileResolver.ResolveWithReason が正であり、
/// ここでは判定を二重実装せず、その結果を ProfileSwitchDecision へ組み立てるだけ。
/// </summary>
public static class ProfileSwitchJudge
{
    /// <summary>
    /// previousProfileByKind に含まれる device 種別ごとに resolver で再解決し、1 tick 分の判断を返す。
    /// previousProfileByKind のキー集合が対象 device 種別を決める（呼び出し側が instancesByKind 相当を渡す）。
    /// </summary>
    public static ProfileSwitchDecision Decide(
        long sequence,
        ForegroundApplicationIdentity? previousIdentity,
        IReadOnlyDictionary<string, string> previousProfileByKind,
        ForegroundApplicationIdentity? identity,
        AppProfileResolver resolver)
    {
        var outcomes = new List<ProfileSwitchKindOutcome>(previousProfileByKind.Count);
        foreach (var (deviceKind, previousProfileId) in previousProfileByKind)
        {
            var (document, matchKind) = resolver.ResolveWithReason(deviceKind, identity);
            // instancesByKind は resolver.DefaultByKind に profile がある種別だけを含む前提
            // （ResidentInputHost.Start の配線条件と同じ）ので document は必ず非 null。
            var selectedProfileId = document?.ProfileId
                ?? throw new InvalidOperationException(
                    $"device 種別 '{deviceKind}' に解決可能な profile がありません（既定関連付けの欠落）。");

            outcomes.Add(new ProfileSwitchKindOutcome(
                deviceKind, matchKind, selectedProfileId, previousProfileId, selectedProfileId != previousProfileId));
        }

        return new ProfileSwitchDecision(
            sequence,
            identity?.NormalizedFullPath,
            identity?.PackageFamilyName,
            identity?.ProcessId,
            identity?.ProcessStartTimeUtc,
            outcomes,
            DetectProcessGenerationChange(previousIdentity, identity));
    }

    /// <summary>
    /// 世代交代判定: 同一 path かつ (pid 差 または 開始時刻差)。
    /// 開始時刻が null 同士なら差が生じないため、自然に pid だけの判定になる。
    /// </summary>
    private static bool DetectProcessGenerationChange(
        ForegroundApplicationIdentity? previous, ForegroundApplicationIdentity? current)
    {
        if (previous is null || current is null)
        {
            return false;
        }

        if (previous.NormalizedFullPath is null || current.NormalizedFullPath is null)
        {
            return false;
        }

        if (previous.NormalizedFullPath != current.NormalizedFullPath)
        {
            return false;
        }

        return previous.ProcessId != current.ProcessId || previous.ProcessStartTimeUtc != current.ProcessStartTimeUtc;
    }
}
