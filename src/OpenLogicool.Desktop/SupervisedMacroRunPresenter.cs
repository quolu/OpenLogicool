using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Desktop;

public sealed record SupervisedMacroRunView(
    string Text,
    bool CanStart,
    bool CanDispatch,
    bool CanStop);

/// <summary>教師付きrunの内部状態を、操作・期待画面・全履歴を含む利用者向け日本語へ投影する。</summary>
public static class SupervisedMacroRunPresenter
{
    public static SupervisedMacroRunView Project(
        SupervisedMacroRunSnapshot run,
        IReadOnlyList<LearningRouteStepItem> routeSteps)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(routeSteps);
        var current = routeSteps.FirstOrDefault(step => step.Sequence == run.CurrentStepSequence);
        var latest = run.History.LastOrDefault();
        var lines = new List<string>
        {
            $"実行: {StateLabel(run.State)}　step {run.CurrentStepSequence}/{run.TotalSteps}",
            current is null
                ? $"現在の操作: step {run.CurrentStepSequence}"
                : $"現在の操作: {current.Edge.PrimitiveLabel}　{current.Edge.SourceLabel} → {current.Edge.DestinationLabel}",
            current is null
                ? "期待画面: 学習済みの操作前／操作後画面"
                : $"期待画面: 操作前「{current.Edge.SourceLabel}」／操作後「{current.Edge.DestinationLabel}」",
            $"操作前: {AuditLabel(latest?.BeforeAudit)}　｜　送信: {DispatchLabel(latest)}",
            $"操作後: {AuditLabel(latest?.AfterAudit)}",
        };
        if (run.StopReason != SupervisedMacroStopReason.None)
        {
            lines.Add($"停止理由: {StopReasonLabel(run.StopReason)}");
        }
        lines.Add(run.StatusMessage);
        lines.Add("履歴:");
        if (run.History.Count == 0)
        {
            lines.Add("・まだ実行していません");
        }
        else
        {
            foreach (var item in run.History)
            {
                var route = routeSteps.FirstOrDefault(step => step.Sequence == item.StepSequence);
                var operation = route is null
                    ? $"step {item.StepSequence}"
                    : $"{route.Edge.SourceLabel} → {route.Edge.DestinationLabel} / {route.Edge.PrimitiveLabel}";
                lines.Add(
                    $"・{item.StepSequence}. {operation}: 前={AuditLabel(item.BeforeAudit)}、" +
                    $"送信={DispatchLabel(item)}、後={AuditLabel(item.AfterAudit)}");
            }
        }

        return new SupervisedMacroRunView(
            string.Join(Environment.NewLine, lines),
            run.State is SupervisedMacroRunState.Completed or SupervisedMacroRunState.Stopped,
            run.CanDispatch,
            run.State is not (SupervisedMacroRunState.Completed or SupervisedMacroRunState.Stopped));
    }

    private static string StateLabel(SupervisedMacroRunState state) => state switch
    {
        SupervisedMacroRunState.AwaitingBeforeAudit => "操作前画面を確認中",
        SupervisedMacroRunState.ReadyToDispatch => "次の一手を送信可能",
        SupervisedMacroRunState.AwaitingAfterAudit => "操作後画面を確認中",
        SupervisedMacroRunState.Completed => "完了",
        SupervisedMacroRunState.Stopped => "停止",
        SupervisedMacroRunState.OutcomeUnknown => "結果不明・停止必須",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string StopReasonLabel(SupervisedMacroStopReason reason) => reason switch
    {
        SupervisedMacroStopReason.BeforeAuditFailed => "操作前画面が一致しない",
        SupervisedMacroStopReason.AfterAuditFailed => "操作後画面が一致しない",
        SupervisedMacroStopReason.DispatchFault => "Nano送信結果を確定できない",
        SupervisedMacroStopReason.DispatchNotStarted => "Nano入力を送信せず停止した",
        SupervisedMacroStopReason.RuntimeUnavailable => "実行環境を準備できない",
        SupervisedMacroStopReason.ObservationFault => "操作後画面を取得できない",
        SupervisedMacroStopReason.UserStopped => "利用者が停止した",
        SupervisedMacroStopReason.None => "なし",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static string AuditLabel(VisualMacroAuditResult? audit) => audit is null
        ? "未確認"
        : audit.Status switch
        {
            VisualMacroAuditStatus.Confirmed => "確認済み",
            VisualMacroAuditStatus.UnexpectedState => "別の画面",
            VisualMacroAuditStatus.Ambiguous => "判定不能",
            VisualMacroAuditStatus.Unavailable => "取得不能",
            VisualMacroAuditStatus.Stale => "古い画面",
            _ => throw new ArgumentOutOfRangeException(nameof(audit)),
        };

    private static string DispatchLabel(SupervisedMacroStepHistory? history) => history switch
    {
        { DispatchReported: true } => "Nanoへ1回送信済み（画面確認が成功条件）",
        { DispatchArmed: true } => "Nano送信の結果不明",
        _ => "未送信",
    };
}
