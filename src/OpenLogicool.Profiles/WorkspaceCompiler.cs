using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Profiles;

/// <summary>workspace compile の結果: device 種別ごとの profile document と、適用前に表示する警告（MAP-004）。</summary>
public sealed record WorkspaceCompilation(
    IReadOnlyList<MappingProfileDocument> Profiles,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Action-centric binding editor の機能中核（pure）。
/// WorkspaceDocument を device 種別ごとの MappingProfileDocument へ compile する
/// （MAP-001: 一つの action を複数 device control へ／MAP-002: 同じ action へ両 device から到達）。
///
/// 検証の分担:
/// - 構造の誤り（未知 action・未知 device・重複定義・selector 衝突・binding 重複・layer 不整合）は例外＝適用不可。
///   binding／layer の整合は Domain の MappingProfile 構築子を通して検証し、規則を二重化しない。
/// - 適用はできるが意図と違う可能性（未割当 action・到達不能 layer）は Warnings＝適用前に表示（MAP-004）。
/// </summary>
public static class WorkspaceCompiler
{
    public static WorkspaceCompilation Compile(WorkspaceDocument document)
    {
        if (document.SchemaVersion != ContractSchemaVersions.Revision01)
        {
            throw new ArgumentException(
                $"WorkspaceDocument schema version '{document.SchemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。",
                nameof(document));
        }

        if (document.WorkspaceId.Length == 0)
        {
            throw new ArgumentException("WorkspaceId が空です。", nameof(document));
        }

        var actionsById = new Dictionary<string, WorkspaceActionEntry>(StringComparer.Ordinal);
        foreach (var action in document.Actions)
        {
            if (!actionsById.TryAdd(action.ActionId, action))
            {
                throw new ArgumentException($"action '{action.ActionId}' が重複しています。", nameof(document));
            }
        }

        var devicesByKind = new Dictionary<string, WorkspaceDeviceLayout>(StringComparer.Ordinal);
        foreach (var device in document.Devices)
        {
            if (!devicesByKind.TryAdd(device.DeviceKind, device))
            {
                throw new ArgumentException($"device 種別 '{device.DeviceKind}' の layout が重複しています。", nameof(document));
            }
        }

        var boundActionIds = new HashSet<string>(StringComparer.Ordinal);
        var bindingsByKind = document.Devices.ToDictionary(
            device => device.DeviceKind,
            _ => new List<MappingBindingEntry>(),
            StringComparer.Ordinal);
        foreach (var binding in document.Bindings)
        {
            if (!actionsById.TryGetValue(binding.ActionId, out var action))
            {
                throw new ArgumentException(
                    $"binding ({binding.DeviceKind}, {binding.ControlId}, {binding.LayerId}) が未定義の action '{binding.ActionId}' を参照しています。",
                    nameof(document));
            }

            if (!bindingsByKind.TryGetValue(binding.DeviceKind, out var kindBindings))
            {
                throw new ArgumentException(
                    $"binding (action '{binding.ActionId}') が layout 未定義の device 種別 '{binding.DeviceKind}' を参照しています。",
                    nameof(document));
            }

            kindBindings.Add(new MappingBindingEntry(binding.ControlId, binding.LayerId, action.Outputs));
            boundActionIds.Add(binding.ActionId);
        }

        var warnings = new List<string>();
        foreach (var action in document.Actions)
        {
            if (!boundActionIds.Contains(action.ActionId))
            {
                warnings.Add($"未割当: action '{action.ActionId}'（{action.Name}）はどの device にも割り当てられていません。");
            }
        }

        var profiles = new List<MappingProfileDocument>();
        foreach (var device in document.Devices)
        {
            var selectorTargets = device.LatchSelectors.Concat(device.HoldSelectors)
                .Select(selector => selector.LayerId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var layerId in device.LayerIds)
            {
                if (layerId != device.DefaultLayerId && !selectorTargets.Contains(layerId))
                {
                    warnings.Add($"到達不能 layer: {device.DeviceKind} の layer '{layerId}' はどの selector からも選択されません。");
                }
            }

            var profile = new MappingProfileDocument(
                ContractSchemaVersions.Revision01,
                ProfileId: $"{document.WorkspaceId}-{device.DeviceKind}",
                device.DeviceKind,
                document.ProfileRevision,
                document.MappingRevision,
                device.DefaultLayerId,
                device.LayerIds,
                device.LatchSelectors,
                device.HoldSelectors,
                bindingsByKind[device.DeviceKind]);

            // binding 重複・selector 衝突・layer 不整合・空 outputs は Domain 検証をそのまま通す
            _ = MappingProfileMaterializer.ToProfile(profile);
            profiles.Add(profile);
        }

        return new WorkspaceCompilation(profiles, warnings);
    }
}
