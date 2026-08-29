using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Persistence.Tests;

public sealed class SqliteDemonstrationSessionStoreTests : IDisposable
{
    private const string Schema = ContractSchemaVersions.Revision03;
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddHours(1);

    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(), $"openlogicool-demonstration-{Guid.NewGuid():N}.db");

    [Fact]
    public void Recorded_original_keeps_its_revision_chain_across_reopen()
    {
        DemonstrationEvent click;
        DemonstrationEvent focusLost;
        DemonstrationEvent focusRegained;
        DemonstrationEvent stopped;

        using (var connection = OpenMigrated())
        {
            var store = new SqliteDemonstrationSessionStore(connection);
            var record = store.Start(Session());
            Assert.Equal(DemonstrationSessionState.Recording, record.State);
            Assert.Null(record.RevisionId);
            Assert.Empty(record.Events);

            click = store.Append(OperationDraft(Click()));
            focusLost = store.Append(new DemonstrationEventDraft(
                Schema,
                "demo-session-1",
                DemonstrationEventKind.FocusLost,
                Start.AddSeconds(10),
                FocusChange: new DemonstrationFocusChange(
                    Schema, @"C:\other\chat.exe", null, Start.AddSeconds(10))));
            focusRegained = store.Append(new DemonstrationEventDraft(
                Schema,
                "demo-session-1",
                DemonstrationEventKind.FocusRegained,
                Start.AddSeconds(20),
                FocusChange: new DemonstrationFocusChange(
                    Schema, @"C:\games\nikke\nikke.exe", "obs-resumed", Start.AddSeconds(20))));
            stopped = store.Append(new DemonstrationEventDraft(
                Schema,
                "demo-session-1",
                DemonstrationEventKind.Stopped,
                Start.AddSeconds(30),
                Stop: new DemonstrationStop(Schema, "利用者が停止", Start.AddSeconds(30))));
        }

        using (var connection = OpenMigrated())
        {
            var store = new SqliteDemonstrationSessionStore(connection);
            var record = Assert.IsType<DemonstrationSessionRecord>(store.Load("demo-session-1"));

            Assert.Equal(DemonstrationSessionState.Stopped, record.State);
            Assert.Equal("アークを開く", record.Session.Goal);
            Assert.Equal(stopped.ResultingRevisionId, record.RevisionId);
            Assert.Equal([1L, 2L, 3L, 4L], record.Events.Select(item => item.Sequence));
            Assert.Equal(
                [click.ResultingRevisionId, focusLost.ResultingRevisionId, focusRegained.ResultingRevisionId, stopped.ResultingRevisionId],
                record.Events.Select(item => item.ResultingRevisionId));
            Assert.Null(record.Events[0].ParentRevisionId);
            Assert.Equal(
                record.Events.Take(3).Select(item => item.ResultingRevisionId),
                record.Events.Skip(1).Select(item => item.ParentRevisionId));

            var replayedClick = Assert.IsType<DemonstrationOperation>(record.Events[0].Operation);
            Assert.Equal(GameInteractionOperations.Click, replayedClick.Operation);
            Assert.Equal([0.534, 0.628], replayedClick.Target.NormalizedPoint!);
            Assert.Equal("obs-before", replayedClick.Before.ObservationId);
            Assert.Equal(41, replayedClick.Before.Frame.Sequence);
            Assert.Equal(GameTransitionJudgement.Moved, replayedClick.Comparison.Judgement);
            Assert.Equal(10_059, replayedClick.After.ElapsedMilliseconds);
            Assert.Equal("evidence-1", replayedClick.TransitionEvidenceId);

            Assert.Equal("obs-resumed", record.Events[2].FocusChange!.ResumedObservationId);
            Assert.Equal("利用者が停止", record.Events[3].Stop!.Reason);
            Assert.Equal(["demo-session-1"], store.ListSessionIds("nikke", "env-1"));
        }
    }

    [Fact]
    public void The_original_cannot_be_reopened_restarted_or_appended_after_stop()
    {
        using var connection = OpenMigrated();
        var store = new SqliteDemonstrationSessionStore(connection);
        store.Start(Session());

        Assert.Throws<InvalidOperationException>(() => store.Start(Session()));

        store.Append(new DemonstrationEventDraft(
            Schema,
            "demo-session-1",
            DemonstrationEventKind.Stopped,
            Start.AddSeconds(30),
            Stop: new DemonstrationStop(Schema, "利用者が停止", Start.AddSeconds(30))));

        Assert.Throws<ArgumentException>(() => store.Append(OperationDraft(Click(), Start.AddSeconds(31))));
        Assert.Single(store.Load("demo-session-1")!.Events);
    }

    [Fact]
    public void Rejected_appends_leave_no_trace_in_the_original()
    {
        using var connection = OpenMigrated();
        var store = new SqliteDemonstrationSessionStore(connection);
        store.Start(Session());
        var accepted = store.Append(OperationDraft(Click()));

        var outsideClientFrame = Click();
        outsideClientFrame = outsideClientFrame with
        {
            Target = outsideClientFrame.Target with { NormalizedPoint = [1.4, 0.5] },
        };
        Assert.Throws<ArgumentException>(() =>
            store.Append(OperationDraft(outsideClientFrame, Start.AddSeconds(5))));

        var record = store.Load("demo-session-1")!;
        Assert.Single(record.Events);
        Assert.Equal(accepted.ResultingRevisionId, record.RevisionId);
        Assert.Equal(DemonstrationSessionState.Recording, record.State);
    }

    [Fact]
    public void Appending_to_an_unknown_session_fails_instead_of_creating_one()
    {
        using var connection = OpenMigrated();
        var store = new SqliteDemonstrationSessionStore(connection);

        Assert.Throws<InvalidOperationException>(() => store.Append(OperationDraft(Click())));
        Assert.Null(store.Load("demo-session-1"));
        Assert.Empty(store.ListSessionIds("nikke", "env-1"));
    }

    [Fact]
    public void Stored_rows_written_with_an_unsupported_schema_are_refused_on_read()
    {
        using var connection = OpenMigrated();
        var store = new SqliteDemonstrationSessionStore(connection);
        store.Start(Session());
        store.Append(OperationDraft(Click()));

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "UPDATE demonstration_events SET schema_version = $schemaVersion WHERE session_id = $sessionId;";
            command.Parameters.AddWithValue("$schemaVersion", "9.9.9");
            command.Parameters.AddWithValue("$sessionId", "demo-session-1");
            command.ExecuteNonQuery();
        }

        var error = Assert.Throws<InvalidOperationException>(() => store.Load("demo-session-1"));
        Assert.Contains("未対応", error.Message, StringComparison.Ordinal);
    }

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

    private static DemonstrationSessionDraft Session() =>
        new(
            Schema,
            "demo-session-1",
            "nikke",
            "env-1",
            "アークを開く",
            @"C:\games\nikke\nikke.exe",
            "window-nikke",
            "windows-demonstration-recorder-v1",
            Start);

    private static DemonstrationEventDraft OperationDraft(
        DemonstrationOperation operation,
        DateTimeOffset? occurredUtc = null) =>
        new(
            Schema,
            "demo-session-1",
            DemonstrationEventKind.Operation,
            occurredUtc ?? operation.OccurredUtc,
            Operation: operation);

    private static DemonstrationOperation Click() =>
        new(
            Schema,
            "demo-op-1",
            GameInteractionOperations.Click,
            DemonstrationInputSource.Mouse,
            new DemonstrationFrameBinding(Schema, "obs-before", 41, 7, "window-nikke", [0.534, 0.628]),
            Scene("obs-before", 41),
            new GameInteractionStabilityResult(
                Schema,
                GameInteractionStabilityStatus.Stable,
                [Scene("obs-after", 96)],
                Scene("obs-after", 96),
                17,
                8_500,
                10_059,
                null),
            new GameTransitionComparison(
                Schema,
                "obs-before",
                "obs-after",
                GameTransitionJudgement.Moved,
                [],
                ["意味構造が変化した"]),
            "evidence-1",
            1_000,
            11_059,
            Start.AddSeconds(3));

    private static ObservedScene Scene(string observationId, long frameSequence) =>
        new(
            Schema,
            $"scene-{observationId}",
            observationId,
            new CapturedFrameReference(
                Schema,
                "window-nikke",
                CaptureBackend.WindowsGraphicsCapture,
                frameSequence,
                frameSequence * 16.0,
                Start.AddMilliseconds(frameSequence * 16),
                7,
                12,
                8),
            CaptureAvailability.Available,
            StateIdentityStatus.Novel,
            null,
            [],
            [],
            "local-target-tracking-v1");
}
