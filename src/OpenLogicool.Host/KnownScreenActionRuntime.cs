using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Perception;

namespace OpenLogicool.Host;

public sealed record KnownScreenActionExecutionResult(
    string ActionId,
    string SourceStateId,
    string? ExpectedDestinationStateId,
    GameInteractionDispatchReceipt Dispatch,
    GameInteractionStabilityResult Stability,
    GameTransitionComparison Comparison,
    string? ObservedDestinationStateId,
    bool TransitionObserved,
    bool DestinationMatched,
    int AiCallCount);

/// <summary>既知ページ索引からactionを解決し、AIを呼ばずNanoへ一回だけ送る。</summary>
public sealed class KnownScreenActionRuntime(
    IGameObservationRuntime observation,
    NanoGameInteractionActions actions,
    IGameInteractionStabilityWaiter stability,
    GameTransitionJudge judge,
    ILearnedSceneProfileStore profiles,
    string gameId,
    string environmentScope,
    ExplorationWaitCondition waitCondition,
    IExplorationCandidateRiskPolicy riskPolicy,
    bool gamePolicyAllowsExecute)
{
    private const double HoverMaximumMeanAbsoluteDifference = 0.5;

    public async ValueTask<KnownScreenActionExecutionResult> ExecuteKnownAsync(
        string actionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        var observed = await observation.ObserveAsync(cancellationToken).ConfigureAwait(false);
        var before = await observation.DiscoverTargetsAsync(observed, cancellationToken).ConfigureAwait(false);
        if (before.CaptureAvailability != CaptureAvailability.Available)
        {
            throw new InvalidOperationException("freshな現在画面を取得できないため保存済みactionを実行しません。");
        }
        if (before.StateIdentity != StateIdentityStatus.Known || before.StateHypothesisId is null)
        {
            throw new InvalidOperationException("現在ページを既知索引へ一意に照合できません。");
        }
        var profile = profiles.Load(gameId, environmentScope)
            ?? throw new InvalidOperationException("既知ページ索引がありません。");
        var state = profile.States.Single(item => item.StateId == before.StateHypothesisId);
        var signature = state.Affordances.SingleOrDefault(item => item.CandidateId == actionId)
            ?? throw new InvalidOperationException("現在ページに指定actionは保存されていません。");
        if (signature.DestinationStateId is null)
        {
            throw new InvalidOperationException("行き先未確定の既知actionは実行できません。");
        }
        var target = before.Affordances.SingleOrDefault(item => item.CandidateId == actionId)
            ?? throw new InvalidOperationException("保存済みactionを最新OCR frameへ再束縛できません。");
        if (!gamePolicyAllowsExecute)
        {
            throw new InvalidOperationException("Game Policyが既知action実行を許可していません。");
        }
        var risk = riskPolicy.Evaluate(target);
        if (risk.Level == ExplorationRiskLevel.Prohibited)
        {
            throw new InvalidOperationException($"現在のrisk policyが既知actionを禁止しました: {string.Join(',', risk.RiskTags)}");
        }
        var operation = signature.AllowedPrimitives.SingleOrDefault()
            ?? throw new InvalidOperationException("既知actionの操作種別が一意ではありません。");
        if (!GameInteractionOperations.InputOperations.Contains(operation, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("既知actionの操作種別は未対応です。");
        }
        var binding = GameInteractionTargetBinding.From(target);
        var dispatch = operation switch
        {
            GameInteractionOperations.Hover => actions.Hover(binding, observed),
            GameInteractionOperations.Click => actions.Click(binding, observed),
            GameInteractionOperations.KeyTap => actions.KeyTap(
                new GameInteractionKeyTapRequest(
                    ContractSchemaVersions.Revision03,
                    observed.ObservationId,
                    observed.Frame.Sequence,
                    observed.Frame.TransformRevision,
                    observed.Frame.SourceId,
                    signature.KeyTokens ?? throw new InvalidOperationException("KeyTap索引にkey tokenがありません。")),
                observed),
            GameInteractionOperations.Scroll => actions.Scroll(
                new GameInteractionScrollRequest(
                    ContractSchemaVersions.Revision03,
                    binding,
                    signature.VerticalScrollSteps.GetValueOrDefault(),
                    signature.HorizontalScrollSteps.GetValueOrDefault()),
                observed),
            GameInteractionOperations.Drag => actions.Drag(
                new GameInteractionDragRequest(
                    ContractSchemaVersions.Revision03,
                    binding,
                    signature.DragDestinationNormalized
                        ?? throw new InvalidOperationException("Drag索引に移動先がありません。")),
                observed),
            _ => throw new InvalidOperationException("既知actionの操作種別は未対応です。"),
        };
        var waited = await stability.WaitStableAsync(before, waitCondition, cancellationToken).ConfigureAwait(false);
        var comparison = judge.Compare(before, waited);
        if (operation == GameInteractionOperations.Hover
            && waited.Status == GameInteractionStabilityStatus.Stable
            && signature.VisualPatch is not null
            && observation is ILastCapturedFrameProvider frameProvider
            && frameProvider.LastFrame is { } lastFrame
            && !VisualPatchMatcher.Matches(
                signature.VisualPatch,
                lastFrame,
                signature.NormalizedBounds,
                maximumMeanAbsoluteDifference: HoverMaximumMeanAbsoluteDifference))
        {
            comparison = new GameTransitionComparison(
                ContractSchemaVersions.Revision03,
                before.ObservationId,
                waited.StableScene?.ObservationId,
                GameTransitionJudgement.Moved,
                [new EvidenceRegion(
                    ContractSchemaVersions.Revision03,
                    "rect",
                    signature.NormalizedBounds,
                    "visual-patch-hover-change")],
                ["保存済みtarget patchがhover後に変化"]);
        }
        var observedDestination = waited.StableScene?.StateHypothesisId;
        var transitionObserved = comparison.Judgement == GameTransitionJudgement.Moved;
        var destinationMatched = transitionObserved
            && observedDestination is not null
            && string.Equals(signature.DestinationStateId, observedDestination, StringComparison.Ordinal);
        return new KnownScreenActionExecutionResult(
            actionId,
            state.StateId,
            signature.DestinationStateId,
            dispatch,
            waited,
            comparison,
            observedDestination,
            transitionObserved,
            destinationMatched,
            AiCallCount: 0);
    }
}
