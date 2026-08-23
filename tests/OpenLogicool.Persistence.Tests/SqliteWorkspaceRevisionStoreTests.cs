using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Persistence.Tests;

public sealed class SqliteWorkspaceRevisionStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"openlogicool-workspace-revision-{Guid.NewGuid():N}.db");

    private static WorkspaceDocument Document(
        string workspaceId,
        string profileRevision = "rev-1",
        WorkspaceG13LcdSetting? g13Lcd = null) =>
        new(
            ContractSchemaVersions.Revision01,
            workspaceId,
            profileRevision,
            MappingRevision: "map-1",
            Actions: [new WorkspaceActionEntry("dodge", "回避", ["Key:Space"])],
            Devices:
            [
                new WorkspaceDeviceLayout("G13", "base", ["base"], [], []),
            ],
            Bindings: [new WorkspaceActionBinding("dodge", "G13", "G1", "base")],
            G13Lcd: g13Lcd);

    private SqliteConnection OpenMigrated()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    [Fact]
    public void Append_numbers_revisions_sequentially_per_workspace()
    {
        using var connection = OpenMigrated();
        var store = new SqliteWorkspaceRevisionStore(connection);

        Assert.Equal(1, store.Append(Document("ws-a", "rev-1"), "2026-08-16T00:00:00Z"));
        Assert.Equal(2, store.Append(Document("ws-a", "rev-2"), "2026-08-16T00:01:00Z"));
        Assert.Equal(1, store.Append(Document("ws-b"), "2026-08-16T00:02:00Z"));

        var revisions = store.ListRevisions("ws-a");
        Assert.Equal([1L, 2L], revisions.Select(revision => revision.RevisionNumber));
        Assert.Equal(["rev-1", "rev-2"], revisions.Select(revision => revision.Document.ProfileRevision));
        Assert.Equal("2026-08-16T00:01:00Z", revisions[1].SavedAtUtc);
    }

    [Fact]
    public void Revisions_survive_reopening_the_database()
    {
        var lcd = new WorkspaceG13LcdSetting(
            WorkspaceG13LcdContentKind.Text,
            Convert.ToBase64String(new byte[960]),
            null,
            "NIKKE");
        using (var connection = OpenMigrated())
        {
            new SqliteWorkspaceRevisionStore(connection).Append(Document("ws-a", g13Lcd: lcd), "2026-08-16T00:00:00Z");
        }

        using (var connection = OpenMigrated())
        {
            var restored = new SqliteWorkspaceRevisionStore(connection).ListRevisions("ws-a");

            var revision = Assert.Single(restored);
            Assert.Equal(
                System.Text.Json.JsonSerializer.Serialize(Document("ws-a", g13Lcd: lcd)),
                System.Text.Json.JsonSerializer.Serialize(revision.Document));
            Assert.Equal(lcd, revision.Document.G13Lcd);
        }
    }

    [Fact]
    public void Unknown_schema_version_is_rejected_on_append()
    {
        using var connection = OpenMigrated();
        var store = new SqliteWorkspaceRevisionStore(connection);

        Assert.Throws<ArgumentException>(() =>
            store.Append(Document("ws-a") with { SchemaVersion = "9.9.9" }, "2026-08-16T00:00:00Z"));
    }

    [Fact]
    public void Unknown_schema_version_in_a_stored_row_fails_the_read()
    {
        using var connection = OpenMigrated();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO workspace_revisions (workspace_id, revision_number, schema_version, saved_at_utc, document_json)
                VALUES ('ws-a', 1, '0.0.1', '2026-08-16T00:00:00Z', '{}');
                """;
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() =>
            new SqliteWorkspaceRevisionStore(connection).ListRevisions("ws-a"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
