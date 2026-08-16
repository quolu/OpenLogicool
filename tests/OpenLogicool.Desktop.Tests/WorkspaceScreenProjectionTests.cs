using System.Linq;
using OpenLogicool.Profiles;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class WorkspaceScreenProjectionTests
{
    // Host が Ui() で行う変換（WorkspaceStageStatus -> WorkspaceStageCell）と同じものをここで再現し、
    // 段階4セルの語彙が WorkspaceApplyReport（OpenLogicool.Profiles）と同一であることを直接突き合わせる。
    private static readonly WorkspaceStageCell[] StageCells = WorkspaceApplyReport
        .Build(savedRevisionNumber: null, hostResident: false)
        .Select(stage => new WorkspaceStageCell(stage.Stage, stage.State, stage.Detail))
        .ToArray();

    private static readonly ApplicationRailEntryInput[] RailEntries =
    [
        new("*", "共通設定（どのアプリでもない時）", IsRunning: false, IsAssociated: true),
        new(@"c:\game\nikke.exe", "NIKKE", IsRunning: true, IsAssociated: true),
        new(@"c:\windows\explorer.exe", "explorer.exe", IsRunning: true, IsAssociated: false),
    ];

    private static WorkspaceScreenSnapshot Snapshot(
        string foregroundStateLabel = "一致 app（path）",
        string? foregroundWindowTitle = "NIKKE",
        long? revisionNumber = null,
        int g13Count = 1,
        int g600Count = 1) =>
        new(foregroundStateLabel, foregroundWindowTitle, revisionNumber, StageCells, g13Count, g600Count, RailEntries);

    [Fact]
    public void Chrome_header_carries_all_five_fields_from_the_snapshot_and_selection()
    {
        var view = WorkspaceScreenProjection.Project(Snapshot(revisionNumber: 5), @"c:\game\nikke.exe");

        Assert.Equal("NIKKE", view.Chrome.EditingLabel);
        Assert.Equal("NIKKE 用", view.Chrome.LiveAssignmentLabel);
        Assert.Equal("一致 app（path）", view.Chrome.CurrentEffectiveLabel);
        Assert.Equal("NIKKE（一致 app（path））", view.Chrome.TargetWindowLabel);
        Assert.Equal("revision 5", view.Chrome.AppliedRevisionLabel);
        Assert.Equal("手動入力", view.Chrome.ExecutionModeLabel);
    }

    [Fact]
    public void Live_assignment_label_states_unavailable_without_a_foreground_window_title()
    {
        var view = WorkspaceScreenProjection.Project(Snapshot(foregroundWindowTitle: null), "*");

        Assert.Equal("取得不能", view.Chrome.LiveAssignmentLabel);
    }

    [Fact]
    public void Unsaved_selection_shows_draft_not_a_fake_revision_number()
    {
        var view = WorkspaceScreenProjection.Project(Snapshot(revisionNumber: null), "*");

        Assert.Equal("下書き", view.Chrome.AppliedRevisionLabel);
    }

    [Fact]
    public void Stage_cells_carry_the_same_vocabulary_as_the_source_stages_without_alteration()
    {
        var view = WorkspaceScreenProjection.Project(Snapshot(), "*");

        Assert.Equal(StageCells, view.Chrome.StageCells);
    }

    [Fact]
    public void Editing_selection_does_not_move_when_only_the_foreground_observation_changes()
    {
        const string editingApplication = @"c:\game\nikke.exe";

        var beforeAltTab = WorkspaceScreenProjection.Project(
            Snapshot(foregroundStateLabel: "一致 app（path）", foregroundWindowTitle: "NIKKE"),
            editingApplication);
        var afterAltTab = WorkspaceScreenProjection.Project(
            Snapshot(foregroundStateLabel: "既定 app（identity 識別済み・関連付けなし）", foregroundWindowTitle: "OpenLogicool Input Studio"),
            editingApplication);

        Assert.Equal("NIKKE", beforeAltTab.Chrome.EditingLabel);
        Assert.Equal("NIKKE", afterAltTab.Chrome.EditingLabel);
        Assert.NotEqual(beforeAltTab.Chrome.CurrentEffectiveLabel, afterAltTab.Chrome.CurrentEffectiveLabel);
    }

    [Fact]
    public void Rail_rows_reflect_running_association_and_selection_state()
    {
        var view = WorkspaceScreenProjection.Project(Snapshot(), @"c:\game\nikke.exe");

        var nikke = Assert.Single(view.RailRows, row => row.ApplicationFullPath == @"c:\game\nikke.exe");
        Assert.True(nikke.IsRunning);
        Assert.True(nikke.IsAssociated);
        Assert.True(nikke.IsSelected);

        var explorer = Assert.Single(view.RailRows, row => row.ApplicationFullPath == @"c:\windows\explorer.exe");
        Assert.True(explorer.IsRunning);
        Assert.False(explorer.IsAssociated);
        Assert.False(explorer.IsSelected);

        var defaultRow = Assert.Single(view.RailRows, row => row.ApplicationFullPath == "*");
        Assert.False(defaultRow.IsRunning);
        Assert.False(defaultRow.IsSelected);
    }

    [Fact]
    public void One_sided_disconnection_is_stated_as_editable_not_hidden()
    {
        var view = WorkspaceScreenProjection.Project(Snapshot(g13Count: 0, g600Count: 1), "*");

        var g13 = Assert.Single(view.DeviceConnections, connection => connection.DeviceKind == "G13");
        Assert.Equal("未接続（編集は可）", g13.ConnectionLabel);

        var g600 = Assert.Single(view.DeviceConnections, connection => connection.DeviceKind == "G600");
        Assert.Equal("接続中（1 台）", g600.ConnectionLabel);
    }
}
