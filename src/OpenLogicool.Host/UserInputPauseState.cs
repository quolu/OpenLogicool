namespace OpenLogicool.Host;

internal sealed record UserInputPauseSnapshot(bool Paused, int HeldCount, long IdleMilliseconds,
    long UserEvents, long NanoEvents, long LostReleases = 0, int[]? HeldCodes = null);

/// <summary>入力元別の押下状態と、全解放後の無入力の時間を管理する。</summary>
internal sealed class UserInputPauseState(Func<long> milliseconds, Func<int, bool>? isDown = null)
{
    /// <summary>入力がこの時間途切れたら、人の操作が終わったとみなす（利用者の指定）。</summary>
    public const int QuietMilliseconds = 5000;

    private long lostReleases;
    private readonly Lock gate = new();
    private readonly HashSet<(nint Source, int Code)> held = [];
    private long lastActivity = milliseconds();
    private long userEvents;
    private long nanoEvents;

    public UserInputPauseSnapshot Snapshot()
    {
        lock (gate)
        {
            var idle = milliseconds() - lastActivity;
            // 離した合図は、昇格した窓や保護された画面が前面の間は届かない。押下状態の正本はOSなので、
            // 入力が途絶えた時にOSへ確かめ、実際には離されているキーを外す。
            if (isDown is not null && held.Count > 0 && idle >= QuietMilliseconds)
                lostReleases += held.RemoveWhere(item => !isDown(item.Code));
            return new(held.Count > 0 || idle < QuietMilliseconds, held.Count, idle, userEvents, nanoEvents, lostReleases,
                held.Select(item => item.Code).Distinct().Order().ToArray());
        }
    }

    public void SeedHeld(int code) { lock (gate) held.Add((0, code)); }
    public void NanoInput() { lock (gate) nanoEvents++; }
    public void Activity() { lock (gate) { userEvents++; lastActivity = milliseconds(); } }
    public void Button(nint source, int code, bool down)
    {
        lock (gate)
        {
            userEvents++; lastActivity = milliseconds();
            if (down) held.Add((source, code));
            else { held.Remove((source, code)); held.Remove((0, code)); }
        }
    }
    public void Removed(nint source)
    {
        lock (gate)
            if (held.RemoveWhere(item => item.Source == source) > 0) lastActivity = milliseconds();
    }
}
