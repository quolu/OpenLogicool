namespace OpenLogicool.Domain;

public enum RunControlPhase
{
    Running,
    Paused,
    ManualIntervention,
    Abandoned,
}

/// <summary>
/// Run 制御の pure 状態機（PB-007/013・§6.8）。
/// - 一手実行（step）は Paused かつ再観察待ちでない時だけ許す。
/// - manual intervention 中は進行操作を一切受けない。終了後は新しい Observation が
///   記録されるまで進行（resume／step／version switch）を拒否する（§6.8）。
/// - version 切替は Paused かつ「今の停止位置で観測を取り直した後」だけ許す（§6.8）。
/// - Abandoned は終端で、以後の操作は存在しない。
/// </summary>
public sealed class RunControlState
{
    private RunControlState(RunControlPhase phase, bool needsReobservation, bool observedInCurrentHold)
    {
        Phase = phase;
        NeedsReobservation = needsReobservation;
        ObservedInCurrentHold = observedInCurrentHold;
    }

    public static RunControlState Start() => new(RunControlPhase.Running, needsReobservation: false, observedInCurrentHold: false);

    public RunControlPhase Phase { get; }

    /// <summary>manual intervention 終了後、新しい Observation が来るまで true（§6.8）。</summary>
    public bool NeedsReobservation { get; }

    /// <summary>現在の Paused 停止位置に入ってから Observation が記録されたか（version 切替の再照合条件）。</summary>
    public bool ObservedInCurrentHold { get; }

    /// <summary>連続実行（Running）で次の dispatch を出してよいか。</summary>
    public bool CanDispatch => Phase == RunControlPhase.Running;

    /// <summary>一手実行してよいか（PB-007: Paused でだけ step する）。</summary>
    public bool CanStep => Phase == RunControlPhase.Paused && !NeedsReobservation;

    /// <summary>version 切替してよいか（§6.8: Paused かつ現在 state 再照合後だけ）。</summary>
    public bool CanSwitchVersion => Phase == RunControlPhase.Paused && !NeedsReobservation && ObservedInCurrentHold;

    public RunControlState Pause() => Phase switch
    {
        RunControlPhase.Running => new(RunControlPhase.Paused, NeedsReobservation, observedInCurrentHold: false),
        _ => throw Invalid(nameof(Pause)),
    };

    public RunControlState Resume() => Phase switch
    {
        RunControlPhase.Paused when NeedsReobservation =>
            throw new InvalidOperationException("manual intervention 後は新しい Observation が記録されるまで再開できません（§6.8）。"),
        RunControlPhase.Paused => new(RunControlPhase.Running, needsReobservation: false, observedInCurrentHold: false),
        _ => throw Invalid(nameof(Resume)),
    };

    public RunControlState BeginManualIntervention() => Phase switch
    {
        RunControlPhase.Running or RunControlPhase.Paused =>
            new(RunControlPhase.ManualIntervention, needsReobservation: false, observedInCurrentHold: false),
        _ => throw Invalid(nameof(BeginManualIntervention)),
    };

    public RunControlState EndManualIntervention() => Phase switch
    {
        // 終了先は Paused。自動で Running へ戻らず、再観察→明示 resume だけが進行を再開する（PB-013 の自動合流なし）。
        RunControlPhase.ManualIntervention => new(RunControlPhase.Paused, needsReobservation: true, observedInCurrentHold: false),
        _ => throw Invalid(nameof(EndManualIntervention)),
    };

    /// <summary>
    /// Observation が journal へ記録されたことを反映する。Paused では再照合済みになる。
    /// manual intervention 中は拒否する——journal 上「介入開始と終了の間に observation event は
    /// 現れない」ことが再開照合（PB-009・t10）の前提であり、介入中の観測は終了後の新しい
    /// Observation ではない（§6.8）。
    /// </summary>
    public RunControlState ObservationRecorded() => Phase switch
    {
        RunControlPhase.Paused => new(RunControlPhase.Paused, needsReobservation: false, observedInCurrentHold: true),
        RunControlPhase.Running => this,
        _ => throw Invalid(nameof(ObservationRecorded)),
    };

    public RunControlState Abandon() => Phase switch
    {
        RunControlPhase.Abandoned => throw Invalid(nameof(Abandon)),
        _ => new(RunControlPhase.Abandoned, needsReobservation: false, observedInCurrentHold: false),
    };

    private InvalidOperationException Invalid(string operation) =>
        new($"Run 制御の {Phase} で {operation} は実行できません（PB-007）。");
}
