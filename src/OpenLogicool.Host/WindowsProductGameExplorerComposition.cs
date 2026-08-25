using OpenLogicool.AI;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Exploration;
using OpenLogicool.Input;
using OpenLogicool.Perception;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

public sealed class ZeroSeedFrameStateRecognizer : IFrameRecognizer
{
    public RecognitionResult Recognize(OpenLogicool.Contracts.Capture.CapturedFrame frame) =>
        new("zero-seed-state-v1", false, []);
}

public sealed class WindowsProductGameExplorerSession(
    ProductGameExplorerRuntime runtime,
    WindowsWgcGameFrameSource frameSource,
    FoundryLocalVisionClient visionClient) : IDisposable
{
    public ProductGameExplorerRuntime Runtime { get; } = runtime;

    public void Dispose()
    {
        frameSource.Dispose();
        visionClient.Dispose();
    }
}

/// <summary>Windows／Foundry／Nano／Durable Attempt／Structure Storeの正規composition。</summary>
public static class WindowsProductGameExplorerComposition
{
    public static WindowsProductGameExplorerSession Create(
        string gameId,
        nint window,
        IGameStructureStore structureStore,
        RunJournal runJournal,
        AttemptDispatchGate attemptGate,
        ExplorationPolicy explorationPolicy,
        GamePolicyRecord gamePolicy,
        IFrameRecognizer frameRecognizer,
        string frameEvidenceDirectory,
        Uri foundryEndpoint,
        string foundryModelId,
        SerialHidProtocolSession nanoSession,
        SerialHidEmitter nanoEmitter,
        Func<GameCaptureScreenBounds> captureScreenBounds,
        ILearnedSceneProfileStore? learnedSceneProfileStore = null,
        string? targetIntent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        if (!string.Equals(gameId, gamePolicy.GameId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Game Policyと探索gameが一致しません。", nameof(gamePolicy));
        }
        var gamePolicyDecision = GamePolicyGate.Evaluate(gamePolicy, GameAutomationMode.Explore);
        var ids = new GuidExplorationIdSource();
        var coordinator = new ExplorationCoordinator(
            structureStore,
            runJournal,
            attemptGate,
            new ExplorationRunBinding(
                OpenLogicool.Contracts.Shared.ContractSchemaVersions.Revision03,
                $"exploration:{Guid.NewGuid():N}",
                gameId,
                explorationPolicy.EnvironmentScope,
                "product-game-explorer",
                "product-game-explorer-v1",
                1),
            explorationPolicy,
            ids);
        var frameSource = new WindowsWgcGameFrameSource(
            window,
            explorationPolicy.TargetWindowSourceId,
            TimeSpan.FromSeconds(10));
        var visionClient = new FoundryLocalVisionClient(
            foundryEndpoint,
            foundryModelId,
            TimeSpan.FromSeconds(30));
        var labelProvider = new FoundryLocalDiscoveryVisionProvider(visionClient);
        var targetDiscovery = new FoundryLabelTargetDiscoveryAdapter(
            labelProvider,
            new WindowsGameOcrRecognizer(),
            new WindowsGameFramePngEncoder(),
            () => coordinator.CurrentStructureRevisionId,
            targetIntent);
        var observationRuntime = new ProductGameObservationRuntime(
            frameSource,
            new LiveObservationSource(frameRecognizer),
            targetDiscovery,
            new LocalPngGameFrameEvidenceSink(
                frameEvidenceDirectory,
                new WindowsGameFramePngEncoder()));
        var nanoDevice = new SerialHidNanoGameInputDevice(
            nanoSession,
            nanoEmitter,
            new WindowsSerialHidCursorOracle());
        var actions = new NanoGameInteractionActions(
            nanoDevice,
            new WindowsGameInteractionCoordinateMapper(captureScreenBounds));
        var stability = new GameInteractionStabilityRuntime(
            observationRuntime,
            new SystemGameInteractionClock(),
            TimeSpan.FromMilliseconds(100));
        var stableIds = new InMemoryStableStructureIdRegistry();
        var knowledge = new StructureKnowledgeController(structureStore, stableIds, ids);
        var structureLearner = new GameInteractionStructureLearner(
            structureStore,
            knowledge,
            stableIds,
            ids,
            coordinator,
            gameId,
            explorationPolicy.EnvironmentScope);
        var knownScreenIndex = learnedSceneProfileStore is null
            ? null
            : new IncrementalKnownScreenIndex(
                learnedSceneProfileStore,
                gameId,
                explorationPolicy.EnvironmentScope,
                gameId);
        var runtime = new ProductGameExplorerRuntime(
            gameId,
            observationRuntime,
            actions,
            stability,
            new GameTransitionJudge(),
            new GameTransitionLearningController(new ExplorationCoordinatorOutcomeRecorder(coordinator)),
            structureLearner,
            new ProductExplorationCoordinatorAdapter(coordinator),
            new WindowsGameExplorationCandidateRiskPolicy(
                DeterministicExplorationCandidateRiskPolicy.SafeMenuDefault),
            explorationPolicy,
            gamePolicyDecision.IsAllowed,
            interactionWaitCondition: new ExplorationWaitCondition(
                OpenLogicool.Contracts.Shared.ContractSchemaVersions.Revision03,
                2,
                1_000,
                60_000),
            knownScreenIndex: knownScreenIndex);
        return new WindowsProductGameExplorerSession(runtime, frameSource, visionClient);
    }
}
