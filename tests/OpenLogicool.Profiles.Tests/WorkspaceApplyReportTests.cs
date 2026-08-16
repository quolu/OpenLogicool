using OpenLogicool.Profiles;
using Xunit;

namespace OpenLogicool.Profiles.Tests;

public sealed class WorkspaceApplyReportTests
{
    private static WorkspaceStageStatus Stage(IReadOnlyList<WorkspaceStageStatus> stages, string name) =>
        Assert.Single(stages, stage => stage.Stage.StartsWith(name, StringComparison.Ordinal));

    [Fact]
    public void Dry_run_reports_nothing_saved_and_nothing_applied()
    {
        var stages = WorkspaceApplyReport.Build(savedRevisionNumber: null, hostResident: false);

        Assert.Equal("成立", Stage(stages, "編集").State);
        Assert.Equal("未実施", Stage(stages, "保存").State);
        Assert.Equal("未実施", Stage(stages, "runtime").State);
    }

    [Fact]
    public void Saved_while_host_resident_is_not_reported_as_applied()
    {
        var stages = WorkspaceApplyReport.Build(savedRevisionNumber: 3, hostResident: true);

        var saved = Stage(stages, "保存");
        Assert.Equal("成立", saved.State);
        Assert.Contains("revision 3", saved.Detail);

        var runtime = Stage(stages, "runtime");
        Assert.Equal("未反映", runtime.State);
        Assert.Contains("再起動", runtime.Detail);
    }

    [Fact]
    public void Saved_without_resident_host_is_distinguished_from_resident_case()
    {
        var stages = WorkspaceApplyReport.Build(savedRevisionNumber: 1, hostResident: false);

        var runtime = Stage(stages, "runtime");
        Assert.Equal("未適用", runtime.State);
        Assert.Contains("次回起動", runtime.Detail);
    }

    [Fact]
    public void Device_stage_is_never_reported_as_established()
    {
        foreach (var stages in new[]
        {
            WorkspaceApplyReport.Build(null, false),
            WorkspaceApplyReport.Build(5, true),
            WorkspaceApplyReport.Build(5, false),
        })
        {
            Assert.Equal("対象外", Stage(stages, "device").State);
        }
    }
}
