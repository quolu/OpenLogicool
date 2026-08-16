using System.Linq;
using OpenLogicool.Contracts.Profiles;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

/// <summary>Action 盤／Binding Inspector の pure 表示変換（設計 §2 案A）。</summary>
public sealed class WorkspaceEditorProjectionTests
{
    private static WorkspaceDocument SampleDocument()
    {
        var draft = WorkspaceDocumentEditor.CreateDraft("ws");
        draft = WorkspaceDocumentEditor.AddAction(draft, "dodge", "回避", ["Key:LShift"]);
        draft = WorkspaceDocumentEditor.AddAction(draft, "unbound", "未割当", ["Key:X"]);
        draft = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G13", "G7", "base");
        draft = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G13", "G8", "m2");
        draft = WorkspaceDocumentEditor.SetBinding(draft, "dodge", "G600", "G12", "base");
        return draft;
    }

    [Fact]
    public void Board_row_formats_default_layer_assignment_bare_and_other_layers_with_a_suffix()
    {
        var view = WorkspaceEditorProjection.Project(SampleDocument(), selectedActionId: null);

        var dodge = Assert.Single(view.Rows, row => row.ActionId == "dodge");
        var g13Cell = Assert.Single(dodge.DeviceAssignments, cell => cell.DeviceKind == "G13");
        Assert.Equal("G7/G8:m2", g13Cell.AssignmentLabel);

        var g600Cell = Assert.Single(dodge.DeviceAssignments, cell => cell.DeviceKind == "G600");
        Assert.Equal("G12", g600Cell.AssignmentLabel);
    }

    [Fact]
    public void Board_row_shows_an_em_dash_for_a_device_with_no_binding()
    {
        var view = WorkspaceEditorProjection.Project(SampleDocument(), selectedActionId: null);

        var unbound = Assert.Single(view.Rows, row => row.ActionId == "unbound");
        Assert.All(unbound.DeviceAssignments, cell => Assert.Equal("—", cell.AssignmentLabel));
    }

    [Fact]
    public void No_selection_means_no_inspector()
    {
        var view = WorkspaceEditorProjection.Project(SampleDocument(), selectedActionId: null);

        Assert.Null(view.Inspector);
        Assert.All(view.Rows, row => Assert.False(row.IsSelected));
    }

    [Fact]
    public void Selecting_an_action_opens_the_inspector_with_its_bindings_and_canonical_outputs()
    {
        var view = WorkspaceEditorProjection.Project(SampleDocument(), selectedActionId: "dodge");

        Assert.NotNull(view.Inspector);
        Assert.Equal("dodge", view.Inspector!.ActionId);
        Assert.Equal("Key:LShift", view.Inspector.OutputsTokenText);
        Assert.Equal(2, view.Inspector.Bindings.Count(binding => binding.DeviceKind == "G13"));
        Assert.Single(view.Inspector.Bindings, binding => binding.DeviceKind == "G600");

        var selectedRow = Assert.Single(view.Rows, row => row.ActionId == "dodge");
        Assert.True(selectedRow.IsSelected);
    }

    [Fact]
    public void Device_binding_options_exclude_layer_selector_controls()
    {
        var view = WorkspaceEditorProjection.Project(SampleDocument(), selectedActionId: "dodge");

        var g13Options = Assert.Single(view.Inspector!.DeviceOptions, options => options.DeviceKind == "G13");
        Assert.DoesNotContain(g13Options.Controls, control => control.ControlId is "M1" or "M2" or "M3");
        Assert.Contains(g13Options.Controls, control => control.ControlId == "G1");
        // G13 は G1/G2/G20/STICK_PRESS だけ確認済み（実測台帳）
        Assert.True(g13Options.Controls.Single(control => control.ControlId == "G1").IsConfirmed);
        Assert.False(g13Options.Controls.Single(control => control.ControlId == "G21").IsConfirmed);

        var g600Options = Assert.Single(view.Inspector.DeviceOptions, options => options.DeviceKind == "G600");
        Assert.DoesNotContain(g600Options.Controls, control => control.ControlId == "G6");
    }

    [Fact]
    public void ParseOutputs_splits_on_whitespace_and_drops_empty_entries()
    {
        Assert.Equal(["Key:LCtrl", "Key:C"], WorkspaceEditorProjection.ParseOutputs("  Key:LCtrl   Key:C "));
        Assert.Empty(WorkspaceEditorProjection.ParseOutputs("   "));
    }
}
