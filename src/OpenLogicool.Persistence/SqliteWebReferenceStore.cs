using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Research;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Persistence;

/// <summary>
/// IWebReferenceStoreのSQLite実装。通常書込みはINSERTだけで、payloadの物理削除は
/// source単位のpreview→delete+tombstone transactionだけが行う。
/// </summary>
public sealed class SqliteWebReferenceStore(SqliteConnection connection) : IWebReferenceStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public void AppendSource(WebReferenceSource source)
    {
        WebReferenceContractSchema.Validate(source);
        var json = JsonSerializer.Serialize(source, Json);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO web_reference_sources (source_id, schema_version, policy, source_json, payload_bytes)
            VALUES ($id, $schema, $policy, $json, $bytes);
            """;
        command.Parameters.AddWithValue("$id", source.SourceId);
        command.Parameters.AddWithValue("$schema", source.SchemaVersion);
        command.Parameters.AddWithValue("$policy", source.PolicyDecision.Policy.ToString());
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$bytes", ByteCount(json));
        command.ExecuteNonQuery();
    }

    public void AppendDocument(ReferenceDocument document)
    {
        WebReferenceContractSchema.Validate(document);
        var source = FindSource(document.SourceId)
            ?? throw new InvalidOperationException($"source '{document.SourceId}' がありません。");
        WebReferenceContractSchema.Validate(document, source);
        if (document.ParentDocumentId is { } parentId)
        {
            var parent = FindDocument(parentId)
                ?? throw new InvalidOperationException($"parent document '{parentId}' がありません。");
            if (parent.SourceId != document.SourceId || parent.Revision + 1 != document.Revision)
            {
                throw new InvalidOperationException("document revisionは同じsourceの直前revisionへ連結しなければなりません。");
            }
        }

        var json = JsonSerializer.Serialize(document, Json);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO web_reference_documents (
                document_id, source_id, revision_number, parent_document_id,
                schema_version, policy, document_json, payload_bytes)
            VALUES ($id, $source, $revision, $parent, $schema, $policy, $json, $bytes);
            """;
        command.Parameters.AddWithValue("$id", document.DocumentId);
        command.Parameters.AddWithValue("$source", document.SourceId);
        command.Parameters.AddWithValue("$revision", document.Revision);
        command.Parameters.AddWithValue("$parent", (object?)document.ParentDocumentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$schema", document.SchemaVersion);
        command.Parameters.AddWithValue("$policy", document.Policy.ToString());
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$bytes", ByteCount(json));
        command.ExecuteNonQuery();
    }

    public void AppendFact(WebReferenceFact fact)
    {
        WebReferenceContractSchema.Validate(fact);
        RequireReferencesExist(fact.SourceReferenceIds, "fact");
        RequireContradictionsExist(fact.ContradictionIds);
        if (fact.ParentFactId is { } parentId)
        {
            var parent = FindFact(parentId)
                ?? throw new InvalidOperationException($"parent fact '{parentId}' がありません。");
            if (parent.Revision + 1 != fact.Revision)
            {
                throw new InvalidOperationException("fact revisionは直前revisionへ連結しなければなりません。");
            }
        }

        var json = JsonSerializer.Serialize(fact, Json);
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO web_reference_facts (
                    fact_id, revision_number, parent_fact_id, schema_version, fact_json, payload_bytes)
                VALUES ($id, $revision, $parent, $schema, $json, $bytes);
                """;
            command.Parameters.AddWithValue("$id", fact.FactId);
            command.Parameters.AddWithValue("$revision", fact.Revision);
            command.Parameters.AddWithValue("$parent", (object?)fact.ParentFactId ?? DBNull.Value);
            command.Parameters.AddWithValue("$schema", fact.SchemaVersion);
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$bytes", ByteCount(json));
            command.ExecuteNonQuery();
        }

        InsertReferences(
            transaction,
            "web_reference_fact_sources",
            "fact_id",
            fact.FactId,
            fact.SourceReferenceIds);
        InsertRelations(
            transaction,
            "web_reference_fact_contradictions",
            "fact_id",
            fact.FactId,
            "contradiction_id",
            fact.ContradictionIds);
        transaction.Commit();
    }

    public void AppendContradiction(WebReferenceContradiction contradiction)
    {
        WebReferenceContractSchema.Validate(contradiction);
        RequireReferencesExist(contradiction.SourceReferenceIds, "contradiction");
        if (FindFact(contradiction.LeftFactId) is null || FindFact(contradiction.RightFactId) is null)
        {
            throw new InvalidOperationException("contradictionが参照するfactがありません。");
        }

        if (contradiction.ParentContradictionId is { } parentId)
        {
            var parent = FindContradiction(parentId)
                ?? throw new InvalidOperationException($"parent contradiction '{parentId}' がありません。");
            if (parent.Revision + 1 != contradiction.Revision)
            {
                throw new InvalidOperationException("contradiction revisionは直前revisionへ連結しなければなりません。");
            }
        }

        var json = JsonSerializer.Serialize(contradiction, Json);
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO web_reference_contradictions (
                    contradiction_id, revision_number, parent_contradiction_id,
                    left_fact_id, right_fact_id, schema_version, contradiction_json, payload_bytes)
                VALUES ($id, $revision, $parent, $left, $right, $schema, $json, $bytes);
                """;
            command.Parameters.AddWithValue("$id", contradiction.ContradictionId);
            command.Parameters.AddWithValue("$revision", contradiction.Revision);
            command.Parameters.AddWithValue("$parent", (object?)contradiction.ParentContradictionId ?? DBNull.Value);
            command.Parameters.AddWithValue("$left", contradiction.LeftFactId);
            command.Parameters.AddWithValue("$right", contradiction.RightFactId);
            command.Parameters.AddWithValue("$schema", contradiction.SchemaVersion);
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$bytes", ByteCount(json));
            command.ExecuteNonQuery();
        }

        InsertReferences(
            transaction,
            "web_reference_contradiction_sources",
            "contradiction_id",
            contradiction.ContradictionId,
            contradiction.SourceReferenceIds);
        transaction.Commit();
    }

    public long AppendResearchRun(ResearchRun run)
    {
        WebReferenceContractSchema.Validate(run);
        RequireAcquisitionReferencesExist(run.Attempts);
        var json = JsonSerializer.Serialize(run, Json);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO web_reference_research_runs (run_id, revision_number, schema_version, run_json)
            VALUES (
                $id,
                (SELECT COALESCE(MAX(revision_number), 0) + 1
                 FROM web_reference_research_runs WHERE run_id = $id),
                $schema,
                $json)
            RETURNING revision_number;
            """;
        command.Parameters.AddWithValue("$id", run.RunId);
        command.Parameters.AddWithValue("$schema", run.SchemaVersion);
        command.Parameters.AddWithValue("$json", json);
        return (long)command.ExecuteScalar()!;
    }

    public void AppendExclusion(WebReferenceSourceExclusion exclusion)
    {
        WebReferenceContractSchema.Validate(exclusion);
        InsertJson(
            "web_reference_exclusions",
            "exclusion_id",
            exclusion.ExclusionId,
            exclusion.SchemaVersion,
            "exclusion_json",
            JsonSerializer.Serialize(exclusion, Json));
    }

    public void AppendReacquisitionRequest(WebReferenceReacquisitionRequest request)
    {
        WebReferenceContractSchema.Validate(request);
        InsertJson(
            "web_reference_reacquisition_requests",
            "request_id",
            request.RequestId,
            request.SchemaVersion,
            "request_json",
            JsonSerializer.Serialize(request, Json),
            ("source_id", request.SourceId));
    }

    public IReadOnlyList<WebReferenceSource> ListSources() =>
        ReadJsonRows<WebReferenceSource>(
            "SELECT schema_version, source_json FROM web_reference_sources ORDER BY source_id;");

    public IReadOnlyList<ReferenceDocument> ListDocuments(string? sourceId = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sourceId is null
            ? "SELECT schema_version, document_json FROM web_reference_documents ORDER BY source_id, revision_number;"
            : "SELECT schema_version, document_json FROM web_reference_documents WHERE source_id = $source ORDER BY revision_number;";
        if (sourceId is not null)
        {
            command.Parameters.AddWithValue("$source", sourceId);
        }

        return ReadJsonRows<ReferenceDocument>(command);
    }

    public IReadOnlyList<WebReferenceFact> ListFacts() =>
        ReadJsonRows<WebReferenceFact>(
            "SELECT schema_version, fact_json FROM web_reference_facts ORDER BY fact_id;");

    public IReadOnlyList<WebReferenceContradiction> ListContradictions() =>
        ReadJsonRows<WebReferenceContradiction>(
            "SELECT schema_version, contradiction_json FROM web_reference_contradictions ORDER BY contradiction_id;");

    public IReadOnlyList<ResearchRunRevisionRecord> ListResearchRuns(string? runId = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = runId is null
            ? "SELECT revision_number, schema_version, run_json FROM web_reference_research_runs ORDER BY run_id, revision_number;"
            : "SELECT revision_number, schema_version, run_json FROM web_reference_research_runs WHERE run_id = $id ORDER BY revision_number;";
        if (runId is not null)
        {
            command.Parameters.AddWithValue("$id", runId);
        }

        using var reader = command.ExecuteReader();
        var result = new List<ResearchRunRevisionRecord>();
        while (reader.Read())
        {
            RequireSchema(reader.GetString(1), "ResearchRun");
            result.Add(new(
                reader.GetInt64(0),
                DeserializeAndValidate<ResearchRun>(reader.GetString(2), "ResearchRun")));
        }

        return result;
    }

    public IReadOnlyList<WebReferenceDeletionTombstone> ListTombstones() =>
        ReadJsonRows<WebReferenceDeletionTombstone>(
            "SELECT schema_version, tombstone_json FROM web_reference_tombstones ORDER BY tombstone_id;");

    public IReadOnlyList<WebReferenceSourceExclusion> ListExclusions() =>
        ReadJsonRows<WebReferenceSourceExclusion>(
            "SELECT schema_version, exclusion_json FROM web_reference_exclusions ORDER BY exclusion_id;");

    public IReadOnlyList<WebReferenceReacquisitionRequest> ListReacquisitionRequests() =>
        ReadJsonRows<WebReferenceReacquisitionRequest>(
            "SELECT schema_version, request_json FROM web_reference_reacquisition_requests ORDER BY request_id;");

    public WebReferenceDeletionPreview PreviewDeleteSource(string sourceId)
    {
        RequireText(sourceId, nameof(sourceId));
        if (FindSource(sourceId) is null)
        {
            throw new InvalidOperationException($"source '{sourceId}' がありません。");
        }

        var documentIds = ReadIds(
            "SELECT document_id FROM web_reference_documents WHERE source_id = $source ORDER BY document_id;",
            sourceId);
        var factIds = ReadAffectedEntityIds(sourceId, "fact");
        var contradictionIds = ReadAffectedEntityIds(sourceId, "contradiction");
        var bytes = ScalarLong(
                "SELECT COALESCE(SUM(payload_bytes), 0) FROM web_reference_sources WHERE source_id = $source;",
                sourceId)
            + SumPayloadBytes("web_reference_documents", "document_id", documentIds)
            + SumPayloadBytes("web_reference_facts", "fact_id", factIds)
            + SumPayloadBytes("web_reference_contradictions", "contradiction_id", contradictionIds);
        var preview = new WebReferenceDeletionPreview(
            ContractSchemaVersions.Revision01,
            sourceId,
            documentIds,
            factIds,
            contradictionIds,
            bytes);
        WebReferenceContractSchema.Validate(preview);
        return preview;
    }

    public WebReferenceDeletionTombstone DeleteSource(
        string sourceId,
        string tombstoneId,
        DateTimeOffset deletedUtc,
        string reason)
    {
        RequireText(tombstoneId, nameof(tombstoneId));
        RequireText(reason, nameof(reason));
        var preview = PreviewDeleteSource(sourceId);
        var tombstone = new WebReferenceDeletionTombstone(
            ContractSchemaVersions.Revision01,
            tombstoneId,
            sourceId,
            deletedUtc,
            reason,
            preview.DocumentIds,
            preview.FactIds,
            preview.ContradictionIds);
        WebReferenceContractSchema.Validate(tombstone);
        var json = JsonSerializer.Serialize(tombstone, Json);

        using var transaction = connection.BeginTransaction();
        DeleteReferences(transaction, "web_reference_fact_contradictions", "contradiction_id", preview.ContradictionIds);
        DeleteReferences(transaction, "web_reference_fact_contradictions", "fact_id", preview.FactIds);
        DeleteReferences(transaction, "web_reference_contradiction_sources", "contradiction_id", preview.ContradictionIds);
        DeleteRows(transaction, "web_reference_contradictions", "contradiction_id", preview.ContradictionIds);
        DeleteReferences(transaction, "web_reference_fact_sources", "fact_id", preview.FactIds);
        DeleteRows(transaction, "web_reference_facts", "fact_id", preview.FactIds);
        DeleteRows(transaction, "web_reference_documents", "document_id", preview.DocumentIds);
        using (var deleteSource = connection.CreateCommand())
        {
            deleteSource.Transaction = transaction;
            deleteSource.CommandText = "DELETE FROM web_reference_sources WHERE source_id = $source;";
            deleteSource.Parameters.AddWithValue("$source", sourceId);
            if (deleteSource.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException($"source '{sourceId}' の削除件数が1ではありません。");
            }
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO web_reference_tombstones (tombstone_id, source_id, schema_version, tombstone_json)
                VALUES ($id, $source, $schema, $json);
                """;
            insert.Parameters.AddWithValue("$id", tombstone.TombstoneId);
            insert.Parameters.AddWithValue("$source", tombstone.SourceId);
            insert.Parameters.AddWithValue("$schema", tombstone.SchemaVersion);
            insert.Parameters.AddWithValue("$json", json);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return tombstone;
    }

    public WebReferenceExportBundle Export(DateTimeOffset exportedUtc) => new(
        ContractSchemaVersions.Revision01,
        exportedUtc,
        ListSources(),
        ListDocuments(),
        ListFacts(),
        ListContradictions(),
        ListResearchRuns(),
        ListTombstones(),
        ListExclusions(),
        ListReacquisitionRequests());

    private WebReferenceSource? FindSource(string sourceId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT schema_version, source_json FROM web_reference_sources WHERE source_id = $id;";
        command.Parameters.AddWithValue("$id", sourceId);
        return ReadSingle<WebReferenceSource>(command, "WebReferenceSource");
    }

    private ReferenceDocument? FindDocument(string documentId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT schema_version, document_json FROM web_reference_documents WHERE document_id = $id;";
        command.Parameters.AddWithValue("$id", documentId);
        return ReadSingle<ReferenceDocument>(command, "ReferenceDocument");
    }

    private WebReferenceFact? FindFact(string factId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT schema_version, fact_json FROM web_reference_facts WHERE fact_id = $id;";
        command.Parameters.AddWithValue("$id", factId);
        return ReadSingle<WebReferenceFact>(command, "WebReferenceFact");
    }

    private WebReferenceContradiction? FindContradiction(string contradictionId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT schema_version, contradiction_json FROM web_reference_contradictions WHERE contradiction_id = $id;";
        command.Parameters.AddWithValue("$id", contradictionId);
        return ReadSingle<WebReferenceContradiction>(command, "WebReferenceContradiction");
    }

    private static int ByteCount(string json) => Encoding.UTF8.GetByteCount(json);

    private void InsertJson(
        string table,
        string idColumn,
        string id,
        string schema,
        string jsonColumn,
        string json,
        params (string Column, string Value)[] extra)
    {
        var columns = new[] { idColumn, "schema_version", jsonColumn }.Concat(extra.Select(value => value.Column));
        var parameters = new[] { "$id", "$schema", "$json" }.Concat(extra.Select((_, index) => $"$extra{index}"));
        using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)});";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$schema", schema);
        command.Parameters.AddWithValue("$json", json);
        for (var index = 0; index < extra.Length; index++)
        {
            command.Parameters.AddWithValue($"$extra{index}", extra[index].Value);
        }

        command.ExecuteNonQuery();
    }

    private void InsertReferences(
        SqliteTransaction transaction,
        string table,
        string ownerColumn,
        string ownerId,
        IReadOnlyList<string> references) =>
        InsertRelations(transaction, table, ownerColumn, ownerId, "source_reference_id", references);

    private void InsertRelations(
        SqliteTransaction transaction,
        string table,
        string ownerColumn,
        string ownerId,
        string targetColumn,
        IReadOnlyList<string> targetIds)
    {
        foreach (var targetId in targetIds)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {table} ({ownerColumn}, {targetColumn}) VALUES ($owner, $target);";
            command.Parameters.AddWithValue("$owner", ownerId);
            command.Parameters.AddWithValue("$target", targetId);
            command.ExecuteNonQuery();
        }
    }

    private IReadOnlyList<T> ReadJsonRows<T>(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return ReadJsonRows<T>(command);
    }

    private static IReadOnlyList<T> ReadJsonRows<T>(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read())
        {
            RequireSchema(reader.GetString(0), typeof(T).Name);
            result.Add(DeserializeAndValidate<T>(reader.GetString(1), typeof(T).Name));
        }

        return result;
    }

    private static T? ReadSingle<T>(SqliteCommand command, string kind) where T : class
    {
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        RequireSchema(reader.GetString(0), kind);
        return DeserializeAndValidate<T>(reader.GetString(1), kind);
    }

    private static T DeserializeAndValidate<T>(string json, string kind)
    {
        var value = JsonSerializer.Deserialize<T>(json, Json)
            ?? throw new InvalidOperationException($"{kind} JSONがnullです。");
        switch (value)
        {
            case WebReferenceSource source:
                WebReferenceContractSchema.Validate(source);
                break;
            case ReferenceDocument document:
                WebReferenceContractSchema.Validate(document);
                break;
            case WebReferenceFact fact:
                WebReferenceContractSchema.Validate(fact);
                break;
            case WebReferenceContradiction contradiction:
                WebReferenceContractSchema.Validate(contradiction);
                break;
            case ResearchRun run:
                WebReferenceContractSchema.Validate(run);
                break;
            case WebReferenceDeletionTombstone tombstone:
                WebReferenceContractSchema.Validate(tombstone);
                break;
            case WebReferenceSourceExclusion exclusion:
                WebReferenceContractSchema.Validate(exclusion);
                break;
            case WebReferenceReacquisitionRequest request:
                WebReferenceContractSchema.Validate(request);
                break;
            default:
                throw new InvalidOperationException($"{kind} payload型 '{typeof(T).Name}' の検証器がありません。");
        }

        return value;
    }

    private static void RequireSchema(string schema, string kind)
    {
        if (schema != ContractSchemaVersions.Revision01)
        {
            throw new InvalidOperationException($"{kind} schema version '{schema}' は未対応です。");
        }
    }

    private IReadOnlyList<string> ReadIds(string sql, string sourceId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$source", sourceId);
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private IReadOnlyList<string> ReadAffectedEntityIds(string sourceId, string entityKind)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH RECURSIVE affected(kind, id) AS (
                SELECT 'fact', relation.fact_id
                FROM web_reference_fact_sources relation
                WHERE relation.source_reference_id = $source
                   OR relation.source_reference_id IN (
                       SELECT document_id FROM web_reference_documents WHERE source_id = $source)
                UNION
                SELECT 'contradiction', relation.contradiction_id
                FROM web_reference_contradiction_sources relation
                WHERE relation.source_reference_id = $source
                   OR relation.source_reference_id IN (
                       SELECT document_id FROM web_reference_documents WHERE source_id = $source)
                UNION
                SELECT 'fact', child.fact_id
                FROM web_reference_facts child
                JOIN affected parent
                  ON parent.kind = 'fact' AND child.parent_fact_id = parent.id
                UNION
                SELECT 'contradiction', contradiction.contradiction_id
                FROM web_reference_contradictions contradiction
                JOIN affected fact
                  ON fact.kind = 'fact'
                 AND (contradiction.left_fact_id = fact.id OR contradiction.right_fact_id = fact.id)
                UNION
                SELECT 'contradiction', child.contradiction_id
                FROM web_reference_contradictions child
                JOIN affected parent
                  ON parent.kind = 'contradiction'
                 AND child.parent_contradiction_id = parent.id
                UNION
                SELECT 'fact', relation.fact_id
                FROM web_reference_fact_contradictions relation
                JOIN affected contradiction
                  ON contradiction.kind = 'contradiction'
                 AND relation.contradiction_id = contradiction.id
            )
            SELECT id FROM affected WHERE kind = $kind ORDER BY id;
            """;
        command.Parameters.AddWithValue("$source", sourceId);
        command.Parameters.AddWithValue("$kind", entityKind);
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private long ScalarLong(string sql, string sourceId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$source", sourceId);
        return (long)command.ExecuteScalar()!;
    }

    private long SumPayloadBytes(string table, string idColumn, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        using var command = connection.CreateCommand();
        var parameters = ids.Select((_, index) => $"$id{index}").ToArray();
        command.CommandText =
            $"SELECT COALESCE(SUM(payload_bytes), 0) FROM {table} WHERE {idColumn} IN ({string.Join(", ", parameters)});";
        for (var index = 0; index < ids.Count; index++)
        {
            command.Parameters.AddWithValue(parameters[index], ids[index]);
        }

        return (long)command.ExecuteScalar()!;
    }

    private void RequireReferencesExist(IReadOnlyList<string> referenceIds, string ownerKind)
    {
        foreach (var referenceId in referenceIds)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT EXISTS (
                    SELECT 1 FROM web_reference_sources WHERE source_id = $id
                    UNION ALL
                    SELECT 1 FROM web_reference_documents WHERE document_id = $id);
                """;
            command.Parameters.AddWithValue("$id", referenceId);
            if ((long)command.ExecuteScalar()! == 0)
            {
                throw new InvalidOperationException($"{ownerKind} source reference '{referenceId}' がありません。");
            }
        }
    }

    private void RequireContradictionsExist(IReadOnlyList<string> contradictionIds)
    {
        foreach (var contradictionId in contradictionIds)
        {
            if (FindContradiction(contradictionId) is null)
            {
                throw new InvalidOperationException($"fact contradiction '{contradictionId}' がありません。");
            }
        }
    }

    private void RequireAcquisitionReferencesExist(IReadOnlyList<WebReferenceAcquisitionAttempt> attempts)
    {
        foreach (var attempt in attempts)
        {
            if (attempt.SourceId is null)
            {
                continue;
            }

            var source = FindSource(attempt.SourceId)
                ?? throw new InvalidOperationException($"research attempt source '{attempt.SourceId}' がありません。");
            var documentId = attempt.NewDocumentId ?? attempt.ExistingDocumentId!;
            var document = FindDocument(documentId)
                ?? throw new InvalidOperationException($"research attempt document '{documentId}' がありません。");
            if (!string.Equals(document.SourceId, source.SourceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("research attemptのsourceとdocumentが一致しません。");
            }
        }
    }

    private void DeleteReferences(
        SqliteTransaction transaction,
        string table,
        string ownerColumn,
        IReadOnlyList<string> ids) => DeleteRows(transaction, table, ownerColumn, ids);

    private void DeleteRows(
        SqliteTransaction transaction,
        string table,
        string idColumn,
        IReadOnlyList<string> ids)
    {
        foreach (var id in ids)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table} WHERE {idColumn} = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name}が空です。", name);
        }
    }
}
