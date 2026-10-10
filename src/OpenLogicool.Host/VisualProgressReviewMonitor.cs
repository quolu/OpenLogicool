namespace OpenLogicool.Host;

/// <summary>未確認の操作を繰り返さず、既知の表示への復帰を観測する。</summary>
internal sealed class VisualProgressReviewMonitor
{
    public const long NotificationGraceMs = 60_000;
    public bool IsHolding { get; private set; }
    private string? blockedSignature;
    private string? stableSignature;
    private long stableAt;
    private bool blockedHudVisible;
    private long heldAt;
    private bool notificationTaken;
    private bool blockedInhibited;

    public void Hold(VisualProgressChoice candidate, bool hudVisible = false, long now = 0, bool inhibited = false)
    {
        IsHolding = true;
        blockedSignature = candidate.Signature;
        blockedHudVisible = hudVisible;
        stableSignature = null;
        heldAt = now;
        notificationTaken = false;
        blockedInhibited = inhibited;
    }

    /// <summary>利用者へ直接申請できた表示は、様子見後の担当AIへの通知を出さない。</summary>
    public void MarkNotified() => notificationTaken = true;

    public bool TryTakeNotification(long now)
    {
        if (!IsHolding || notificationTaken || now - heldAt < NotificationGraceMs) return false;
        notificationTaken = true;
        return true;
    }

    public bool TryResume(long now, VisualProgressChoice candidate, bool inhibited, bool hudVisible, bool sceneChanged = false)
    {
        if (!IsHolding) return false;
        if (blockedInhibited && sceneChanged) { IsHolding = false; return true; }
        string? known = inhibited ? blockedInhibited ? null : "停止表示"
            : blockedInhibited ? "停止表示解除" : candidate.Action switch
        {
            VisualProgressAction.Key or VisualProgressAction.Click or VisualProgressAction.Flick or VisualProgressAction.Wait
                when candidate.Signature is not null && candidate.Signature != blockedSignature => candidate.Signature,
            VisualProgressAction.Normal when hudVisible && !blockedHudVisible => "HUD",
            _ => null
        };
        if (known is null) { stableSignature = null; return false; }
        if (stableSignature != known) { stableSignature = known; stableAt = now; return false; }
        if (now - stableAt < 600) return false;
        IsHolding = false;
        return true;
    }
}
