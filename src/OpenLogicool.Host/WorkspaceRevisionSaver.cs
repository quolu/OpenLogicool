using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Persistence;
using OpenLogicool.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// workspace revision の追記＋device 種別ごとの profile upsert を単一 transaction で行う共有実装
/// （APP-007: 部分保存を作らない）。CLI（Program.cs の workspace／undo command）と
/// UI（<see cref="HostWorkspaceEditorIntents"/> の Save／Undo intent）の両方から呼び、
/// 保存規則を二重化しない。
/// </summary>
internal static class WorkspaceRevisionSaver
{
    /// <summary>
    /// revision 追記と profile upsert を単一 transaction で行う。
    /// 保存後の全体が解決可能かも transaction 前に検証する（部分保存に伴う不整合を作らない）。
    /// </summary>
    public static long SaveCompilation(SqliteConnection connection, WorkspaceDocument document, WorkspaceCompilation compilation)
    {
        var store = new SqliteMappingProfileStore(connection);
        var associationStore = new SqliteAppAssociationStore(connection);

        var compiledIds = compilation.Profiles.Select(profile => profile.ProfileId).ToHashSet(StringComparer.Ordinal);
        var prospective = store.ListAll()
            .Where(existing => !compiledIds.Contains(existing.ProfileId))
            .Concat(compilation.Profiles)
            .ToList();
        AppProfileResolver.Build(prospective, associationStore.ListAll());

        ExecuteSql(connection, "BEGIN IMMEDIATE;");
        try
        {
            var revisionNumber = new SqliteWorkspaceRevisionStore(connection)
                .Append(document, DateTime.UtcNow.ToString("o"));
            foreach (var profile in compilation.Profiles)
            {
                store.Upsert(profile);
            }

            ExecuteSql(connection, "COMMIT;");
            return revisionNumber;
        }
        catch
        {
            // SQLite 境界の失敗時に部分保存を残さない（原因はそのまま呼び出し元へ）
            ExecuteSql(connection, "ROLLBACK;");
            throw;
        }
    }

    /// <summary>常駐 host が起動中か（named mutex の観測結果）。</summary>
    public static bool IsHostResident()
    {
        if (Mutex.TryOpenExisting(SingleInstanceGuard.DefaultName, out var mutex))
        {
            mutex.Dispose();
            return true;
        }

        return false;
    }

    private static void ExecuteSql(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
