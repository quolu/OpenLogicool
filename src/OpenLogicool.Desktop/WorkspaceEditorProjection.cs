using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.G600;
using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Desktop;

/// <summary>Action 盤の1行（設計 §2 案A: 行=Semantic Action、列=出力／device ごとの割当）。</summary>
public sealed record ActionBoardRowView(
    string ActionId,
    string Name,
    string OutputsLabel,
    IReadOnlyList<DeviceAssignmentCellView> DeviceAssignments,
    bool IsSelected);

/// <summary>Action 盤の1行×1 device 列。層は列内表記で全層見せる（既定 layer 以外は "control:layer"）。</summary>
public sealed record DeviceAssignmentCellView(string DeviceKind, string AssignmentLabel);

/// <summary>Binding Inspector に出す1 binding（選択中 action の (device, control, layer)）。</summary>
public sealed record BindingRowView(string DeviceKind, string ControlId, string LayerId);

/// <summary>device 1種別の binding 候補（layer 一覧・割当可能 control。selector control は除く）。</summary>
public sealed record ControlOptionView(string ControlId, bool IsConfirmed);

public sealed record DeviceBindingOptionsView(
    string DeviceKind,
    string DefaultLayerId,
    IReadOnlyList<string> LayerIds,
    IReadOnlyList<ControlOptionView> Controls);

/// <summary>唯一の editor（設計 §2.2「右ペイン常時」）。選択中 action が無ければ null。</summary>
public sealed record BindingInspectorView(
    string ActionId,
    string Name,
    string OutputsTokenText,
    IReadOnlyList<BindingRowView> Bindings,
    IReadOnlyList<DeviceBindingOptionsView> DeviceOptions);

/// <summary>Action 盤＋Inspector の表示モデル一式（1回の Project 呼び出しで両方を作る——同じ document から作るため）。</summary>
public sealed record ActionBoardView(
    IReadOnlyList<string> DeviceKinds,
    IReadOnlyList<ActionBoardRowView> Rows,
    BindingInspectorView? Inspector);

/// <summary>
/// WorkspaceDocument → 表示モデルの pure 変換（設計 §3.2 Projection 層の続き）。
/// 選択中 action（selectedActionId）は Window が保持する編集状態であり、document の中身ではない。
/// </summary>
public static class WorkspaceEditorProjection
{
    public static ActionBoardView Project(WorkspaceDocument document, string? selectedActionId)
    {
        var deviceKinds = document.Devices.Select(device => device.DeviceKind).ToArray();
        var layoutByKind = document.Devices.ToDictionary(device => device.DeviceKind, StringComparer.Ordinal);
        var bindingsByAction = document.Bindings.ToLookup(binding => binding.ActionId, StringComparer.Ordinal);

        var rows = document.Actions
            .Select(action =>
            {
                var actionBindings = bindingsByAction[action.ActionId];
                var assignments = deviceKinds
                    .Select(deviceKind => new DeviceAssignmentCellView(
                        deviceKind,
                        FormatAssignment(
                            layoutByKind.TryGetValue(deviceKind, out var layout) ? layout : null,
                            actionBindings.Where(binding => binding.DeviceKind == deviceKind))))
                    .ToArray();

                return new ActionBoardRowView(
                    action.ActionId,
                    action.Name,
                    FormatOutputs(action.Outputs),
                    assignments,
                    IsSelected: action.ActionId == selectedActionId);
            })
            .ToArray();

        BindingInspectorView? inspector = null;
        var selectedAction = selectedActionId is null
            ? null
            : document.Actions.FirstOrDefault(action => action.ActionId == selectedActionId);
        if (selectedAction is not null)
        {
            var bindingRows = bindingsByAction[selectedAction.ActionId]
                .Select(binding => new BindingRowView(binding.DeviceKind, binding.ControlId, binding.LayerId))
                .OrderBy(row => row.DeviceKind, StringComparer.Ordinal)
                .ThenBy(row => row.LayerId, StringComparer.Ordinal)
                .ToArray();
            var deviceOptions = document.Devices.Select(BuildDeviceOptions).ToArray();

            inspector = new BindingInspectorView(
                selectedAction.ActionId,
                selectedAction.Name,
                FormatOutputs(selectedAction.Outputs),
                bindingRows,
                deviceOptions);
        }

        return new ActionBoardView(deviceKinds, rows, inspector);
    }

    /// <summary>canonical な出力 token 文字列（設計「ピッカーが token を書き、canonical 文字列を常に見せる」）。</summary>
    public static string FormatOutputs(IReadOnlyList<string> outputs) =>
        outputs.Count == 0 ? string.Empty : string.Join(" ", outputs);

    /// <summary>textbox の表示文字列を outputs 列へ戻す（空白区切り・空要素は落とす）。</summary>
    public static IReadOnlyList<string> ParseOutputs(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string FormatAssignment(WorkspaceDeviceLayout? layout, IEnumerable<WorkspaceActionBinding> bindings)
    {
        var parts = bindings
            .OrderBy(binding => binding.LayerId == layout?.DefaultLayerId ? 0 : 1)
            .ThenBy(binding => binding.LayerId, StringComparer.Ordinal)
            .ThenBy(binding => binding.ControlId, StringComparer.Ordinal)
            .Select(binding => layout is not null && binding.LayerId == layout.DefaultLayerId
                ? binding.ControlId
                : $"{binding.ControlId}:{binding.LayerId}")
            .ToArray();
        return parts.Length == 0 ? "—" : string.Join("/", parts);
    }

    private static DeviceBindingOptionsView BuildDeviceOptions(WorkspaceDeviceLayout layout)
    {
        var selectorControls = layout.LatchSelectors.Select(selector => selector.ControlId)
            .Concat(layout.HoldSelectors.Select(selector => selector.ControlId))
            .ToHashSet(StringComparer.Ordinal);

        var controls = ControlCatalogFor(layout.DeviceKind)
            .Where(controlId => !selectorControls.Contains(controlId))
            .Select(controlId => new ControlOptionView(controlId, ConfirmedButtonsFor(layout.DeviceKind)?.Contains(controlId) ?? false))
            .ToArray();

        return new DeviceBindingOptionsView(layout.DeviceKind, layout.DefaultLayerId, layout.LayerIds, controls);
    }

    private static IReadOnlyList<string> ControlCatalogFor(string deviceKind) => deviceKind switch
    {
        "G13" => G13Controls.Buttons,
        "G600" => G600Controls.Buttons,
        _ => [],
    };

    private static IReadOnlySet<string>? ConfirmedButtonsFor(string deviceKind) => deviceKind switch
    {
        "G13" => G13Controls.ConfirmedButtons,
        "G600" => G600Controls.ConfirmedButtons,
        _ => null,
    };
}
