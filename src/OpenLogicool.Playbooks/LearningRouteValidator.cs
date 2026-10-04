using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Playbooks;

/// <summary>保存された学習ルートのedge列が、現在Structureでも同一environment内で連続することを検証する。</summary>
public static class LearningRouteValidator
{
    public static void Validate(LearningRouteRevision route, GameStructureRevision structure)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(structure);
        if (!string.Equals(route.EnvironmentScope, structure.EnvironmentScope, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("学習ルートとStructureのenvironment scopeが一致しません。");
        }

        if (route.RecordedSteps is { } recorded)
        {
            if (recorded.Any(step => step.ExpectedJudgement is null))
                throw new InvalidOperationException("確認待ちの手順があります。記録画面で該当する手順だけを修復してください。");
            // 画面変化なしの利用者指定は構造edgeを持たず、製品の逐次Compareで再生する。
            if (recorded.Any(step => step.EdgeId is null))
                throw new InvalidOperationException("画像から確認した手順はマクロ画面の逐次再生を使ってください。構造の確認済み遷移へは昇格していません。");
        }

        _ = StructurePlaybookSynthesizer.Synthesize(
            structure,
            route.EdgeIds,
            $"validation:{route.VersionId}",
            StructurePlaybookExecutionMode.Supervised);
    }
}
