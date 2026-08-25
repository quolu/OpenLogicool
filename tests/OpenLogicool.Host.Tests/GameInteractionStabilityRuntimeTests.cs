using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class GameInteractionStabilityRuntimeTests
{
    [Fact]
    public async Task Waits_for_semantic_stability_instead_of_frame_identity()
    {
        var scenes = new Queue<ObservedScene>([
            Scene("after-1", 2, 0.10),
            Scene("after-2", 3, 0.11),
            Scene("after-3", 4, 0.12),
            Scene("after-4", 5, 0.13),
        ]);
        var clock = new FakeClock();
        var runtime = new GameInteractionStabilityRuntime(
            new ObservationRuntime(scenes),
            clock,
            TimeSpan.FromMilliseconds(100));

        var result = await runtime.WaitStableAsync(
            Scene("before", 1, 0.10),
            new ExplorationWaitCondition(
                ContractSchemaVersions.Revision03,
                3,
                300,
                1_000));

        Assert.Equal(GameInteractionStabilityStatus.Stable, result.Status);
        Assert.Equal(4, result.Observations.Count);
        Assert.Equal(4, result.StableFramesObserved);
        Assert.Equal(300, result.StableMillisecondsObserved);
        Assert.Equal(600, result.ElapsedMilliseconds);
    }

    [Fact]
    public async Task Does_not_sample_the_pre_action_screen_during_the_minimum_stability_window()
    {
        var clock = new FakeClock();
        var scenes = new Queue<ObservedScene>([
            Scene("after-1", 2, 0.10),
            Scene("after-2", 3, 0.11),
            Scene("after-3", 4, 0.12),
            Scene("after-4", 5, 0.13),
        ]);
        var observation = new ObservationRuntime(scenes, observedAt: () => clock.ElapsedMilliseconds);
        var runtime = new GameInteractionStabilityRuntime(
            observation,
            clock,
            TimeSpan.FromMilliseconds(100));

        var result = await runtime.WaitStableAsync(
            Scene("before", 1, 0.10),
            new ExplorationWaitCondition(
                ContractSchemaVersions.Revision03,
                3,
                300,
                1_000));

        Assert.Equal(GameInteractionStabilityStatus.Stable, result.Status);
        Assert.Equal(300, observation.FirstObservedAtMilliseconds);
    }

    [Fact]
    public async Task Timeout_remains_timeout_and_is_not_stayed()
    {
        var scenes = new Queue<ObservedScene>([
            Scene("a", 2, 0.1, "A"),
            Scene("b", 3, 0.7, "B"),
            Scene("c", 4, 0.1, "A"),
        ]);
        var clock = new FakeClock();
        var runtime = new GameInteractionStabilityRuntime(
            new ObservationRuntime(scenes, repeatLast: true),
            clock,
            TimeSpan.FromMilliseconds(100));

        var result = await runtime.WaitStableAsync(
            Scene("before", 1, 0.1),
            new ExplorationWaitCondition(
                ContractSchemaVersions.Revision03,
                10,
                2_000,
                300));

        Assert.Equal(GameInteractionStabilityStatus.TimedOut, result.Status);
        Assert.Null(result.StableScene);
    }

    [Fact]
    public async Task Timeout_cancels_an_in_flight_observation_instead_of_waiting_past_the_contract()
    {
        var runtime = new GameInteractionStabilityRuntime(
            new HangingObservationRuntime(),
            new SystemGameInteractionClock(),
            TimeSpan.FromMilliseconds(10));

        var result = await runtime.WaitStableAsync(
            Scene("before", 1, 0.1),
            new ExplorationWaitCondition(
                ContractSchemaVersions.Revision03,
                2,
                0,
                50));

        Assert.Equal(GameInteractionStabilityStatus.TimedOut, result.Status);
        Assert.Contains("観測処理", result.FailureReason, StringComparison.Ordinal);
    }

    private sealed class FakeClock : IGameInteractionClock
    {
        public long ElapsedMilliseconds { get; private set; }

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            ElapsedMilliseconds += checked((long)delay.TotalMilliseconds);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ObservationRuntime(
        Queue<ObservedScene> scenes,
        bool repeatLast = false,
        Func<long>? observedAt = null) : IGameObservationRuntime
    {
        private ObservedScene? current;
        private ObservedScene? last;

        public long? FirstObservedAtMilliseconds { get; private set; }

        public ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            FirstObservedAtMilliseconds ??= observedAt?.Invoke();
            var selected = scenes.Count > 0
                ? scenes.Dequeue()
                : repeatLast && last is not null
                    ? last
                    : throw new InvalidOperationException("no scene");
            current = selected;
            last = selected;
            return ValueTask.FromResult(new ObservationResult(
                ContractSchemaVersions.Revision03,
                selected.ObservationId,
                selected.Frame,
                selected.CaptureAvailability,
                selected.StateIdentity,
                selected.StateCandidates,
                selected.PerceptionVersion,
                selected.Frame.FreshnessMs,
                null));
        }

        public ValueTask<ObservedScene> DiscoverTargetsAsync(
            ObservationResult observation,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(current!);
    }

    private sealed class HangingObservationRuntime : IGameObservationRuntime
    {
        public async ValueTask<ObservationResult> ObserveAsync(
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public ValueTask<ObservedScene> DiscoverTargetsAsync(
            ObservationResult observation,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("unreachable");
    }

    private static ObservedScene Scene(
        string id,
        long sequence,
        double x,
        string label = "部隊") => new(
        ContractSchemaVersions.Revision03,
        $"scene-{id}",
        id,
        new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            "window:game",
            CaptureBackend.WindowsGraphicsCapture,
            sequence,
            sequence * 100,
            DateTimeOffset.UnixEpoch,
            3,
            10,
            250),
        CaptureAvailability.Available,
        StateIdentityStatus.Novel,
        $"hypothesis:{id}",
        [],
        [new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            $"candidate-{id}",
            id,
            sequence,
            3,
            "window:game",
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "foundry-local-text-region",
                [x, 0.2, 0.1, 0.1],
                $"locator-{id}"),
            [new EvidenceRegion(
                ContractSchemaVersions.Revision03,
                "rect",
                [x, 0.2, 0.1, 0.1],
                "foundry-local")],
            0.5,
            [GameInteractionOperations.Click],
            "text",
            label)],
        "foundry-local-controls");
}
