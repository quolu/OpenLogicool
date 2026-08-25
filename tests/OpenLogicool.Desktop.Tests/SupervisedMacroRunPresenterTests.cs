using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Desktop;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class SupervisedMacroRunPresenterTests
{
    [Fact]
    public void Outcome_unknown_shows_operation_expected_screens_audits_dispatch_reason_and_history()
    {
        var before = new VisualMacroAuditResult(
            1, VisualMacroAuditPhase.Before, "state:lobby", "observation:1",
            VisualMacroAuditStatus.Confirmed, "一致");
        var run = new SupervisedMacroRunSnapshot(
            "run:1",
            new SupervisedMacroRunPin("macro:1", "route:v1", "structure:1", "game", "env"),
            SupervisedMacroRunState.OutcomeUnknown,
            SupervisedMacroStopReason.DispatchFault,
            1,
            2,
            "入力結果を確定できないため停止しました。",
            [new SupervisedMacroStepHistory(1, before, "attempt:1", true, false, null)]);

        var view = SupervisedMacroRunPresenter.Project(run, [Step(1)]);

        Assert.Contains("結果不明・停止必須", view.Text);
        Assert.Contains("現在の操作: クリック　ロビー → 部隊編成", view.Text);
        Assert.Contains("期待画面: 操作前「ロビー」／操作後「部隊編成」", view.Text);
        Assert.Contains("操作前: 確認済み", view.Text);
        Assert.Contains("送信: Nano送信の結果不明", view.Text);
        Assert.Contains("停止理由: Nano送信結果を確定できない", view.Text);
        Assert.Contains("履歴:", view.Text);
        Assert.Contains("1. ロビー → 部隊編成 / クリック", view.Text);
        Assert.False(view.CanStart);
        Assert.False(view.CanDispatch);
        Assert.True(view.CanStop);
    }

    [Fact]
    public void Completed_run_allows_a_new_start_and_disables_stop()
    {
        var run = new SupervisedMacroRunSnapshot(
            "run:1",
            new SupervisedMacroRunPin("macro:1", "route:v1", "structure:1", "game", "env"),
            SupervisedMacroRunState.Completed,
            SupervisedMacroStopReason.None,
            1,
            1,
            "完了しました。",
            []);

        var view = SupervisedMacroRunPresenter.Project(run, [Step(1)]);

        Assert.True(view.CanStart);
        Assert.False(view.CanDispatch);
        Assert.False(view.CanStop);
    }

    private static LearningRouteStepItem Step(int sequence) => new(
        sequence,
        new LearningRouteEdgeItem(
            "edge:1", "ロビー", "部隊編成", "クリック", "部隊", "部隊編成を表示",
            "確認済み", "危険操作なし"));
}
