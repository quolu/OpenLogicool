using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

/// <summary>Learning Route storeをmacro catalog／compositionへ投影するHost境界。</summary>
public sealed class HostMacroCatalog(
    SqliteConnection connection,
    TimeProvider? timeProvider = null)
{
    private readonly SqliteConnection connection = connection ?? throw new ArgumentNullException(nameof(connection));
    private readonly SqliteLearningRouteStore routes = new(connection);
    private readonly SqliteGameStructureStore structures = new(connection);
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public IReadOnlyList<MacroCatalogItem> ListMacros() =>
        Scopes()
            .SelectMany(scope => routes.ListRouteIds(scope.GameId, scope.EnvironmentScope))
            .Select(routes.LoadLatest)
            .Where(route => route is not null && route.Status != LearningRouteStatus.Retired)
            .Select(route => Project(route!))
            .OrderBy(item => item.GameId, StringComparer.Ordinal)
            .ThenBy(item => item.Goal, StringComparer.Ordinal)
            .ToArray();

    public LearningRouteRevision Resolve(MacroVersionReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var route = reference.VersionId is null
            ? routes.LoadLatest(reference.RouteId)
            : routes.ReadRevisions(reference.RouteId)
                .SingleOrDefault(candidate => string.Equals(candidate.VersionId, reference.VersionId, StringComparison.Ordinal));
        return route ?? throw new InvalidOperationException("指定したmacro versionが見つかりません。");
    }

    public MacroCatalogItem Compose(MacroCompositionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Sources.Count < 2)
        {
            throw new ArgumentException("統合するマクロを2件以上選んでください。", nameof(request));
        }
        var sources = request.Sources.Select(Resolve).ToArray();
        var structure = structures.LoadRevision(sources[0].GameId, sources[0].EnvironmentScope);
        var routeId = $"macro:composed:{Guid.NewGuid():N}";
        var saved = routes.Append(MacroRouteComposer.Compose(
            routeId,
            request.Goal,
            sources,
            structure,
            time.GetUtcNow()));
        return Project(saved);
    }

    private IReadOnlyList<(string GameId, string EnvironmentScope)> Scopes()
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT DISTINCT game_id, environment_scope FROM learning_route_revisions ORDER BY game_id, environment_scope;";
        using var reader = command.ExecuteReader();
        var result = new List<(string, string)>();
        while (reader.Read()) result.Add((reader.GetString(0), reader.GetString(1)));
        return result;
    }

    private static MacroCatalogItem Project(LearningRouteRevision route) => new(
        route.RouteId,
        route.VersionId,
        route.GameId,
        route.EnvironmentScope,
        route.Goal,
        route.RevisionNumber,
        route.EdgeIds.Count,
        route.Status switch
        {
            LearningRouteStatus.Draft => "下書き",
            LearningRouteStatus.Compiled => "実行可能",
            LearningRouteStatus.Verified => "確認済み",
            LearningRouteStatus.Retired => "非対応",
            _ => throw new ArgumentOutOfRangeException(),
        });
}
