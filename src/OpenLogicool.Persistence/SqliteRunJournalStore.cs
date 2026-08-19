using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Persistence;

/// <summary>
/// IRunJournalStore の SQLite 実装（PB-006、OPS-008）。
/// INSERT と Run 単位 DELETE だけを持ち、UPDATE の口を持たない。
/// (run_id, run_sequence) 主キーと event_id UNIQUE 制約が再追記を DB 側でも拒否する。
/// 未知 schema version・壊れた値は例外として現れ、黙って読み飛ばさない。
/// </summary>
public sealed class SqliteRunJournalStore(SqliteConnection connection) : IRunJournalStore
{
    public void Append(RunEvent runEvent)
    {
        if (runEvent.SchemaVersion != ContractSchemaVersions.Revision01)
        {
            throw new ArgumentException(
                $"RunEvent schema version '{runEvent.SchemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。",
                nameof(runEvent));
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO run_events (
                run_id, run_sequence, schema_version, event_id, playbook_id, playbook_version_id,
                node_or_transition_id, command_id, attempt_id, causation_id, correlation_id,
                executor_epoch, actor_type, occurred_utc, persisted_utc, observation_id,
                payload_type, payload_json)
            VALUES (
                $runId, $runSequence, $schemaVersion, $eventId, $playbookId, $playbookVersionId,
                $nodeOrTransitionId, $commandId, $attemptId, $causationId, $correlationId,
                $executorEpoch, $actorType, $occurredUtc, $persistedUtc, $observationId,
                $payloadType, $payloadJson);
            """;
        command.Parameters.AddWithValue("$runId", runEvent.RunId);
        command.Parameters.AddWithValue("$runSequence", runEvent.RunSequence);
        command.Parameters.AddWithValue("$schemaVersion", runEvent.SchemaVersion);
        command.Parameters.AddWithValue("$eventId", runEvent.EventId);
        command.Parameters.AddWithValue("$playbookId", runEvent.PlaybookId);
        command.Parameters.AddWithValue("$playbookVersionId", runEvent.PlaybookVersionId);
        command.Parameters.AddWithValue("$nodeOrTransitionId", (object?)runEvent.NodeOrTransitionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$commandId", (object?)runEvent.CommandId ?? DBNull.Value);
        command.Parameters.AddWithValue("$attemptId", (object?)runEvent.AttemptId ?? DBNull.Value);
        command.Parameters.AddWithValue("$causationId", runEvent.CausationId);
        command.Parameters.AddWithValue("$correlationId", runEvent.CorrelationId);
        command.Parameters.AddWithValue("$executorEpoch", runEvent.ExecutorEpoch);
        command.Parameters.AddWithValue("$actorType", runEvent.ActorType.ToString());
        command.Parameters.AddWithValue("$occurredUtc", Format(runEvent.OccurredUtc));
        command.Parameters.AddWithValue("$persistedUtc", Format(runEvent.PersistedUtc));
        command.Parameters.AddWithValue("$observationId", (object?)runEvent.ObservationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$payloadType", runEvent.PayloadType);
        command.Parameters.AddWithValue("$payloadJson", runEvent.PayloadJson);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<RunEvent> ReadRun(string runId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT run_sequence, schema_version, event_id, playbook_id, playbook_version_id,
                   node_or_transition_id, command_id, attempt_id, causation_id, correlation_id,
                   executor_epoch, actor_type, occurred_utc, persisted_utc, observation_id,
                   payload_type, payload_json
            FROM run_events
            WHERE run_id = $runId
            ORDER BY run_sequence;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();

        var events = new List<RunEvent>();
        while (reader.Read())
        {
            var schemaVersion = reader.GetString(1);
            if (schemaVersion != ContractSchemaVersions.Revision01)
            {
                throw new InvalidOperationException(
                    $"run '{runId}' sequence {reader.GetInt64(0)} の schema version '{schemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。");
            }

            events.Add(new RunEvent(
                SchemaVersion: schemaVersion,
                EventId: reader.GetString(2),
                RunId: runId,
                RunSequence: reader.GetInt64(0),
                PlaybookId: reader.GetString(3),
                PlaybookVersionId: reader.GetString(4),
                NodeOrTransitionId: reader.IsDBNull(5) ? null : reader.GetString(5),
                CommandId: reader.IsDBNull(6) ? null : reader.GetString(6),
                AttemptId: reader.IsDBNull(7) ? null : reader.GetString(7),
                CausationId: reader.GetString(8),
                CorrelationId: reader.GetString(9),
                ExecutorEpoch: reader.GetInt64(10),
                ActorType: Enum.Parse<RunEventActorType>(reader.GetString(11)),
                OccurredUtc: Parse(reader.GetString(12)),
                PersistedUtc: Parse(reader.GetString(13)),
                ObservationId: reader.IsDBNull(14) ? null : reader.GetString(14),
                PayloadType: reader.GetString(15),
                PayloadJson: reader.GetString(16)));
        }

        return events;
    }

    public IReadOnlyList<string> ListRunIds()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT run_id FROM run_events ORDER BY run_id;";
        using var reader = command.ExecuteReader();

        var runIds = new List<string>();
        while (reader.Read())
        {
            runIds.Add(reader.GetString(0));
        }

        return runIds;
    }

    public IReadOnlyList<ExpiredRunPreview> PreviewExpiredRuns(DateTimeOffset asOfUtc, int retentionDays)
    {
        if (retentionDays is < 1 or > 365)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionDays), retentionDays, "retention は 1〜365 日です（「削除するまで」は preview を呼ばないことで表します）。");
        }

        var cutoff = Format(asOfUtc.AddDays(-retentionDays));
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT run_id, MAX(persisted_utc), COUNT(*)
            FROM run_events
            GROUP BY run_id
            HAVING MAX(persisted_utc) < $cutoff
            ORDER BY run_id;
            """;
        command.Parameters.AddWithValue("$cutoff", cutoff);
        using var reader = command.ExecuteReader();

        var expired = new List<ExpiredRunPreview>();
        while (reader.Read())
        {
            expired.Add(new ExpiredRunPreview(reader.GetString(0), Parse(reader.GetString(1)), reader.GetInt64(2)));
        }

        return expired;
    }

    public void DeleteRun(string runId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM run_events WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);
        command.ExecuteNonQuery();
    }

    // 固定長 UTC 表記（"o" の UTC 形）。文字列比較が時刻順比較と一致することを retention 判定が前提にする。
    private static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
