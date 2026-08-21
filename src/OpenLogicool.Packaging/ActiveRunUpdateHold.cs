namespace OpenLogicool.Packaging;

public enum UpdateStartDisposition
{
    Allowed,
    HeldForActiveRun,
}

public sealed record UpdateStartDecision(UpdateStartDisposition Disposition)
{
    public bool CanStartUpdate => Disposition == UpdateStartDisposition.Allowed;
}

public enum RunResumeCompatibility
{
    Compatible,
    Incompatible,
}

public sealed record RunResumeDecision(
    RunResumeCompatibility Compatibility,
    string PinnedArtifactVersion,
    string InstalledArtifactVersion)
{
    public bool CanResume => Compatibility == RunResumeCompatibility.Compatible;
}

/// <summary>
/// active Run と update の境界を判定する pure contract。
/// Run の active 判定と pin の保持は既存 Playbooks 側の責務であり、ここでは再実装しない。
/// </summary>
public static class ActiveRunUpdateHold
{
    /// <summary>active Run がある間は update を開始させない。</summary>
    public static UpdateStartDecision DecideUpdateStart(bool hasActiveRun) =>
        new(hasActiveRun ? UpdateStartDisposition.HeldForActiveRun : UpdateStartDisposition.Allowed);

    /// <summary>
    /// update 後の resume は Run が pin した artifact version と installed version の ordinal 完全一致だけを許可する。
    /// 異なる version を互換と推測したり、別 version へ自動移行したりしない。
    /// </summary>
    public static RunResumeDecision DecideResume(string pinnedArtifactVersion, string installedArtifactVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pinnedArtifactVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedArtifactVersion);

        var compatibility = string.Equals(
            pinnedArtifactVersion,
            installedArtifactVersion,
            StringComparison.Ordinal)
            ? RunResumeCompatibility.Compatible
            : RunResumeCompatibility.Incompatible;

        return new RunResumeDecision(compatibility, pinnedArtifactVersion, installedArtifactVersion);
    }
}
