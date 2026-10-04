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
    private readonly IDemonstrationTimelineReanalysis reanalysis;
    private DemonstrationRecordingStatus? analysisStatus;
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
        TimeProvider? timeProvider = null,
        IDemonstrationTimelineReanalysis? reanalysis = null)
    {
        this.databasePath = Path.GetFullPath(databasePath);
        this.liveSessionFactory = liveSessionFactory ?? throw new ArgumentNullException(nameof(liveSessionFactory));
        gate = recordingGate ?? throw new ArgumentNullException(nameof(recordingGate));
        targetSettings = MacroTargetSettingsStore.ForDatabase(this.databasePath);
        this.waitCondition = waitCondition
            ?? new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000);
        time = timeProvider ?? TimeProvider.System;
        this.reanalysis = reanalysis ?? new WindowsDemonstrationTimelineReanalysis(this.databasePath);
    }

    public async Task<DemonstrationSessionSummary> StartAsync(string goal, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (stateGate)
        {
            if (liveSession is not null || analysisStatus is not null)
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
                    live.Timeline is null ? "recorder-1.0.0" : "recorder-2.0.0",
                    time.GetUtcNow());
                if (live.Timeline is { } timeline)
                {
                    if (!gate.TryBeginRecording(out var refusal)) throw new InvalidOperationException(refusal);
                    try
                    {
                        await timeline.StartAsync(draft, cancellationToken).ConfigureAwait(false);
                        live.Collector.Start(timeline);
                        lock (stateGate)
                        {
                            liveSession = live;
                            this.connection = connection;
                        }
                        return Summarize(new DemonstrationSessionRecord(draft, DemonstrationSessionState.Recording, null, []));
                    }
                    catch
                    {
                        gate.EndRecording();
                        throw;
                    }
                }
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
        if (liveSession?.Timeline is not null)
        {
            var timelineLive = liveSession;
            var timelineConnection = connection!;
            try
            {
                timelineLive.Collector.Stop();
                var result = await timelineLive.Timeline.StopAndAnalyzeAsync(
                    new SqliteDemonstrationSessionStore(timelineConnection), cancellationToken).ConfigureAwait(false);
                return Summarize(result);
            }
            finally
            {
                lock (stateGate)
                {
                    liveSession = null;
                    connection = null;
                }
                timelineLive.Dispose();
                timelineConnection.Dispose();
                gate.EndRecording();
            }
        }
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
            if (analysisStatus is not null) return analysisStatus;
            if (liveSession?.Timeline is { } timeline) return timeline.Status();
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
            .OrderByDescending(summary => summary.ReanalyzedUtc ?? summary.StartedUtc)
            .ToArray();
    }

    public IReadOnlyList<DemonstrationStepSummary> ListSteps(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        using var connection = OpenAndMigrate();
        var store = new SqliteDemonstrationSessionStore(connection);
        var session = store.Load(sessionId)
            ?? throw new InvalidOperationException($"操作デモ原本 '{sessionId}' がありません。");
        var stepNumber = 0;
        return session.Events
            .Where(item => item.Kind == DemonstrationEventKind.Operation)
            .Select(item =>
            {
                stepNumber++;
                var operation = item.Operation!;
                return new DemonstrationStepSummary(
                    stepNumber,
                    OperationLabel(operation.Operation),
                    TransitionLabel(operation.Comparison.Judgement));
            })
            .ToArray();
    }

    public async Task<DemonstrationSessionSummary> ReanalyzeAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!gate.TryBeginRecording(out var refusal)) throw new InvalidOperationException(refusal);
        try
        {
            using var connection = OpenAndMigrate();
            var store = new SqliteDemonstrationSessionStore(connection);
            var source = store.Load(sessionId) ?? throw new InvalidOperationException("選択したデモがありません。");
            if (source.Session.SourceSessionId is { } originalId)
                source = store.Load(originalId) ?? throw new InvalidOperationException("再解析元のデモがありません。");
            lock (stateGate) analysisStatus = new(DemonstrationRecorderStatus.Analyzing, source.Session.SessionId,
                0, 0, 0, 0, 0, 0, source.Events.Count(item => item.Operation is not null));
            var result = await reanalysis.AnalyzeAsync(source, store, count =>
            {
                lock (stateGate) analysisStatus = analysisStatus! with { AnalyzedOperations = count };
            }, cancellationToken).ConfigureAwait(false);
            return Summarize(result);
        }
        finally
        {
            lock (stateGate) analysisStatus = null;
            gate.EndRecording();
        }
    }

    private static string OperationLabel(string operation) => operation switch
    {
        GameInteractionOperations.Click => "クリック",
        GameInteractionOperations.KeyTap => "キー入力",
        GameInteractionOperations.Scroll => "スクロール",
        GameInteractionOperations.Drag => "ドラッグ",
        GameInteractionOperations.Hover => "ホバー",
        _ => operation,
    };

    private static string TransitionLabel(GameTransitionJudgement judgement) => judgement switch
    {
        GameTransitionJudgement.Moved => "画面が変わった",
        GameTransitionJudgement.Stayed => "変化なし",
        GameTransitionJudgement.Undetermined => "判定できず",
        _ => judgement.ToString(),
    };

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

        var compiler = CreateCompiler(connection, session);

        var result = compiler.Compile(session);
        return ProjectMacro(result.Route);
    }

    private DemonstrationRouteCompiler CreateCompiler(SqliteConnection connection, DemonstrationSessionRecord session)
    {
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
        return new DemonstrationRouteCompiler(structures, structureLearner, routes, time);

    }

    public DemonstrationCandidate? LoadCandidate(string sessionId)
    {
        using var connection = OpenAndMigrate();
        var session = new SqliteDemonstrationSessionStore(connection).Load(sessionId)
            ?? throw new InvalidOperationException("操作デモ原本がありません。");
        var route = new SqliteLearningRouteStore(connection).LoadLatest(DemonstrationGoalRouteIds.Create(
            session.Session.GameId, session.Session.EnvironmentScope, session.Session.Goal));
        return route?.RecordedSteps is not { Count: > 0 } steps || steps[0].OriginalSessionId != sessionId
            ? null : ProjectCandidate(connection, route);
    }

    public DemonstrationCandidate ReviewStep(string sessionId, string versionId, int stepNumber,
        GameTransitionJudgement expected, string reason)
    {
        if (expected is not (GameTransitionJudgement.Moved or GameTransitionJudgement.Stayed))
            throw new ArgumentException("期待結果は画面変化あり／なしを指定してください。", nameof(expected));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        using var connection = OpenAndMigrate();
        var route = LoadEditableCandidate(connection, sessionId, versionId, stepNumber);
        var detail = ProjectCandidate(connection, route).Steps[stepNumber - 1];
        if (!File.Exists(detail.BeforeImagePath) || !File.Exists(detail.AfterImagePath))
            throw new InvalidOperationException("前後画像が揃っていません。この一手だけを再記録してください。");
        var steps = route.RecordedSteps!.ToArray();
        steps[stepNumber - 1] = steps[stepNumber - 1] with
        {
            ExpectedJudgement = expected,
            EdgeId = expected == GameTransitionJudgement.Moved ? steps[stepNumber - 1].EdgeId : null,
            ReviewReason = $"利用者の画像確認: {reason.Trim()}",
        };
        return SaveCandidate(connection, route, steps, $"手順 {stepNumber} の期待結果だけを利用者が指定");
    }

    public DemonstrationCandidate ReplaceStep(string sessionId, string versionId, int stepNumber, string replacementSessionId)
    {
        using var connection = OpenAndMigrate();
        var route = LoadEditableCandidate(connection, sessionId, versionId, stepNumber);
        var replacement = new SqliteDemonstrationSessionStore(connection).Load(replacementSessionId)
            ?? throw new InvalidOperationException("差し替える記録がありません。");
        if (replacement.Session.GameId != route.GameId || replacement.Session.EnvironmentScope != route.EnvironmentScope)
            throw new InvalidOperationException("差し替える記録のゲームまたは画面サイズが一致しません。");
        var steps = route.RecordedSteps!.ToArray();
        steps[stepNumber - 1] = CreateCompiler(connection, replacement).CompileStep(replacement) with
        {
            OriginalSessionId = steps[stepNumber - 1].OriginalSessionId,
        };
        return SaveCandidate(connection, route, steps, $"手順 {stepNumber} だけを再記録で差し替え");
    }

    private static LearningRouteRevision LoadEditableCandidate(SqliteConnection connection, string sessionId,
        string versionId, int stepNumber)
    {
        var session = new SqliteDemonstrationSessionStore(connection).Load(sessionId)
            ?? throw new InvalidOperationException("操作デモ原本がありません。");
        var route = new SqliteLearningRouteStore(connection).LoadLatest(DemonstrationGoalRouteIds.Create(
            session.Session.GameId, session.Session.EnvironmentScope, session.Session.Goal))
            ?? throw new InvalidOperationException("先にこのデモから候補を作成してください。");
        if (route.VersionId != versionId)
            throw new InvalidOperationException("保存版が更新されています。操作一覧を読み直してください。");
        if (route.RecordedSteps is null || stepNumber < 1 || stepNumber > route.RecordedSteps.Count)
            throw new ArgumentOutOfRangeException(nameof(stepNumber));
        if (route.RecordedSteps[0].OriginalSessionId != sessionId)
            throw new InvalidOperationException("この候補は別の記録から作成されています。対象の記録を選び直してください。");
        return route;
    }

    private DemonstrationCandidate SaveCandidate(SqliteConnection connection, LearningRouteRevision route,
        IReadOnlyList<DemonstrationRouteStep> steps, string reason)
    {
        var structure = new SqliteGameStructureStore(connection).LoadRevision(route.GameId, route.EnvironmentScope);
        var saved = new SqliteLearningRouteStore(connection).Append(new LearningRouteDraft(route.SchemaVersion,
            route.RouteId, route.VersionId, route.GameId, route.EnvironmentScope, structure.RevisionId, route.Goal,
            steps.Where(step => step.EdgeId is not null).Select(step => step.EdgeId!).ToArray(),
            LearningRouteAuthor.User, route.UserInstruction, reason,
            steps.Any(step => step.ExpectedJudgement is null) ? LearningRouteStatus.Draft : LearningRouteStatus.Compiled,
            time.GetUtcNow(), steps));
        return ProjectCandidate(connection, saved);
    }

    private static DemonstrationCandidate ProjectCandidate(SqliteConnection connection, LearningRouteRevision route)
    {
        var store = new SqliteDemonstrationSessionStore(connection);
        var sources = route.RecordedSteps!.Select(step => step.SessionId).Distinct().ToDictionary(id => id,
            id => store.Load(id) ?? throw new InvalidOperationException("手順の原本がありません。"));
        var steps = route.RecordedSteps!.Select((step, index) =>
        {
            var operation = sources[step.SessionId].Events.Single(item => item.Operation?.OperationId == step.OperationId).Operation!;
            var after = operation.After.StableScene ?? operation.After.Observations.LastOrDefault();
            return new DemonstrationCandidateStep(index + 1, OperationLabel(step.Operation),
                step.ExpectedJudgement is null ? "確認待ち" : step.EdgeId is null ? "利用者が期待結果を指定" : "画面変化を確認",
                step.ReviewReason, operation.Before.Frame.Artifact?.LocalPath, after?.Frame.Artifact?.LocalPath);
        }).ToArray();
        return new DemonstrationCandidate(ProjectMacro(route), steps);
    }

    private static MacroCatalogItem ProjectMacro(LearningRouteRevision route) => new(route.RouteId, route.VersionId,
        route.GameId, route.EnvironmentScope, route.Goal, route.RevisionNumber, route.StepCount,
        route.PendingStepCount > 0 ? $"確認待ち {route.PendingStepCount}件" : "再生候補");

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
            var ownsTimeline = liveSession?.Timeline is not null;
            liveSession?.Dispose();
            if (ownsTimeline) gate.EndRecording();
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
        record.Session.StartedUtc,
        record.Session.ReanalyzedUtc);

    private sealed class NoopEngineeringLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry)
        {
        }
    }
}
