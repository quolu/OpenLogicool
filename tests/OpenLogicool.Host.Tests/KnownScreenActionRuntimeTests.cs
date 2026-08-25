using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Host;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class KnownScreenActionRuntimeTests
{
    [Fact]
    public async Task Executes_saved_action_once_without_ai_and_verifies_saved_destination()
    {
        var profile = Profile();
        var store = new ProfileStore(profile);
        var before = Scene("state-a", 1, includeAction: true);
        var after = Scene("state-b", 2, includeAction: false);
        var observation = new ObservationRuntime(before);
        var device = new RecordingDevice();
        var actions = new NanoGameInteractionActions(device, new Mapper());
        var stability = new Stability(after);
        var runtime = new KnownScreenActionRuntime(
            observation,
            actions,
            stability,
            new GameTransitionJudge(),
            store,
            "nikke",
            "env",
            new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 60_000),
            DeterministicExplorationCandidateRiskPolicy.SafeMenuDefault,
            gamePolicyAllowsExecute: true);

        var result = await runtime.ExecuteKnownAsync("action-a");

        Assert.Equal(0, result.AiCallCount);
        Assert.Equal(GameInteractionDispatchStatus.Dispatched, result.Dispatch.Status);
        Assert.Equal(1, device.ClickCount);
        Assert.Equal("state-b", result.ObservedDestinationStateId);
        Assert.True(result.DestinationMatched);
    }

    private static LearnedSceneProfileDocument Profile() => new(
        ContractSchemaVersions.Revision03,
        "profile-1",
        "known-screen-index-v1",
        "nikke",
        "env",
        "nikke",
        null,
        1_000,
        0.04,
        [
            State("state-a", [new LearnedAffordanceSignature(
                "action-a",
                "locator-a",
                "アリーナ",
                [0.4, 0.4, 0.1, 0.05],
                [GameInteractionOperations.Click],
                ["evidence-a"],
                "state-b")]),
            State("state-b", []),
        ],
        ["evidence-a", "evidence-b"]);

    private static LearnedStateSceneSignature State(
        string stateId,
        IReadOnlyList<LearnedAffordanceSignature> affordances) => new(
            stateId,
            "known-screen-index-v1",
            [
                new LearnedSceneAnchor($"{stateId}-anchor-1", [0.2, 0.2, 0.1, 0.04], $"evidence-{stateId}"),
                new LearnedSceneAnchor($"{stateId}-anchor-2", [0.6, 0.6, 0.1, 0.04], $"evidence-{stateId}"),
            ],
            affordances,
            [$"evidence-{stateId}"]);

    private static ObservedScene Scene(string stateId, long sequence, bool includeAction)
    {
        var observationId = $"observation-{sequence}";
        var frame = new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            "window:nikke",
            CaptureBackend.WindowsGraphicsCapture,
            sequence,
            sequence * 100,
            DateTimeOffset.UnixEpoch,
            1,
            0,
            0);
        var action = new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            "action-a",
            observationId,
            sequence,
            1,
            "window:nikke",
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "ocr-normalized-rect",
                [0.4, 0.4, 0.1, 0.05],
                "locator-a"),
            [new EvidenceRegion(ContractSchemaVersions.Revision03, "rect", [0.4, 0.4, 0.1, 0.05], "ocr")],
            1,
            [GameInteractionOperations.Click],
            "text",
            "アリーナ");
        return new ObservedScene(
            ContractSchemaVersions.Revision03,
            $"scene-{sequence}",
            observationId,
            frame,
            CaptureAvailability.Available,
            StateIdentityStatus.Known,
            stateId,
            [new StateCandidate(
                ContractSchemaVersions.Revision03,
                stateId,
                1,
                [new EvidenceRegion(ContractSchemaVersions.Revision03, "rect", [0.2, 0.2, 0.1, 0.04], "ocr")])],
            includeAction ? [action] : [],
            "known-screen-index-v1");
    }

    private sealed class ObservationRuntime(ObservedScene scene) : IGameObservationRuntime
    {
        public ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ObservationResult(
                ContractSchemaVersions.Revision03,
                scene.ObservationId,
                scene.Frame,
                scene.CaptureAvailability,
                scene.StateIdentity,
                scene.StateCandidates,
                scene.PerceptionVersion,
                scene.Frame.FreshnessMs,
                null));

        public ValueTask<ObservedScene> DiscoverTargetsAsync(
            ObservationResult observation,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(scene);
    }

    private sealed class Stability(ObservedScene after) : IGameInteractionStabilityWaiter
    {
        public ValueTask<GameInteractionStabilityResult> WaitStableAsync(
            ObservedScene before,
            ExplorationWaitCondition condition,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GameInteractionStabilityResult(
                ContractSchemaVersions.Revision03,
                GameInteractionStabilityStatus.Stable,
                [after],
                after,
                2,
                1_000,
                1_000,
                null));
    }

    private sealed class ProfileStore(LearnedSceneProfileDocument profile) : ILearnedSceneProfileStore
    {
        public void Upsert(LearnedSceneProfileDocument document) => throw new NotSupportedException();
        public LearnedSceneProfileDocument? Load(string gameId, string environmentScope) => profile;
    }

    private sealed class RecordingDevice : INanoGameInputDevice
    {
        public int ClickCount { get; private set; }
        public string Hover(SerialHidCursorPoint target) => throw new NotSupportedException();
        public string Click(SerialHidCursorPoint target) { ClickCount++; return "click"; }
        public string KeyTap(IReadOnlyList<string> keys) => throw new NotSupportedException();
        public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps) => throw new NotSupportedException();
        public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination) => throw new NotSupportedException();
    }

    private sealed class Mapper : IGameInteractionCoordinateMapper
    {
        public SerialHidCursorPoint MapTargetCenter(GameInteractionTargetBinding target) => new(10, 20);
        public SerialHidCursorPoint MapNormalized(IReadOnlyList<double> normalizedPoint) => new(0, 0);
    }
}
