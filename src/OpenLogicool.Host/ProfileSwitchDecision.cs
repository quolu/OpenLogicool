namespace OpenLogicool.Host;

/// <summary>
/// 単一 device 種別に対する profile 切替の解決結果（診断可能化・APP-005）。
/// 判定規則の実装は AppProfileResolver.ResolveWithReason に委ね、ここでは二重実装しない。
/// </summary>
public sealed record ProfileSwitchKindOutcome(
    string DeviceKind,
    string MatchKind,           // "package" / "path" / "default" / "identity-unavailable"
    string SelectedProfileId,
    string? PreviousProfileId,
    bool Changed);

/// <summary>
/// foreground poll 1 tick 分の profile 切替判断（pure・診断可能化・APP-005）。
/// 実際の記録要否（ring への保持）は呼び出し側（ResidentInputHost／ProfileSwitchDecisionRing）が決める。
/// </summary>
public sealed record ProfileSwitchDecision(
    long Sequence,
    string? ObservedFullPath,
    string? ObservedPackageFamilyName,
    int? ObservedProcessId,
    DateTime? ObservedProcessStartTimeUtc,
    IReadOnlyList<ProfileSwitchKindOutcome> Outcomes,
    bool ProcessGenerationChanged)
{
    /// <summary>いずれかの device 種別で実際に選択 profile が変わったか。</summary>
    public bool Changed => Outcomes.Any(outcome => outcome.Changed);
}
