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
    FoundryLocalVisionClient visionClient,
    ILocalAiCallCounter aiCallCounter) : IDisposable
{
    public ProductGameExplorerRuntime Runtime { get; } = runtime;
    public int AiCallCount => aiCallCounter.AiCallCount;

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
        string? targetIntent = null,
        bool includeVisualTargets = false,
        string interactionOperation = GameInteractionOperations.Click,
        IReadOnlyList<string>? interactionKeyTokens = null,
        int? interactionVerticalScrollSteps = null,
        int? interactionHorizontalScrollSteps = null,
        IReadOnlyList<double>? interactionDragDestination = null,
        IReadOnlyList<double>? visualSearchRegion = null,
        ExplorationWaitCondition? interactionWaitCondition = null,
        bool allowAiDiscovery = true,
        bool learnNonMovedRouteOutcomes = true)
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
        IProductGameTargetDiscovery targetDiscovery = includeVisualTargets
            ? new FoundryControlTargetDiscoveryAdapter(
                new FoundryLocalControlDiscoveryProvider(visionClient),
                new WindowsGameOcrRecognizer(),
                new WindowsGameFramePngEncoder(),
                () => coordinator.CurrentStructureRevisionId,
                targetIntent,
                interactionOperation,
                visualSearchRegion)
            : new FoundryLabelTargetDiscoveryAdapter(
                new FoundryLocalDiscoveryVisionProvider(visionClient),
                new WindowsGameOcrRecognizer(),
                new WindowsGameFramePngEncoder(),
                () => coordinator.CurrentStructureRevisionId,
                targetIntent,
                interactionOperation);
        if (learnedSceneProfileStore is not null)
        {
            targetDiscovery = new WindowsKnownFirstTargetDiscovery(
                targetDiscovery,
                new WindowsGameOcrRecognizer(),
                learnedSceneProfileStore,
                gameId,
                explorationPolicy.EnvironmentScope,
                targetIntent,
                interactionOperation,
                allowAiDiscovery);
        }
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
                UnclassifiedExplorationCandidateRiskPolicy.Default),
            explorationPolicy,
            gamePolicyDecision.IsAllowed,
            interactionWaitCondition: interactionWaitCondition ?? new ExplorationWaitCondition(
                OpenLogicool.Contracts.Shared.ContractSchemaVersions.Revision03,
                2,
                1_000,
                10_000),
            knownScreenIndex: knownScreenIndex,
            interactionOperation: interactionOperation,
            interactionKeyTokens: interactionKeyTokens,
            interactionVerticalScrollSteps: interactionVerticalScrollSteps,
            interactionHorizontalScrollSteps: interactionHorizontalScrollSteps,
            interactionDragDestination: interactionDragDestination,
            learnNonMovedRouteOutcomes: learnNonMovedRouteOutcomes);
        return new WindowsProductGameExplorerSession(
            runtime,
            frameSource,
            visionClient,
            (ILocalAiCallCounter)targetDiscovery);
    }
}
