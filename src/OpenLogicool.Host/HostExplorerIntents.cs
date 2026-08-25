using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Desktop;
using OpenLogicool.Exploration;
using OpenLogicool.Persistence;

namespace OpenLogicool.Host;

/// <summary>実行中の探索だけが提供する可変状態。保存構造の閲覧・訂正はこの境界が無くても使える。</summary>
public sealed record HostExplorerRuntimeSnapshot(
    string GameId,
    string EnvironmentScope,
    string ActiveProbeLabel,
    string RiskLabel,
    string ApprovalReason,
    int RemainingProbeCount,
    long RemainingElapsedMilliseconds,
    long RemainingInferenceMilliseconds,
    IReadOnlyList<string> RecoveryPathEdgeIds,
    string StopReasonLabel,
    bool CanPause,
    bool CanStep,
    bool CanAbandon);

/// <summary>Explorer UIと実行中coordinatorを繋ぐ制御port。ProductGameExplorerRuntimeが実装する。</summary>
public interface IHostExplorerRuntimeControl
{
    HostExplorerRuntimeSnapshot Snapshot { get; }

    void Pause();

    void Step();

    void Abandon();
}

/// <summary>構造DB・利用者訂正authority・実行中探索制御をDesktop intentへ合成するHost境界。</summary>
public sealed class HostExplorerIntents : IExplorerIntents
{
    private readonly SqliteConnection connection;
    private readonly SqliteGameStructureStore store;
    private readonly StructureCorrectionController corrections;
    private readonly IHostExplorerRuntimeControl? runtime;
    private readonly TimeProvider timeProvider;

    public HostExplorerIntents(
        SqliteConnection connection,
        IHostExplorerRuntimeControl? runtime = null,
        TimeProvider? timeProvider = null)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        store = new SqliteGameStructureStore(connection);
        corrections = new StructureCorrectionController(store, new GuidExplorationIdSource());
        this.runtime = runtime;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<ExplorerScopeOption> ListScopes()
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT DISTINCT game_id, environment_scope FROM structure_events ORDER BY game_id, environment_scope;";
        using var reader = command.ExecuteReader();
        var scopes = new List<ExplorerScopeOption>();
        while (reader.Read())
        {
            scopes.Add(new ExplorerScopeOption(reader.GetString(0), reader.GetString(1)));
        }
        return scopes;
    }

    public ExplorerScreenSnapshot Load(string gameId, string environmentScope)
    {
        RequireScope(gameId, environmentScope);
        var revision = store.LoadRevision(gameId, environmentScope);
        var live = MatchesRuntime(gameId, environmentScope) ? runtime!.Snapshot : null;
        return Project(gameId, revision, live);
    }

    public ExplorerScreenSnapshot Pause(string gameId, string environmentScope) =>
        Control(gameId, environmentScope, static control => control.Pause());

    public ExplorerScreenSnapshot Step(string gameId, string environmentScope) =>
        Control(gameId, environmentScope, static control => control.Step());

    public ExplorerScreenSnapshot Abandon(string gameId, string environmentScope) =>
        Control(gameId, environmentScope, static control => control.Abandon());

    public ExplorerScreenSnapshot Correct(
        string gameId,
        string environmentScope,
        ExplorerLabelCorrection correction)
    {
        RequireScope(gameId, environmentScope);
        ArgumentNullException.ThrowIfNull(correction);
        var now = timeProvider.GetUtcNow();
        _ = corrections.RelabelNode(
            gameId,
            environmentScope,
            new StructureLabelCorrectionRequest(
                correction.StateId,
                correction.NewLabel,
                correction.Reason,
                now,
                now));
        return Load(gameId, environmentScope);
    }

    private ExplorerScreenSnapshot Control(
        string gameId,
        string environmentScope,
        Action<IHostExplorerRuntimeControl> operation)
    {
        RequireScope(gameId, environmentScope);
        if (!MatchesRuntime(gameId, environmentScope))
        {
            throw new InvalidOperationException("このゲームの探索は現在実行されていません。");
        }

        operation(runtime!);
        return Load(gameId, environmentScope);
    }

    private bool MatchesRuntime(string gameId, string environmentScope) =>
        runtime is not null
        && string.Equals(runtime.Snapshot.GameId, gameId, StringComparison.Ordinal)
        && string.Equals(runtime.Snapshot.EnvironmentScope, environmentScope, StringComparison.Ordinal);

    private static ExplorerScreenSnapshot Project(
        string gameId,
        GameStructureRevision revision,
        HostExplorerRuntimeSnapshot? live)
    {
        var activeNodes = revision.ScreenGraph.Nodes.Where(node => !node.Retired).ToArray();
        var allStates = revision.ScreenGraph.Nodes.Select(node => (node.VerificationState, node.Retired))
            .Concat(revision.ScreenGraph.Edges.Select(edge => (edge.VerificationState, edge.Retired)))
            .Concat(revision.StateFacts.Select(fact => (fact.VerificationState, fact.Retired)))
            .ToArray();
        var verification = new ExplorerVerificationCounts(
            allStates.Count(item => !item.Retired && item.VerificationState == StructureVerificationState.Candidate),
            allStates.Count(item => !item.Retired && item.VerificationState == StructureVerificationState.Replayed),
            allStates.Count(item => !item.Retired && item.VerificationState == StructureVerificationState.Verified),
            allStates.Count(item => item.Retired || item.VerificationState == StructureVerificationState.Retired));
        var frontier = activeNodes
            .Where(node => node.VerificationState == StructureVerificationState.Candidate)
            .Select(node => node.StateId)
            .Concat(revision.ScreenGraph.Edges
                .Where(edge => !edge.Retired && (edge.DestinationStateId is null || edge.VerificationState == StructureVerificationState.Candidate))
                .Select(edge => edge.EdgeId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var nodes = activeNodes
            .OrderBy(node => node.StateId, StringComparer.Ordinal)
            .Select(node => new ExplorerNodeItem(
                node.StateId,
                string.IsNullOrWhiteSpace(node.ProvisionalLabel) ? node.StateId : node.ProvisionalLabel,
                EvidenceLabel(node.VerificationState, node.Retired)))
            .ToArray();

        return new ExplorerScreenSnapshot(
            gameId,
            revision.EnvironmentScope,
            revision.RevisionId,
            activeNodes.Count(node => node.VerificationState is StructureVerificationState.Replayed or StructureVerificationState.Verified),
            activeNodes.Count(node => node.VerificationState == StructureVerificationState.Candidate),
            frontier,
            live?.ActiveProbeLabel ?? "（実行中の一手なし）",
            live?.RiskLabel ?? "（実行中の評価なし）",
            live?.ApprovalReason ?? "（承認待ちなし）",
            live?.RemainingProbeCount ?? 0,
            live?.RemainingElapsedMilliseconds ?? 0,
            live?.RemainingInferenceMilliseconds ?? 0,
            live?.RecoveryPathEdgeIds ?? [],
            live?.StopReasonLabel ?? "停止していません",
            verification,
            nodes,
            live?.CanPause == true,
            live?.CanStep == true,
            live?.CanAbandon == true,
            nodes.Length > 0);
    }

    private static string EvidenceLabel(StructureVerificationState state, bool retired) =>
        retired || state == StructureVerificationState.Retired ? "非対応" : state switch
        {
            StructureVerificationState.Candidate => "未確認",
            StructureVerificationState.Replayed => "強い推定",
            StructureVerificationState.Verified => "確認済み",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static void RequireScope(string gameId, string environmentScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
    }
}
