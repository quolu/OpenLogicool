using Microsoft.Data.Sqlite;
using System.IO;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Persistence;

namespace OpenLogicool.Host;

/// <summary>
/// awaitを跨ぐmacro runtime向けSQLite port。各同期操作が自分のconnectionを所有し、
/// Microsoft.Data.Sqlite objectを別threadへ持ち越さない。
/// </summary>
internal sealed class MacroSqliteConnectionFactory(string databasePath)
{
    private readonly string databasePath = Path.GetFullPath(databasePath);

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }
}

internal sealed class MacroGameStructureStore(MacroSqliteConnectionFactory connections) : IGameStructureStore
{
    public StructureEvent Append(StructureEventDraft draft, string? expectedParentRevisionId, DateTimeOffset persistedUtc)
    { using var db = connections.Open(); return new SqliteGameStructureStore(db).Append(draft, expectedParentRevisionId, persistedUtc); }
    public IReadOnlyList<StructureEvent> ReadEvents(string gameId, string environmentScope)
    { using var db = connections.Open(); return new SqliteGameStructureStore(db).ReadEvents(gameId, environmentScope); }
    public IReadOnlyList<string> ListGameIds()
    { using var db = connections.Open(); return new SqliteGameStructureStore(db).ListGameIds(); }
    public GameStructureRevision LoadRevision(string gameId, string environmentScope)
    { using var db = connections.Open(); return new SqliteGameStructureStore(db).LoadRevision(gameId, environmentScope); }
    public StructureKnowledgePackExport Export(string gameId, string environmentScope, DateTimeOffset createdUtc)
    { using var db = connections.Open(); return new SqliteGameStructureStore(db).Export(gameId, environmentScope, createdUtc); }
}

internal sealed class MacroLearningRouteStore(MacroSqliteConnectionFactory connections) : ILearningRouteStore
{
    public LearningRouteRevision Append(LearningRouteDraft draft)
    { using var db = connections.Open(); return new SqliteLearningRouteStore(db).Append(draft); }
    public IReadOnlyList<LearningRouteRevision> ReadRevisions(string routeId)
    { using var db = connections.Open(); return new SqliteLearningRouteStore(db).ReadRevisions(routeId); }
    public LearningRouteRevision? LoadLatest(string routeId)
    { using var db = connections.Open(); return new SqliteLearningRouteStore(db).LoadLatest(routeId); }
    public IReadOnlyList<string> ListRouteIds(string gameId, string environmentScope)
    { using var db = connections.Open(); return new SqliteLearningRouteStore(db).ListRouteIds(gameId, environmentScope); }
}

internal sealed class MacroLearnedSceneProfileStore(MacroSqliteConnectionFactory connections) : ILearnedSceneProfileStore
{
    public void Upsert(LearnedSceneProfileDocument document)
    { using var db = connections.Open(); new SqliteLearnedSceneProfileStore(db).Upsert(document); }
    public LearnedSceneProfileDocument? Load(string gameId, string environmentScope)
    { using var db = connections.Open(); return new SqliteLearnedSceneProfileStore(db).Load(gameId, environmentScope); }
}

internal sealed class MacroRunJournalStore(MacroSqliteConnectionFactory connections) : IRunJournalStore
{
    public void Append(RunEvent runEvent)
    { using var db = connections.Open(); new SqliteRunJournalStore(db).Append(runEvent); }
    public IReadOnlyList<RunEvent> ReadRun(string runId)
    { using var db = connections.Open(); return new SqliteRunJournalStore(db).ReadRun(runId); }
    public IReadOnlyList<string> ListRunIds()
    { using var db = connections.Open(); return new SqliteRunJournalStore(db).ListRunIds(); }
    public IReadOnlyList<ExpiredRunPreview> PreviewExpiredRuns(DateTimeOffset asOfUtc, int retentionDays)
    { using var db = connections.Open(); return new SqliteRunJournalStore(db).PreviewExpiredRuns(asOfUtc, retentionDays); }
    public void DeleteRun(string runId)
    { using var db = connections.Open(); new SqliteRunJournalStore(db).DeleteRun(runId); }
}
