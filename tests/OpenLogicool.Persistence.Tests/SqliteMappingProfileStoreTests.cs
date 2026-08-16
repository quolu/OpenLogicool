using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Persistence.Tests;

public sealed class SqliteMappingProfileStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"openlogicool-profile-store-{Guid.NewGuid():N}.db");

    private static MappingProfileDocument Document(string profileId, string revision = "rev-1") =>
        new(
            ContractSchemaVersions.Revision01,
            profileId,
            DeviceKind: "G600",
            ProfileRevision: revision,
            MappingRevision: "map-1",
            DefaultLayerId: "normal",
            LayerIds: ["normal", "shift"],
            LatchSelectors: [],
            HoldSelectors: [new LayerSelectorEntry("G6", "shift")],
            Bindings: [new MappingBindingEntry("G9", "normal", ["Key:F13"])]);

    private SqliteConnection OpenMigrated()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    [Fact]
    public void Documents_survive_reopening_the_database()
    {
        using (var connection = OpenMigrated())
        {
            var store = new SqliteMappingProfileStore(connection);
            store.Upsert(Document("profile-b"));
            store.Upsert(Document("profile-a"));
        }

        using (var connection = OpenMigrated())
        {
            var restored = new SqliteMappingProfileStore(connection).ListAll();

            Assert.Equal(["profile-a", "profile-b"], restored.Select(document => document.ProfileId));
            // record の collection member は参照比較になるため、構造比較は JSON 正規形で行う
            Assert.Equal(
                System.Text.Json.JsonSerializer.Serialize(Document("profile-a")),
                System.Text.Json.JsonSerializer.Serialize(restored[0]));
        }
    }

    [Fact]
    public void Upsert_overwrites_by_profile_id()
    {
        using var connection = OpenMigrated();
        var store = new SqliteMappingProfileStore(connection);

        store.Upsert(Document("profile-a", revision: "rev-1"));
        store.Upsert(Document("profile-a", revision: "rev-2"));

        var restored = store.ListAll();
        Assert.Single(restored);
        Assert.Equal("rev-2", restored[0].ProfileRevision);
    }

    [Fact]
    public void Unknown_schema_version_is_rejected_on_upsert()
    {
        using var connection = OpenMigrated();
        var store = new SqliteMappingProfileStore(connection);

        Assert.Throws<ArgumentException>(() =>
            store.Upsert(Document("profile-a") with { SchemaVersion = "9.9.9" }));
    }

    [Fact]
    public void Unknown_schema_version_in_a_stored_row_fails_the_read()
    {
        using var connection = OpenMigrated();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO mapping_profiles (profile_id, schema_version, document_json) VALUES ('old', '0.0.1', '{}');";
            command.ExecuteNonQuery();
        }

        var store = new SqliteMappingProfileStore(connection);

        Assert.Throws<InvalidOperationException>(() => store.ListAll());
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
