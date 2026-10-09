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

    public void Hold(VisualProgressChoice candidate, bool hudVisible = false, long now = 0)
    {
        IsHolding = true;
        blockedSignature = candidate.Signature;
        blockedHudVisible = hudVisible;
        stableSignature = null;
        heldAt = now;
        notificationTaken = false;
    }

    public bool TryTakeNotification(long now)
    {
        if (!IsHolding || notificationTaken || now - heldAt < NotificationGraceMs) return false;
        notificationTaken = true;
        return true;
    }

    public bool TryResume(long now, VisualProgressChoice candidate, bool inhibited, bool hudVisible)
    {
        if (!IsHolding) return false;
        string? known = inhibited ? "停止表示" : candidate.Action switch
        {
            VisualProgressAction.Key or VisualProgressAction.Click or VisualProgressAction.Wait
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
