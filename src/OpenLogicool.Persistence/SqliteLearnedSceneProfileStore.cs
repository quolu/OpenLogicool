using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Persistence;

public sealed class SqliteLearnedSceneProfileStore(SqliteConnection connection) : ILearnedSceneProfileStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public void Upsert(LearnedSceneProfileDocument document)
    {
        Upsert(document, transaction: null);
    }

    internal void Upsert(LearnedSceneProfileDocument document, SqliteTransaction? transaction)
    {
        LearnedSceneProfileValidator.Validate(document);
        if (transaction is not null && !ReferenceEquals(transaction.Connection, connection))
        {
            throw new ArgumentException("transactionはこのscene profile storeのconnectionに属していません。", nameof(transaction));
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO learned_scene_profiles (
                game_id, environment_scope, schema_version, profile_id, profile_version, document_json)
            VALUES ($gameId, $environmentScope, $schemaVersion, $profileId, $profileVersion, $documentJson)
            ON CONFLICT(game_id, environment_scope) DO UPDATE SET
                schema_version = excluded.schema_version,
                profile_id = excluded.profile_id,
                profile_version = excluded.profile_version,
                document_json = excluded.document_json;
            """;
        command.Parameters.AddWithValue("$gameId", document.GameId);
        command.Parameters.AddWithValue("$environmentScope", document.EnvironmentScope);
        command.Parameters.AddWithValue("$schemaVersion", document.SchemaVersion);
        command.Parameters.AddWithValue("$profileId", document.ProfileId);
        command.Parameters.AddWithValue("$profileVersion", document.ProfileVersion);
        command.Parameters.AddWithValue("$documentJson", JsonSerializer.Serialize(document, Json));
        command.ExecuteNonQuery();
    }

    public LearnedSceneProfileDocument? Load(string gameId, string environmentScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT document_json
            FROM learned_scene_profiles
            WHERE game_id = $gameId AND environment_scope = $environmentScope;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue("$environmentScope", environmentScope);
        var json = command.ExecuteScalar() as string;
        if (json is null)
        {
            return null;
        }
        var document = JsonSerializer.Deserialize<LearnedSceneProfileDocument>(json, Json)
            ?? throw new InvalidOperationException("学習済みscene profile JSONがnullです。");
        LearnedSceneProfileValidator.Validate(document);
        return document;
    }
}
