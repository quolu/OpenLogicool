using System.IO;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Perception;
using OpenLogicool.Persistence;

namespace OpenLogicool.Host;

/// <summary>
/// 探索runtimeの**観測半分だけ**を操作デモ記録へ渡すadapter。
///
/// <see cref="ProductGameExplorerRuntime"/> と同じ部品（observation runtime／stability waiter／
/// <see cref="GameTransitionJudge"/>）をそのまま使い、入力dispatch（Nano action）は持たない。
/// 記録器はこの面しか受け取らないので、構造上、記録中に入力を出せない。
/// </summary>
public sealed class WindowsDemonstrationObservationRuntime(
    IGameObservationRuntime observation,
    IGameInteractionStabilityWaiter stability,
    GameTransitionJudge judge) : IDemonstrationObservationRuntime
{
    private readonly IGameObservationRuntime observation = observation ?? throw new ArgumentNullException(nameof(observation));
    private readonly IGameInteractionStabilityWaiter stability = stability ?? throw new ArgumentNullException(nameof(stability));
    private readonly GameTransitionJudge judge = judge ?? throw new ArgumentNullException(nameof(judge));

    public ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default) =>
        observation.ObserveAsync(cancellationToken);

    public ValueTask<ObservedScene> DiscoverTargetsAsync(
        ObservationResult observationResult, CancellationToken cancellationToken = default) =>
        observation.DiscoverTargetsAsync(observationResult, cancellationToken);

    public ValueTask<GameInteractionStabilityResult> WaitStableAsync(
        ObservedScene before, ExplorationWaitCondition condition, CancellationToken cancellationToken = default) =>
        stability.WaitStableAsync(before, condition, cancellationToken);

    public GameTransitionComparison Compare(ObservedScene before, GameInteractionStabilityResult after) =>
        judge.Compare(before, after);
}

/// <summary>
/// 対象gameの実windowを、操作デモ記録が使える観測面とOS入力取得へ束ねるWindows合成。
///
/// 探索・macro実行と**同じ**capture（WGC）・recognizer・target discoveryを使う。ここだけ別の
/// discoveryにすると同じ画面が別のstateとして同定され、記録から導出したrouteが探索で育てた
/// structureへ繋がらなくなる。Nano・SendInput・Computer Useはこの経路に一切登場しない。
/// </summary>
public sealed class WindowsDemonstrationLiveSessionFactory(
    string databasePath,
    WindowsFoundryLocalRuntimeResolver? foundry = null) : IDemonstrationLiveSessionFactory
{
    private readonly string databasePath = Path.GetFullPath(databasePath);
    private readonly WindowsFoundryLocalRuntimeResolver foundry = foundry ?? new();

    public DemonstrationLiveSession Create(string targetProcessName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProcessName);
        var target = WindowsGameTargetLocator.Locate(targetProcessName);
        string environment;
        using (var connection = Open())
        {
            environment = DemonstrationEnvironmentScope.Resolve(
                connection, target.ProcessName, target.Bounds.Width, target.Bounds.Height);
        }

        var connections = new MacroSqliteConnectionFactory(databasePath);
        var structures = new MacroGameStructureStore(connections);
        var profiles = new MacroLearnedSceneProfileStore(connections);
        var lazyFoundry = new WindowsLazyFoundryControlDiscoveryProvider(foundry.ResolvePreferredVisionModel);
        var unusedFoundry = new FoundryLocalRuntime(new Uri("http://127.0.0.1:1"), "lazy-not-resolved");

        IDisposable? visionResource = null;
        try
        {
            var (discovery, resource) = WindowsProductGameExplorerComposition.CreateTargetDiscovery(
                target.ProcessName,
                environment,
                () => structures.LoadRevision(target.ProcessName, environment).RevisionId,
                unusedFoundry.Endpoint,
                unusedFoundry.ModelId,
                profiles,
                targetIntent: null,
                includeVisualTargets: true,
                controlDiscoveryProvider: lazyFoundry,
                controlDiscoveryResource: null);
            visionResource = resource;

            var frameSource = new WindowsWgcGameFrameSource(
                target.Window,
                $"window:demonstration:{target.ProcessId}",
                TimeSpan.FromSeconds(10));
            var evidenceDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenLogicool", "demonstration-evidence", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            var observation = new ProductGameObservationRuntime(
                frameSource,
                new LiveObservationSource(new ZeroSeedFrameStateRecognizer()),
                discovery,
                new LocalPngGameFrameEvidenceSink(evidenceDirectory, new WindowsGameFramePngEncoder()));
            var runtime = new WindowsDemonstrationObservationRuntime(
                observation,
                new GameInteractionStabilityRuntime(
                    observation, new SystemGameInteractionClock(), TimeSpan.FromMilliseconds(100)),
                new GameTransitionJudge());

            // 座標の正規化は、操作時のwindow位置をその都度読む（記録中に窓が動いても追従する）。
            var mapper = new WindowsGameInteractionCoordinateMapper(
                () => WindowsGameTargetLocator.Locate(target.ProcessName).Bounds);
            var collector = new WindowsDemonstrationInputCollector();

            return new DemonstrationLiveSession(
                ResolveApplicationPath(target),
                $"window:demonstration:{target.ProcessId}",
                environment,
                runtime,
                collector,
                point => mapper.TryMapScreenToNormalized(point.X, point.Y),
                new CompositeResource(visionResource, lazyFoundry));
        }
        catch
        {
            visionResource?.Dispose();
            lazyFoundry.Dispose();
            throw;
        }
    }

    private static string ResolveApplicationPath(WindowsGameTarget target) =>
        ForegroundAppTracker.GetProcessFullPath((uint)target.ProcessId)
        ?? throw new InvalidOperationException(
            $"対象process '{target.ProcessName}' の実行file pathを取得できません。");

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private sealed class CompositeResource(params IDisposable?[] resources) : IDisposable
    {
        public void Dispose()
        {
            foreach (var resource in resources)
            {
                resource?.Dispose();
            }
        }
    }
}

/// <summary>
/// 操作デモを記録するenvironment scopeの決め方。
///
/// そのgameで既に使われているscopeへ合わせる。新しいscopeを勝手に作ると、記録から導出した
/// routeが探索で育てた既存structureと別世界になり、合成も再生も噛み合わなくなる。
/// 解像度が一致するscopeを最優先し、無ければ直近のscope、1件も無いときだけ新規に作る。
/// </summary>
public static class DemonstrationEnvironmentScope
{
    public static string Resolve(SqliteConnection connection, string gameId, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT environment_scope, MAX(event_sequence) FROM structure_events WHERE game_id = $game "
            + "GROUP BY environment_scope ORDER BY MAX(event_sequence) DESC;";
        command.Parameters.AddWithValue("$game", gameId);
        using var reader = command.ExecuteReader();
        var resolution = $"{width}x{height}";
        string? first = null;
        while (reader.Read())
        {
            var scope = reader.GetString(0);
            first ??= scope;
            if (scope.Contains(resolution, StringComparison.OrdinalIgnoreCase))
            {
                return scope;
            }
        }
        return first ?? $"{gameId}:{resolution}";
    }
}
