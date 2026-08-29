using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Contracts.Exploration;

/// <summary>Game Operatorの上位機能が必ず通る基本機能名。</summary>
public static class GameInteractionOperations
{
    public const string Observe = "observe";
    public const string DiscoverTargets = "discover-targets";
    public const string Hover = "hover";
    public const string Click = "click";
    public const string KeyTap = "key-tap";
    public const string Scroll = "scroll";
    public const string Drag = "drag";
    public const string WaitStable = "wait-stable";
    public const string Compare = "compare";
    public const string LearnTransition = "learn-transition";

    public static IReadOnlyList<string> All { get; } =
    [
        Observe,
        DiscoverTargets,
        Hover,
        Click,
        KeyTap,
        Scroll,
        Drag,
        WaitStable,
        Compare,
        LearnTransition,
    ];

    public static IReadOnlyList<string> InputOperations { get; } =
    [
        Hover,
        Click,
        KeyTap,
        Scroll,
        Drag,
    ];
}

public enum GameInteractionDispatchStatus
{
    Dispatched,
    DispatchFailed,
}

public enum GameInteractionStabilityStatus
{
    Stable,
    TimedOut,
    Unavailable,
    Fault,
}

public enum GameTransitionJudgement
{
    Moved,
    Stayed,
    Undetermined,
}

/// <summary>入力対象を操作直前のObservationへ固定する。</summary>
public sealed record GameInteractionTargetBinding(
    string SchemaVersion,
    string ObservationId,
    long FrameSequence,
    long TransformRevision,
    string TargetWindowSourceId,
    string CandidateId,
    string LocatorRevision,
    IReadOnlyList<double> NormalizedBounds)
{
    public static GameInteractionTargetBinding From(AffordanceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new GameInteractionTargetBinding(
            ContractSchemaVersions.Revision03,
            candidate.ObservationId,
            candidate.FrameSequence,
            candidate.TransformRevision,
            candidate.TargetWindowSourceId,
            candidate.CandidateId,
            candidate.Locator.LocatorRevision,
            candidate.Locator.NormalizedBounds.ToArray());
    }
}

/// <summary>ACKとゲーム内結果を混同しない、Nano一回送信の結果。</summary>
public sealed record GameInteractionDispatchReceipt(
    string SchemaVersion,
    string Operation,
    GameInteractionDispatchStatus Status,
    string ObservationId,
    string TargetWindowSourceId,
    string Route,
    int DispatchCount,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    string? CandidateId,
    string? TransportReceiptId,
    string? FailureReason);

/// <summary>raw pixel一致ではなく意味構造が連続した結果を保持する。</summary>
public sealed record GameInteractionStabilityResult(
    string SchemaVersion,
    GameInteractionStabilityStatus Status,
    IReadOnlyList<ObservedScene> Observations,
    ObservedScene? StableScene,
    int StableFramesObserved,
    long StableMillisecondsObserved,
    long ElapsedMilliseconds,
    string? FailureReason);

/// <summary>操作前後の意味状態を三値で判定した根拠。</summary>
public sealed record GameTransitionComparison(
    string SchemaVersion,
    string BeforeObservationId,
    string? AfterObservationId,
    GameTransitionJudgement Judgement,
    IReadOnlyList<EvidenceRegion> ChangedRegions,
    IReadOnlyList<string> Reasons);

public sealed record GameInteractionKeyTapRequest(
    string SchemaVersion,
    string ObservationId,
    long FrameSequence,
    long TransformRevision,
    string TargetWindowSourceId,
    IReadOnlyList<string> Keys);

public sealed record GameInteractionScrollRequest(
    string SchemaVersion,
    GameInteractionTargetBinding Target,
    int VerticalSteps,
    int HorizontalSteps);

public sealed record GameInteractionDragRequest(
    string SchemaVersion,
    GameInteractionTargetBinding Start,
    IReadOnlyList<double> DestinationNormalized);

public sealed record GameTransitionLearningRequest(
    string SchemaVersion,
    string ProposalId,
    ObservedScene Before,
    GameInteractionDispatchReceipt Dispatch,
    GameInteractionStabilityResult Stability,
    GameTransitionComparison Comparison,
    string AttemptId,
    string TransitionEvidenceId,
    string EnvironmentScope,
    long DispatchMonotonicMilliseconds,
    long ObservationCompletedMonotonicMilliseconds,
    DateTimeOffset RecordedUtc,
    string? ExplorationRunId = null);

public enum GameTransitionLearningStatus
{
    Learned,
    DispatchFailed,
}

public sealed record GameTransitionLearningResult(
    GameTransitionLearningStatus Status,
    TransitionEvidence? Evidence,
    string Detail);

/// <summary>
/// Capture、AI、Nano、永続化の実装詳細を上位探索loopから分離する製品port。
/// 各入力methodは一回だけdispatchし、自動retryや別route fallbackを行わない。
/// </summary>
public interface IGameObservationRuntime
{
    ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default);

    ValueTask<ObservedScene> DiscoverTargetsAsync(
        ObservationResult observation,
        CancellationToken cancellationToken = default);
}

public interface IGameInteractionRuntime : Playbooks.IDemonstrationObservationRuntime
{

    ValueTask<GameInteractionDispatchReceipt> HoverAsync(
        GameInteractionTargetBinding target,
        CancellationToken cancellationToken = default);

    ValueTask<GameInteractionDispatchReceipt> ClickAsync(
        GameInteractionTargetBinding target,
        CancellationToken cancellationToken = default);

    ValueTask<GameInteractionDispatchReceipt> KeyTapAsync(
        GameInteractionKeyTapRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<GameInteractionDispatchReceipt> ScrollAsync(
        GameInteractionScrollRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<GameInteractionDispatchReceipt> DragAsync(
        GameInteractionDragRequest request,
        CancellationToken cancellationToken = default);

    GameTransitionLearningResult LearnTransition(GameTransitionLearningRequest request);
}
