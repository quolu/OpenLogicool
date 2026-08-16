using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// resident host が起動時に適用する profile の選択規則（pure）。
/// 現段階は device 種別（DeviceKind）ごとに 0 または 1 profile だけを許す。
/// 複数ある場合は選択規則が未定義なので明示エラーにする（foreground app 切替 APP-006 は将来の機能）。
/// </summary>
public static class HostProfileSelection
{
    public static IReadOnlyDictionary<string, MappingProfileDocument> SelectByDeviceKind(
        IReadOnlyList<MappingProfileDocument> documents)
    {
        var byKind = new Dictionary<string, MappingProfileDocument>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (!byKind.TryAdd(document.DeviceKind, document))
            {
                throw new InvalidOperationException(
                    $"device 種別 '{document.DeviceKind}' に複数の profile（'{byKind[document.DeviceKind].ProfileId}' と '{document.ProfileId}'）が保存されています。" +
                    "現段階の resident host は種別ごとに1 profile だけを適用できます。");
            }
        }

        return byKind;
    }
}
