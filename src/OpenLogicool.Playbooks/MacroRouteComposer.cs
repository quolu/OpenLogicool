using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Playbooks;

/// <summary>保存済みmacro versionを参照順に連結し、新しいLearning Route draftを作る。</summary>
public static class MacroRouteComposer
{
    public static LearningRouteDraft Compose(
        string routeId,
        string goal,
        IReadOnlyList<LearningRouteRevision> sources,
        GameStructureRevision structure,
        DateTimeOffset createdUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(structure);
        if (sources.Count < 2)
        {
            throw new ArgumentException("合成には2件以上のmacro versionが必要です。", nameof(sources));
        }
        var first = sources[0];
        if (sources.Any(source => source.Status is LearningRouteStatus.Draft or LearningRouteStatus.Retired))
        {
            throw new InvalidOperationException("合成できるのはCompiledまたはVerifiedのmacro versionだけです。");
        }
        if (sources.Any(source => source.EdgeIds.Count == 0))
        {
            throw new InvalidOperationException("空のmacro versionは合成できません。");
        }
        if (sources.Any(source =>
                !string.Equals(source.GameId, first.GameId, StringComparison.Ordinal)
                || !string.Equals(source.EnvironmentScope, first.EnvironmentScope, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("異なるgameまたはenvironment scopeのmacroは合成できません。");
        }
        var edges = sources.SelectMany(source => source.EdgeIds).ToArray();
        var prospective = new LearningRouteRevision(
            ContractSchemaVersions.Revision03,
            routeId,
            1,
            "route:composition-validation",
            null,
            first.GameId,
            first.EnvironmentScope,
            structure.RevisionId,
            goal.Trim(),
            edges,
            LearningRouteAuthor.User,
            "複数マクロを選択順に統合",
            $"macro {sources.Count}件を統合",
            LearningRouteStatus.Compiled,
            createdUtc);
        LearningRouteValidator.Validate(prospective, structure);
        return new LearningRouteDraft(
            prospective.SchemaVersion,
            prospective.RouteId,
            prospective.ParentVersionId,
            prospective.GameId,
            prospective.EnvironmentScope,
            prospective.StructureRevisionId,
            prospective.Goal,
            prospective.EdgeIds,
            prospective.Author,
            prospective.UserInstruction,
            prospective.ChangeReason,
            prospective.Status,
            prospective.CreatedUtc);
    }
}
