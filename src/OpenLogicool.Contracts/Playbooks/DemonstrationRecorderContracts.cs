using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Contracts.Playbooks;

/// <summary>操作デモ記録器のlifecycle。</summary>
public enum DemonstrationRecorderStatus
{
    Idle,
    Recording,
    Paused,
    Stopped,
}

/// <summary>OS層／device層が観測した、意味付け前の生入力edge。</summary>
public enum DemonstrationInputEdgeKind
{
    PointerDown,
    PointerUp,
    KeyDown,
    KeyUp,
    Wheel,
}

/// <summary>
/// OSが与えるdesktop絶対座標。記録器がclient frameへ正規化するまでの一時的な運搬にだけ使い、
/// 原本（<see cref="DemonstrationFrameBinding"/>）には正規化後の値だけが入る。
/// </summary>
public sealed record DemonstrationScreenPoint(int X, int Y);

/// <summary>
/// 記録器が受け取る入力edge。mouse／keyboardはWindows環境別adapterが、
/// G13／G600は既存device adapterのedgeがここへ入る。
/// </summary>
public sealed record DemonstrationInputEdge(
    string SchemaVersion,
    DemonstrationInputSource Source,
    DemonstrationInputEdgeKind Kind,
    string ControlId,
    string OutputToken,
    double MonotonicMs,
    DateTimeOffset OccurredUtc,
    DemonstrationScreenPoint? ScreenPoint = null,
    int WheelVerticalSteps = 0,
    int WheelHorizontalSteps = 0);

/// <summary>
/// 生入力edgeの受け口。実装は必ず非blockingで、例外を投げない。
/// OSのlow-level hook procedureと fast path worker から直接呼ばれるため、
/// ここで待つとOSの入力配送と device→emitter の経路を止める。
/// </summary>
public interface IDemonstrationInputSink
{
    void Observe(DemonstrationInputEdge edge);
}

/// <summary>
/// mouse／keyboardのOS取得を所有する環境別adapter。共通側はこのlifecycleだけを知る。
/// </summary>
public interface IDemonstrationInputCollector : IDisposable
{
    void Start(IDemonstrationInputSink sink);

    void Stop();
}

/// <summary>
/// 記録中の観測面。入力dispatchのmethodを持たないので、記録器は構造上、入力を出せない。
/// </summary>
public interface IDemonstrationObservationRuntime : IGameObservationRuntime
{
    ValueTask<GameInteractionStabilityResult> WaitStableAsync(
        ObservedScene before,
        ExplorationWaitCondition condition,
        CancellationToken cancellationToken = default);

    GameTransitionComparison Compare(
        ObservedScene before,
        GameInteractionStabilityResult after);
}
