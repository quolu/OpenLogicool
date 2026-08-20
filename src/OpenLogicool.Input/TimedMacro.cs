using OpenLogicool.Domain;

namespace OpenLogicool.Input;

/// <summary>R5 の advanced macro が繰返しを継続する条件。</summary>
public enum TimedMacroMode
{
    RepeatWhileHeld,
    Toggle,
    FiniteRepeat,
}

/// <summary>scheduler が観測できる advanced macro の明示状態。</summary>
public enum TimedMacroState
{
    Idle,
    Waiting,
    Running,
    Stopped,
}

/// <summary>一回の macro action。outputs は一回の原子的な tap として下流へ渡し、保持しない。</summary>
public sealed record TimedMacroEmission(IReadOnlyList<string> Outputs);

/// <summary>advanced macro の定義。既存の有限 <c>Tap:</c> sequence はこの型へ移さない。</summary>
public sealed class TimedMacroDefinition
{
    public TimedMacroDefinition(
        IReadOnlyList<string> outputs,
        int delayMs,
        int intervalMs,
        TimedMacroMode mode,
        int repeatCount = 0)
    {
        if (outputs.Count == 0)
        {
            throw new ArgumentException("timed macro の outputs は空にできません。", nameof(outputs));
        }

        if (delayMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayMs));
        }

        if (intervalMs < 10)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalMs), "interval は 10 ms 以上でなければなりません。");
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (mode == TimedMacroMode.FiniteRepeat ? repeatCount <= 0 : repeatCount != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(repeatCount));
        }

        foreach (var output in outputs)
        {
            if (OutputTokens.IsSequenceStep(output))
            {
                throw new ArgumentException("既存の有限 Tap sequence を timed macro へ混在させられません。", nameof(outputs));
            }

            OutputTokens.Parse(output);
        }

        Outputs = Array.AsReadOnly(outputs.ToArray());
        DelayMs = delayMs;
        IntervalMs = intervalMs;
        Mode = mode;
        RepeatCount = repeatCount;
    }

    public IReadOnlyList<string> Outputs { get; }

    public int DelayMs { get; }

    public int IntervalMs { get; }

    public TimedMacroMode Mode { get; }

    /// <summary>FiniteRepeat だけで正数。それ以外は 0。</summary>
    public int RepeatCount { get; }
}

/// <summary>profile の control/layer に付加する advanced macro 定義。</summary>
public sealed record TimedMacroBinding(string ControlId, string LayerId, TimedMacroDefinition Definition);

/// <summary>
/// advanced macro の pure state machine。timer、thread、SendInput を持たない。
/// emission は各回で完結する action であり、停止後に保持 output を残さない。
/// </summary>
public sealed class TimedMacro
{
    private readonly TimedMacroDefinition _definition;
    private long _lastClockMs;
    private long _nextEmissionAtMs;
    private int _remainingRepeats;

    public TimedMacro(TimedMacroDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public TimedMacroState State { get; private set; } = TimedMacroState.Idle;

    /// <summary>physical down または toggle 操作で開始する。toggle は active 中の再操作で停止する。</summary>
    public void Activate(long monotonicMs)
    {
        ObserveClock(monotonicMs);
        if (State == TimedMacroState.Stopped)
        {
            throw new InvalidOperationException("停止済み macro は Resume 後にだけ開始できます。");
        }

        if (_definition.Mode == TimedMacroMode.Toggle && IsActive)
        {
            State = TimedMacroState.Idle;
            return;
        }

        if (IsActive)
        {
            throw new InvalidOperationException("active な macro を重ねて開始できません。");
        }

        _remainingRepeats = _definition.RepeatCount;
        _nextEmissionAtMs = checked(monotonicMs + _definition.DelayMs);
        State = _definition.DelayMs == 0 ? TimedMacroState.Running : TimedMacroState.Waiting;
    }

    /// <summary>physical up。RepeatWhileHeld だけを停止し、他 mode の進行を暗黙に変えない。</summary>
    public void Release(long monotonicMs)
    {
        ObserveClock(monotonicMs);
        if (_definition.Mode == TimedMacroMode.RepeatWhileHeld && IsActive)
        {
            State = TimedMacroState.Idle;
        }
    }

    /// <summary>期限に達した一回分だけを返す。遅延後に backlog を連射しない。</summary>
    public IReadOnlyList<TimedMacroEmission> AdvanceTo(long monotonicMs)
    {
        ObserveClock(monotonicMs);
        if (!IsActive || monotonicMs < _nextEmissionAtMs)
        {
            return [];
        }

        var emission = new TimedMacroEmission(_definition.Outputs);
        if (_definition.Mode == TimedMacroMode.FiniteRepeat && --_remainingRepeats == 0)
        {
            State = TimedMacroState.Idle;
            return [emission];
        }

        _nextEmissionAtMs = checked(monotonicMs + _definition.IntervalMs);
        State = TimedMacroState.Running;
        return [emission];
    }

    /// <summary>
    /// 停止境界。以後 AdvanceTo は action を返さない。emission は保持 output を作らないため release は不要。
    /// </summary>
    public void Stop()
    {
        State = TimedMacroState.Stopped;
    }

    /// <summary>停止後に新しい physical down を受け付ける。過去の toggle／repeat 状態は復元しない。</summary>
    public void Resume()
    {
        if (State == TimedMacroState.Stopped)
        {
            State = TimedMacroState.Idle;
        }
    }

    /// <summary>
    /// profile 適用前の文法 gate。timed macro binding と通常 output binding の同一 cell 混在を拒否する。
    /// </summary>
    public static void ValidateForProfileApplication(MappingProfile profile, IEnumerable<TimedMacroBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(bindings);

        var cells = new HashSet<(string ControlId, string LayerId)>();
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            ArgumentNullException.ThrowIfNull(binding.Definition);

            if (!profile.LayerIds.Contains(binding.LayerId))
            {
                throw new ArgumentException($"timed macro の layer '{binding.LayerId}' が profile に存在しません。", nameof(bindings));
            }

            var cell = (binding.ControlId, binding.LayerId);
            if (!cells.Add(cell))
            {
                throw new ArgumentException($"timed macro binding ({binding.ControlId}, {binding.LayerId}) が重複しています。", nameof(bindings));
            }

            if (profile.TryResolve(binding.ControlId, binding.LayerId, out _))
            {
                throw new ArgumentException(
                    $"binding ({binding.ControlId}, {binding.LayerId}) で timed macro と通常 output token を混在させられません。",
                    nameof(bindings));
            }
        }
    }

    private bool IsActive => State is TimedMacroState.Waiting or TimedMacroState.Running;

    private void ObserveClock(long monotonicMs)
    {
        if (monotonicMs < _lastClockMs)
        {
            throw new ArgumentOutOfRangeException(nameof(monotonicMs), "monotonic clock を巻き戻せません。");
        }

        _lastClockMs = monotonicMs;
    }
}
