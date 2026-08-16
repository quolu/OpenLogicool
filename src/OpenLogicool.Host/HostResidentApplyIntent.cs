using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Desktop;
using OpenLogicool.Input;
using OpenLogicool.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// <see cref="IResidentApplyIntent"/> の実装（<c>ui --resident</c> 同居時だけ Desktop へ渡す・t09 第4段残作業④）。
/// 保存直後に compile し、常駐中の対象 device instance へ <see cref="FastPathPump.RequestProfileChange"/> で
/// 即時反映する（新規 down から有効・device write はしない＝MAP-010）。
/// </summary>
public sealed class HostResidentApplyIntent(
    FastPathPump pump,
    IReadOnlyDictionary<string, IReadOnlyList<string>> deviceInstanceIdsByKind) : IResidentApplyIntent
{
    public void ApplyIfResident(WorkspaceDocument document)
    {
        var compilation = WorkspaceCompiler.Compile(document);
        foreach (var profileDocument in compilation.Profiles)
        {
            if (!deviceInstanceIdsByKind.TryGetValue(profileDocument.DeviceKind, out var instanceIds))
            {
                continue;
            }

            var profile = MappingProfileMaterializer.ToProfile(profileDocument);
            foreach (var deviceInstanceId in instanceIds)
            {
                pump.RequestProfileChange(deviceInstanceId, profile);
            }
        }
    }

    public IReadOnlyList<string> DrainTraceLines()
    {
        var kindByInstanceId = deviceInstanceIdsByKind
            .SelectMany(pair => pair.Value.Select(instanceId => (InstanceId: instanceId, Kind: pair.Key)))
            .ToDictionary(pair => pair.InstanceId, pair => pair.Kind, StringComparer.Ordinal);

        var lines = new List<string>();
        foreach (var entry in pump.DrainTrace())
        {
            if (entry.Edge != PhysicalInputEdge.Down || !entry.Emitted)
            {
                continue;
            }

            var kindLabel = kindByInstanceId.TryGetValue(entry.DeviceInstanceId, out var kind) ? kind : entry.DeviceInstanceId;
            var outputsLabel = entry.OutputTokens.Count == 0 ? "（割当なし）" : string.Join(" ", entry.OutputTokens);
            lines.Add($"{kindLabel} の {entry.ControlId} を押した → {outputsLabel} を送りました");
        }

        return lines;
    }
}
