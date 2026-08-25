using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;

namespace OpenLogicool.Host;

public interface IGameInteractionClock
{
    long ElapsedMilliseconds { get; }

    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemGameInteractionClock : IGameInteractionClock
{
    private readonly System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

    public long ElapsedMilliseconds => stopwatch.ElapsedMilliseconds;

    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
}

public interface IGameInteractionStabilityWaiter
{
    ValueTask<GameInteractionStabilityResult> WaitStableAsync(
        ObservedScene before,
        ExplorationWaitCondition condition,
        CancellationToken cancellationToken = default);
}

public sealed class GameInteractionStabilityRuntime(
    IGameObservationRuntime observationRuntime,
    IGameInteractionClock clock,
    TimeSpan sampleInterval) : IGameInteractionStabilityWaiter
{
    public async ValueTask<GameInteractionStabilityResult> WaitStableAsync(
        ObservedScene before,
        ExplorationWaitCondition condition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(condition);
        if (condition.StableFrames <= 0
            || condition.MinimumStableMilliseconds < 0
            || condition.TimeoutMilliseconds <= 0
            || sampleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("stable frame、minimum、timeout、sample intervalが不正です。");
        }
        var started = clock.ElapsedMilliseconds;
        var window = new GameSceneStabilityWindow(condition);
        var observations = new List<ObservedScene>();
        ObservedScene? lastStable = null;
        var lastStableFrames = 0;
        long lastStableMilliseconds = 0;
        if (condition.MinimumStableMilliseconds > 0)
        {
            await clock.DelayAsync(
                TimeSpan.FromMilliseconds(condition.MinimumStableMilliseconds),
                cancellationToken).ConfigureAwait(false);
        }
        while (clock.ElapsedMilliseconds - started < condition.TimeoutMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObservationResult observation;
            ObservedScene scene;
            var remainingMilliseconds = condition.TimeoutMilliseconds
                - (clock.ElapsedMilliseconds - started);
            if (remainingMilliseconds <= 0)
            {
                break;
            }
            using var observationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            observationTimeout.CancelAfter(TimeSpan.FromMilliseconds(remainingMilliseconds));
            try
            {
                observation = await observationRuntime
                    .ObserveAsync(observationTimeout.Token)
                    .ConfigureAwait(false);
                scene = await observationRuntime
                    .DiscoverTargetsAsync(observation, observationTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return lastStable is not null
                    ? Result(
                        GameInteractionStabilityStatus.Stable,
                        observations,
                        lastStable,
                        window,
                        clock.ElapsedMilliseconds - started,
                        null,
                        lastStableFrames,
                        lastStableMilliseconds)
                    : Result(
                        GameInteractionStabilityStatus.TimedOut,
                        observations,
                        null,
                        window,
                        clock.ElapsedMilliseconds - started,
                        "観測処理がtimeoutを超えました。");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Result(
                    GameInteractionStabilityStatus.Fault,
                    observations,
                    null,
                    window,
                    clock.ElapsedMilliseconds - started,
                    exception.Message);
            }
            observations.Add(scene);
            if (!string.Equals(scene.Frame.SourceId, before.Frame.SourceId, StringComparison.Ordinal)
                || scene.Frame.Backend != before.Frame.Backend
                || scene.Frame.TransformRevision != before.Frame.TransformRevision)
            {
                return Result(
                    GameInteractionStabilityStatus.Fault,
                    observations,
                    null,
                    window,
                    clock.ElapsedMilliseconds - started,
                    "capture binding changed");
            }
            var elapsed = clock.ElapsedMilliseconds - started;
            var signature = GameSceneSemanticComparer.Signature(scene);
            if (lastStable is not null
                && (scene.CaptureAvailability != CaptureAvailability.Available
                    || !signature.HasEvidence
                    || !GameSceneSemanticComparer.StableEquivalent(
                        GameSceneSemanticComparer.Signature(lastStable),
                        signature)))
            {
                lastStable = null;
                lastStableFrames = 0;
                lastStableMilliseconds = 0;
            }
            if (window.Observe(scene, elapsed))
            {
                lastStable = scene;
                lastStableFrames = window.StableFramesObserved;
                lastStableMilliseconds = window.StableMillisecondsObserved(elapsed);
            }
            await clock.DelayAsync(sampleInterval, cancellationToken).ConfigureAwait(false);
        }
        return lastStable is not null
            ? Result(
                GameInteractionStabilityStatus.Stable,
                observations,
                lastStable,
                window,
                clock.ElapsedMilliseconds - started,
                null,
                lastStableFrames,
                lastStableMilliseconds)
            : Result(
                GameInteractionStabilityStatus.TimedOut,
                observations,
                null,
                window,
                clock.ElapsedMilliseconds - started,
                "意味構造がtimeout内に安定しませんでした。");
    }

    private static GameInteractionStabilityResult Result(
        GameInteractionStabilityStatus status,
        IReadOnlyList<ObservedScene> observations,
        ObservedScene? stable,
        GameSceneStabilityWindow window,
        long elapsed,
        string? failure,
        int? stableFrames = null,
        long? stableMilliseconds = null) =>
        new(
            ContractSchemaVersions.Revision03,
            status,
            observations.ToArray(),
            stable,
            stableFrames ?? window.StableFramesObserved,
            stableMilliseconds ?? window.StableMillisecondsObserved(elapsed),
            elapsed,
            failure);
}
