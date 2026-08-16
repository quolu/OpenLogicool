using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Persistence.Tests;

public sealed class SqliteAppAssociationStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"openlogicool-association-store-{Guid.NewGuid():N}.db");

    private static AppProfileAssociation Association(string path, string profileId) =>
        new(ContractSchemaVersions.Revision01, path, "G600", profileId);

    private SqliteConnection OpenMigrated()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    [Fact]
    public void Associations_survive_reopening_the_database()
    {
        using (var connection = OpenMigrated())
        {
            var store = new SqliteAppAssociationStore(connection);
            store.Upsert(Association(@"c:\game\game.exe", "p-game"));
            store.Upsert(Association("*", "p-main"));
        }

        using (var connection = OpenMigrated())
        {
            var restored = new SqliteAppAssociationStore(connection).ListAll();

            Assert.Equal(
                [Association("*", "p-main"), Association(@"c:\game\game.exe", "p-game")],
                restored);
        }
    }

    [Fact]
    public void Upsert_overwrites_by_path_and_device_kind()
    {
        using var connection = OpenMigrated();
        var store = new SqliteAppAssociationStore(connection);

        store.Upsert(Association(@"c:\game\game.exe", "p-old"));
        store.Upsert(Association(@"c:\game\game.exe", "p-new"));

        var restored = Assert.Single(store.ListAll());
        Assert.Equal("p-new", restored.ProfileId);
    }

    [Fact]
    public void Package_matcher_kind_round_trips()
    {
        using (var connection = OpenMigrated())
        {
            var store = new SqliteAppAssociationStore(connection);
            store.Upsert(new AppProfileAssociation(
                ContractSchemaVersions.Revision01, "chrome_8wekyb3d8bbwe", "G600", "p-store", AppMatcherKind.Package));
        }

        using (var connection = OpenMigrated())
        {
            var restored = Assert.Single(new SqliteAppAssociationStore(connection).ListAll());
            Assert.Equal(AppMatcherKind.Package, restored.MatcherKind);
            Assert.Equal("chrome_8wekyb3d8bbwe", restored.ApplicationFullPath);
        }
    }

    [Fact]
    public void Unknown_schema_version_is_rejected_on_upsert()
    {
        using var connection = OpenMigrated();
        var store = new SqliteAppAssociationStore(connection);

        Assert.Throws<ArgumentException>(() =>
            store.Upsert(new AppProfileAssociation("rev-99", @"c:\game\game.exe", "G600", "p-1")));
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
