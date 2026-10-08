namespace OpenLogicool.Host;

internal sealed record UserInputPauseSnapshot(bool Paused, int HeldCount, long IdleMilliseconds,
    long UserEvents, long NanoEvents);

/// <summary>入力元別の押下状態と、全解放後3秒の無入力を管理する。</summary>
internal sealed class UserInputPauseState(Func<long> milliseconds)
{
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
            return new(held.Count > 0 || idle < 3000, held.Count, idle, userEvents, nanoEvents);
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
