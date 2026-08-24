using Microsoft.Data.Sqlite;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Persistence.Tests;

public sealed class SqliteMigrationRunnerTests
{
    [Fact]
    public void Apply_to_an_empty_database_records_all_initial_migrations()
    {
        using var connection = OpenInMemoryConnection();

        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, ReadMigrationNumbers(connection));
    }

    [Fact]
    public void Reapplying_does_not_record_a_migration_twice()
    {
        using var connection = OpenInMemoryConnection();
        var runner = new SqliteMigrationRunner(InitialSqliteMigrations.All);

        runner.Apply(connection);
        runner.Apply(connection);

        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, ReadMigrationNumbers(connection));
    }

    [Fact]
    public void Existing_migration_seven_database_receives_document_revision_index()
    {
        using var connection = OpenInMemoryConnection();
        new SqliteMigrationRunner(InitialSqliteMigrations.All.Where(migration => migration.Number <= 7)).Apply(connection);

        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, ReadMigrationNumbers(connection));
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM pragma_index_list('web_reference_documents') WHERE name = 'ux_web_reference_documents_source_revision';";
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'web_reference_fact_contradictions';";
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'structure_events';";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void A_missing_migration_number_is_rejected()
    {
        var migrations = new[]
        {
            InitialSqliteMigrations.All[0],
            new SqlMigration(3, "CREATE TABLE later_migration (id INTEGER PRIMARY KEY);"),
        };

        Assert.Throws<InvalidOperationException>(() => new SqliteMigrationRunner(migrations));
    }

    [Fact]
    public void A_failing_migration_surfaces_its_exception()
    {
        using var connection = OpenInMemoryConnection();
        var migrations = new[]
        {
            InitialSqliteMigrations.All[0],
            new SqlMigration(2, "CREATE TABLE broken (")
        };

        var runner = new SqliteMigrationRunner(migrations);

        Assert.Throws<SqliteException>(() => runner.Apply(connection));
        Assert.Equal(new long[] { 1 }, ReadMigrationNumbers(connection));
    }

    private static SqliteConnection OpenInMemoryConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static long[] ReadMigrationNumbers(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT migration_number FROM schema_migrations ORDER BY migration_number;";
        using var reader = command.ExecuteReader();

        var numbers = new List<long>();
        while (reader.Read())
        {
            numbers.Add(reader.GetInt64(0));
        }

        return numbers.ToArray();
    }
}

