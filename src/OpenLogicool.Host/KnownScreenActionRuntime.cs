using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;

namespace OpenLogicool.Host;

public sealed record KnownScreenActionExecutionResult(
    string ActionId,
    string SourceStateId,
    string? ExpectedDestinationStateId,
    GameInteractionDispatchReceipt Dispatch,
    GameInteractionStabilityResult Stability,
    GameTransitionComparison Comparison,
    string? ObservedDestinationStateId,
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
    public async ValueTask<KnownScreenActionExecutionResult> ExecuteKnownAsync(
        string actionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        var observed = await observation.ObserveAsync(cancellationToken).ConfigureAwait(false);
        var before = await observation.DiscoverTargetsAsync(observed, cancellationToken).ConfigureAwait(false);
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
        if (!signature.AllowedPrimitives.Contains(GameInteractionOperations.Click, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("既知actionはClickではありません。");
        }
        var dispatch = actions.Click(GameInteractionTargetBinding.From(target), observed);
        var waited = await stability.WaitStableAsync(before, waitCondition, cancellationToken).ConfigureAwait(false);
        var comparison = judge.Compare(before, waited);
        var observedDestination = waited.StableScene?.StateHypothesisId;
        var matched = signature.DestinationStateId is null
            ? comparison.Judgement != GameTransitionJudgement.Undetermined
            : string.Equals(signature.DestinationStateId, observedDestination, StringComparison.Ordinal);
        return new KnownScreenActionExecutionResult(
            actionId,
            state.StateId,
            signature.DestinationStateId,
            dispatch,
            waited,
            comparison,
            observedDestination,
            matched,
            AiCallCount: 0);
    }
}
