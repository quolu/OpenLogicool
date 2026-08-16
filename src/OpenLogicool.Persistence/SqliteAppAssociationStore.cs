using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Persistence;

/// <summary>
/// IAppAssociationStore の SQLite 実装（OPS-002）。
/// 未知 schema version は読み飛ばさず例外にする。
/// </summary>
public sealed class SqliteAppAssociationStore(SqliteConnection connection) : IAppAssociationStore
{
    public void Upsert(AppProfileAssociation association)
    {
        if (association.SchemaVersion != ContractSchemaVersions.Revision01)
        {
            throw new ArgumentException(
                $"AppProfileAssociation schema version '{association.SchemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。",
                nameof(association));
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO app_profile_associations (application_full_path, device_kind, schema_version, profile_id, matcher_kind)
            VALUES ($applicationFullPath, $deviceKind, $schemaVersion, $profileId, $matcherKind)
            ON CONFLICT (application_full_path, device_kind) DO UPDATE SET
                schema_version = excluded.schema_version,
                profile_id = excluded.profile_id,
                matcher_kind = excluded.matcher_kind;
            """;
        command.Parameters.AddWithValue("$applicationFullPath", association.ApplicationFullPath);
        command.Parameters.AddWithValue("$deviceKind", association.DeviceKind);
        command.Parameters.AddWithValue("$schemaVersion", association.SchemaVersion);
        command.Parameters.AddWithValue("$profileId", association.ProfileId);
        command.Parameters.AddWithValue("$matcherKind", association.MatcherKind);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<AppProfileAssociation> ListAll()
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT application_full_path, device_kind, schema_version, profile_id, matcher_kind
            FROM app_profile_associations
            ORDER BY application_full_path, device_kind;
            """;
        using var reader = command.ExecuteReader();

        var associations = new List<AppProfileAssociation>();
        while (reader.Read())
        {
            var schemaVersion = reader.GetString(2);
            if (schemaVersion != ContractSchemaVersions.Revision01)
            {
                throw new InvalidOperationException(
                    $"app association ('{reader.GetString(0)}', '{reader.GetString(1)}') の schema version '{schemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。");
            }

            associations.Add(new AppProfileAssociation(
                schemaVersion,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return associations;
    }
}
