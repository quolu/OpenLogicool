using System.Security.Cryptography;
using System.Text;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Perception;

namespace OpenLogicool.Host;

public interface IProductGameStepRuntime
{
    void SetRouteTarget(StructureScreenEdge? edge, bool repairing);
    ValueTask<ProductGameExplorerStepResult> ExecuteNextAsync(CancellationToken cancellationToken = default);
}

public interface IPurposeGoalCompletionEvaluator
{
    bool IsSatisfied(string goal, ObservedScene scene, AffordanceCandidate target);
}

public sealed class SemanticTextGoalCompletionEvaluator : IPurposeGoalCompletionEvaluator
{
    public bool IsSatisfied(string goal, ObservedScene scene, AffordanceCandidate target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(target);
        var core = GoalCore(goal);
        if (Matches(core, target.SemanticLabel)) return true;
        return scene.Affordances.Select(candidate => candidate.SemanticLabel)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Any(text => Matches(core, text));
    }

    private static bool Matches(string core, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = OcrTextMatcher.Normalize(text);
        return normalized.Length > 0
            && (core.Contains(normalized, StringComparison.Ordinal)
                || normalized.Contains(core, StringComparison.Ordinal)
                || OcrTextMatcher.Similarity(core, normalized) >= OcrTextMatcher.DefaultMinimumSimilarity);
    }

    private static string GoalCore(string goal)
    {
        var normalized = OcrTextMatcher.Normalize(goal);
        foreach (var suffix in new[] { "を開く", "へ移動する", "へ進む", "を選ぶ", "を押す", "開く" })
        {
            var value = OcrTextMatcher.Normalize(suffix);
            if (normalized.EndsWith(value, StringComparison.Ordinal)) return normalized[..^value.Length];
        }
        return normalized;
    }
}

public enum PurposeDirectedStepStatus { Advanced, Completed, LearningContinues, Stopped }

public sealed record PurposeDirectedStepResult(
    PurposeDirectedStepStatus Status,
    int StepIndex,
    ProductGameExplorerStepResult Step,
    LearningRouteRevision? Route,
    bool UsedSavedRoute,
    string Detail);

public static class PurposeLearningRouteIds
{
    public static string Create(string gameId, string environmentScope, string goal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        var material = $"{gameId}\n{environmentScope}\n{goal.Normalize(NormalizationForm.FormKC).Trim()}";
        return $"purpose:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()}";
    }
}

/// <summary>既存の一手runtimeを、goal単位のappend-only Learning Routeへ合成する。</summary>
public sealed class PurposeDirectedExplorationRuntime
{
    private readonly IProductGameStepRuntime steps;
    private readonly IGameStructureStore structures;
    private readonly ILearningRouteStore routes;
    private readonly IPurposeGoalCompletionEvaluator completion;
    private readonly TimeProvider time;
    private readonly string gameId;
    private readonly string environmentScope;
    private readonly string goal;
    private readonly string routeId;
    private readonly MacroPlaybackMode playbackMode;
    private readonly IRecordedMacroResultVerifier? recordedResults;
    private readonly int? endStepIndexExclusive;
    private readonly int startStepIndex;
    private readonly IMacroStepConfirmation? stepConfirmation;
    private readonly Action<MacroStepConfirmationRequest, MacroStepDecision>? recordConfirmation;
    private LearningRouteRevision? route;
    private int stepIndex;
    private bool repairing;

    public PurposeDirectedExplorationRuntime(
        string gameId,
        string environmentScope,
        string goal,
        IProductGameStepRuntime steps,
        IGameStructureStore structures,
        ILearningRouteStore routes,
        IPurposeGoalCompletionEvaluator completion,
        TimeProvider? timeProvider = null,
        MacroPlaybackMode playbackMode = MacroPlaybackMode.AiMonitored,
        LearningRouteRevision? initialRoute = null,
        int startStepIndex = 0,
        int? endStepIndexExclusive = null,
        IRecordedMacroResultVerifier? recordedResults = null,
        IMacroStepConfirmation? stepConfirmation = null,
        Action<MacroStepConfirmationRequest, MacroStepDecision>? recordConfirmation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        this.gameId = gameId;
        this.environmentScope = environmentScope;
        this.goal = goal;
        this.steps = steps;
        this.structures = structures;
        this.routes = routes;
        this.completion = completion;
        time = timeProvider ?? TimeProvider.System;
        this.playbackMode = playbackMode;
        routeId = initialRoute?.RouteId ?? PurposeLearningRouteIds.Create(gameId, environmentScope, goal);
        route = initialRoute ?? routes.LoadLatest(routeId);
        if (startStepIndex < 0 || startStepIndex > 0 && (route is null || startStepIndex >= route.StepCount))
            throw new ArgumentOutOfRangeException(nameof(startStepIndex));
        stepIndex = startStepIndex;
        this.startStepIndex = startStepIndex;
        this.endStepIndexExclusive = endStepIndexExclusive ?? initialRoute?.StepCount;
        this.recordedResults = recordedResults;
        this.stepConfirmation = stepConfirmation;
        this.recordConfirmation = recordConfirmation;
        if (this.endStepIndexExclusive is { } end && (route is null || end <= startStepIndex || end > route.StepCount))
            throw new ArgumentOutOfRangeException(nameof(endStepIndexExclusive));
        if (route is not null && (route.GameId != gameId || route.EnvironmentScope != environmentScope || route.Goal != goal))
            throw new InvalidOperationException("目的routeのscopeまたはgoalが一致しません。");
    }

    public LearningRouteRevision? Route => route;
    public int StepIndex => stepIndex;

    public async ValueTask<PurposeDirectedStepResult> ExecuteNextAsync(CancellationToken cancellationToken = default)
    {
        if (endStepIndexExclusive is { } limit && stepIndex >= limit)
            throw new InvalidOperationException("指定した手順範囲の再生は終了しています。");
        var saved = route is not null && stepIndex < route.StepCount;
        var beforeRevision = structures.LoadRevision(gameId, environmentScope);
        StructureScreenEdge? routeEdge;
        var userConfirms = saved && route!.RecordedSteps is not null && stepConfirmation is not null;
        try { routeEdge = RecordedRoutePlayback.NextEdge(route, stepIndex, beforeRevision, userConfirms); }
        catch (DemonstrationStepReviewRequiredException exception)
        {
            return new(PurposeDirectedStepStatus.Stopped, stepIndex,
                new ProductGameExplorerStepResult(ProductGameExplorerStepStatus.Paused, null, null, null,
                    null, null, null, beforeRevision.RevisionId, exception.Message), route, saved, exception.Message);
        }
        steps.SetRouteTarget(routeEdge, repairing);
        var step = await steps.ExecuteNextAsync(cancellationToken).ConfigureAwait(false);
        if (userConfirms && step.Dispatch?.Status == GameInteractionDispatchStatus.Dispatched)
        {
            var actual = step.Stability?.StableScene ?? step.Stability?.Observations.LastOrDefault();
            if (actual is null || actual.CaptureAvailability != CaptureAvailability.Available
                || string.IsNullOrWhiteSpace(actual.Frame.Artifact?.LocalPath))
                return new(PurposeDirectedStepStatus.Stopped, stepIndex, step, route, saved,
                    $"手順 {stepIndex + 1} の操作後画像を取得できず、確認を続けられません。{step.Detail}");
            var check = RecordedRoutePlayback.VerifyResult(recordedResults, route, stepIndex, actual);
            var label = routeEdge!.Primitive switch
            {
                GameInteractionOperations.Click => "クリック",
                GameInteractionOperations.Drag => "ドラッグ",
                GameInteractionOperations.KeyTap => "キー操作",
                GameInteractionOperations.Scroll => "スクロール",
                _ => routeEdge.Primitive,
            };
            var review = new MacroStepConfirmationRequest($"step-confirmation:{Guid.NewGuid():N}",
                stepIndex + 1, label, step.Comparison?.Judgement.ToString() ?? "未判定",
                check?.ExpectedImagePath, actual.Frame.Artifact?.LocalPath,
                check?.Status switch
                {
                    RecordedMacroResultStatus.Matched => "録画の文字配置・画像特徴と一致しています。",
                    RecordedMacroResultStatus.Different => "録画の文字配置・画像特徴と違いがあります。",
                    _ => "自動照合の根拠を取得できませんでした。",
                });
            var decision = await stepConfirmation!.RequestAsync(review, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            recordConfirmation?.Invoke(review, decision);
            if (decision != MacroStepDecision.Accept)
                return new(PurposeDirectedStepStatus.Stopped, stepIndex, step, route, saved,
                    decision == MacroStepDecision.Correct
                        ? $"手順 {stepIndex + 1} の補正のため停止しました。記録画面でこの手順を補正し、開始手順を指定して再開してください。"
                        : $"手順 {stepIndex + 1} の確認で再生を中止しました。");
            // ユーザーのOKはこの実行の確認。期待結果・観測事実・routeを変更しない。
            stepIndex++;
            var finished = stepIndex == (endStepIndexExclusive ?? route!.StepCount);
            return new(finished ? PurposeDirectedStepStatus.Completed : PurposeDirectedStepStatus.Advanced,
                stepIndex, step, route, true, finished
                    ? $"手順 {startStepIndex + 1}〜{stepIndex} の結果確認が完了しました。"
                        + (stepIndex < route!.StepCount ? $"次は手順 {stepIndex + 1} です。" : "")
                    : $"手順 {stepIndex} の結果をユーザーが確認しました。");
        }
        if (step.Status != ProductGameExplorerStepStatus.Learned || step.Comparison is null)
            return new(PurposeDirectedStepStatus.Stopped, stepIndex, step, route, saved, step.Detail);
        var expected = RecordedRoutePlayback.Expected(route, stepIndex);
        if (step.Comparison.Judgement != expected)
        {
            if (route?.RecordedSteps is { } recordedSteps && saved
                && (recordedSteps[stepIndex].EdgeId is null || expected == GameTransitionJudgement.Stayed))
                return new(PurposeDirectedStepStatus.Stopped, stepIndex, step, route, saved,
                    $"手順 {stepIndex + 1} の結果が期待と一致しません。記録画面でこの手順だけを修復してください。旧版は保持しています。");
            if (playbackMode == MacroPlaybackMode.AiFree)
            {
                return new(PurposeDirectedStepStatus.Stopped, stepIndex, step, route, saved,
                    "AI監視なし再生で非遷移を確認したため停止しました。マクロは変更していません。");
            }
            repairing = saved || repairing;
            return new(PurposeDirectedStepStatus.LearningContinues, stepIndex, step, route, saved,
                "非遷移outcomeを保存し、同じstepだけをAI再探索します。");
        }

        var after = step.Stability?.StableScene ?? step.Stability?.Observations.LastOrDefault()
            ?? throw new InvalidOperationException("Moved stepにafter Observationがありません。");
        var verification = RecordedRoutePlayback.VerifyResult(recordedResults, route, stepIndex, after);
        if (verification is { Status: not RecordedMacroResultStatus.Matched })
        {
            if (verification.Status == RecordedMacroResultStatus.Unavailable || playbackMode == MacroPlaybackMode.AiFree)
                return new(PurposeDirectedStepStatus.Stopped, stepIndex, step, route, saved, verification.Detail);
            repairing = true;
            return new(PurposeDirectedStepStatus.LearningContinues, stepIndex, step, route, saved,
                verification.Detail + "この手順だけをAIで修復します。");
        }
        var goalSatisfied = !saved && completion.IsSatisfied(goal, after, step.Target!);
        if (!saved || repairing || route?.RecordedSteps is { } recorded && saved
            && recorded[stepIndex].EdgeId is null && expected == GameTransitionJudgement.Moved)
        {
            _ = step.Learning?.Evidence?.EvidenceId
                ?? throw new InvalidOperationException("Moved stepにTransition Evidenceがありません。");
            var committedEdgeId = step.CommittedEdgeId
                ?? throw new InvalidOperationException("Moved stepにcommit済みEdgeIdがありません。");
            var current = structures.LoadRevision(gameId, environmentScope);
            var learnedEdge = current.ScreenGraph.Edges.Single(edge => edge.EdgeId == committedEdgeId);
            var edgeIds = route?.EdgeIds.ToList() ?? [];
            var recordedSteps = RecordedRoutePlayback.RepairSteps(route, stepIndex, learnedEdge.EdgeId);
            if (recordedSteps is not null) edgeIds = recordedSteps.Where(item => item.EdgeId is not null).Select(item => item.EdgeId!).ToList();
            else if (repairing && stepIndex < edgeIds.Count) edgeIds[stepIndex] = learnedEdge.EdgeId;
            else edgeIds.Add(learnedEdge.EdgeId);
            route = routes.Append(new LearningRouteDraft(
                ContractSchemaVersions.Revision03, routeId, route?.VersionId, gameId, environmentScope,
                current.RevisionId, goal, edgeIds, LearningRouteAuthor.Ai, null,
                repairing ? $"step {stepIndex + 1}だけを再探索結果へ差替え" : $"step {stepIndex + 1}を逐次追記",
                goalSatisfied ? LearningRouteStatus.Compiled : route?.Status ?? LearningRouteStatus.Draft,
                time.GetUtcNow(), recordedSteps));
        }
        repairing = false;
        stepIndex++;
        var completed = goalSatisfied
            || endStepIndexExclusive is { } last && stepIndex == last
            || saved && stepIndex == route!.StepCount && route.PendingStepCount == 0
                && (route.RecordedSteps is not null || route.Status != LearningRouteStatus.Draft);
        return new(completed ? PurposeDirectedStepStatus.Completed : PurposeDirectedStepStatus.Advanced,
            stepIndex, step, route, saved, completed ? endStepIndexExclusive is { } ending && ending < route!.StepCount
                ? $"手順 {startStepIndex + 1}〜{ending} の再生が完了しました。次は手順 {ending + 1} です。"
                : "目的を完了しました。"
                : expected == GameTransitionJudgement.Stayed ? "記録と同じ無変化を照合して次stepへ進みます。" : "Movedを保存して次stepへ進みます。");
    }
}
