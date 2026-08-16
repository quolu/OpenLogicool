using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Persistence;

/// <summary>
/// IWorkspaceRevisionStore の SQLite 実装（MAP-009）。
/// revision は workspace ごとの連番で append-only に追記し、上書き・削除はしない。
/// 未知 version・壊れた JSON は例外として現れ、黙って読み飛ばさない。
/// </summary>
public sealed class SqliteWorkspaceRevisionStore(SqliteConnection connection) : IWorkspaceRevisionStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public long Append(WorkspaceDocument document, string savedAtUtc)
    {
        if (document.SchemaVersion != ContractSchemaVersions.Revision01)
        {
            throw new ArgumentException(
                $"WorkspaceDocument schema version '{document.SchemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。",
                nameof(document));
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO workspace_revisions (workspace_id, revision_number, schema_version, saved_at_utc, document_json)
            VALUES (
                $workspaceId,
                (SELECT COALESCE(MAX(revision_number), 0) + 1 FROM workspace_revisions WHERE workspace_id = $workspaceId),
                $schemaVersion,
                $savedAtUtc,
                $documentJson)
            RETURNING revision_number;
            """;
        command.Parameters.AddWithValue("$workspaceId", document.WorkspaceId);
        command.Parameters.AddWithValue("$schemaVersion", document.SchemaVersion);
        command.Parameters.AddWithValue("$savedAtUtc", savedAtUtc);
        command.Parameters.AddWithValue("$documentJson", JsonSerializer.Serialize(document, Json));
        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<WorkspaceRevisionRecord> ListRevisions(string workspaceId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT revision_number, schema_version, saved_at_utc, document_json
            FROM workspace_revisions
            WHERE workspace_id = $workspaceId
            ORDER BY revision_number;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        using var reader = command.ExecuteReader();

        var revisions = new List<WorkspaceRevisionRecord>();
        while (reader.Read())
        {
            var revisionNumber = reader.GetInt64(0);
            var schemaVersion = reader.GetString(1);
            if (schemaVersion != ContractSchemaVersions.Revision01)
            {
                throw new InvalidOperationException(
                    $"workspace '{workspaceId}' revision {revisionNumber} の schema version '{schemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。");
            }

            var document = JsonSerializer.Deserialize<WorkspaceDocument>(reader.GetString(3), Json)
                ?? throw new InvalidOperationException(
                    $"workspace '{workspaceId}' revision {revisionNumber} の document JSON が null です。");
            if (document.WorkspaceId != workspaceId)
            {
                throw new InvalidOperationException(
                    $"workspace revision row '{workspaceId}' と document 内 WorkspaceId '{document.WorkspaceId}' が一致しません。");
            }

            revisions.Add(new WorkspaceRevisionRecord(revisionNumber, reader.GetString(2), document));
        }

        return revisions;
    }
}
