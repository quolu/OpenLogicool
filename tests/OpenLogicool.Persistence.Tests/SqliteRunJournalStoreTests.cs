using System.Reflection;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Persistence.Tests;

public sealed class SqliteRunJournalStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"openlogicool-run-journal-{Guid.NewGuid():N}.db");

    private SqliteConnection OpenMigrated()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static RunEvent Event(
        string runId,
        long sequence,
        string persistedUtc = "2026-08-19T00:00:00Z",
        string? eventId = null) =>
        new(
            "0.1.0",
            eventId ?? $"event-{runId}-{sequence}",
            runId,
            sequence,
            "playbook-1",
            "playbook-version-1",
            "node-1",
            "command-1",
            "attempt-1",
            "cause-1",
            $"correlation-{sequence}",
            1,
            RunEventActorType.Automation,
            DateTimeOffset.Parse("2026-08-19T00:00:00Z"),
            DateTimeOffset.Parse(persistedUtc),
            "observation-1",
            RunEventPayloadTypes.Observation,
            """{"kind":"observation"}""");

    [Fact]
    public void Events_roundtrip_in_sequence_order_with_all_fields()
    {
        using var connection = OpenMigrated();
        var store = new SqliteRunJournalStore(connection);
        var first = Event("run-a", 1);
        var second = Event("run-a", 2) with { NodeOrTransitionId = null, CommandId = null, AttemptId = null, ObservationId = null };
        store.Append(second);
        store.Append(first);

        var events = store.ReadRun("run-a");

        Assert.Equal([first, second], events);
    }

    [Fact]
    public void Journal_survives_reopening_and_replay_restores_sequence_state()
    {
        using (var connection = OpenMigrated())
        {
            var store = new SqliteRunJournalStore(connection);
            store.Append(Event("run-a", 1));
            store.Append(Event("run-a", 2));
            store.Append(Event("run-b", 1));
        }

        // OPS-008: 再 open した DB の journal だけから復元し、続きの append 位置が正しい。
        using (var connection = OpenMigrated())
        {
            var store = new SqliteRunJournalStore(connection);
            Assert.Equal(["run-a", "run-b"], store.ListRunIds());

            var replayed = RunEventSequenceModel.Replay(store.ListRunIds().SelectMany(store.ReadRun));
            _ = replayed.Append(Event("run-a", 3));
            Assert.Throws<InvalidOperationException>(() => replayed.Append(Event("run-a", 2)));
            Assert.Throws<InvalidOperationException>(() => replayed.Append(Event("run-b", 3)));
        }
    }

    [Fact]
    public void Append_rejects_a_duplicate_run_sequence()
    {
        using var connection = OpenMigrated();
        var store = new SqliteRunJournalStore(connection);
        store.Append(Event("run-a", 1));

        Assert.Throws<SqliteException>(() => store.Append(Event("run-a", 1, eventId: "event-other")));
    }

    [Fact]
    public void Append_rejects_a_duplicate_event_id()
    {
        using var connection = OpenMigrated();
        var store = new SqliteRunJournalStore(connection);
        store.Append(Event("run-a", 1, eventId: "event-shared"));

        Assert.Throws<SqliteException>(() => store.Append(Event("run-a", 2, eventId: "event-shared")));
    }

    [Fact]
    public void Append_rejects_an_unknown_schema_version()
    {
        using var connection = OpenMigrated();
        var store = new SqliteRunJournalStore(connection);

        Assert.Throws<ArgumentException>(() => store.Append(Event("run-a", 1) with { SchemaVersion = "9.9.9" }));
    }

    [Fact]
    public void Store_exposes_no_update_api()
    {
        var updaters = typeof(SqliteRunJournalStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(SqliteRunJournalStore))
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Where(name => name is not ("Append" or "ReadRun" or "ListRunIds" or "PreviewExpiredRuns" or "DeleteRun"));
        Assert.Empty(updaters);
    }

    [Fact]
    public void DeleteRun_removes_only_the_named_run()
    {
        using var connection = OpenMigrated();
        var store = new SqliteRunJournalStore(connection);
        store.Append(Event("run-a", 1));
        store.Append(Event("run-b", 1));

        store.DeleteRun("run-a");

        Assert.Empty(store.ReadRun("run-a"));
        Assert.Single(store.ReadRun("run-b"));
    }

    [Fact]
    public void PreviewExpiredRuns_lists_expired_runs_without_deleting()
    {
        using var connection = OpenMigrated();
        var store = new SqliteRunJournalStore(connection);
        store.Append(Event("run-old", 1, persistedUtc: "2026-05-01T00:00:00Z"));
        store.Append(Event("run-touched", 1, persistedUtc: "2026-05-01T00:00:00Z"));
        store.Append(Event("run-touched", 2, persistedUtc: "2026-08-18T00:00:00Z"));

        var expired = store.PreviewExpiredRuns(DateTimeOffset.Parse("2026-08-19T00:00:00Z"), retentionDays: 90);

        // 最後の event が retention 内の run は、古い event を含んでいても期限切れではない。
        var preview = Assert.Single(expired);
        Assert.Equal("run-old", preview.RunId);
        Assert.Equal(DateTimeOffset.Parse("2026-05-01T00:00:00Z"), preview.LastPersistedUtc);
        Assert.Equal(1, preview.EventCount);
        Assert.Single(store.ReadRun("run-old"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void PreviewExpiredRuns_rejects_retention_outside_the_contract_range(int retentionDays)
    {
        using var connection = OpenMigrated();
        var store = new SqliteRunJournalStore(connection);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.PreviewExpiredRuns(DateTimeOffset.Parse("2026-08-19T00:00:00Z"), retentionDays));
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
