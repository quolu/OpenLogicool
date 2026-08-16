using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// foreground app（EXE full path）→ device 種別ごとの適用 profile の解決規則（pure）。
///
/// - 関連付けは正規化済み小文字 full path の完全一致だけで解決する。
/// - 既定 profile: ApplicationFullPath "*" の関連付けがあればその profile、
///   なければその種別の profile がちょうど1件の場合だけその1件（従来動作の互換）。
///   複数 profile があるのに "*" 既定が無い種別は選択規則が定まらないため明示エラー。
/// - foreground が識別不能（null）または関連付けなしの app は既定 profile を適用する。
/// </summary>
public sealed class AppProfileResolver
{
    /// <summary>既定 profile を表す ApplicationFullPath の予約値。</summary>
    public const string DefaultMarker = "*";

    private readonly Dictionary<string, MappingProfileDocument> _defaultByKind;
    private readonly Dictionary<(string Path, string Kind), MappingProfileDocument> _byApp;

    private AppProfileResolver(
        Dictionary<string, MappingProfileDocument> defaultByKind,
        Dictionary<(string, string), MappingProfileDocument> byApp)
    {
        _defaultByKind = defaultByKind;
        _byApp = byApp;
    }

    /// <summary>種別ごとの既定 profile（fast path 配線対象の決定に使う）。</summary>
    public IReadOnlyDictionary<string, MappingProfileDocument> DefaultByKind => _defaultByKind;

    /// <summary>app 関連付けを1件以上持つか（foreground 監視の要否）。</summary>
    public bool HasAppAssociations => _byApp.Count > 0;

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

        return new AppProfileResolver(defaultByKind, byApp);
    }

    /// <summary>foreground app に適用すべき profile（種別に profile が無ければ null）。</summary>
    public MappingProfileDocument? Resolve(string deviceKind, string? foregroundFullPath)
    {
        if (foregroundFullPath is not null &&
            _byApp.TryGetValue((NormalizePath(foregroundFullPath), deviceKind), out var associated))
        {
            return associated;
        }

        return _defaultByKind.TryGetValue(deviceKind, out var fallback) ? fallback : null;
    }

    /// <summary>関連付け保存・照合に使う path 正規化（Windows path は大文字小文字を区別しない）。</summary>
    public static string NormalizePath(string path) => path.ToLowerInvariant();
}
