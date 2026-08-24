using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Persistence.Tests;

public sealed class SqliteGameStructureStoreTests : IDisposable
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(), $"openlogicool-structure-{Guid.NewGuid():N}.db");

    [Fact]
    public void Append_assigns_a_revision_chain_and_events_survive_reopening()
    {
        StructureEvent first;
        StructureEvent second;
        using (var connection = OpenMigrated())
        {
            var store = new SqliteGameStructureStore(connection);
            first = store.Append(Draft("event-1", StructureEventKind.ObservationRecorded), null, Time(1));
            second = store.Append(
                Draft("event-2", StructureEventKind.ProbeProposed),
                first.ResultingStructureRevisionId,
                Time(2));

            Assert.Equal(1, first.Sequence);
            Assert.Equal(first.ResultingStructureRevisionId, second.ParentStructureRevisionId);
        }

        using (var connection = OpenMigrated())
        {
            var store = new SqliteGameStructureStore(connection);
            Assert.Equal(
                JsonSerializer.Serialize(new[] { first, second }),
                JsonSerializer.Serialize(store.ReadEvents("game-1", "env-1")));
            var revision = store.LoadRevision("game-1", "env-1");
            Assert.Equal(second.ResultingStructureRevisionId, revision.RevisionId);
            Assert.Equal(2, revision.ThroughEvidenceSequence);
            Assert.Equal(["game-1"], store.ListGameIds());
        }
    }

    [Fact]
    public void Append_rejects_stale_parent_and_duplicate_event_id()
    {
        using var connection = OpenMigrated();
        var store = new SqliteGameStructureStore(connection);
        var first = store.Append(Draft("event-1", StructureEventKind.ObservationRecorded), null, Time(1));

        Assert.Throws<InvalidOperationException>(() =>
            store.Append(Draft("event-2", StructureEventKind.ProbeProposed), null, Time(2)));
        Assert.Throws<SqliteException>(() =>
            store.Append(Draft("event-1", StructureEventKind.ProbeProposed), first.ResultingStructureRevisionId, Time(2)));
    }

    [Fact]
    public void Crash_replay_keeps_unresolved_dispatch_as_outcome_unknown()
    {
        using (var connection = OpenMigrated())
        {
            var store = new SqliteGameStructureStore(connection);
            store.Append(
                Draft("armed-1", StructureEventKind.DispatchArmed) with { AttemptId = "attempt-1" },
                null,
                Time(1));
        }

        using (var connection = OpenMigrated())
        {
            var dispatch = Assert.Single(
                new SqliteGameStructureStore(connection).LoadRevision("game-1", "env-1").Dispatches);
            Assert.Equal("attempt-1", dispatch.AttemptId);
            Assert.Equal(ExplorationOutcomeKind.OutcomeUnknown, dispatch.Outcome);
        }
    }

    [Fact]
    public void Export_contains_the_replayed_revision_and_complete_append_only_history()
    {
        using var connection = OpenMigrated();
        var store = new SqliteGameStructureStore(connection);
        var first = store.Append(Draft("event-1", StructureEventKind.ObservationRecorded), null, Time(1));
        _ = store.Append(
            Draft("event-2", StructureEventKind.ManualInterventionRecorded) with
            {
                Actor = StructureEventActor.User,
                EvidenceIds = ["observation-1"],
            },
            first.ResultingStructureRevisionId,
            Time(2));

        var export = store.Export("game-1", "env-1", Time(3));

        Assert.Equal(ContractSchemaVersions.Revision03, export.SchemaVersion);
        Assert.Equal(export.Events[^1].ResultingStructureRevisionId, export.Revision.RevisionId);
        Assert.Equal(["event-1", "event-2"], export.Events.Select(item => item.EventId));
        Assert.StartsWith("knowledge:", export.ExportId, StringComparison.Ordinal);
    }

    [Fact]
    public void Append_rejects_unknown_schema_and_invalid_correction_actor()
    {
        using var connection = OpenMigrated();
        var store = new SqliteGameStructureStore(connection);

        Assert.Throws<ArgumentException>(() =>
            store.Append(Draft("event-1", StructureEventKind.ObservationRecorded) with
            {
                SchemaVersion = "9.9.9",
            }, null, Time(1)));
        Assert.Throws<ArgumentException>(() =>
            store.Append(Draft("event-2", StructureEventKind.CorrectionApplied) with
            {
                Actor = StructureEventActor.Controller,
                PayloadType = StructureEventPayloadTypes.MutationBatch,
                PayloadJson = "{\"SchemaVersion\":\"0.3.0\",\"Mutations\":[]}",
            }, null, Time(1)));
    }

    [Fact]
    public void Store_exposes_no_update_or_delete_api()
    {
        var methods = typeof(SqliteGameStructureStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(SqliteGameStructureStore))
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name);

        Assert.Equal(
            ["Append", "Export", "ListGameIds", "LoadRevision", "ReadEvents"],
            methods.Order(StringComparer.Ordinal));
    }

    private SqliteConnection OpenMigrated()
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static StructureEventDraft Draft(string eventId, StructureEventKind kind) => new(
        ContractSchemaVersions.Revision03,
        eventId,
        "game-1",
        "env-1",
        kind,
        StructureEventActor.Controller,
        "correlation-1",
        "causation-1",
        "observation-1",
        null,
        null,
        [],
        StructureEventPayloadTypes.None,
        "{}",
        null,
        Time(0));

    private static DateTimeOffset Time(int seconds) => DateTimeOffset.UnixEpoch.AddSeconds(seconds);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
