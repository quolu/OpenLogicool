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
            new SqlMigration(
                2,
                """
                CREATE TABLE mapping_profiles (
                    profile_id TEXT PRIMARY KEY,
                    schema_version TEXT NOT NULL,
                    document_json TEXT NOT NULL
                );
                """),
            new SqlMigration(
                3,
                """
                CREATE TABLE app_profile_associations (
                    application_full_path TEXT NOT NULL,
                    device_kind TEXT NOT NULL,
                    schema_version TEXT NOT NULL,
                    profile_id TEXT NOT NULL,
                    PRIMARY KEY (application_full_path, device_kind)
                );
                """),
            new SqlMigration(
                4,
                """
                CREATE TABLE workspace_revisions (
                    workspace_id TEXT NOT NULL,
                    revision_number INTEGER NOT NULL,
                    schema_version TEXT NOT NULL,
                    saved_at_utc TEXT NOT NULL,
                    document_json TEXT NOT NULL,
                    PRIMARY KEY (workspace_id, revision_number)
                );
                """),
            new SqlMigration(
                5,
                "ALTER TABLE app_profile_associations ADD COLUMN matcher_kind TEXT NOT NULL DEFAULT 'path';"),
            new SqlMigration(
                6,
                """
                CREATE TABLE run_events (
                    run_id TEXT NOT NULL,
                    run_sequence INTEGER NOT NULL,
                    schema_version TEXT NOT NULL,
                    event_id TEXT NOT NULL UNIQUE,
                    playbook_id TEXT NOT NULL,
                    playbook_version_id TEXT NOT NULL,
                    node_or_transition_id TEXT NULL,
                    command_id TEXT NULL,
                    attempt_id TEXT NULL,
                    causation_id TEXT NOT NULL,
                    correlation_id TEXT NOT NULL,
                    executor_epoch INTEGER NOT NULL,
                    actor_type TEXT NOT NULL,
                    occurred_utc TEXT NOT NULL,
                    persisted_utc TEXT NOT NULL,
                    observation_id TEXT NULL,
                    payload_type TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    PRIMARY KEY (run_id, run_sequence)
                );
                """),
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
