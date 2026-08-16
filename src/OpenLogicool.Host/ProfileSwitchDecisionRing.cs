namespace OpenLogicool.Host;

/// <summary>
/// profile 切替判断の有界 ring（drop-oldest・診断可能化・APP-005）。
/// 200ms ごとに同一状態を積むとノイズで診断にならないため、
/// 「実際に切替が起きた／process 世代交代があった／一致種別が変わった」のいずれかを満たす
/// decision だけを記録する。lock は追加時の最小範囲に留める。
/// </summary>
public sealed class ProfileSwitchDecisionRing
{
    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly List<ProfileSwitchDecision> _entries = new();
    private readonly Dictionary<string, string> _lastMatchKindByKind = new(StringComparer.Ordinal);

    public ProfileSwitchDecisionRing(int capacity = 128)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "capacity は正である必要があります。");
        }

        _capacity = capacity;
    }

    /// <summary>decision を評価し、記録対象なら ring へ追加する。記録したら true を返す。</summary>
    public bool Record(ProfileSwitchDecision decision)
    {
        lock (_lock)
        {
            var matchKindChanged = false;
            foreach (var outcome in decision.Outcomes)
            {
                if (!_lastMatchKindByKind.TryGetValue(outcome.DeviceKind, out var previousMatchKind) ||
                    previousMatchKind != outcome.MatchKind)
                {
                    matchKindChanged = true;
                }

                _lastMatchKindByKind[outcome.DeviceKind] = outcome.MatchKind;
            }

            if (!decision.Changed && !decision.ProcessGenerationChanged && !matchKindChanged)
            {
                return false;
            }

            _entries.Add(decision);
            if (_entries.Count > _capacity)
            {
                _entries.RemoveAt(0);
            }

            return true;
        }
    }

    /// <summary>現在保持している decision のスナップショット（記録順）。</summary>
    public IReadOnlyList<ProfileSwitchDecision> Snapshot()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }
}
