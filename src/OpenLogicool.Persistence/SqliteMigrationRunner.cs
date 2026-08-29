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
            new SqlMigration(
                7,
                """
                CREATE TABLE web_reference_sources (
                    source_id TEXT PRIMARY KEY,
                    schema_version TEXT NOT NULL,
                    policy TEXT NOT NULL,
                    source_json TEXT NOT NULL,
                    payload_bytes INTEGER NOT NULL
                );
                CREATE TABLE web_reference_documents (
                    document_id TEXT PRIMARY KEY,
                    source_id TEXT NOT NULL,
                    revision_number INTEGER NOT NULL,
                    parent_document_id TEXT NULL UNIQUE,
                    schema_version TEXT NOT NULL,
                    policy TEXT NOT NULL,
                    document_json TEXT NOT NULL,
                    payload_bytes INTEGER NOT NULL
                );
                CREATE TABLE web_reference_facts (
                    fact_id TEXT PRIMARY KEY,
                    revision_number INTEGER NOT NULL,
                    parent_fact_id TEXT NULL UNIQUE,
                    schema_version TEXT NOT NULL,
                    fact_json TEXT NOT NULL,
                    payload_bytes INTEGER NOT NULL
                );
                CREATE TABLE web_reference_fact_sources (
                    fact_id TEXT NOT NULL,
                    source_reference_id TEXT NOT NULL,
                    PRIMARY KEY (fact_id, source_reference_id)
                );
                CREATE TABLE web_reference_contradictions (
                    contradiction_id TEXT PRIMARY KEY,
                    revision_number INTEGER NOT NULL,
                    parent_contradiction_id TEXT NULL UNIQUE,
                    left_fact_id TEXT NOT NULL,
                    right_fact_id TEXT NOT NULL,
                    schema_version TEXT NOT NULL,
                    contradiction_json TEXT NOT NULL,
                    payload_bytes INTEGER NOT NULL
                );
                CREATE TABLE web_reference_contradiction_sources (
                    contradiction_id TEXT NOT NULL,
                    source_reference_id TEXT NOT NULL,
                    PRIMARY KEY (contradiction_id, source_reference_id)
                );
                CREATE TABLE web_reference_research_runs (
                    run_id TEXT NOT NULL,
                    revision_number INTEGER NOT NULL,
                    schema_version TEXT NOT NULL,
                    run_json TEXT NOT NULL,
                    PRIMARY KEY (run_id, revision_number)
                );
                CREATE TABLE web_reference_tombstones (
                    tombstone_id TEXT PRIMARY KEY,
                    source_id TEXT NOT NULL,
                    schema_version TEXT NOT NULL,
                    tombstone_json TEXT NOT NULL
                );
                CREATE TABLE web_reference_exclusions (
                    exclusion_id TEXT PRIMARY KEY,
                    schema_version TEXT NOT NULL,
                    exclusion_json TEXT NOT NULL
                );
                CREATE TABLE web_reference_reacquisition_requests (
                    request_id TEXT PRIMARY KEY,
                    source_id TEXT NOT NULL,
                    schema_version TEXT NOT NULL,
                    request_json TEXT NOT NULL
                );
                """),
            new SqlMigration(
                8,
                """
                CREATE TABLE web_reference_fact_contradictions (
                    fact_id TEXT NOT NULL,
                    contradiction_id TEXT NOT NULL,
                    PRIMARY KEY (fact_id, contradiction_id)
                );
                CREATE UNIQUE INDEX ux_web_reference_documents_source_revision
                ON web_reference_documents (source_id, revision_number);
                """),
            new SqlMigration(
                9,
                """
                CREATE TABLE structure_events (
                    game_id TEXT NOT NULL,
                    environment_scope TEXT NOT NULL,
                    event_sequence INTEGER NOT NULL,
                    schema_version TEXT NOT NULL,
                    event_id TEXT NOT NULL UNIQUE,
                    parent_revision_id TEXT NULL,
                    resulting_revision_id TEXT NOT NULL,
                    event_kind TEXT NOT NULL,
                    actor TEXT NOT NULL,
                    correlation_id TEXT NOT NULL,
                    causation_id TEXT NOT NULL,
                    observation_id TEXT NULL,
                    proposal_id TEXT NULL,
                    attempt_id TEXT NULL,
                    evidence_ids_json TEXT NOT NULL,
                    payload_type TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    outcome TEXT NULL,
                    occurred_utc TEXT NOT NULL,
                    persisted_utc TEXT NOT NULL,
                    PRIMARY KEY (game_id, environment_scope, event_sequence),
                    UNIQUE (game_id, environment_scope, resulting_revision_id)
                );
                """),
            new SqlMigration(
                10,
                """
                CREATE TABLE learning_route_revisions (
                    route_id TEXT NOT NULL,
                    revision_number INTEGER NOT NULL,
                    version_id TEXT NOT NULL UNIQUE,
                    parent_version_id TEXT NULL,
                    game_id TEXT NOT NULL,
                    environment_scope TEXT NOT NULL,
                    schema_version TEXT NOT NULL,
                    document_json TEXT NOT NULL,
                    PRIMARY KEY (route_id, revision_number),
                    UNIQUE (route_id, parent_version_id)
                );
                CREATE INDEX ix_learning_route_revisions_game_environment
                ON learning_route_revisions (game_id, environment_scope, route_id);
                """),
            new SqlMigration(
                11,
                """
                CREATE TABLE learned_scene_profiles (
                    game_id TEXT NOT NULL,
                    environment_scope TEXT NOT NULL,
                    schema_version TEXT NOT NULL,
                    profile_id TEXT NOT NULL UNIQUE,
                    profile_version TEXT NOT NULL,
                    document_json TEXT NOT NULL,
                    PRIMARY KEY (game_id, environment_scope)
                );
                """),
            new SqlMigration(
                12,
                """
                CREATE TABLE demonstration_sessions (
                    session_id TEXT PRIMARY KEY,
                    game_id TEXT NOT NULL,
                    environment_scope TEXT NOT NULL,
                    schema_version TEXT NOT NULL,
                    started_utc TEXT NOT NULL,
                    session_json TEXT NOT NULL
                );
                CREATE INDEX ix_demonstration_sessions_game_environment
                ON demonstration_sessions (game_id, environment_scope, session_id);
                CREATE TABLE demonstration_events (
                    session_id TEXT NOT NULL,
                    event_sequence INTEGER NOT NULL,
                    schema_version TEXT NOT NULL,
                    event_id TEXT NOT NULL UNIQUE,
                    parent_revision_id TEXT NULL,
                    resulting_revision_id TEXT NOT NULL,
                    event_kind TEXT NOT NULL,
                    occurred_utc TEXT NOT NULL,
                    persisted_utc TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    PRIMARY KEY (session_id, event_sequence),
                    UNIQUE (session_id, resulting_revision_id)
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
