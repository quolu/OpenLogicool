using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// foreground app → device 種別ごとの適用 profile の解決規則（pure・APP-004）。
///
/// - 照合順序は固定: ①PackageFamilyName が非 null かつ "package" matcher に完全一致（小文字正規化）
///   →②NormalizedFullPath が非 null かつ "path" matcher に完全一致（小文字正規化）→③既定。
/// - 既定 profile: ApplicationFullPath "*" の関連付けがあればその profile、
///   なければその種別の profile がちょうど1件の場合だけその1件（従来動作の互換）。
///   複数 profile があるのに "*" 既定が無い種別は選択規則が定まらないため明示エラー。
/// - foreground が識別不能（識別要素がすべて null）または関連付けなしの app は既定 profile を適用する。
/// </summary>
public sealed class AppProfileResolver
{
    /// <summary>既定 profile を表す ApplicationFullPath の予約値。</summary>
    public const string DefaultMarker = "*";

    private readonly Dictionary<string, MappingProfileDocument> _defaultByKind;
    private readonly Dictionary<(string Path, string Kind), MappingProfileDocument> _byApp;
    private readonly Dictionary<(string PackageFamilyName, string Kind), MappingProfileDocument> _byPackage;

    private AppProfileResolver(
        Dictionary<string, MappingProfileDocument> defaultByKind,
        Dictionary<(string, string), MappingProfileDocument> byApp,
        Dictionary<(string, string), MappingProfileDocument> byPackage)
    {
        _defaultByKind = defaultByKind;
        _byApp = byApp;
        _byPackage = byPackage;
    }

    /// <summary>種別ごとの既定 profile（fast path 配線対象の決定に使う）。</summary>
    public IReadOnlyDictionary<string, MappingProfileDocument> DefaultByKind => _defaultByKind;

    /// <summary>app 関連付けを1件以上持つか（foreground 監視の要否）。</summary>
    public bool HasAppAssociations => _byApp.Count > 0 || _byPackage.Count > 0;

    public static AppProfileResolver Build(
        IReadOnlyList<MappingProfileDocument> documents,
        IReadOnlyList<AppProfileAssociation> associations)
    {
        var documentsById = new Dictionary<string, MappingProfileDocument>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (!documentsById.TryAdd(document.ProfileId, document))
            {
                throw new InvalidOperationException($"profile ID '{document.ProfileId}' が重複しています。");
            }
        }

        var defaultByKind = new Dictionary<string, MappingProfileDocument>(StringComparer.Ordinal);
        var byApp = new Dictionary<(string, string), MappingProfileDocument>();
        var byPackage = new Dictionary<(string, string), MappingProfileDocument>();
        foreach (var association in associations)
        {
            if (!documentsById.TryGetValue(association.ProfileId, out var document))
            {
                throw new InvalidOperationException(
                    $"関連付け ('{association.ApplicationFullPath}', '{association.DeviceKind}') が未保存の profile '{association.ProfileId}' を参照しています。");
            }

            if (document.DeviceKind != association.DeviceKind)
            {
                throw new InvalidOperationException(
                    $"関連付け ('{association.ApplicationFullPath}', '{association.DeviceKind}') の profile '{association.ProfileId}' は device 種別 '{document.DeviceKind}' の profile です。");
            }

            if (association.ApplicationFullPath == DefaultMarker)
            {
                defaultByKind[association.DeviceKind] = document;
            }
            else if (association.MatcherKind == AppMatcherKind.Package)
            {
                byPackage[(NormalizePackage(association.ApplicationFullPath), association.DeviceKind)] = document;
            }
            else
            {
                byApp[(NormalizePath(association.ApplicationFullPath), association.DeviceKind)] = document;
            }
        }

        foreach (var kindGroup in documents.GroupBy(document => document.DeviceKind, StringComparer.Ordinal))
        {
            if (defaultByKind.ContainsKey(kindGroup.Key))
            {
                continue;
            }

            var kindDocuments = kindGroup.ToArray();
            if (kindDocuments.Length == 1)
            {
                defaultByKind[kindGroup.Key] = kindDocuments[0];
            }
            else
            {
                throw new InvalidOperationException(
                    $"device 種別 '{kindGroup.Key}' に複数の profile（{string.Join(", ", kindDocuments.Select(d => $"'{d.ProfileId}'"))}）がありますが、" +
                    $"既定（ApplicationFullPath \"{DefaultMarker}\"）の関連付けがありません。associate で既定を指定してください。");
            }
        }

        return new AppProfileResolver(defaultByKind, byApp, byPackage);
    }

    /// <summary>
    /// foreground app に適用すべき profile（種別に profile が無ければ null）。
    /// 照合順序: ①package family name 一致 →②full path 一致 →③既定。
    /// </summary>
    public MappingProfileDocument? Resolve(string deviceKind, ForegroundApplicationIdentity? identity) =>
        ResolveWithReason(deviceKind, identity).Document;

    /// <summary>
    /// Resolve と同じ解決規則に、診断用の一致種別（APP-005）を添えて返す。
    /// 一致種別: "package"（package family name 一致）／"path"（full path 一致）／
    /// "default"（識別はできたがどの関連付けにも一致せず既定へ）／
    /// "identity-unavailable"（foreground window/process の識別要素が一つも取得できず既定へ）。
    /// 判定規則はここが正であり、Resolve はこの結果から Document だけを取り出す（二重実装しない）。
    /// </summary>
    public (MappingProfileDocument? Document, string MatchKind) ResolveWithReason(
        string deviceKind, ForegroundApplicationIdentity? identity)
    {
        if (identity is null || (identity.NormalizedFullPath is null && identity.PackageFamilyName is null))
        {
            return (_defaultByKind.TryGetValue(deviceKind, out var unavailableFallback) ? unavailableFallback : null,
                "identity-unavailable");
        }

        if (identity.PackageFamilyName is { } packageFamilyName &&
            _byPackage.TryGetValue((NormalizePackage(packageFamilyName), deviceKind), out var packageMatch))
        {
            return (packageMatch, "package");
        }

        if (identity.NormalizedFullPath is { } normalizedFullPath &&
            _byApp.TryGetValue((NormalizePath(normalizedFullPath), deviceKind), out var pathMatch))
        {
            return (pathMatch, "path");
        }

        return (_defaultByKind.TryGetValue(deviceKind, out var fallback) ? fallback : null, "default");
    }

    /// <summary>関連付け保存・照合に使う path 正規化（Windows path は大文字小文字を区別しない）。</summary>
    public static string NormalizePath(string path) => path.ToLowerInvariant();

    /// <summary>関連付け保存・照合に使う package family name 正規化。</summary>
    public static string NormalizePackage(string packageFamilyName) => packageFamilyName.ToLowerInvariant();
}
