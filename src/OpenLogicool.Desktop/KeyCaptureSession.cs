using System.Diagnostics;
using System.Windows.Input;

namespace OpenLogicool.Desktop;

/// <summary>
/// キー録画modalのpure状態。最初のchordを離した時点で候補を固定し、
/// それより後に発生した実機押下だけを確定信号として受理する。
/// </summary>
public sealed class KeyCaptureSession
{
    private readonly List<Key> recordedKeys = [];
    private readonly HashSet<Key> currentlyHeld = [];

    public IReadOnlyList<Key> RecordedKeys => recordedKeys;

    public string? CandidateToken => recordedKeys.Count == 0
        ? null
        : KeyCaptureTokenizer.ToChordText(recordedKeys);

    public bool IsReady => CandidateToken is not null && currentlyHeld.Count == 0 && ReadyAtMonotonicMs is not null;

    public double? ReadyAtMonotonicMs { get; private set; }

    public void KeyDown(Key key)
    {
        if (IsReady)
        {
            return;
        }

        if (currentlyHeld.Add(key) && !recordedKeys.Contains(key))
        {
            recordedKeys.Add(key);
        }
    }

    public void KeyUp(Key key)
    {
        currentlyHeld.Remove(key);
        if (currentlyHeld.Count == 0 && recordedKeys.Count > 0)
        {
            ReadyAtMonotonicMs = MonotonicMilliseconds();
        }
    }

    public bool CanCommitFromDevicePress(double inputMonotonicMs) =>
        IsReady && ReadyAtMonotonicMs <= inputMonotonicMs;

    public void Reset()
    {
        recordedKeys.Clear();
        currentlyHeld.Clear();
        ReadyAtMonotonicMs = null;
    }

    private static double MonotonicMilliseconds() =>
        Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
}
