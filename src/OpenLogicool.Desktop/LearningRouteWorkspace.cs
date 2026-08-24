namespace OpenLogicool.Desktop;

public sealed record LearningRouteScopeOption(string GameId, string EnvironmentScope)
{
    public string DisplayLabel => $"{GameId}　｜　{EnvironmentScope}";
}

public sealed record LearningRouteEdgeItem(
    string EdgeId,
    string SourceLabel,
    string DestinationLabel,
    string PrimitiveLabel,
    string LocatorLabel,
    string ExpectedOutcomeLabel,
    string VerificationLabel,
    string RiskLabel)
{
    public string DisplayLabel => $"{SourceLabel} → {DestinationLabel}　{PrimitiveLabel}　[{VerificationLabel}]";
}

public sealed record LearningRouteStepItem(int Sequence, LearningRouteEdgeItem Edge)
{
    public string DisplayLabel => $"{Sequence}. {Edge.DisplayLabel}";
}

public sealed record LearningRouteScreenSnapshot(
    string GameId,
    string EnvironmentScope,
    string StructureRevisionId,
    string? RouteId,
    string? VersionId,
    long RevisionNumber,
    string Goal,
    string UserInstruction,
    IReadOnlyList<LearningRouteEdgeItem> AvailableEdges,
    IReadOnlyList<LearningRouteStepItem> Steps,
    string SaveStateLabel,
    string MacroStateLabel,
    string LastAuditLabel,
    bool CanUndo);

public sealed record LearningRouteSaveRequest(
    string GameId,
    string EnvironmentScope,
    string StructureRevisionId,
    string? RouteId,
    string? ParentVersionId,
    string Goal,
    IReadOnlyList<string> EdgeIds,
    string UserInstruction);

public interface ILearningRouteIntents
{
    IReadOnlyList<LearningRouteScopeOption> ListScopes();

    LearningRouteScreenSnapshot Load(string gameId, string environmentScope);

    LearningRouteScreenSnapshot Save(LearningRouteSaveRequest request);

    LearningRouteScreenSnapshot Undo(string gameId, string environmentScope, string routeId, string currentVersionId);

    LearningRouteScreenSnapshot Compile(string gameId, string environmentScope, string routeId, string currentVersionId);
}

/// <summary>学習ルート画面の入力検証とHost intent発行を担う。</summary>
public sealed class LearningRouteWorkspace(ILearningRouteIntents intents)
{
    private readonly ILearningRouteIntents intents = intents ?? throw new ArgumentNullException(nameof(intents));

    public IReadOnlyList<LearningRouteScopeOption> ListScopes() => intents.ListScopes();

    public LearningRouteScreenSnapshot Load(LearningRouteScopeOption scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return intents.Load(scope.GameId, scope.EnvironmentScope);
    }

    public LearningRouteScreenSnapshot Save(
        LearningRouteScopeOption scope,
        LearningRouteScreenSnapshot current,
        string goal,
        IReadOnlyList<string> edgeIds,
        string userInstruction)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(edgeIds);
        if (string.IsNullOrWhiteSpace(goal) || edgeIds.Count == 0 || edgeIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("目的と1件以上の操作stepが必要です。", nameof(edgeIds));
        }
        if (!string.Equals(scope.GameId, current.GameId, StringComparison.Ordinal)
            || !string.Equals(scope.EnvironmentScope, current.EnvironmentScope, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("選択中のゲームと表示中の学習ルートが一致しません。");
        }

        return intents.Save(new LearningRouteSaveRequest(
            scope.GameId,
            scope.EnvironmentScope,
            current.StructureRevisionId,
            current.RouteId,
            current.VersionId,
            goal.Trim(),
            edgeIds.ToArray(),
            userInstruction.Trim()));
    }

    public LearningRouteScreenSnapshot Undo(LearningRouteScopeOption scope, LearningRouteScreenSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(current);
        if (!current.CanUndo || string.IsNullOrWhiteSpace(current.RouteId) || string.IsNullOrWhiteSpace(current.VersionId))
        {
            throw new InvalidOperationException("元に戻せる保存版がありません。");
        }
        return intents.Undo(scope.GameId, scope.EnvironmentScope, current.RouteId, current.VersionId);
    }

    public LearningRouteScreenSnapshot Compile(LearningRouteScopeOption scope, LearningRouteScreenSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(current);
        if (string.IsNullOrWhiteSpace(current.RouteId) || string.IsNullOrWhiteSpace(current.VersionId))
        {
            throw new InvalidOperationException("先に操作ルートを保存してください。");
        }
        return intents.Compile(scope.GameId, scope.EnvironmentScope, current.RouteId, current.VersionId);
    }
}
