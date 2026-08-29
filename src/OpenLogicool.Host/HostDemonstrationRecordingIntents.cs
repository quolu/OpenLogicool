using Microsoft.Data.Sqlite;
using System.IO;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

/// <summary>
/// 操作デモ記録の開始／停止／状態／session一覧と、記録から作るmacroをまとめたHost境界。
/// 対象game選択はPhase 13のmacro automation intentsと同じ<see cref="MacroTargetSettingsStore"/>を、
/// 記録／再生排他は同じ<see cref="DemonstrationRecordingGate"/>を共有し、別の実行coordinatorを作らない。
/// </summary>
public sealed class HostDemonstrationRecordingIntents : IDemonstrationRecordingIntents, IDisposable
{
    private readonly string databasePath;
    private readonly IDemonstrationLiveSessionFactory liveSessionFactory;
    private readonly MacroTargetSettingsStore targetSettings;
    private readonly DemonstrationRecordingGate gate;
    private readonly ExplorationWaitCondition waitCondition;
    private readonly TimeProvider time;
    private readonly object stateGate = new();
    private DemonstrationRecorder? recorder;
    private DemonstrationLiveSession? liveSession;
    private DemonstrationRecordingPump? pump;
    private bool disposed;

    public HostDemonstrationRecordingIntents(
        string databasePath,
        IDemonstrationLiveSessionFactory liveSessionFactory,
        DemonstrationRecordingGate recordingGate,
        ExplorationWaitCondition? waitCondition = null,
        TimeProvider? timeProvider = null)
    {
        this.databasePath = Path.GetFullPath(databasePath);
        this.liveSessionFactory = liveSessionFactory ?? throw new ArgumentNullException(nameof(liveSessionFactory));
        gate = recordingGate ?? throw new ArgumentNullException(nameof(recordingGate));
        targetSettings = MacroTargetSettingsStore.ForDatabase(this.databasePath);
        this.waitCondition = waitCondition
            ?? new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000);
        time = timeProvider ?? TimeProvider.System;
    }

    public async Task<DemonstrationSessionSummary> StartAsync(string goal, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (stateGate)
        {
            if (recorder is not null)
            {
                throw new InvalidOperationException("既に記録中です。");
            }
        }

        var targetSetting = targetSettings.Load()
            ?? throw new InvalidOperationException("先にアプリ側でマクロ対象game profileを選んでください。");
        var live = liveSessionFactory.Create(targetSetting.ProcessName);
        try
        {
            var connection = OpenAndMigrate();
            try
            {
                var store = new SqliteDemonstrationSessionStore(connection);
                var newRecorder = new DemonstrationRecorder(store, live.Runtime, live.Normalize, gate, waitCondition);
                var draft = new DemonstrationSessionDraft(
                    ContractSchemaVersions.Revision03,
                    $"demo:{Guid.NewGuid():N}",
                    targetSetting.ProcessName,
                    live.EnvironmentScope,
                    goal,
                    live.TargetApplicationPath,
                    live.TargetWindowSourceId,
                    "recorder-1.0.0",
                    time.GetUtcNow());
                var record = await newRecorder.StartAsync(draft, cancellationToken).ConfigureAwait(false);
                var newPump = new DemonstrationRecordingPump(newRecorder);
                live.Collector.Start(newPump);
                lock (stateGate)
                {
                    recorder = newRecorder;
                    liveSession = live;
                    pump = newPump;
                    this.connection = connection;
                }
                return Summarize(record);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
        catch
        {
            live.Dispose();
            throw;
        }
    }

    public async Task<DemonstrationSessionSummary> StopAsync(CancellationToken cancellationToken = default)
    {
        DemonstrationRecorder active;
        SqliteConnection activeConnection;
        DemonstrationLiveSession activeLive;
        DemonstrationRecordingPump activePump;
        lock (stateGate)
        {
            active = recorder ?? throw new InvalidOperationException("記録が開始されていません。");
            activeConnection = connection!;
            activeLive = liveSession!;
            activePump = pump!;
        }

        try
        {
            // 新しいOS edgeを止めてから、既に受理済みの分を処理し終えるまで待つ。
            // 停止eventを先に積むと、直前に押下だけ処理済みで解放が未処理の入力を
            // 「押しっぱなしのまま停止」として誤って扱ってしまう。
            activeLive.Collector.Stop();
            await activePump.DrainAndStopAsync().ConfigureAwait(false);
            var record = active.StopAsync("利用者が記録を停止しました。", time.GetUtcNow());
            return Summarize(record);
        }
        finally
        {
            lock (stateGate)
            {
                recorder = null;
                liveSession = null;
                pump = null;
                connection = null;
            }
            activePump.Dispose();
            activeLive.Dispose();
            activeConnection.Dispose();
        }
    }

    public DemonstrationRecordingStatus Status()
    {
        lock (stateGate)
        {
            if (recorder is null)
            {
                return new DemonstrationRecordingStatus(DemonstrationRecorderStatus.Idle, null, 0, 0, 0, 0, 0);
            }

            var counters = recorder.Counters;
            return new DemonstrationRecordingStatus(
                recorder.Status,
                recorder.SessionId,
                recorder.HeldPressCount,
                counters.IgnoredWhilePaused,
                counters.IgnoredOutsideClientFrame,
                counters.UnpairedReleases,
                counters.DiscardedHeldPresses);
        }
    }

    public IReadOnlyList<DemonstrationSessionSummary> ListSessions()
    {
        var targetSetting = targetSettings.Load();
        if (targetSetting is null)
        {
            return [];
        }

        using var connection = OpenAndMigrate();
        var store = new SqliteDemonstrationSessionStore(connection);
        return ListEnvironmentScopes(connection, targetSetting.ProcessName)
            .SelectMany(environmentScope => store.ListSessionIds(targetSetting.ProcessName, environmentScope))
            .Select(sessionId => store.Load(sessionId))
            .Where(record => record is not null)
            .Select(record => Summarize(record!))
            .OrderByDescending(summary => summary.StartedUtc)
            .ToArray();
    }

    private static IReadOnlyList<string> ListEnvironmentScopes(SqliteConnection connection, string gameId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT environment_scope FROM demonstration_sessions WHERE game_id = $gameId;";
        command.Parameters.AddWithValue("$gameId", gameId);
        using var reader = command.ExecuteReader();
        var scopes = new List<string>();
        while (reader.Read())
        {
            scopes.Add(reader.GetString(0));
        }

        return scopes;
    }

    public MacroCatalogItem CreateMacroFromSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        using var connection = OpenAndMigrate();
        var sessionStore = new SqliteDemonstrationSessionStore(connection);
        var session = sessionStore.Load(sessionId)
            ?? throw new InvalidOperationException($"操作デモ原本 '{sessionId}' がありません。");

        var structures = new SqliteGameStructureStore(connection);
        var routes = new SqliteLearningRouteStore(connection);
        var idRegistry = new InMemoryStableStructureIdRegistry();
        var eventIds = new GuidExplorationIdSource();
        var knowledge = new StructureKnowledgeController(structures, idRegistry, eventIds);
        var runJournal = new RunJournal(new SqliteRunJournalStore(connection), new NoopEngineeringLog());
        var attemptGate = new AttemptDispatchGate(runJournal);
        var policy = new ExplorationPolicy(
            ContractSchemaVersions.Revision03,
            $"demo-policy:{Guid.NewGuid():N}",
            session.Session.GameId,
            session.Session.TargetWindowSourceId,
            session.Session.EnvironmentScope,
            "demonstration",
            GameInteractionOperations.InputOperations,
            [],
            new ExplorationBudget(ContractSchemaVersions.Revision03, int.MaxValue, long.MaxValue, long.MaxValue),
            "owner-delegated-demonstration",
            "none",
            new ExplorationStopPolicy(ContractSchemaVersions.Revision03, 1_000),
            []);
        var coordinator = new ExplorationCoordinator(
            structures,
            runJournal,
            attemptGate,
            new ExplorationRunBinding(
                ContractSchemaVersions.Revision03,
                $"demo-compile:{Guid.NewGuid():N}",
                session.Session.GameId,
                session.Session.EnvironmentScope,
                "demonstration-route-compiler",
                "demonstration-route-compiler-v1",
                1),
            policy,
            eventIds);
        var structureLearner = new GameInteractionStructureLearner(
            structures, knowledge, idRegistry, eventIds, coordinator, session.Session.GameId, session.Session.EnvironmentScope);
        var compiler = new DemonstrationRouteCompiler(structures, structureLearner, routes, time);

        var result = compiler.Compile(session);
        var revision = result.Route;
        return new MacroCatalogItem(
            revision.RouteId,
            revision.VersionId,
            revision.GameId,
            revision.EnvironmentScope,
            revision.Goal,
            revision.RevisionNumber,
            revision.EdgeIds.Count,
            revision.Status.ToString());
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lock (stateGate)
        {
            pump?.Dispose();
            liveSession?.Dispose();
            connection?.Dispose();
            pump = null;
            liveSession = null;
            recorder = null;
            connection = null;
        }
    }

    private SqliteConnection? connection;

    private SqliteConnection OpenAndMigrate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var newConnection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        newConnection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(newConnection);
        return newConnection;
    }

    private static DemonstrationSessionSummary Summarize(DemonstrationSessionRecord record) => new(
        record.Session.SessionId,
        record.Session.Goal,
        record.Session.GameId,
        record.Session.EnvironmentScope,
        record.State,
        record.Events.Count(item => item.Kind == DemonstrationEventKind.Operation),
        record.Session.StartedUtc);

    private sealed class NoopEngineeringLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry)
        {
        }
    }
}
