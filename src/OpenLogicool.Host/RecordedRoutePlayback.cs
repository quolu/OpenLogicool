using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;

namespace OpenLogicool.Host;

/// <summary>記録候補の確認待ちを、入力前に製品へ返す。</summary>
public sealed class DemonstrationStepReviewRequiredException(int stepNumber)
    : InvalidOperationException($"手順 {stepNumber} は確認待ちです。記録画面で画像確認またはこの手順の再記録をしてください。")
{
    public int StepNumber { get; } = stepNumber;
}

/// <summary>記録順と期待結果を再生へ渡す。利用者の指定を構造の観測済み事実へ昇格させない。</summary>
public static class RecordedRoutePlayback
{
    public static StructureScreenEdge? NextEdge(LearningRouteRevision? route, int index, GameStructureRevision structure)
    {
        if (route is null || index >= route.StepCount) return null;
        var step = route.RecordedSteps?[index];
        if (step is { ExpectedJudgement: null }) throw new DemonstrationStepReviewRequiredException(index + 1);
        var edgeId = step?.EdgeId ?? (step is null ? route.EdgeIds[index] : null);
        if (edgeId is not null) return structure.ScreenGraph.Edges.Single(edge => edge.EdgeId == edgeId && !edge.Retired);
        // 構造へ保存しない入力指定。実行時の新しいframeへ束縛し、実観測で初めて遷移を学習する。
        return new StructureScreenEdge(ContractSchemaVersions.Revision03,
            $"demo-replay:{step!.SessionId}:{step.OperationId}", "demonstration:recorded", null, null,
            step.OperationId, "demonstration-review-1", step.Operation, "", [], false,
            step.OperationId, null, DemonstrationRouteCompiler.ReplayWaitCondition, [], [],
            StructureVerificationState.Candidate, TargetSemanticKey: $"demonstration|{step.OperationId}",
            TargetNormalizedBounds: step.NormalizedPoint is { Count: 2 } point ? [point[0], point[1], 0d, 0d] : [0d, 0d, 0d, 0d],
            KeyTokens: step.KeyTokens, VerticalScrollSteps: step.VerticalScrollSteps,
            HorizontalScrollSteps: step.HorizontalScrollSteps, DragDestinationNormalized: step.DragDestinationNormalized);
    }

    public static GameTransitionJudgement Expected(LearningRouteRevision? route, int index) =>
        route?.RecordedSteps is { } steps && index < steps.Count
            ? steps[index].ExpectedJudgement ?? throw new DemonstrationStepReviewRequiredException(index + 1)
            : GameTransitionJudgement.Moved;

    public static IReadOnlyList<DemonstrationRouteStep>? RepairSteps(LearningRouteRevision? route, int index, string edgeId)
    {
        if (route?.RecordedSteps is not { } steps) return null;
        var repaired = steps.ToArray();
        repaired[index] = repaired[index] with { EdgeId = edgeId, ExpectedJudgement = GameTransitionJudgement.Moved,
            ReviewReason = "この手順の再生結果から遷移を保存しました。" };
        return repaired;
    }
}
