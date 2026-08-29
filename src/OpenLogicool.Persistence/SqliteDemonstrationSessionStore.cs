using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Persistence;

/// <summary>
/// 操作デモ原本をappend-onlyのevent列として保存する。
/// 更新・削除は公開せず、受入規則は<see cref="DemonstrationSessionValidator"/>だけが持つ。
/// </summary>
public sealed class SqliteDemonstrationSessionStore(SqliteConnection connection) : IDemonstrationSessionStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public DemonstrationSessionRecord Start(DemonstrationSessionDraft draft)
    {
        DemonstrationSessionValidator.ValidateSession(draft);

        using var transaction = connection.BeginTransaction();
        if (ReadSession(draft.SessionId, transaction) is not null)
        {
            throw new InvalidOperationException($"操作デモ原本 '{draft.SessionId}' は既に開始されています。");
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO demonstration_sessions (
                    session_id, game_id, environment_scope, schema_version, started_utc, session_json)
                VALUES ($sessionId, $gameId, $environmentScope, $schemaVersion, $startedUtc, $sessionJson);
                """;
            command.Parameters.AddWithValue("$sessionId", draft.SessionId);
            command.Parameters.AddWithValue("$gameId", draft.GameId);
            command.Parameters.AddWithValue("$environmentScope", draft.EnvironmentScope);
            command.Parameters.AddWithValue("$schemaVersion", draft.SchemaVersion);
            command.Parameters.AddWithValue("$startedUtc", Timestamp(draft.StartedUtc));
            command.Parameters.AddWithValue("$sessionJson", JsonSerializer.Serialize(draft, Json));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return new DemonstrationSessionRecord(draft, DemonstrationSessionState.Recording, null, []);
    }

    public DemonstrationEvent Append(DemonstrationEventDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        using var transaction = connection.BeginTransaction();
        var session = ReadSession(draft.SessionId, transaction)
            ?? throw new InvalidOperationException($"操作デモ原本 '{draft.SessionId}' がありません。");
        var existing = ReadEvents(draft.SessionId, transaction);

        DemonstrationSessionValidator.ValidateAppend(session, existing, draft);

        var sequence = existing.Count + 1;
        var parentRevisionId = existing.Count == 0 ? null : existing[^1].ResultingRevisionId;
        var payloadJson = JsonSerializer.Serialize(
            new DemonstrationEventPayload(draft.Kind, draft.Operation, draft.FocusChange, draft.Stop),
            Json);
        var eventId = DemonstrationRevisionIds.EventId(draft.SessionId, sequence, payloadJson);
        var revisionId = DemonstrationRevisionIds.Next(draft.SessionId, parentRevisionId, sequence, eventId);

        var stored = new DemonstrationEvent(
            draft.SchemaVersion,
            draft.SessionId,
            sequence,
            eventId,
            parentRevisionId,
            revisionId,
            draft.Kind,
            draft.OccurredUtc.ToUniversalTime(),
            DateTimeOffset.UtcNow,
            draft.Operation,
            draft.FocusChange,
            draft.Stop);

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO demonstration_events (
                    session_id, event_sequence, schema_version, event_id, parent_revision_id,
                    resulting_revision_id, event_kind, occurred_utc, persisted_utc, payload_json)
                VALUES (
                    $sessionId, $sequence, $schemaVersion, $eventId, $parentRevisionId,
                    $resultingRevisionId, $eventKind, $occurredUtc, $persistedUtc, $payloadJson);
                """;
            command.Parameters.AddWithValue("$sessionId", stored.SessionId);
            command.Parameters.AddWithValue("$sequence", stored.Sequence);
            command.Parameters.AddWithValue("$schemaVersion", stored.SchemaVersion);
            command.Parameters.AddWithValue("$eventId", stored.EventId);
            command.Parameters.AddWithValue("$parentRevisionId", (object?)stored.ParentRevisionId ?? DBNull.Value);
            command.Parameters.AddWithValue("$resultingRevisionId", stored.ResultingRevisionId);
            command.Parameters.AddWithValue("$eventKind", stored.Kind.ToString());
            command.Parameters.AddWithValue("$occurredUtc", Timestamp(stored.OccurredUtc));
            command.Parameters.AddWithValue("$persistedUtc", Timestamp(stored.PersistedUtc));
            command.Parameters.AddWithValue("$payloadJson", payloadJson);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return stored;
    }

    public DemonstrationSessionRecord? Load(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var session = ReadSession(sessionId, transaction: null);
        if (session is null)
        {
            return null;
        }

        var events = ReadEvents(sessionId, transaction: null);
        var state = events.Count > 0 && events[^1].Kind == DemonstrationEventKind.Stopped
            ? DemonstrationSessionState.Stopped
            : DemonstrationSessionState.Recording;
        return new DemonstrationSessionRecord(
            session,
            state,
            events.Count == 0 ? null : events[^1].ResultingRevisionId,
            events);
    }

    public IReadOnlyList<string> ListSessionIds(string gameId, string environmentScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT session_id
            FROM demonstration_sessions
            WHERE game_id = $gameId AND environment_scope = $environmentScope
            ORDER BY started_utc, session_id;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue("$environmentScope", environmentScope);
        using var reader = command.ExecuteReader();
        var sessionIds = new List<string>();
        while (reader.Read())
        {
            sessionIds.Add(reader.GetString(0));
        }

        return sessionIds;
    }

    private DemonstrationSessionDraft? ReadSession(string sessionId, SqliteTransaction? transaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT schema_version, session_json FROM demonstration_sessions WHERE session_id = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        RequireKnownSchema(reader.GetString(0), $"操作デモ原本 '{sessionId}'");
        return JsonSerializer.Deserialize<DemonstrationSessionDraft>(reader.GetString(1), Json)
            ?? throw new InvalidOperationException($"操作デモ原本 '{sessionId}' の見出しがnullです。");
    }

    private IReadOnlyList<DemonstrationEvent> ReadEvents(string sessionId, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT event_sequence, schema_version, event_id, parent_revision_id, resulting_revision_id,
                   occurred_utc, persisted_utc, payload_json
            FROM demonstration_events
            WHERE session_id = $sessionId
            ORDER BY event_sequence;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        using var reader = command.ExecuteReader();

        var events = new List<DemonstrationEvent>();
        while (reader.Read())
        {
            var sequence = reader.GetInt64(0);
            var schemaVersion = reader.GetString(1);
            RequireKnownSchema(schemaVersion, $"操作デモ原本 '{sessionId}' event {sequence}");

            var payload = JsonSerializer.Deserialize<DemonstrationEventPayload>(reader.GetString(7), Json)
                ?? throw new InvalidOperationException(
                    $"操作デモ原本 '{sessionId}' event {sequence} のpayloadがnullです。");

            events.Add(new DemonstrationEvent(
                schemaVersion,
                sessionId,
                sequence,
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                payload.Kind,
                ParseTimestamp(reader.GetString(5)),
                ParseTimestamp(reader.GetString(6)),
                payload.Operation,
                payload.FocusChange,
                payload.Stop));
        }

        return events;
    }

    private static void RequireKnownSchema(string schemaVersion, string subject)
    {
        if (!string.Equals(schemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{subject} のschema '{schemaVersion}' は未対応です。");
        }
    }

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record DemonstrationEventPayload(
        DemonstrationEventKind Kind,
        DemonstrationOperation? Operation,
        DemonstrationFocusChange? FocusChange,
        DemonstrationStop? Stop);
}
