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

public interface IProductGameTransitionComparisonNormalizer
{
    GameTransitionComparison Normalize(
        string operation,
        StructureScreenEdge? routeTarget,
        ObservedScene before,
        GameInteractionStabilityResult after,
        GameTransitionComparison comparison);
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
    string Detail,
    string? CommittedEdgeId = null);

/// <summary>基本10機能を一つのzero-seed探索stepへ合成する製品runtime。</summary>
public sealed class ProductGameExplorerRuntime : IHostExplorerRuntimeControl, IGameInteractionRuntime, IProductGameStepRuntime
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
    private readonly bool learnNonMovedRouteOutcomes;
    private readonly IProductGameTransitionComparisonNormalizer? comparisonNormalizer;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim execution = new(1, 1);
    private ObservationResult? currentObservation;
    private StructureScreenEdge? routeTarget;
    private bool routeTargetIsRepairing;
    private volatile bool paused;
    private volatile bool abandoned;
    private string activeProbeLabel = "（実行中の一手なし）";
    private string riskLabel = "（実行中の評価なし）";
    private string stopReasonLabel = "停止していません";

    private string ActiveOperation => routeTarget?.Primitive ?? interactionOperation;
    private IReadOnlyList<string>? ActiveKeyTokens => routeTarget?.KeyTokens ?? interactionKeyTokens;
    private int? ActiveVerticalScrollSteps => routeTarget?.VerticalScrollSteps ?? interactionVerticalScrollSteps;
    private int? ActiveHorizontalScrollSteps => routeTarget?.HorizontalScrollSteps ?? interactionHorizontalScrollSteps;
    private IReadOnlyList<double>? ActiveDragDestination => routeTarget?.DragDestinationNormalized ?? interactionDragDestination;
    private ExplorationWaitCondition ActiveWaitCondition => routeTarget?.WaitCondition ?? interactionWaitCondition;

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
    IReadOnlyList<double>? interactionDragDestination = null,
    bool learnNonMovedRouteOutcomes = true,
    IProductGameTransitionComparisonNormalizer? comparisonNormalizer = null)
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
        this.learnNonMovedRouteOutcomes = learnNonMovedRouteOutcomes;
        this.comparisonNormalizer = comparisonNormalizer;
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
                    ActiveKeyTokens!),
                cancellationToken),
            GameInteractionOperations.Scroll => ScrollAsync(
                new GameInteractionScrollRequest(
                    ContractSchemaVersions.Revision03,
                    target,
                    ActiveVerticalScrollSteps.GetValueOrDefault(),
                    ActiveHorizontalScrollSteps.GetValueOrDefault()),
                cancellationToken),
            GameInteractionOperations.Drag => DragAsync(
                new GameInteractionDragRequest(
                    ContractSchemaVersions.Revision03,
                    target,
                    ActiveDragDestination!),
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

    public void SetRouteTarget(StructureScreenEdge? edge, bool repairing)
    {
        if (edge?.Primitive == GameInteractionOperations.KeyTap && edge.KeyTokens is not { Count: > 0 }
            || edge?.Primitive == GameInteractionOperations.Scroll
                && edge.VerticalScrollSteps.GetValueOrDefault() == 0
                && edge.HorizontalScrollSteps.GetValueOrDefault() == 0
            || edge?.Primitive == GameInteractionOperations.Drag
                && edge.DragDestinationNormalized is not { Count: 2 })
        {
            throw new InvalidOperationException($"保存edge '{edge.EdgeId}' に {edge.Primitive} の操作parameterがありません。");
        }
        routeTarget = edge;
        routeTargetIsRepairing = repairing;
        if (observation is IProductGameRouteControl routeControl)
        {
            routeControl.SetRouteTarget(edge);
        }
    }

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
            var currentRouteTarget = routeTarget;
            var currentRouteTargetIsRepairing = routeTargetIsRepairing;
            var observed = await ObserveAsync(cancellationToken).ConfigureAwait(false);
            var routeControl = observation as IProductGameRouteControl;
            ObservedScene? precomputedComparison = null;
            ObservedScene before;
            if (ActiveOperation == GameInteractionOperations.KeyTap)
            {
                routeControl?.BeginComparison();
                try
                {
                    precomputedComparison = await DiscoverTargetsAsync(observed, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    routeControl?.EndComparison();
                }
                before = precomputedComparison with
                {
                    Affordances = [.. precomputedComparison.Affordances, GlobalKeyCandidate(observed)],
                };
            }
            else
            {
                before = await DiscoverTargetsAsync(observed, cancellationToken).ConfigureAwait(false);
            }
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
            currentObservation = observed;
            var indexedTarget = target with
            {
                AllowedPrimitives = [ActiveOperation],
                KeyTokens = ActiveKeyTokens,
                VerticalScrollSteps = ActiveVerticalScrollSteps,
                HorizontalScrollSteps = ActiveHorizontalScrollSteps,
                DragDestinationNormalized = ActiveDragDestination,
            };
            if (ActiveOperation != GameInteractionOperations.KeyTap
                && (currentRouteTarget is null || currentRouteTargetIsRepairing))
            {
                _ = knownScreenIndex?.RememberControl(
                    sourceSamples,
                    indexedTarget,
                    $"control:{before.ObservationId}:{target.CandidateId}");
            }
            routeControl?.BeginComparison();
            ObservedScene comparisonBefore;
            try
            {
                comparisonBefore = precomputedComparison
                    ?? await DiscoverTargetsAsync(observed, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                routeControl?.EndComparison();
                throw;
            }
            var structureBefore = comparisonBefore with
            {
                Affordances =
                [
                    .. comparisonBefore.Affordances.Where(candidate =>
                        candidate.CandidateId != indexedTarget.CandidateId),
                    indexedTarget with { SemanticKind = "probe-target" },
                ],
            };
            var attemptId = coordinator.GetActiveAttemptId(proposal.ProposalId);
            GameInteractionDispatchReceipt? dispatch = null;
            try
            {
                coordinator.Dispatch(proposal.ProposalId, () =>
                {
                    dispatch = DispatchOperationAsync(
                            ActiveOperation,
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
                routeControl?.EndComparison();
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
            GameInteractionStabilityResult waited;
            try
            {
                waited = await WaitStableAsync(comparisonBefore, proposal.WaitCondition, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                routeControl?.EndComparison();
            }
            var comparison = Compare(comparisonBefore, waited);
            if (comparisonNormalizer is not null)
            {
                comparison = comparisonNormalizer.Normalize(
                    ActiveOperation,
                    currentRouteTarget,
                    comparisonBefore,
                    waited,
                    comparison);
            }
            if (comparison.Judgement != GameTransitionJudgement.Moved
                && observation is IProductGameRediscoveryTrigger rediscovery)
            {
                rediscovery.MarkTransitionUnconfirmed(before, target);
            }
            else if (comparison.Judgement == GameTransitionJudgement.Moved
                     && observation is IProductGameRediscoveryTrigger confirmed)
            {
                confirmed.MarkTransitionConfirmed(before, target);
            }
            if (waited.Observations.Count == 0)
            {
                stopReasonLabel = "after Observationを取得できずOutcomeUnknown";
                var unknownComparison = comparison with
                {
                    AfterObservationId = comparisonBefore.ObservationId,
                    Judgement = GameTransitionJudgement.Undetermined,
                };
                var unknownStability = waited with
                {
                    Observations = [comparisonBefore],
                    StableScene = null,
                };
                var unknownLearning = LearnTransition(new GameTransitionLearningRequest(
                    ContractSchemaVersions.Revision03,
                    proposal.ProposalId,
                    structureBefore,
                    dispatch!,
                    unknownStability,
                    unknownComparison,
                    attemptId,
                    $"transition:{proposal.ProposalId}",
                    EnvironmentScope,
                    checked((long)before.Frame.MonotonicMs),
                    checked((long)comparisonBefore.Frame.MonotonicMs),
                    time.GetUtcNow(),
                    policy.PolicyRevisionId));
                return new ProductGameExplorerStepResult(
                    ProductGameExplorerStepStatus.ObservationUndetermined,
                    before,
                    target,
                    dispatch,
                    unknownStability,
                    unknownComparison,
                    unknownLearning,
                    coordinator.CurrentStructureRevisionId,
                    stopReasonLabel);
            }
            var after = waited.StableScene ?? waited.Observations[^1];
            var learned = LearnTransition(new GameTransitionLearningRequest(
                ContractSchemaVersions.Revision03,
                proposal.ProposalId,
                structureBefore,
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
            string? committedEdgeId = null;
            if (learned.Evidence is not null)
            {
                if (currentRouteTarget is not null
                    && !currentRouteTargetIsRepairing
                    && (comparison.Judgement == GameTransitionJudgement.Moved || !learnNonMovedRouteOutcomes))
                {
                    committedEdgeId = currentRouteTarget.EdgeId;
                }
                else
                {
                    if (comparison.Judgement == GameTransitionJudgement.Moved
                        && ActiveOperation != GameInteractionOperations.KeyTap)
                    {
                        _ = knownScreenIndex?.RememberDestination(
                            sourceSamples,
                            indexedTarget,
                            waited.Observations
                                .Where(scene => GameSceneSemanticComparer.StableEquivalent(scene, after))
                                .ToArray(),
                            learned.Evidence.EvidenceId);
                    }
                    var committed = structure.Commit(
                        structureBefore,
                        after,
                        learned.Evidence,
                        proposal.WaitCondition,
                        risk.RiskTags,
                        risk.Reversible,
                        time.GetUtcNow());
                    committedEdgeId = committed.EdgeId;
                }
            }
            activeProbeLabel = "（実行中の一手なし）";
            stopReasonLabel = "停止していません";
            return new ProductGameExplorerStepResult(
                ProductGameExplorerStepStatus.Learned,
                before,
                target,
                dispatch,
                waited,
                comparison,
                learned,
                coordinator.CurrentStructureRevisionId,
                learned.Detail,
                committedEdgeId);
        }
        finally
        {
            execution.Release();
        }
    }

    private (AffordanceCandidate Candidate, ExplorationCandidateRiskDecision Risk)? Select(ObservedScene scene)
    {
        var candidates = scene.Affordances
            .Where(candidate => candidate.AllowedPrimitives.Contains(ActiveOperation, StringComparer.Ordinal));
        if (ActiveOperation == GameInteractionOperations.KeyTap)
        {
            candidates = candidates.Where(candidate => string.Equals(candidate.SemanticKind, "global-key", StringComparison.Ordinal));
        }
        foreach (var candidate in candidates)
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
            ActiveOperation,
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
            ActiveWaitCondition,
            ["capture-unavailable", "stale-transform", "budget-exhausted"]);

    private AffordanceCandidate GlobalKeyCandidate(ObservationResult observed) => new(
        ContractSchemaVersions.Revision03,
        $"global-key:{observed.ObservationId}",
        observed.ObservationId,
        observed.Frame.Sequence,
        observed.Frame.TransformRevision,
        observed.Frame.SourceId,
        new AffordanceLocator(ContractSchemaVersions.Revision03, "global-key", [0d, 0d, 1d, 1d], "global-key:v1"),
        [],
        1,
        [GameInteractionOperations.KeyTap],
        "global-key",
        string.Join('+', ActiveKeyTokens!));

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
