using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

/// <summary>構造正本と学習ルート版をGame Operatorの一つの編集journeyへ投影するHost境界。</summary>
public sealed class HostLearningRouteIntents : ILearningRouteIntents
{
    private static readonly string[] ProhibitedRiskTags =
        ["spend-premium-currency", "spend-rare-resource", "spend-real-money"];

    private readonly SqliteConnection connection;
    private readonly SqliteGameStructureStore structures;
    private readonly SqliteLearningRouteStore routes;
    private readonly TimeProvider timeProvider;
    private readonly Dictionary<string, string> macroStates = new(StringComparer.Ordinal);

    public HostLearningRouteIntents(SqliteConnection connection, TimeProvider? timeProvider = null)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        structures = new SqliteGameStructureStore(connection);
        routes = new SqliteLearningRouteStore(connection);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<LearningRouteScopeOption> ListScopes()
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT DISTINCT game_id, environment_scope FROM structure_events ORDER BY game_id, environment_scope;";
        using var reader = command.ExecuteReader();
        var scopes = new List<LearningRouteScopeOption>();
        while (reader.Read())
        {
            scopes.Add(new LearningRouteScopeOption(reader.GetString(0), reader.GetString(1)));
        }
        return scopes;
    }

    public LearningRouteScreenSnapshot Load(string gameId, string environmentScope)
    {
        RequireScope(gameId, environmentScope);
        var structure = structures.LoadRevision(gameId, environmentScope);
        var latest = routes.ListRouteIds(gameId, environmentScope)
            .Select(routes.LoadLatest)
            .Where(route => route is not null && route.Status != LearningRouteStatus.Retired)
            .OrderByDescending(route => route!.CreatedUtc)
            .ThenByDescending(route => route!.RevisionNumber)
            .FirstOrDefault();
        return Project(gameId, structure, latest, null);
    }

    public LearningRouteScreenSnapshot Save(LearningRouteSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireScope(request.GameId, request.EnvironmentScope);
        var structure = structures.LoadRevision(request.GameId, request.EnvironmentScope);
        if (!string.Equals(request.StructureRevisionId, structure.RevisionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("構造探索の保存版が更新されました。画面を読み直してから保存してください。");
        }
        var routeId = string.IsNullOrWhiteSpace(request.RouteId)
            ? $"learning:{Guid.NewGuid():N}"
            : request.RouteId;
        var latest = routes.LoadLatest(routeId);
        if (!string.Equals(request.ParentVersionId, latest?.VersionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("学習ルートの保存版が更新されました。画面を読み直してください。");
        }

        var prospective = new LearningRouteRevision(
            ContractSchemaVersions.Revision03,
            routeId,
            (latest?.RevisionNumber ?? 0) + 1,
            "route:validation",
            latest?.VersionId,
            request.GameId,
            request.EnvironmentScope,
            structure.RevisionId,
            request.Goal,
            request.EdgeIds,
            LearningRouteAuthor.User,
            request.UserInstruction,
            string.IsNullOrWhiteSpace(request.UserInstruction) ? "利用者が画面から保存" : request.UserInstruction,
            LearningRouteStatus.Draft,
            timeProvider.GetUtcNow());
        LearningRouteValidator.Validate(prospective, structure);

        var saved = routes.Append(new LearningRouteDraft(
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
            prospective.CreatedUtc));
        macroStates.Remove(saved.RouteId);
        return Project(request.GameId, structure, saved, "新しい保存版を作成しました。");
    }

    public LearningRouteScreenSnapshot Undo(
        string gameId,
        string environmentScope,
        string routeId,
        string currentVersionId)
    {
        RequireScope(gameId, environmentScope);
        var history = routes.ReadRevisions(routeId);
        var current = history.LastOrDefault()
            ?? throw new InvalidOperationException("元に戻す学習ルートがありません。");
        if (!string.Equals(current.VersionId, currentVersionId, StringComparison.Ordinal) || history.Count < 2)
        {
            throw new InvalidOperationException("元に戻せる直前の保存版がありません。");
        }
        var previous = history[^2];
        var structure = structures.LoadRevision(gameId, environmentScope);
        var prospective = previous with
        {
            RevisionNumber = current.RevisionNumber + 1,
            VersionId = "route:validation",
            ParentVersionId = current.VersionId,
            Author = LearningRouteAuthor.User,
            UserInstruction = "直前の保存版へ戻す",
            ChangeReason = $"revision {previous.RevisionNumber}へ戻す",
            Status = LearningRouteStatus.Draft,
            CreatedUtc = timeProvider.GetUtcNow(),
        };
        LearningRouteValidator.Validate(prospective, structure);
        var restored = routes.Append(new LearningRouteDraft(
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
            prospective.CreatedUtc));
        macroStates.Remove(routeId);
        return Project(gameId, structure, restored, $"保存版 {previous.RevisionNumber} の内容へ戻しました。");
    }

    public LearningRouteScreenSnapshot Compile(
        string gameId,
        string environmentScope,
        string routeId,
        string currentVersionId)
    {
        RequireScope(gameId, environmentScope);
        var route = routes.LoadLatest(routeId)
            ?? throw new InvalidOperationException("保存済みの学習ルートがありません。");
        if (!string.Equals(route.VersionId, currentVersionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("最新の保存版を読み直してからマクロを生成してください。");
        }
        var structure = structures.LoadRevision(gameId, environmentScope);
        var macro = VisualMacroCompiler.Compile(route, structure, ProhibitedRiskTags);
        macroStates[routeId] = macro.ExecutionMode == VisualMacroExecutionMode.Verified
            ? $"確認済みとして生成（{macro.Steps.Count} step）"
            : $"教師付きとして生成（{macro.Steps.Count} step）";
        return Project(gameId, structure, route, "検証付きマクロを生成しました。");
    }

    private LearningRouteScreenSnapshot Project(
        string gameId,
        GameStructureRevision structure,
        LearningRouteRevision? route,
        string? saveMessage)
    {
        var nodes = structure.ScreenGraph.Nodes.ToDictionary(node => node.StateId, StringComparer.Ordinal);
        var edgeItems = structure.ScreenGraph.Edges
            .Where(edge => !edge.Retired && edge.DestinationStateId is not null
                           && nodes.ContainsKey(edge.SourceStateId) && nodes.ContainsKey(edge.DestinationStateId))
            .OrderBy(edge => edge.EdgeId, StringComparer.Ordinal)
            .Select(edge => new LearningRouteEdgeItem(
                edge.EdgeId,
                Label(nodes[edge.SourceStateId]),
                Label(nodes[edge.DestinationStateId!]),
                edge.Primitive,
                edge.LocatorRevision,
                $"{Label(nodes[edge.DestinationStateId!])}へ遷移し、{edge.WaitCondition.StableFrames} frame安定",
                EvidenceLabel(edge.VerificationState),
                edge.RiskTags.Count == 0 ? "なし" : string.Join(", ", edge.RiskTags)))
            .ToArray();
        var byId = edgeItems.ToDictionary(edge => edge.EdgeId, StringComparer.Ordinal);
        var steps = route?.EdgeIds.Select((edgeId, index) =>
            new LearningRouteStepItem(index + 1, byId[edgeId])).ToArray() ?? [];
        var historyCount = route is null ? 0 : routes.ReadRevisions(route.RouteId).Count;
        return new LearningRouteScreenSnapshot(
            gameId,
            structure.EnvironmentScope,
            structure.RevisionId,
            route?.RouteId,
            route?.VersionId,
            route?.RevisionNumber ?? 0,
            route?.Goal ?? "達成したいことを入力",
            route?.UserInstruction ?? "この順序で保存",
            edgeItems,
            steps,
            saveMessage ?? (route is null ? "未保存" : $"保存済み（版 {route.RevisionNumber}）"),
            route is not null && macroStates.TryGetValue(route.RouteId, out var macroState) ? macroState : "未生成",
            "未実行",
            historyCount >= 2);
    }

    private static string Label(StructureScreenNode node) =>
        string.IsNullOrWhiteSpace(node.ProvisionalLabel) ? node.StateId : node.ProvisionalLabel;

    private static string EvidenceLabel(StructureVerificationState state) => state switch
    {
        StructureVerificationState.Candidate => "未確認",
        StructureVerificationState.Replayed => "強い推定",
        StructureVerificationState.Verified => "確認済み",
        StructureVerificationState.Retired => "非対応",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static void RequireScope(string gameId, string environmentScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
    }
}
