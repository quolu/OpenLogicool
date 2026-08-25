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

    [Fact]
    public async Task Saved_hover_without_transition_is_not_accepted()
    {
        var store = new ProfileStore(Profile(GameInteractionOperations.Hover, "state-a"));
        var before = Scene("state-a", 1, includeAction: true, GameInteractionOperations.Hover);
        var after = Scene("state-a", 2, includeAction: true, GameInteractionOperations.Hover);
        var device = new RecordingDevice();
        var runtime = new KnownScreenActionRuntime(
            new ObservationRuntime(before),
            new NanoGameInteractionActions(device, new Mapper()),
            new Stability(after),
            new GameTransitionJudge(),
            store,
            "nikke",
            "env",
            new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 60_000),
            DeterministicExplorationCandidateRiskPolicy.SafeMenuDefault,
            gamePolicyAllowsExecute: true);

        var result = await runtime.ExecuteKnownAsync("action-a");

        Assert.Equal(1, device.HoverCount);
        Assert.Equal(0, device.ClickCount);
        Assert.Equal(GameTransitionJudgement.Stayed, result.Comparison.Judgement);
        Assert.False(result.DestinationMatched);
        Assert.Equal(0, result.AiCallCount);
    }

    [Theory]
    [InlineData(GameInteractionOperations.KeyTap)]
    [InlineData(GameInteractionOperations.Scroll)]
    [InlineData(GameInteractionOperations.Drag)]
    public async Task Executes_saved_non_click_operation_once_without_ai(string operation)
    {
        var profile = Profile(operation) with
        {
            States =
            [
                State("state-a", [OperationSignature(operation)]),
                State("state-b", []),
            ],
        };
        var device = new RecordingDevice();
        var runtime = new KnownScreenActionRuntime(
            new ObservationRuntime(Scene("state-a", 1, includeAction: true, operation)),
            new NanoGameInteractionActions(device, new Mapper()),
            new Stability(Scene("state-b", 2, includeAction: false, operation)),
            new GameTransitionJudge(),
            new ProfileStore(profile),
            "nikke",
            "env",
            new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 60_000),
            DeterministicExplorationCandidateRiskPolicy.SafeMenuDefault,
            gamePolicyAllowsExecute: true);

        var result = await runtime.ExecuteKnownAsync("action-a");

        Assert.Equal(0, result.AiCallCount);
        Assert.Equal(GameTransitionJudgement.Moved, result.Comparison.Judgement);
        Assert.True(result.DestinationMatched);
        Assert.Equal(operation == GameInteractionOperations.KeyTap ? 1 : 0, device.KeyTapCount);
        Assert.Equal(operation == GameInteractionOperations.Scroll ? 1 : 0, device.ScrollCount);
        Assert.Equal(operation == GameInteractionOperations.Drag ? 1 : 0, device.DragCount);
    }

    private static LearnedAffordanceSignature OperationSignature(string operation) => new(
        "action-a",
        "locator-a",
        "アリーナ",
        [0.4, 0.4, 0.1, 0.05],
        [operation],
        ["evidence-a"],
        "state-b",
        KeyTokens: operation == GameInteractionOperations.KeyTap ? ["Key:Esc"] : null,
        VerticalScrollSteps: operation == GameInteractionOperations.Scroll ? -3 : null,
        HorizontalScrollSteps: operation == GameInteractionOperations.Scroll ? 0 : null,
        DragDestinationNormalized: operation == GameInteractionOperations.Drag ? [0.7, 0.7] : null);

    private static LearnedSceneProfileDocument Profile(
        string operation = GameInteractionOperations.Click,
        string destinationStateId = "state-b") => new(
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
                [operation],
                ["evidence-a"],
                destinationStateId)]),
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

    private static ObservedScene Scene(
        string stateId,
        long sequence,
        bool includeAction,
        string operation = GameInteractionOperations.Click)
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
            [operation],
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
        public int HoverCount { get; private set; }
        public int KeyTapCount { get; private set; }
        public int ScrollCount { get; private set; }
        public int DragCount { get; private set; }
        public string Hover(SerialHidCursorPoint target) { HoverCount++; return "hover"; }
        public string Click(SerialHidCursorPoint target) { ClickCount++; return "click"; }
        public string KeyTap(IReadOnlyList<string> keys) { KeyTapCount++; return "key-tap"; }
        public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps) { ScrollCount++; return "scroll"; }
        public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination) { DragCount++; return "drag"; }
    }

    private sealed class Mapper : IGameInteractionCoordinateMapper
    {
        public SerialHidCursorPoint MapTargetCenter(GameInteractionTargetBinding target) => new(10, 20);
        public SerialHidCursorPoint MapNormalized(IReadOnlyList<double> normalizedPoint) => new(0, 0);
    }
}
