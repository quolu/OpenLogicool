using System.Linq;
using OpenLogicool.Contracts.Profiles;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

/// <summary>
/// Action-centric binding editor（APP-003）の pure 編集操作。<see cref="WorkspaceDocument"/> は record
/// （不変）なので、各操作が新しい document へ正しく反映されることだけを確認する（I/O なし）。
/// </summary>
public sealed class WorkspaceDocumentEditorTests
{
    [Fact]
    public void CreateDraft_has_the_default_g13_and_g600_layout_with_no_actions_or_bindings()
    {
        var draft = WorkspaceDocumentEditor.CreateDraft("ws-smoke");

        Assert.Equal("ws-smoke", draft.WorkspaceId);
        Assert.Empty(draft.Actions);
        Assert.Empty(draft.Bindings);

        var g13 = Assert.Single(draft.Devices, device => device.DeviceKind == "G13");
        Assert.Equal("base", g13.DefaultLayerId);
        Assert.Equal(["base", "m2", "m3"], g13.LayerIds);
        Assert.Equal(
            new[] { ("M1", "base"), ("M2", "m2"), ("M3", "m3") },
            g13.LatchSelectors.Select(selector => (selector.ControlId, selector.LayerId)));
        Assert.Empty(g13.HoldSelectors);

        var g600 = Assert.Single(draft.Devices, device => device.DeviceKind == "G600");
        Assert.Equal("base", g600.DefaultLayerId);
        Assert.Equal(["base", "shift"], g600.LayerIds);
        Assert.Empty(g600.LatchSelectors);
        Assert.Equal(("G6", "shift"), (Assert.Single(g600.HoldSelectors).ControlId, Assert.Single(g600.HoldSelectors).LayerId));
    }

    [Fact]
    public void AddAction_appends_the_action_and_rejects_duplicate_ids()
    {
        var draft = WorkspaceDocumentEditor.CreateDraft("ws");
        var withAction = WorkspaceDocumentEditor.AddAction(draft, "dodge", "回避", ["Key:Space"]);

        var added = Assert.Single(withAction.Actions);
        Assert.Equal("dodge", added.ActionId);
        Assert.Equal("回避", added.Name);
        Assert.Equal(["Key:Space"], added.Outputs);
        Assert.Empty(draft.Actions); // 元の document は不変のまま

        Assert.Throws<ArgumentException>(() => WorkspaceDocumentEditor.AddAction(withAction, "dodge", "回避2", ["Key:X"]));
    }

    [Fact]
    public void SetBinding_replaces_the_same_actions_binding_at_the_same_device_and_layer()
    {
        var draft = WorkspaceDocumentEditor.AddAction(WorkspaceDocumentEditor.CreateDraft("ws"), "dodge", "回避", ["Key:Space"]);

        var bound = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G13", "G1", "base");
        var rebound = WorkspaceDocumentEditor.SetBinding(bound, "dodge", "G13", "G2", "base");

        var binding = Assert.Single(rebound.Bindings);
        Assert.Equal(("dodge", "G13", "G2", "base"), (binding.ActionId, binding.DeviceKind, binding.ControlId, binding.LayerId));
    }

    [Fact]
    public void SetBinding_on_an_unknown_action_is_rejected()
    {
        var draft = WorkspaceDocumentEditor.CreateDraft("ws");

        Assert.Throws<ArgumentException>(() => WorkspaceDocumentEditor.SetBinding(draft, "missing", "G13", "G1", "base"));
    }

    [Fact]
    public void DeleteAction_removes_the_action_and_its_bindings()
    {
        var draft = WorkspaceDocumentEditor.AddAction(WorkspaceDocumentEditor.CreateDraft("ws"), "dodge", "回避", ["Key:Space"]);
        draft = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G13", "G1", "base");
        draft = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G600", "G9", "base");

        var deleted = WorkspaceDocumentEditor.DeleteAction(draft, "dodge");

        Assert.Empty(deleted.Actions);
        Assert.Empty(deleted.Bindings); // 宙に浮いた binding を残さない
    }

    [Fact]
    public void RemoveBinding_only_removes_the_targeted_device_and_layer()
    {
        var draft = WorkspaceDocumentEditor.AddAction(WorkspaceDocumentEditor.CreateDraft("ws"), "dodge", "回避", ["Key:Space"]);
        draft = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G13", "G1", "base");
        draft = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G600", "G9", "base");

        var afterRemoval = WorkspaceDocumentEditor.RemoveBinding(draft, "dodge", "G13", "base");

        var remaining = Assert.Single(afterRemoval.Bindings);
        Assert.Equal("G600", remaining.DeviceKind);
    }

    [Fact]
    public void RenameAction_and_SetActionOutputs_update_only_the_targeted_action()
    {
        var draft = WorkspaceDocumentEditor.CreateDraft("ws");
        draft = WorkspaceDocumentEditor.AddAction(draft, "dodge", "回避", ["Key:Space"]);
        draft = WorkspaceDocumentEditor.AddAction(draft, "attack", "攻撃", ["Key:F"]);

        var renamed = WorkspaceDocumentEditor.RenameAction(draft, "dodge", "回避（改）");
        Assert.Equal("回避（改）", renamed.Actions.Single(action => action.ActionId == "dodge").Name);
        Assert.Equal("攻撃", renamed.Actions.Single(action => action.ActionId == "attack").Name);

        var retokened = WorkspaceDocumentEditor.SetActionOutputs(draft, "dodge", ["Key:LCtrl", "Key:C"]);
        Assert.Equal(["Key:LCtrl", "Key:C"], retokened.Actions.Single(action => action.ActionId == "dodge").Outputs);
    }

    [Fact]
    public void SetG600ShiftAsButton_removes_shift_layer_and_hold_selector()
    {
        var draft = WorkspaceDocumentEditor.CreateDraft("ws");

        var converted = WorkspaceDocumentEditor.SetG600ShiftAsButton(draft);

        var layout = converted.Devices.Single(device => device.DeviceKind == "G600");
        Assert.Equal(["base"], layout.LayerIds);
        Assert.Empty(layout.HoldSelectors);
    }

    [Fact]
    public void SetG600ShiftAsButton_refuses_while_shift_layer_bindings_remain()
    {
        var draft = WorkspaceDocumentEditor.AddAction(WorkspaceDocumentEditor.CreateDraft("ws"), "dodge", "回避", ["Key:Space"]);
        draft = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G600", "G9", "shift");

        var error = Assert.Throws<ArgumentException>(() => WorkspaceDocumentEditor.SetG600ShiftAsButton(draft));
        Assert.Contains("残っています", error.Message);
    }

    [Fact]
    public void SetG600ShiftAsSelector_restores_shift_layer_and_makes_g6_a_selector_again()
    {
        var draft = WorkspaceDocumentEditor.SetG600ShiftAsButton(WorkspaceDocumentEditor.CreateDraft("ws"));

        var restored = WorkspaceDocumentEditor.SetG600ShiftAsSelector(draft);

        var layout = restored.Devices.Single(device => device.DeviceKind == "G600");
        Assert.Equal(["base", "shift"], layout.LayerIds);
        var selector = Assert.Single(layout.HoldSelectors);
        Assert.Equal("G6", selector.ControlId);
        Assert.Equal("shift", selector.LayerId);
    }

    [Fact]
    public void SetG600ShiftAsSelector_refuses_while_g6_bindings_remain()
    {
        var draft = WorkspaceDocumentEditor.SetG600ShiftAsButton(WorkspaceDocumentEditor.CreateDraft("ws"));
        draft = WorkspaceDocumentEditor.AddAction(draft, "dodge", "回避", ["Key:Space"]);
        draft = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G600", "G6", "base");

        var error = Assert.Throws<ArgumentException>(() => WorkspaceDocumentEditor.SetG600ShiftAsSelector(draft));
        Assert.Contains("G6", error.Message);
    }

    [Fact]
    public void Set_and_clear_g13_lcd_only_replace_the_lcd_setting()
    {
        var draft = WorkspaceDocumentEditor.CreateDraft("ws");
        var setting = new WorkspaceG13LcdSetting(
            WorkspaceG13LcdContentKind.Text,
            Convert.ToBase64String(new byte[960]),
            null,
            "NIKKE");

        var configured = WorkspaceDocumentEditor.SetG13Lcd(draft, setting);
        var cleared = WorkspaceDocumentEditor.ClearG13Lcd(configured);

        Assert.Equal(setting, configured.G13Lcd);
        Assert.Null(cleared.G13Lcd);
        Assert.Equal(draft.Actions, cleared.Actions);
        Assert.Equal(draft.Bindings, cleared.Bindings);
    }
}
