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
            .Concat(scene.DiscoveryEvidence?.LocalGroundingTexts ?? [])
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
        LearningRouteRevision? initialRoute = null)
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
        if (route is not null && (route.GameId != gameId || route.EnvironmentScope != environmentScope || route.Goal != goal))
            throw new InvalidOperationException("目的routeのscopeまたはgoalが一致しません。");
    }

    public LearningRouteRevision? Route => route;
    public int StepIndex => stepIndex;

    public async ValueTask<PurposeDirectedStepResult> ExecuteNextAsync(CancellationToken cancellationToken = default)
    {
        var saved = route is not null && stepIndex < route.EdgeIds.Count;
        var beforeRevision = structures.LoadRevision(gameId, environmentScope);
        var routeEdge = saved
            ? beforeRevision.ScreenGraph.Edges.Single(edge => edge.EdgeId == route!.EdgeIds[stepIndex] && !edge.Retired)
            : null;
        steps.SetRouteTarget(routeEdge, repairing);
        var step = await steps.ExecuteNextAsync(cancellationToken).ConfigureAwait(false);
        if (step.Status != ProductGameExplorerStepStatus.Learned || step.Comparison is null)
            return new(PurposeDirectedStepStatus.Stopped, stepIndex, step, route, saved, step.Detail);
        if (step.Comparison.Judgement != GameTransitionJudgement.Moved)
        {
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
        var goalSatisfied = !saved && completion.IsSatisfied(goal, after, step.Target!);
        if (!saved || repairing)
        {
            _ = step.Learning?.Evidence?.EvidenceId
                ?? throw new InvalidOperationException("Moved stepにTransition Evidenceがありません。");
            var committedEdgeId = step.CommittedEdgeId
                ?? throw new InvalidOperationException("Moved stepにcommit済みEdgeIdがありません。");
            var current = structures.LoadRevision(gameId, environmentScope);
            var learnedEdge = current.ScreenGraph.Edges.Single(edge => edge.EdgeId == committedEdgeId);
            var edgeIds = route?.EdgeIds.ToList() ?? [];
            if (repairing && stepIndex < edgeIds.Count) edgeIds[stepIndex] = learnedEdge.EdgeId;
            else edgeIds.Add(learnedEdge.EdgeId);
            route = routes.Append(new LearningRouteDraft(
                ContractSchemaVersions.Revision03, routeId, route?.VersionId, gameId, environmentScope,
                current.RevisionId, goal, edgeIds, LearningRouteAuthor.Ai, null,
                repairing ? $"step {stepIndex + 1}だけを再探索結果へ差替え" : $"step {stepIndex + 1}を逐次追記",
                goalSatisfied ? LearningRouteStatus.Compiled : route?.Status ?? LearningRouteStatus.Draft,
                time.GetUtcNow()));
        }
        repairing = false;
        stepIndex++;
        var completed = goalSatisfied
            || saved && stepIndex == route!.EdgeIds.Count && route.Status != LearningRouteStatus.Draft;
        return new(completed ? PurposeDirectedStepStatus.Completed : PurposeDirectedStepStatus.Advanced,
            stepIndex, step, route, saved, completed ? "目的を完了しました。" : "Movedを保存して次stepへ進みます。");
    }
}
