using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Persistence;

/// <summary>
/// IMappingProfileStore の SQLite 実装（OPS-002: 設定を app 再起動後に復元できる）。
/// document は versioned JSON として格納し、読み出し時に schema version を検証する。
/// 未知 version・壊れた JSON は例外として現れ、黙って読み飛ばさない。
/// </summary>
public sealed class SqliteMappingProfileStore(SqliteConnection connection) : IMappingProfileStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public void Upsert(MappingProfileDocument document)
    {
        if (document.SchemaVersion != ContractSchemaVersions.Revision01)
        {
            throw new ArgumentException(
                $"MappingProfileDocument schema version '{document.SchemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。",
                nameof(document));
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO mapping_profiles (profile_id, schema_version, document_json)
            VALUES ($profileId, $schemaVersion, $documentJson)
            ON CONFLICT (profile_id) DO UPDATE SET
                schema_version = excluded.schema_version,
                document_json = excluded.document_json;
            """;
        command.Parameters.AddWithValue("$profileId", document.ProfileId);
        command.Parameters.AddWithValue("$schemaVersion", document.SchemaVersion);
        command.Parameters.AddWithValue("$documentJson", JsonSerializer.Serialize(document, Json));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<MappingProfileDocument> ListAll()
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT profile_id, schema_version, document_json FROM mapping_profiles ORDER BY profile_id;";
        using var reader = command.ExecuteReader();

        var documents = new List<MappingProfileDocument>();
        while (reader.Read())
        {
            var profileId = reader.GetString(0);
            var schemaVersion = reader.GetString(1);
            if (schemaVersion != ContractSchemaVersions.Revision01)
            {
                throw new InvalidOperationException(
                    $"mapping profile '{profileId}' の schema version '{schemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。");
            }

            var document = JsonSerializer.Deserialize<MappingProfileDocument>(reader.GetString(2), Json)
                ?? throw new InvalidOperationException($"mapping profile '{profileId}' の document JSON が null です。");
            if (document.ProfileId != profileId)
            {
                throw new InvalidOperationException(
                    $"mapping profile row '{profileId}' と document 内 ProfileId '{document.ProfileId}' が一致しません。");
            }

            documents.Add(document);
        }

        return documents;
    }
}
