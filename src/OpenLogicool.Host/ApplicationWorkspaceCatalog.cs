using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// Application Workspace の1行: 一つの app（または既定）に属する device 種別ごとの適用 profile。
/// ApplicationFullPath は正規化済み小文字 full path、既定は AppProfileResolver.DefaultMarker。
/// ProfileIdByKind に無い種別は既定 workspace の割当が適用される（行内では未指定として表す）。
/// </summary>
public sealed record ApplicationWorkspaceRow(
    string ApplicationFullPath,
    IReadOnlyDictionary<string, string> ProfileIdByKind);

/// <summary>
/// Application list／Workspace の機能中核（pure・計画 §5.1〜5.2）。
/// 保存済み profile と app 関連付けから「app 単位で両 device の割当をまとめた編集単位」の一覧を作る。
/// 整合性検証（未保存 profile 参照・既定欠落）は AppProfileResolver.Build が持つため、
/// 呼び出し側は先に Build を通してからこの catalog を作る。
/// </summary>
public static class ApplicationWorkspaceCatalog
{
    /// <summary>既定 workspace を先頭に、app path 昇順で workspace 行を返す。</summary>
    public static IReadOnlyList<ApplicationWorkspaceRow> Build(
        AppProfileResolver resolver,
        IReadOnlyList<AppProfileAssociation> associations)
    {
        var byApp = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var association in associations)
        {
            var path = association.ApplicationFullPath == AppProfileResolver.DefaultMarker
                ? AppProfileResolver.DefaultMarker
                : AppProfileResolver.NormalizePath(association.ApplicationFullPath);
            if (!byApp.TryGetValue(path, out var byKind))
            {
                byApp[path] = byKind = new SortedDictionary<string, string>(StringComparer.Ordinal);
            }

            byKind[association.DeviceKind] = association.ProfileId;
        }

        // 既定 workspace は関連付けの有無に依らず resolver の既定（単一 profile 互換を含む）で構成する
        var defaultByKind = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (kind, document) in resolver.DefaultByKind)
        {
            defaultByKind[kind] = document.ProfileId;
        }

        var rows = new List<ApplicationWorkspaceRow>
        {
            new(AppProfileResolver.DefaultMarker, defaultByKind),
        };
        foreach (var (path, byKind) in byApp)
        {
            if (path == AppProfileResolver.DefaultMarker)
            {
                continue;
            }

            rows.Add(new ApplicationWorkspaceRow(path, byKind));
        }

        return rows;
    }
}
