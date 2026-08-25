using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;

namespace OpenLogicool.Host;

public sealed record ExplorationCandidateRiskDecision(
    ExplorationRiskLevel Level,
    IReadOnlyList<string> RiskTags,
    bool Reversible,
    bool SideEffectFree,
    IReadOnlyList<string> RecoveryEdgeIds,
    string Detail);

public interface IExplorationCandidateRiskPolicy
{
    ExplorationCandidateRiskDecision Evaluate(AffordanceCandidate candidate);
}

/// <summary>
/// 候補文字やOCR推測へ操作拒否の権限を与えない既定policy。
/// 禁止事項は候補認識から推測せず、ExplorationPolicyの明示tagだけをcontrollerが評価する。
/// </summary>
public sealed class UnclassifiedExplorationCandidateRiskPolicy : IExplorationCandidateRiskPolicy
{
    public static UnclassifiedExplorationCandidateRiskPolicy Default { get; } = new();

    public ExplorationCandidateRiskDecision Evaluate(AffordanceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new ExplorationCandidateRiskDecision(
            ExplorationRiskLevel.Unknown,
            [],
            false,
            false,
            [],
            "OCR／AI候補からriskを推測しません。明示Game Policyだけを適用します。");
    }
}

public interface IProductExplorationCoordinator
{
    string CurrentStructureRevisionId { get; }
    int RemainingProbes { get; }
    ExplorationStopReason StopReason { get; }

    void CommitObservation(ObservedScene scene, DateTimeOffset persistedUtc);
    ExplorationAdmissionDecision Propose(ExplorationProposalAdmission admission, DateTimeOffset persistedUtc);
    void Dispatch(string proposalId, Action externalInput, DateTimeOffset persistedUtc);
    string GetActiveAttemptId(string proposalId);
}

public sealed class ProductExplorationCoordinatorAdapter(ExplorationCoordinator coordinator)
    : IProductExplorationCoordinator
{
    public string CurrentStructureRevisionId => coordinator.CurrentStructureRevisionId;
    public int RemainingProbes => coordinator.RemainingProbes;
    public ExplorationStopReason StopReason => coordinator.StopReason;
    public void CommitObservation(ObservedScene scene, DateTimeOffset persistedUtc) =>
        coordinator.CommitObservation(scene, persistedUtc);
    public ExplorationAdmissionDecision Propose(ExplorationProposalAdmission admission, DateTimeOffset persistedUtc) =>
        coordinator.Propose(admission, persistedUtc);
    public void Dispatch(string proposalId, Action externalInput, DateTimeOffset persistedUtc) =>
        coordinator.Dispatch(proposalId, externalInput, persistedUtc);
    public string GetActiveAttemptId(string proposalId) => coordinator.GetActiveAttemptId(proposalId);
}

public enum ProductGameExplorerStepStatus
{
    Learned,
    NoCandidate,
    AdmissionStopped,
    DispatchFailed,
    ObservationUndetermined,
    Paused,
    Abandoned,
}

public sealed record ProductGameExplorerStepResult(
    ProductGameExplorerStepStatus Status,
    ObservedScene? Before,
    AffordanceCandidate? Target,
    GameInteractionDispatchReceipt? Dispatch,
    GameInteractionStabilityResult? Stability,
    GameTransitionComparison? Comparison,
    GameTransitionLearningResult? Learning,
    string StructureRevisionId,
    string Detail);

/// <summary>基本10機能を一つのzero-seed探索stepへ合成する製品runtime。</summary>
public sealed class ProductGameExplorerRuntime : IHostExplorerRuntimeControl, IGameInteractionRuntime
{
    private readonly IGameObservationRuntime observation;
    private readonly NanoGameInteractionActions actions;
    private readonly IGameInteractionStabilityWaiter stability;
    private readonly GameTransitionJudge judge;
    private readonly IGameTransitionLearner learning;
    private readonly IGameInteractionStructureCommitter structure;
    private readonly IProductExplorationCoordinator coordinator;
    private readonly IExplorationCandidateRiskPolicy riskPolicy;
    private readonly IIncrementalKnownScreenIndex? knownScreenIndex;
    private readonly ExplorationPolicy policy;
    private readonly bool gamePolicyAllowsExplore;
    private readonly ExplorationWaitCondition interactionWaitCondition;
    private readonly string interactionOperation;
    private readonly IReadOnlyList<string>? interactionKeyTokens;
    private readonly int? interactionVerticalScrollSteps;
    private readonly int? interactionHorizontalScrollSteps;
    private readonly IReadOnlyList<double>? interactionDragDestination;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim execution = new(1, 1);
    private ObservationResult? currentObservation;
    private volatile bool paused;
    private volatile bool abandoned;
    private string activeProbeLabel = "（実行中の一手なし）";
    private string riskLabel = "（実行中の評価なし）";
    private string stopReasonLabel = "停止していません";

    public ProductGameExplorerRuntime(
        string gameId,
        IGameObservationRuntime observation,
        NanoGameInteractionActions actions,
        IGameInteractionStabilityWaiter stability,
        GameTransitionJudge judge,
        IGameTransitionLearner learning,
        IGameInteractionStructureCommitter structure,
        IProductExplorationCoordinator coordinator,
        IExplorationCandidateRiskPolicy riskPolicy,
        ExplorationPolicy policy,
    bool gamePolicyAllowsExplore,
    TimeProvider? timeProvider = null,
    ExplorationWaitCondition? interactionWaitCondition = null,
    IIncrementalKnownScreenIndex? knownScreenIndex = null,
    string interactionOperation = GameInteractionOperations.Click,
    IReadOnlyList<string>? interactionKeyTokens = null,
    int? interactionVerticalScrollSteps = null,
    int? interactionHorizontalScrollSteps = null,
    IReadOnlyList<double>? interactionDragDestination = null)
    {
        GameId = gameId;
        EnvironmentScope = policy.EnvironmentScope;
        this.observation = observation;
        this.actions = actions;
        this.stability = stability;
        this.judge = judge;
        this.learning = learning;
        this.structure = structure;
        this.coordinator = coordinator;
        this.riskPolicy = riskPolicy;
        this.knownScreenIndex = knownScreenIndex;
        this.policy = policy;
        this.gamePolicyAllowsExplore = gamePolicyAllowsExplore;
        this.interactionWaitCondition = interactionWaitCondition ?? new ExplorationWaitCondition(
            ContractSchemaVersions.Revision03,
            3,
            300,
            10_000);
        if (!GameInteractionOperations.InputOperations.Contains(interactionOperation, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(interactionOperation));
        }
        if (interactionOperation == GameInteractionOperations.KeyTap
            && (interactionKeyTokens is null || interactionKeyTokens.Count == 0)
            || interactionOperation == GameInteractionOperations.Scroll
                && interactionVerticalScrollSteps.GetValueOrDefault() == 0
                && interactionHorizontalScrollSteps.GetValueOrDefault() == 0
            || interactionOperation == GameInteractionOperations.Drag
                && interactionDragDestination is not { Count: 2 })
        {
            throw new ArgumentException("操作種別に必要なKeyTap／Scroll／Drag parameterがありません。");
        }
        this.interactionOperation = interactionOperation;
        this.interactionKeyTokens = interactionKeyTokens;
        this.interactionVerticalScrollSteps = interactionVerticalScrollSteps;
        this.interactionHorizontalScrollSteps = interactionHorizontalScrollSteps;
        this.interactionDragDestination = interactionDragDestination;
        time = timeProvider ?? TimeProvider.System;
    }

    public string GameId { get; }
    public string EnvironmentScope { get; }

    public HostExplorerRuntimeSnapshot Snapshot => new(
        GameId,
        EnvironmentScope,
        activeProbeLabel,
        riskLabel,
        coordinator.RemainingProbes,
        policy.Budget.RemainingElapsedMilliseconds,
        policy.Budget.RemainingInferenceMilliseconds,
        [],
        stopReasonLabel,
        CanPause: !paused && !abandoned,
        CanStep: !abandoned,
        CanAbandon: !abandoned);

    public async ValueTask<ObservationResult> ObserveAsync(
        CancellationToken cancellationToken = default)
    {
        currentObservation = await observation.ObserveAsync(cancellationToken).ConfigureAwait(false);
        return currentObservation;
    }

    public ValueTask<ObservedScene> DiscoverTargetsAsync(
        ObservationResult observationResult,
        CancellationToken cancellationToken = default) =>
        observation.DiscoverTargetsAsync(observationResult, cancellationToken);

    public ValueTask<GameInteractionDispatchReceipt> HoverAsync(
        GameInteractionTargetBinding target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(actions.Hover(target, RequireCurrentObservation()));
    }

    public ValueTask<GameInteractionDispatchReceipt> ClickAsync(
        GameInteractionTargetBinding target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(actions.Click(target, RequireCurrentObservation()));
    }

    private ValueTask<GameInteractionDispatchReceipt> DispatchOperationAsync(
        string operation,
        GameInteractionTargetBinding target,
        CancellationToken cancellationToken) => operation switch
        {
            GameInteractionOperations.Hover => HoverAsync(target, cancellationToken),
            GameInteractionOperations.Click => ClickAsync(target, cancellationToken),
            GameInteractionOperations.KeyTap => KeyTapAsync(
                new GameInteractionKeyTapRequest(
                    ContractSchemaVersions.Revision03,
                    target.ObservationId,
                    target.FrameSequence,
                    target.TransformRevision,
                    target.TargetWindowSourceId,
                    interactionKeyTokens!),
                cancellationToken),
            GameInteractionOperations.Scroll => ScrollAsync(
                new GameInteractionScrollRequest(
                    ContractSchemaVersions.Revision03,
                    target,
                    interactionVerticalScrollSteps.GetValueOrDefault(),
                    interactionHorizontalScrollSteps.GetValueOrDefault()),
                cancellationToken),
            GameInteractionOperations.Drag => DragAsync(
                new GameInteractionDragRequest(
                    ContractSchemaVersions.Revision03,
                    target,
                    interactionDragDestination!),
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    public ValueTask<GameInteractionDispatchReceipt> KeyTapAsync(
        GameInteractionKeyTapRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(actions.KeyTap(request, RequireCurrentObservation()));
    }

    public ValueTask<GameInteractionDispatchReceipt> ScrollAsync(
        GameInteractionScrollRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(actions.Scroll(request, RequireCurrentObservation()));
    }

    public ValueTask<GameInteractionDispatchReceipt> DragAsync(
        GameInteractionDragRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(actions.Drag(request, RequireCurrentObservation()));
    }

    public ValueTask<GameInteractionStabilityResult> WaitStableAsync(
        ObservedScene before,
        ExplorationWaitCondition condition,
        CancellationToken cancellationToken = default) =>
        stability.WaitStableAsync(before, condition, cancellationToken);

    public GameTransitionComparison Compare(
        ObservedScene before,
        GameInteractionStabilityResult after) => judge.Compare(before, after);

    public GameTransitionLearningResult LearnTransition(GameTransitionLearningRequest request) =>
        learning.Learn(request);

    public void Pause()
    {
        paused = true;
        stopReasonLabel = "一時停止中";
    }

    public void Step()
    {
        _ = ExecuteNextAsync(ignorePause: true).AsTask().GetAwaiter().GetResult();
        paused = true;
        stopReasonLabel = "一手実行後に一時停止";
    }

    public void Abandon()
    {
        abandoned = true;
        stopReasonLabel = "利用者が探索を終了";
    }

    public ValueTask<ProductGameExplorerStepResult> ExecuteNextAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteNextAsync(ignorePause: false, cancellationToken);

    private async ValueTask<ProductGameExplorerStepResult> ExecuteNextAsync(
        bool ignorePause,
        CancellationToken cancellationToken = default)
    {
        if (abandoned)
        {
            return Result(ProductGameExplorerStepStatus.Abandoned, "探索は終了済みです。");
        }
        if (paused && !ignorePause)
        {
            return Result(ProductGameExplorerStepStatus.Paused, "探索は一時停止中です。");
        }
        if (!await execution.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("探索stepは既に実行中です。");
        }
        try
        {
            var now = time.GetUtcNow();
            var observed = await ObserveAsync(cancellationToken).ConfigureAwait(false);
            var before = await DiscoverTargetsAsync(observed, cancellationToken).ConfigureAwait(false);
            coordinator.CommitObservation(before, now);
            var selection = Select(before);
            if (selection is null)
            {
                stopReasonLabel = "未調査の許可候補がありません";
                return Result(ProductGameExplorerStepStatus.NoCandidate, stopReasonLabel, before);
            }
            var (target, risk) = selection.Value;
            activeProbeLabel = target.SemanticLabel ?? target.CandidateId;
            riskLabel = string.Join(',', risk.RiskTags);
            var proposal = Proposal(before, target, coordinator.CurrentStructureRevisionId);
            var context = new ExplorationContext(
                ContractSchemaVersions.Revision03,
                $"context:{before.ObservationId}",
                policy,
                before,
                coordinator.CurrentStructureRevisionId,
                before.Affordances.Select(candidate => candidate.CandidateId).ToArray(),
                risk.RecoveryEdgeIds,
                policy.Budget with { RemainingProbes = coordinator.RemainingProbes });
            var admission = new ExplorationProposalAdmission(
                context,
                proposal,
                new ExplorationRiskAssessment(
                    ContractSchemaVersions.Revision03,
                    target.CandidateId,
                    risk.Level,
                    risk.RiskTags,
                    risk.Reversible,
                    risk.SideEffectFree,
                    risk.RecoveryEdgeIds,
                    "deterministic-safe-menu-v1"),
                GamePolicyAllowsExplore: gamePolicyAllowsExplore,
                WithinExplorationScope: true,
                ElapsedMilliseconds: 0,
                InferenceMilliseconds: 0);
            var decision = coordinator.Propose(admission, now);
            if (!decision.DispatchAllowed)
            {
                stopReasonLabel = decision.Detail;
                return Result(ProductGameExplorerStepStatus.AdmissionStopped, decision.Detail, before, target);
            }
            IReadOnlyList<ObservedScene> sourceSamples = [before];
            var stableBefore = before;
            currentObservation = observed;
            var indexedTarget = target with
            {
                AllowedPrimitives = [interactionOperation],
                KeyTokens = interactionKeyTokens,
                VerticalScrollSteps = interactionVerticalScrollSteps,
                HorizontalScrollSteps = interactionHorizontalScrollSteps,
                DragDestinationNormalized = interactionDragDestination,
            };
            _ = knownScreenIndex?.RememberControl(
                sourceSamples,
                indexedTarget,
                $"control:{before.ObservationId}:{target.CandidateId}");
            var attemptId = coordinator.GetActiveAttemptId(proposal.ProposalId);
            GameInteractionDispatchReceipt? dispatch = null;
            try
            {
                coordinator.Dispatch(proposal.ProposalId, () =>
                {
                    dispatch = DispatchOperationAsync(
                            interactionOperation,
                            GameInteractionTargetBinding.From(target),
                            cancellationToken)
                        .AsTask().GetAwaiter().GetResult();
                    if (dispatch.Status == GameInteractionDispatchStatus.DispatchFailed)
                    {
                        throw new InvalidOperationException(dispatch.FailureReason);
                    }
                }, now);
            }
            catch (Exception exception)
            {
                stopReasonLabel = exception.Message;
                return new ProductGameExplorerStepResult(
                    ProductGameExplorerStepStatus.DispatchFailed,
                    before,
                    target,
                    dispatch,
                    null,
                    null,
                    null,
                    coordinator.CurrentStructureRevisionId,
                    exception.Message);
            }
            var waited = await WaitStableAsync(stableBefore, proposal.WaitCondition, cancellationToken).ConfigureAwait(false);
            var comparison = Compare(stableBefore, waited);
            if (comparison.Judgement != GameTransitionJudgement.Moved
                && observation is IProductGameRediscoveryTrigger rediscovery)
            {
                rediscovery.MarkTransitionUnconfirmed(stableBefore, target);
            }
            else if (comparison.Judgement == GameTransitionJudgement.Moved
                     && observation is IProductGameRediscoveryTrigger confirmed)
            {
                confirmed.MarkTransitionConfirmed(stableBefore, target);
            }
            if (waited.Observations.Count == 0)
            {
                stopReasonLabel = "after Observationを取得できずOutcomeUnknown";
                return new ProductGameExplorerStepResult(
                    ProductGameExplorerStepStatus.ObservationUndetermined,
                    before,
                    target,
                    dispatch,
                    waited,
                    comparison,
                    null,
                    coordinator.CurrentStructureRevisionId,
                    stopReasonLabel);
            }
            var after = waited.StableScene ?? waited.Observations[^1];
            var learned = LearnTransition(new GameTransitionLearningRequest(
                ContractSchemaVersions.Revision03,
                proposal.ProposalId,
                stableBefore,
                dispatch!,
                waited,
                comparison,
                attemptId,
                $"transition:{proposal.ProposalId}",
                EnvironmentScope,
                checked((long)before.Frame.MonotonicMs),
                checked((long)after.Frame.MonotonicMs),
                time.GetUtcNow(),
                policy.PolicyRevisionId));
            if (learned.Evidence is not null)
            {
                if (comparison.Judgement == GameTransitionJudgement.Moved)
                {
                    _ = knownScreenIndex?.RememberDestination(
                        sourceSamples,
                        indexedTarget,
                        waited.Observations
                            .Where(scene => GameSceneSemanticComparer.StableEquivalent(
                                GameSceneSemanticComparer.Signature(scene),
                                GameSceneSemanticComparer.Signature(after)))
                            .ToArray(),
                        learned.Evidence.EvidenceId);
                }
                _ = structure.Commit(
                    stableBefore,
                    after,
                    learned.Evidence,
                    proposal.WaitCondition,
                    risk.RiskTags,
                    risk.Reversible,
                    time.GetUtcNow());
            }
            activeProbeLabel = "（実行中の一手なし）";
            stopReasonLabel = "停止していません";
            return new ProductGameExplorerStepResult(
                ProductGameExplorerStepStatus.Learned,
                stableBefore,
                target,
                dispatch,
                waited,
                comparison,
                learned,
                coordinator.CurrentStructureRevisionId,
                learned.Detail);
        }
        finally
        {
            execution.Release();
        }
    }

    private (AffordanceCandidate Candidate, ExplorationCandidateRiskDecision Risk)? Select(ObservedScene scene)
    {
        foreach (var candidate in scene.Affordances
                     .Where(candidate => candidate.AllowedPrimitives.Contains(interactionOperation, StringComparer.Ordinal)))
        {
            var risk = riskPolicy.Evaluate(candidate);
            if (risk.Level != ExplorationRiskLevel.Prohibited)
            {
                return (candidate, risk);
            }
        }
        return null;
    }

    private ExplorationProposal Proposal(
        ObservedScene scene,
        AffordanceCandidate candidate,
        string structureRevisionId) =>
        new(
            ContractSchemaVersions.Revision03,
            $"proposal:{scene.ObservationId}:{candidate.CandidateId}",
            scene.ObservationId,
            structureRevisionId,
            candidate.CandidateId,
            interactionOperation,
            $"probe:{GameSceneSemanticComparer.TargetKey(candidate)}",
            [
                ExplorationOutcomeKind.Destination,
                ExplorationOutcomeKind.Novel,
                ExplorationOutcomeKind.NoChange,
                ExplorationOutcomeKind.Ambiguous,
                ExplorationOutcomeKind.Unavailable,
                ExplorationOutcomeKind.Fault,
                ExplorationOutcomeKind.OutcomeUnknown,
            ],
            interactionWaitCondition,
            ["capture-unavailable", "stale-transform", "budget-exhausted"]);

    private ProductGameExplorerStepResult Result(
        ProductGameExplorerStepStatus status,
        string detail,
        ObservedScene? before = null,
        AffordanceCandidate? target = null) =>
        new(
            status,
            before,
            target,
            null,
            null,
            null,
            null,
            coordinator.CurrentStructureRevisionId,
            detail);

    private ObservationResult RequireCurrentObservation() =>
        currentObservation
        ?? throw new InvalidOperationException("入力前にObserveを実行する必要があります。");
}
