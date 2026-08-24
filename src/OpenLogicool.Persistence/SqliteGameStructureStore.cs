using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;

namespace OpenLogicool.Persistence;

/// <summary>
/// Game StructureのSQLite正本。書込み口はappendだけで、projectionとexportはevent replayから生成する。
/// </summary>
public sealed class SqliteGameStructureStore(SqliteConnection connection) : IGameStructureStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public StructureEvent Append(
        StructureEventDraft draft,
        string? expectedParentRevisionId,
        DateTimeOffset persistedUtc)
    {
        ValidateDraft(draft);

        using var transaction = connection.BeginTransaction();
        var (lastSequence, parentRevisionId) = ReadHead(draft.GameId, draft.EnvironmentScope, transaction);
        if (!string.Equals(expectedParentRevisionId, parentRevisionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"structure revision競合です。expected='{expectedParentRevisionId ?? "<root>"}', actual='{parentRevisionId ?? "<root>"}'。");
        }

        var sequence = lastSequence + 1;
        var resultingRevisionId = StructureRevisionIds.Next(parentRevisionId, draft.EventId, sequence);
        var structureEvent = new StructureEvent(
            draft.SchemaVersion,
            draft.EventId,
            draft.GameId,
            draft.EnvironmentScope,
            sequence,
            parentRevisionId,
            resultingRevisionId,
            draft.Kind,
            draft.Actor,
            draft.CorrelationId,
            draft.CausationId,
            draft.ObservationId,
            draft.ProposalId,
            draft.AttemptId,
            draft.EvidenceIds.ToArray(),
            draft.PayloadType,
            draft.PayloadJson,
            draft.Outcome,
            draft.OccurredUtc.ToUniversalTime(),
            persistedUtc.ToUniversalTime());

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO structure_events (
                game_id, environment_scope, event_sequence, schema_version, event_id,
                parent_revision_id, resulting_revision_id, event_kind, actor,
                correlation_id, causation_id, observation_id, proposal_id, attempt_id,
                evidence_ids_json, payload_type, payload_json, outcome, occurred_utc, persisted_utc)
            VALUES (
                $gameId, $environmentScope, $sequence, $schemaVersion, $eventId,
                $parentRevisionId, $resultingRevisionId, $eventKind, $actor,
                $correlationId, $causationId, $observationId, $proposalId, $attemptId,
                $evidenceIdsJson, $payloadType, $payloadJson, $outcome, $occurredUtc, $persistedUtc);
            """;
        command.Parameters.AddWithValue("$gameId", structureEvent.GameId);
        command.Parameters.AddWithValue("$environmentScope", structureEvent.EnvironmentScope);
        command.Parameters.AddWithValue("$sequence", structureEvent.Sequence);
        command.Parameters.AddWithValue("$schemaVersion", structureEvent.SchemaVersion);
        command.Parameters.AddWithValue("$eventId", structureEvent.EventId);
        command.Parameters.AddWithValue("$parentRevisionId", (object?)structureEvent.ParentStructureRevisionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$resultingRevisionId", structureEvent.ResultingStructureRevisionId);
        command.Parameters.AddWithValue("$eventKind", structureEvent.Kind.ToString());
        command.Parameters.AddWithValue("$actor", structureEvent.Actor.ToString());
        command.Parameters.AddWithValue("$correlationId", structureEvent.CorrelationId);
        command.Parameters.AddWithValue("$causationId", structureEvent.CausationId);
        command.Parameters.AddWithValue("$observationId", (object?)structureEvent.ObservationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$proposalId", (object?)structureEvent.ProposalId ?? DBNull.Value);
        command.Parameters.AddWithValue("$attemptId", (object?)structureEvent.AttemptId ?? DBNull.Value);
        command.Parameters.AddWithValue("$evidenceIdsJson", JsonSerializer.Serialize(structureEvent.EvidenceIds, Json));
        command.Parameters.AddWithValue("$payloadType", structureEvent.PayloadType);
        command.Parameters.AddWithValue("$payloadJson", structureEvent.PayloadJson);
        command.Parameters.AddWithValue("$outcome", structureEvent.Outcome is null ? DBNull.Value : structureEvent.Outcome.Value.ToString());
        command.Parameters.AddWithValue("$occurredUtc", Format(structureEvent.OccurredUtc));
        command.Parameters.AddWithValue("$persistedUtc", Format(structureEvent.PersistedUtc));
        command.ExecuteNonQuery();
        transaction.Commit();
        return structureEvent;
    }

    public IReadOnlyList<StructureEvent> ReadEvents(string gameId, string environmentScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT event_sequence, schema_version, event_id, parent_revision_id, resulting_revision_id,
                   event_kind, actor, correlation_id, causation_id, observation_id, proposal_id, attempt_id,
                   evidence_ids_json, payload_type, payload_json, outcome, occurred_utc, persisted_utc
            FROM structure_events
            WHERE game_id = $gameId AND environment_scope = $environmentScope
            ORDER BY event_sequence;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue("$environmentScope", environmentScope);
        using var reader = command.ExecuteReader();

        var events = new List<StructureEvent>();
        while (reader.Read())
        {
            var sequence = reader.GetInt64(0);
            var schemaVersion = reader.GetString(1);
            if (!string.Equals(schemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"game '{gameId}' environment '{environmentScope}' sequence {sequence} のschema '{schemaVersion}' は未対応です。");
            }

            var eventKind = ParseEnum<StructureEventKind>(reader.GetString(5), "event kind", sequence);
            var actor = ParseEnum<StructureEventActor>(reader.GetString(6), "actor", sequence);
            ExplorationOutcomeKind? outcome = reader.IsDBNull(15)
                ? null
                : ParseEnum<ExplorationOutcomeKind>(reader.GetString(15), "outcome", sequence);
            var evidenceIds = JsonSerializer.Deserialize<string[]>(reader.GetString(12), Json)
                ?? throw new InvalidOperationException($"sequence {sequence} のevidence JSONがnullです。");
            events.Add(new StructureEvent(
                schemaVersion,
                reader.GetString(2),
                gameId,
                environmentScope,
                sequence,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                eventKind,
                actor,
                reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                evidenceIds,
                reader.GetString(13),
                reader.GetString(14),
                outcome,
                Parse(reader.GetString(16)),
                Parse(reader.GetString(17))));
        }

        return events;
    }

    public IReadOnlyList<string> ListGameIds()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT game_id FROM structure_events ORDER BY game_id;";
        using var reader = command.ExecuteReader();
        var gameIds = new List<string>();
        while (reader.Read())
        {
            gameIds.Add(reader.GetString(0));
        }
        return gameIds;
    }

    public GameStructureRevision LoadRevision(string gameId, string environmentScope) =>
        GameStructureProjector.Replay(gameId, environmentScope, ReadEvents(gameId, environmentScope));

    public StructureKnowledgePackExport Export(
        string gameId,
        string environmentScope,
        DateTimeOffset createdUtc)
    {
        var events = ReadEvents(gameId, environmentScope);
        var revision = GameStructureProjector.Replay(gameId, environmentScope, events);
        return new StructureKnowledgePackExport(
            ContractSchemaVersions.Revision03,
            $"knowledge:{revision.RevisionId["structure:".Length..]}",
            gameId,
            environmentScope,
            revision,
            events,
            createdUtc);
    }

    private static (long Sequence, string? RevisionId) ReadHead(
        string gameId,
        string environmentScope,
        SqliteTransaction transaction)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT event_sequence, resulting_revision_id
            FROM structure_events
            WHERE game_id = $gameId AND environment_scope = $environmentScope
            ORDER BY event_sequence DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue("$environmentScope", environmentScope);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetString(1)) : (0, null);
    }

    private static void ValidateDraft(StructureEventDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!string.Equals(draft.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(draft.EventId)
            || string.IsNullOrWhiteSpace(draft.GameId)
            || string.IsNullOrWhiteSpace(draft.EnvironmentScope)
            || string.IsNullOrWhiteSpace(draft.CorrelationId)
            || string.IsNullOrWhiteSpace(draft.CausationId)
            || draft.EvidenceIds is null
            || draft.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || draft.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != draft.EvidenceIds.Count
            || string.IsNullOrWhiteSpace(draft.PayloadType)
            || string.IsNullOrWhiteSpace(draft.PayloadJson))
        {
            throw new ArgumentException("StructureEventDraftの必須fieldまたはschemaが不正です。", nameof(draft));
        }
        if (draft.Kind == StructureEventKind.DispatchArmed
            && (string.IsNullOrWhiteSpace(draft.AttemptId) || draft.Outcome is not null))
        {
            throw new ArgumentException("DispatchArmedはAttemptIdを要求し、Outcomeを許可しません。", nameof(draft));
        }
        if (draft.Kind == StructureEventKind.OutcomeRecorded
            && (string.IsNullOrWhiteSpace(draft.AttemptId) || draft.Outcome is null))
        {
            throw new ArgumentException("OutcomeRecordedはAttemptIdとOutcomeを要求します。", nameof(draft));
        }
        if (draft.Kind is StructureEventKind.MutationApplied or StructureEventKind.CorrectionApplied)
        {
            if (!string.Equals(draft.PayloadType, StructureEventPayloadTypes.MutationBatch, StringComparison.Ordinal))
            {
                throw new ArgumentException("mutation eventはStructureMutationBatchだけを受理します。", nameof(draft));
            }
            var batch = JsonSerializer.Deserialize<StructureMutationBatch>(draft.PayloadJson, Json)
                ?? throw new ArgumentException("mutation payloadがnullです。", nameof(draft));
            if (!string.Equals(batch.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
                || batch.Mutations is null
                || batch.Mutations.Count == 0
                || batch.Mutations.Any(mutation => mutation is null))
            {
                throw new ArgumentException("mutation payloadのschemaまたはoperationが不正です。", nameof(draft));
            }
        }
        else
        {
            try
            {
                using var _ = JsonDocument.Parse(draft.PayloadJson);
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("event payloadがJSONではありません。", nameof(draft), exception);
            }
        }
        if (draft.Kind == StructureEventKind.CorrectionApplied && draft.Actor != StructureEventActor.User)
        {
            throw new ArgumentException("CorrectionAppliedのactorはUserだけです。", nameof(draft));
        }
    }

    private static T ParseEnum<T>(string value, string label, long sequence) where T : struct, Enum
    {
        if (!Enum.TryParse<T>(value, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new InvalidOperationException($"sequence {sequence} の{label} '{value}' は未対応です。");
        }
        return parsed;
    }

    private static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
