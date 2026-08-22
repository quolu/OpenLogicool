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
    ResidentInputHost host,
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
                host.Pump.RequestProfileChange(deviceInstanceId, profile);
            }
        }

        // 前面監視の resolver／profile も保存後の内容へ差し替える（次の app 切替が古い版へ
        // 巻き戻らないように。起動後に初めて関連付けが出来た場合はここで監視が始まる）。
        host.RefreshAppFirstData();
    }

    public string? CurrentForegroundWindowTitle() => ForegroundAppTracker.GetForegroundWindowTitle();

    public IReadOnlyList<ResidentTraceEvent> DrainTraceEvents()
    {
        var kindByInstanceId = deviceInstanceIdsByKind
            .SelectMany(pair => pair.Value.Select(instanceId => (InstanceId: instanceId, Kind: pair.Key)))
            .ToDictionary(pair => pair.InstanceId, pair => pair.Kind, StringComparer.Ordinal);

        var events = new List<ResidentTraceEvent>();
        foreach (var entry in host.Pump.DrainTrace())
        {
            var kindLabel = kindByInstanceId.TryGetValue(entry.DeviceInstanceId, out var kind) ? kind : entry.DeviceInstanceId;
            var isDown = entry.Edge == PhysicalInputEdge.Down;
            string? displayLine = null;
            if (isDown)
            {
                var outputsLabel = entry.OutputTokens.Count == 0 ? "（割当なし）" : string.Join(" ", entry.OutputTokens);
                displayLine = $"{kindLabel} の {entry.ControlId} を押した → {outputsLabel} を送りました";
            }

            events.Add(new ResidentTraceEvent(kindLabel, entry.ControlId, isDown, displayLine));
        }

        return events;
    }
}
