namespace OpenLogicool.Contracts.Playbooks;

/// <summary>録画済み、または録画中の操作デモ原本1件の要約。</summary>
public sealed record DemonstrationSessionSummary(
    string SessionId,
    string Goal,
    string GameId,
    string EnvironmentScope,
    DemonstrationSessionState State,
    int OperationCount,
    DateTimeOffset StartedUtc)
{
    public string StateLabel => State switch
    {
        DemonstrationSessionState.Recording => "記録中",
        DemonstrationSessionState.Stopped => "記録済み",
        _ => State.ToString(),
    };

    public string DisplayLabel => $"{Goal}　{OperationCount} 操作　[{StateLabel}]　{StartedUtc.ToLocalTime():yyyy/MM/dd HH:mm}";
}

/// <summary>記録済み原本の1操作を、内部idを出さずに表す。</summary>
public sealed record DemonstrationStepSummary(
    int StepNumber,
    string OperationLabel,
    string TransitionLabel)
{
    public string DisplayLabel => $"{StepNumber}　{OperationLabel}　→　{TransitionLabel}";
}

/// <summary>記録器の現在状態。原本本文ではなく、UI表示に必要な要約だけを持つ。</summary>
public sealed record DemonstrationRecordingStatus(
    DemonstrationRecorderStatus Status,
    string? SessionId,
    int HeldPressCount,
    long IgnoredWhilePaused,
    long IgnoredOutsideClientFrame,
    long UnpairedReleases,
    long DiscardedHeldPresses);

/// <summary>
/// 記録1回分のlive wiring。実device取得・実観測は環境別実装（Windows等）が持ち、
/// この境界より上は入出力の実体を知らない。
/// </summary>
public sealed record DemonstrationLiveSession(
    string TargetApplicationPath,
    string TargetWindowSourceId,
    string EnvironmentScope,
    IDemonstrationObservationRuntime Runtime,
    IDemonstrationInputCollector Collector,
    Func<DemonstrationScreenPoint, IReadOnlyList<double>?> Normalize,
    IDisposable? Resource = null) : IDisposable
{
    public void Dispose()
    {
        Collector.Dispose();
        Resource?.Dispose();
    }
}

/// <summary>対象processを実windowへ解決し、live記録sessionを1回分だけ作る境界。実装は環境別に持つ。</summary>
public interface IDemonstrationLiveSessionFactory
{
    DemonstrationLiveSession Create(string targetProcessName);
}

/// <summary>
/// 記録の開始／停止／状態／session一覧と、記録から作るmacroをまとめたHost境界。
/// 対象gameの選択・macro保存先・排他gateはPhase 13のmacro automation intentsと共有し、
/// 別の実行coordinatorを作らない。
/// </summary>
public interface IDemonstrationRecordingIntents
{
    Task<DemonstrationSessionSummary> StartAsync(string goal, CancellationToken cancellationToken = default);

    Task<DemonstrationSessionSummary> StopAsync(CancellationToken cancellationToken = default);

    DemonstrationRecordingStatus Status();

    IReadOnlyList<DemonstrationSessionSummary> ListSessions();

    IReadOnlyList<DemonstrationStepSummary> ListSteps(string sessionId);

    MacroCatalogItem CreateMacroFromSession(string sessionId);
}
