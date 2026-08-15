using Microsoft.Data.Sqlite;

namespace OpenLogicool.Persistence;

public sealed record SqlMigration(long Number, string Sql);

public static class InitialSqliteMigrations
{
    public static IReadOnlyList<SqlMigration> All { get; } =
        new[]
        {
            new SqlMigration(
                1,
                "CREATE TABLE IF NOT EXISTS schema_migrations (migration_number INTEGER PRIMARY KEY);"),
        };
}

public sealed class SqliteMigrationRunner
{
    private readonly IReadOnlyList<SqlMigration> migrations;

    public SqliteMigrationRunner(IEnumerable<SqlMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        this.migrations = migrations.OrderBy(migration => migration.Number).ToArray();
        ValidateMigrationNumbers(this.migrations);
    }

    public void Apply(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var initialMigration = migrations[0];
        ExecuteSql(connection, initialMigration.Sql);

        var appliedNumbers = ReadAppliedNumbers(connection);
        foreach (var migration in migrations)
        {
            if (appliedNumbers.Contains(migration.Number))
            {
                continue;
            }

            ExecuteSql(connection, migration.Sql);
            RecordAppliedNumber(connection, migration.Number);
        }
    }

    private static void ValidateMigrationNumbers(IReadOnlyList<SqlMigration> migrations)
    {
        if (migrations.Count == 0)
        {
            throw new InvalidOperationException("少なくとも migration 001 が必要です。");
        }

        for (var index = 0; index < migrations.Count; index++)
        {
            var expectedNumber = index + 1;
            if (migrations[index].Number != expectedNumber)
            {
                throw new InvalidOperationException($"migration {expectedNumber:000} がありません。");
            }
        }
    }

    private static void ExecuteSql(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static HashSet<long> ReadAppliedNumbers(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT migration_number FROM schema_migrations;";
        using var reader = command.ExecuteReader();

        var appliedNumbers = new HashSet<long>();
        while (reader.Read())
        {
            appliedNumbers.Add(reader.GetInt64(0));
        }

        return appliedNumbers;
    }

    private static void RecordAppliedNumber(SqliteConnection connection, long migrationNumber)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO schema_migrations (migration_number) VALUES ($migrationNumber);";
        command.Parameters.AddWithValue("$migrationNumber", migrationNumber);
        command.ExecuteNonQuery();
    }
}
