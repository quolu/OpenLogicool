using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Desktop;

/// <summary>
/// Action-centric binding editor（APP-003）の編集操作（pure・設計 docs/ui-design-phase3.md §3.2）。
/// <see cref="WorkspaceDocument"/> は record（不変）——各操作は with 式で新しい instance を返す。
/// 検証（衝突・未知 action・token 文法）はここでは行わず、compile intent（Host が
/// <c>WorkspaceCompiler.Compile</c> を呼ぶ）に委ねる。この class は document の形を作るだけ。
/// </summary>
public static class WorkspaceDocumentEditor
{
    /// <summary>
    /// 保存済み revision が無い workspace の既定下書き（設計「既定 layer 構成は device catalog から」）。
    /// G13=base+M2+M3 latch、G600=base+G-Shift hold——既存 smoke workspace（<c>FastPathSmoke</c>）と
    /// 同じ構成を既定にする。
    /// </summary>
    public static WorkspaceDocument CreateDraft(string workspaceId) =>
        new(
            ContractSchemaVersions.Revision01,
            workspaceId,
            ProfileRevision: $"rev-{workspaceId}",
            MappingRevision: "map-1",
            Actions: [],
            Devices: [DefaultG13Layout(), DefaultG600Layout()],
            Bindings: []);

    private static WorkspaceDeviceLayout DefaultG13Layout() =>
        new(
            "G13",
            DefaultLayerId: "base",
            LayerIds: ["base", "m2", "m3"],
            LatchSelectors: [new LayerSelectorEntry("M1", "base"), new LayerSelectorEntry("M2", "m2"), new LayerSelectorEntry("M3", "m3")],
            HoldSelectors: []);

    private static WorkspaceDeviceLayout DefaultG600Layout() =>
        new(
            "G600",
            DefaultLayerId: "base",
            LayerIds: ["base", "shift"],
            LatchSelectors: [],
            HoldSelectors: [new LayerSelectorEntry("G6", "shift")]);

    /// <summary>action を追加する（ActionId 重複は拒否）。</summary>
    public static WorkspaceDocument AddAction(WorkspaceDocument document, string actionId, string name, IReadOnlyList<string> outputs)
    {
        if (document.Actions.Any(action => action.ActionId == actionId))
        {
            throw new ArgumentException($"action '{actionId}' は既に存在します。", nameof(actionId));
        }

        return document with { Actions = [.. document.Actions, new WorkspaceActionEntry(actionId, name, outputs)] };
    }

    /// <summary>action を削除する（参照している binding も併せて削除し、宙に浮いた binding を残さない）。</summary>
    public static WorkspaceDocument DeleteAction(WorkspaceDocument document, string actionId) =>
        document with
        {
            Actions = document.Actions.Where(action => action.ActionId != actionId).ToArray(),
            Bindings = document.Bindings.Where(binding => binding.ActionId != actionId).ToArray(),
        };

    /// <summary>action の表示名を変更する（F2）。</summary>
    public static WorkspaceDocument RenameAction(WorkspaceDocument document, string actionId, string newName) =>
        document with
        {
            Actions = document.Actions
                .Select(action => action.ActionId == actionId ? action with { Name = newName } : action)
                .ToArray(),
        };

    /// <summary>action の出力 token 列を差し替える（文法検証は行わない——compile intent の役割）。</summary>
    public static WorkspaceDocument SetActionOutputs(WorkspaceDocument document, string actionId, IReadOnlyList<string> outputs) =>
        document with
        {
            Actions = document.Actions
                .Select(action => action.ActionId == actionId ? action with { Outputs = outputs } : action)
                .ToArray(),
        };

    /// <summary>
    /// action を device の (control, layer) へ割り当てる。同じ action が既に同じ (device, layer) に
    /// 持つ binding があれば置き換える（1 action が同じ layer に複数 control を持つ状態は作らない）。
    /// 他 action との衝突（同じ (device, control, layer) への重複）はここでは検出しない
    /// ——衝突検出と拒否は compile intent（WorkspaceCompiler）の役割。
    /// </summary>
    public static WorkspaceDocument SetBinding(
        WorkspaceDocument document, string actionId, string deviceKind, string controlId, string layerId)
    {
        if (!document.Actions.Any(action => action.ActionId == actionId))
        {
            throw new ArgumentException($"action '{actionId}' が見つかりません。", nameof(actionId));
        }

        var remaining = document.Bindings
            .Where(binding => !(binding.ActionId == actionId && binding.DeviceKind == deviceKind && binding.LayerId == layerId))
            .ToList();
        remaining.Add(new WorkspaceActionBinding(actionId, deviceKind, controlId, layerId));
        return document with { Bindings = remaining };
    }

    /// <summary>action の指定 (device, layer) への binding を外す。</summary>
    public static WorkspaceDocument RemoveBinding(WorkspaceDocument document, string actionId, string deviceKind, string layerId) =>
        document with
        {
            Bindings = document.Bindings
                .Where(binding => !(binding.ActionId == actionId && binding.DeviceKind == deviceKind && binding.LayerId == layerId))
                .ToArray(),
        };
}
