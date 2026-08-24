using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Playbooks;

/// <summary>保存された学習ルートが、指定したStructure版の同一environment内で連続することを検証する。</summary>
public static class LearningRouteValidator
{
    public static void Validate(LearningRouteRevision route, GameStructureRevision structure)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(structure);
        if (!string.Equals(route.StructureRevisionId, structure.RevisionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"学習ルートはStructure版 '{route.StructureRevisionId}' 用ですが、'{structure.RevisionId}' が渡されました。");
        }
        if (!string.Equals(route.EnvironmentScope, structure.EnvironmentScope, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("学習ルートとStructureのenvironment scopeが一致しません。");
        }

        _ = StructurePlaybookSynthesizer.Synthesize(
            structure,
            route.EdgeIds,
            $"validation:{route.VersionId}",
            StructurePlaybookExecutionMode.Supervised);
    }
}
