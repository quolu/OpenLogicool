namespace OpenLogicool.Domain;

/// <summary>
/// 一回の Run（PB-002）。開始時の Playbook version を pin し、差し替え API を持たない。
/// </summary>
public sealed class PlaybookRun
{
    private PlaybookRun(string playbookId, PlaybookGraph pinnedVersion)
    {
        PlaybookId = playbookId;
        PinnedVersion = pinnedVersion;
    }

    public static PlaybookRun Start(string playbookId, PlaybookGraph version)
    {
        if (string.IsNullOrWhiteSpace(playbookId))
        {
            throw new ArgumentException("PlaybookId が空です。", nameof(playbookId));
        }

        ArgumentNullException.ThrowIfNull(version);
        return new PlaybookRun(playbookId, version);
    }

    public string PlaybookId { get; }

    public PlaybookGraph PinnedVersion { get; }

    public string PinnedVersionId => PinnedVersion.VersionId;
}
