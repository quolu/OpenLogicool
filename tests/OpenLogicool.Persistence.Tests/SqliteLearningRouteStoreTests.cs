using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Persistence.Tests;

public sealed class SqliteLearningRouteStoreTests : IDisposable
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(), $"openlogicool-learning-route-{Guid.NewGuid():N}.db");

    [Fact]
    public void Append_keeps_immutable_parent_chain_and_survives_reopen()
    {
        LearningRouteRevision first;
        LearningRouteRevision second;
        using (var connection = OpenMigrated())
        {
            var store = new SqliteLearningRouteStore(connection);
            first = store.Append(Draft(null, ["edge-1", "edge-2"], LearningRouteAuthor.Ai));
            second = store.Append(Draft(first.VersionId, ["edge-1", "edge-3"], LearningRouteAuthor.User));
        }

        using (var connection = OpenMigrated())
        {
            var store = new SqliteLearningRouteStore(connection);
            var revisions = store.ReadRevisions("route-1");
            Assert.Equal([first.VersionId, second.VersionId], revisions.Select(item => item.VersionId));
            Assert.Equal(first.EdgeIds, revisions[0].EdgeIds);
            Assert.Equal(second.EdgeIds, revisions[1].EdgeIds);
            Assert.Equal(second.VersionId, store.LoadLatest("route-1")?.VersionId);
            Assert.Equal(["route-1"], store.ListRouteIds("game-1", "env-1"));
        }
    }

    [Fact]
    public void Append_rejects_stale_parent_and_invalid_document()
    {
        using var connection = OpenMigrated();
        var store = new SqliteLearningRouteStore(connection);
        var first = store.Append(Draft(null, ["edge-1"], LearningRouteAuthor.Ai));

        Assert.Throws<InvalidOperationException>(() =>
            store.Append(Draft(null, ["edge-2"], LearningRouteAuthor.User)));
        Assert.Throws<ArgumentException>(() =>
            store.Append(Draft(first.VersionId, [], LearningRouteAuthor.User)));
    }

    private LearningRouteDraft Draft(
        string? parentVersionId,
        IReadOnlyList<string> edgeIds,
        LearningRouteAuthor author) =>
        new(
            ContractSchemaVersions.Revision03,
            "route-1",
            parentVersionId,
            "game-1",
            "env-1",
            "structure-1",
            "日課を完了する",
            edgeIds,
            author,
            author == LearningRouteAuthor.User ? "2番目を短い経路へ変更" : null,
            author == LearningRouteAuthor.User ? "利用者訂正" : "探索結果から作成",
            LearningRouteStatus.Draft,
            DateTimeOffset.UnixEpoch.AddMinutes(parentVersionId is null ? 1 : 2));

    private SqliteConnection OpenMigrated()
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
