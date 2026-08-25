using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Persistence;

/// <summary>学習ルートを親版つきのappend-only revisionとして保存する。</summary>
public sealed class SqliteLearningRouteStore(SqliteConnection connection) : ILearningRouteStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public LearningRouteRevision Append(LearningRouteDraft draft)
    {
        using var transaction = connection.BeginTransaction();
        var revision = Append(draft, transaction);
        transaction.Commit();
        return revision;
    }

    internal LearningRouteRevision Append(LearningRouteDraft draft, SqliteTransaction transaction)
    {
        ValidateDraft(draft);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection))
        {
            throw new ArgumentException("transactionはこのLearning Route storeのconnectionに属していません。", nameof(transaction));
        }

        var (lastNumber, actualParent) = ReadHead(draft.RouteId, transaction);
        if (!string.Equals(draft.ParentVersionId, actualParent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"学習ルート版が競合しています。expected='{draft.ParentVersionId ?? "<root>"}', actual='{actualParent ?? "<root>"}'。");
        }

        var revisionNumber = lastNumber + 1;
        var revision = new LearningRouteRevision(
            draft.SchemaVersion,
            draft.RouteId,
            revisionNumber,
            LearningRouteVersionIds.Next(draft.RouteId, actualParent, revisionNumber),
            actualParent,
            draft.GameId,
            draft.EnvironmentScope,
            draft.StructureRevisionId,
            draft.Goal,
            draft.EdgeIds.ToArray(),
            draft.Author,
            draft.UserInstruction,
            draft.ChangeReason,
            draft.Status,
            draft.CreatedUtc.ToUniversalTime());

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO learning_route_revisions (
                route_id, revision_number, version_id, parent_version_id,
                game_id, environment_scope, schema_version, document_json)
            VALUES (
                $routeId, $revisionNumber, $versionId, $parentVersionId,
                $gameId, $environmentScope, $schemaVersion, $documentJson);
            """;
        command.Parameters.AddWithValue("$routeId", revision.RouteId);
        command.Parameters.AddWithValue("$revisionNumber", revision.RevisionNumber);
        command.Parameters.AddWithValue("$versionId", revision.VersionId);
        command.Parameters.AddWithValue("$parentVersionId", (object?)revision.ParentVersionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$gameId", revision.GameId);
        command.Parameters.AddWithValue("$environmentScope", revision.EnvironmentScope);
        command.Parameters.AddWithValue("$schemaVersion", revision.SchemaVersion);
        command.Parameters.AddWithValue("$documentJson", JsonSerializer.Serialize(revision, Json));
        command.ExecuteNonQuery();
        return revision;
    }

    public IReadOnlyList<LearningRouteRevision> ReadRevisions(string routeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeId);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT revision_number, schema_version, document_json
            FROM learning_route_revisions
            WHERE route_id = $routeId
            ORDER BY revision_number;
            """;
        command.Parameters.AddWithValue("$routeId", routeId);
        using var reader = command.ExecuteReader();
        var revisions = new List<LearningRouteRevision>();
        while (reader.Read())
        {
            var number = reader.GetInt64(0);
            var schemaVersion = reader.GetString(1);
            if (!string.Equals(schemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"学習ルート '{routeId}' revision {number} のschema '{schemaVersion}' は未対応です。");
            }
            var revision = JsonSerializer.Deserialize<LearningRouteRevision>(reader.GetString(2), Json)
                ?? throw new InvalidOperationException($"学習ルート '{routeId}' revision {number} がnullです。");
            revisions.Add(revision);
        }
        return revisions;
    }

    public LearningRouteRevision? LoadLatest(string routeId) => ReadRevisions(routeId).LastOrDefault();

    public IReadOnlyList<string> ListRouteIds(string gameId, string environmentScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT route_id
            FROM learning_route_revisions
            WHERE game_id = $gameId AND environment_scope = $environmentScope
            ORDER BY route_id;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue("$environmentScope", environmentScope);
        using var reader = command.ExecuteReader();
        var routeIds = new List<string>();
        while (reader.Read())
        {
            routeIds.Add(reader.GetString(0));
        }
        return routeIds;
    }

    private static (long Number, string? VersionId) ReadHead(string routeId, SqliteTransaction transaction)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT revision_number, version_id
            FROM learning_route_revisions
            WHERE route_id = $routeId
            ORDER BY revision_number DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$routeId", routeId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetString(1)) : (0, null);
    }

    private static void ValidateDraft(LearningRouteDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!string.Equals(draft.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(draft.RouteId)
            || string.IsNullOrWhiteSpace(draft.GameId)
            || string.IsNullOrWhiteSpace(draft.EnvironmentScope)
            || string.IsNullOrWhiteSpace(draft.StructureRevisionId)
            || string.IsNullOrWhiteSpace(draft.Goal)
            || draft.EdgeIds is null
            || draft.EdgeIds.Count == 0
            || draft.EdgeIds.Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrWhiteSpace(draft.ChangeReason)
            || !Enum.IsDefined(draft.Author)
            || !Enum.IsDefined(draft.Status))
        {
            throw new ArgumentException("LearningRouteDraftの必須fieldまたはschemaが不正です。", nameof(draft));
        }
    }
}
