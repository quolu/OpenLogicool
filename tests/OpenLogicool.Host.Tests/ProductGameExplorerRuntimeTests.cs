using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Host;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class ProductGameExplorerRuntimeTests
{
    [Fact]
    public async Task One_step_runs_all_foundation_layers_and_learns_once()
    {
        var before = Scene("before-1", 1, "部隊", 0.1);
        var after = Scene("after-1", 2, "設定", 0.7);
        var coordinator = new Coordinator();
        var device = new Device();
        var learning = new Learner();
        var structure = new StructureCommitter();
        var runtime = Runtime(
            new ObservationRuntime([before]),
            new StabilityWaiter(after),
            coordinator,
            device,
            learning,
            structure,
            gamePolicyAllowsExplore: true);

        var result = await runtime.ExecuteNextAsync();

        Assert.Equal(ProductGameExplorerStepStatus.Learned, result.Status);
        Assert.Equal(GameTransitionJudgement.Moved, result.Comparison!.Judgement);
        Assert.Equal(["click"], device.Calls);
        Assert.Equal(1, coordinator.DispatchCalls);
        Assert.Equal(ExplorationAuthorizationSource.OwnerDelegatedAutomation, coordinator.Approval!.AuthorizationSource);
        Assert.NotNull(learning.Request);
        Assert.Equal("nano-click", learning.Request!.Dispatch.TransportReceiptId);
        Assert.Equal(1, structure.Calls);
    }

    [Fact]
    public async Task Same_semantic_target_is_not_probed_twice()
    {
        var before1 = Scene("before-1", 1, "部隊", 0.10);
        var before2 = Scene("before-2", 3, "部隊", 0.12);
        var after = Scene("after-1", 2, "設定", 0.7);
        var coordinator = new Coordinator();
        var device = new Device();
        var runtime = Runtime(
            new ObservationRuntime([before1, before2]),
            new StabilityWaiter(after),
            coordinator,
            device,
            new Learner(),
            new StructureCommitter(),
            gamePolicyAllowsExplore: true);

        var first = await runtime.ExecuteNextAsync();
        var second = await runtime.ExecuteNextAsync();

        Assert.Equal(ProductGameExplorerStepStatus.Learned, first.Status);
        Assert.Equal(ProductGameExplorerStepStatus.NoCandidate, second.Status);
        Assert.Equal(1, coordinator.DispatchCalls);
        Assert.Equal(["click"], device.Calls);
    }

    [Fact]
    public async Task Prohibited_candidate_never_reaches_proposal_or_input()
    {
        var coordinator = new Coordinator();
        var device = new Device();
        var runtime = Runtime(
            new ObservationRuntime([Scene("before", 1, "ダイヤ購入", 0.1)]),
            new StabilityWaiter(Scene("after", 2, "完了", 0.7)),
            coordinator,
            device,
            new Learner(),
            new StructureCommitter(),
            gamePolicyAllowsExplore: true);

        var result = await runtime.ExecuteNextAsync();

        Assert.Equal(ProductGameExplorerStepStatus.NoCandidate, result.Status);
        Assert.Equal(0, coordinator.ProposeCalls);
        Assert.Empty(device.Calls);
    }

    [Fact]
    public async Task Game_policy_denial_stops_before_input()
    {
        var coordinator = new Coordinator();
        var device = new Device();
        var runtime = Runtime(
            new ObservationRuntime([Scene("before", 1, "部隊", 0.1)]),
            new StabilityWaiter(Scene("after", 2, "設定", 0.7)),
            coordinator,
            device,
            new Learner(),
            new StructureCommitter(),
            gamePolicyAllowsExplore: false);

        var result = await runtime.ExecuteNextAsync();

        Assert.Equal(ProductGameExplorerStepStatus.AdmissionStopped, result.Status);
        Assert.Empty(device.Calls);
    }

    [Fact]
    public async Task Nano_failure_is_not_retried_and_does_not_start_after_observation()
    {
        var coordinator = new Coordinator();
        var device = new Device { Failure = new InvalidOperationException("nano fault") };
        var stability = new StabilityWaiter(Scene("after", 2, "設定", 0.7));
        var runtime = Runtime(
            new ObservationRuntime([Scene("before", 1, "部隊", 0.1)]),
            stability,
            coordinator,
            device,
            new Learner(),
            new StructureCommitter(),
            gamePolicyAllowsExplore: true);

        var result = await runtime.ExecuteNextAsync();

        Assert.Equal(ProductGameExplorerStepStatus.DispatchFailed, result.Status);
        Assert.Equal(["click"], device.Calls);
        Assert.Equal(0, stability.Calls);
    }

    [Theory]
    [InlineData(GameInteractionOperations.KeyTap)]
    [InlineData(GameInteractionOperations.Scroll)]
    [InlineData(GameInteractionOperations.Drag)]
    public async Task Product_step_dispatches_non_click_operation_and_learns_transition(string operation)
    {
        var before = Scene("before", 1, "一覧", 0.1, operation);
        var after = Scene("after", 2, "移動後", 0.7, operation);
        var device = new Device();
        var runtime = Runtime(
            new ObservationRuntime([before]),
            new StabilityWaiter(after),
            new Coordinator(),
            device,
            new Learner(),
            new StructureCommitter(),
            gamePolicyAllowsExplore: true,
            operation: operation,
            keyTokens: operation == GameInteractionOperations.KeyTap ? ["Key:Esc"] : null,
            verticalSteps: operation == GameInteractionOperations.Scroll ? -3 : null,
            dragDestination: operation == GameInteractionOperations.Drag ? [0.7, 0.7] : null);

        var result = await runtime.ExecuteNextAsync();

        Assert.Equal(ProductGameExplorerStepStatus.Learned, result.Status);
        Assert.Equal(GameTransitionJudgement.Moved, result.Comparison!.Judgement);
        Assert.Equal([operation], device.Calls);
        Assert.Equal(operation, result.Dispatch!.Operation);
    }

    private static ProductGameExplorerRuntime Runtime(
        IGameObservationRuntime observation,
        IGameInteractionStabilityWaiter stability,
        Coordinator coordinator,
        Device device,
        Learner learning,
        StructureCommitter structure,
        bool gamePolicyAllowsExplore,
        string operation = GameInteractionOperations.Click,
        IReadOnlyList<string>? keyTokens = null,
        int? verticalSteps = null,
        IReadOnlyList<double>? dragDestination = null)
    {
        var actions = new NanoGameInteractionActions(device, new Mapper());
        return new ProductGameExplorerRuntime(
            "nikke",
            observation,
            actions,
            stability,
            new GameTransitionJudge(),
            learning,
            structure,
            coordinator,
            DeterministicExplorationCandidateRiskPolicy.SafeMenuDefault,
            Policy(),
            gamePolicyAllowsExplore,
            TimeProvider.System,
            interactionOperation: operation,
            interactionKeyTokens: keyTokens,
            interactionVerticalScrollSteps: verticalSteps,
            interactionHorizontalScrollSteps: operation == GameInteractionOperations.Scroll ? 0 : null,
            interactionDragDestination: dragDestination);
    }

    private static ExplorationPolicy Policy() => new(
        ContractSchemaVersions.Revision03,
        "policy-1",
        "nikke.exe",
        "window:game",
        "nikke:test",
        "safe-menu",
        GameInteractionOperations.InputOperations,
        ["purchase", "paid-resource", "rare-resource", "gacha", "delete", "account-change"],
        new ExplorationBudget(ContractSchemaVersions.Revision03, 10, 60_000, 60_000),
        true,
        "owner-consent-1",
        "known-menu-or-escape",
        new ExplorationStopPolicy(ContractSchemaVersions.Revision03, 1_000, 1, 2, 2),
        ["budget-exhausted"]);

    private sealed class ObservationRuntime(Queue<ObservedScene> scenes) : IGameObservationRuntime
    {
        public ObservationRuntime(IEnumerable<ObservedScene> scenes) : this(new Queue<ObservedScene>(scenes)) { }
        private ObservedScene? current;

        public ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            current = scenes.Dequeue();
            return ValueTask.FromResult(new ObservationResult(
                ContractSchemaVersions.Revision03,
                current.ObservationId,
                current.Frame,
                current.CaptureAvailability,
                current.StateIdentity,
                current.StateCandidates,
                current.PerceptionVersion,
                current.Frame.FreshnessMs,
                null));
        }

        public ValueTask<ObservedScene> DiscoverTargetsAsync(
            ObservationResult observation,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(current!);
    }

    private sealed class StabilityWaiter(ObservedScene after) : IGameInteractionStabilityWaiter
    {
        public int Calls { get; private set; }

        public ValueTask<GameInteractionStabilityResult> WaitStableAsync(
            ObservedScene before,
            ExplorationWaitCondition condition,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(new GameInteractionStabilityResult(
                ContractSchemaVersions.Revision03,
                GameInteractionStabilityStatus.Stable,
                [after],
                after,
                3,
                300,
                300,
                null));
        }
    }

    private sealed class Coordinator : IProductExplorationCoordinator
    {
        public string CurrentStructureRevisionId { get; private set; } = "structure:root";
        public int RemainingProbes { get; private set; } = 10;
        public ExplorationStopReason StopReason => ExplorationStopReason.None;
        public int ProposeCalls { get; private set; }
        public int DispatchCalls { get; private set; }
        public ExplorationApproval? Approval { get; private set; }
        private ExplorationProposalAdmission? admission;

        public void CommitObservation(ObservedScene scene, DateTimeOffset persistedUtc) =>
            CurrentStructureRevisionId = $"structure:{scene.ObservationId}";

        public ExplorationAdmissionDecision Propose(
            ExplorationProposalAdmission value,
            DateTimeOffset persistedUtc)
        {
            ProposeCalls++;
            admission = value;
            return value.GamePolicyAllowsExplore
                ? new ExplorationAdmissionDecision(
                    ExplorationAdmissionStatus.NeedsApproval,
                    ExplorationStopReason.ApprovalRequired,
                    "owner approval required",
                    false)
                : new ExplorationAdmissionDecision(
                    ExplorationAdmissionStatus.Rejected,
                    ExplorationStopReason.GamePolicyDisabled,
                    "policy denied",
                    false);
        }

        public ExplorationAdmissionDecision Approve(
            ExplorationApproval approval,
            DateTimeOffset persistedUtc)
        {
            Approval = approval;
            return new ExplorationAdmissionDecision(
                ExplorationAdmissionStatus.Allowed,
                ExplorationStopReason.None,
                "allowed",
                true);
        }

        public void Dispatch(string proposalId, Action externalInput, DateTimeOffset persistedUtc)
        {
            DispatchCalls++;
            externalInput();
            RemainingProbes--;
        }

        public string GetActiveAttemptId(string proposalId)
        {
            Assert.Equal(admission!.Proposal.ProposalId, proposalId);
            return "attempt-1";
        }
    }

    private sealed class Learner : IGameTransitionLearner
    {
        public GameTransitionLearningRequest? Request { get; private set; }

        public GameTransitionLearningResult Learn(GameTransitionLearningRequest request)
        {
            Request = request;
            var evidence = new TransitionEvidence(
                ContractSchemaVersions.Revision03,
                request.TransitionEvidenceId,
                request.Before.ObservationId,
                request.Stability.StableScene!.ObservationId,
                request.AttemptId,
                request.Dispatch.CandidateId!,
                request.Dispatch.Operation,
                ExplorationOutcomeKind.Novel,
                request.EnvironmentScope,
                request.DispatchMonotonicMilliseconds,
                request.ObservationCompletedMonotonicMilliseconds,
                request.RecordedUtc,
                request.ExplorationRunId,
                request.Dispatch,
                request.Comparison,
                request.Stability.Observations.Select(scene => scene.ObservationId).ToArray());
            return new GameTransitionLearningResult(GameTransitionLearningStatus.Learned, evidence, "learned");
        }
    }

    private sealed class StructureCommitter : IGameInteractionStructureCommitter
    {
        public int Calls { get; private set; }

        public GameStructureRevision Commit(
            ObservedScene before,
            ObservedScene after,
            TransitionEvidence evidence,
            ExplorationWaitCondition waitCondition,
            IReadOnlyList<string> riskTags,
            bool reversible,
            DateTimeOffset recordedUtc)
        {
            Calls++;
            return null!;
        }
    }

    private sealed class Device : INanoGameInputDevice
    {
        public List<string> Calls { get; } = [];
        public Exception? Failure { get; set; }
        public string Hover(SerialHidCursorPoint target) => Record("hover");
        public string Click(SerialHidCursorPoint target) => Record("click");
        public string KeyTap(IReadOnlyList<string> keys) => Record("key-tap");
        public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps) => Record("scroll");
        public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination) => Record("drag");
        private string Record(string operation)
        {
            Calls.Add(operation);
            if (Failure is not null) throw Failure;
            return $"nano-{operation}";
        }
    }

    private sealed class Mapper : IGameInteractionCoordinateMapper
    {
        public SerialHidCursorPoint MapTargetCenter(GameInteractionTargetBinding target) => new(100, 200);
        public SerialHidCursorPoint MapNormalized(IReadOnlyList<double> normalizedPoint) => new(300, 400);
    }

    private static ObservedScene Scene(
        string id,
        long sequence,
        string label,
        double x,
        string operation = GameInteractionOperations.Click) => new(
        ContractSchemaVersions.Revision03,
        $"scene-{id}",
        id,
        new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            "window:game",
            CaptureBackend.WindowsGraphicsCapture,
            sequence,
            sequence * 1_000,
            DateTimeOffset.UnixEpoch,
            1,
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
            1,
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
            [operation],
            "text",
            label)],
        "foundry-local-controls");
}
