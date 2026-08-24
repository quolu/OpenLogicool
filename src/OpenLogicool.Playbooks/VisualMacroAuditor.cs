using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Playbooks;

/// <summary>ObservedSceneだけを使って各stepの前後画面を判定する。AI・dispatch・retryは行わない。</summary>
public static class VisualMacroAuditor
{
    public static VisualMacroAuditResult AuditBefore(VisualMacroStep step, ObservedScene scene) =>
        Audit(step, scene, VisualMacroAuditPhase.Before, step.SourceStateId);

    public static VisualMacroAuditResult AuditAfter(VisualMacroStep step, ObservedScene scene) =>
        Audit(step, scene, VisualMacroAuditPhase.After, step.DestinationStateId);

    private static VisualMacroAuditResult Audit(
        VisualMacroStep step,
        ObservedScene scene,
        VisualMacroAuditPhase phase,
        string expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(scene);
        var (status, message) = scene.CaptureAvailability switch
        {
            CaptureAvailability.Unavailable => (VisualMacroAuditStatus.Unavailable, "画面を取得できません。"),
            CaptureAvailability.Stale => (VisualMacroAuditStatus.Stale, "画面が更新されていません。"),
            _ when scene.StateIdentity is StateIdentityStatus.Ambiguous or StateIdentityStatus.InsufficientEvidence =>
                (VisualMacroAuditStatus.Ambiguous, "画面状態を一意に判定できません。"),
            _ when scene.StateIdentity == StateIdentityStatus.Known
                   && string.Equals(scene.StateHypothesisId, expectedStateId, StringComparison.Ordinal) =>
                (VisualMacroAuditStatus.Confirmed, "期待する画面を確認しました。"),
            _ => (VisualMacroAuditStatus.UnexpectedState,
                $"期待する画面 '{expectedStateId}' と観測結果 '{scene.StateHypothesisId ?? "<不明>"}' が一致しません。"),
        };
        return new VisualMacroAuditResult(
            step.Sequence,
            phase,
            expectedStateId,
            scene.ObservationId,
            status,
            message);
    }
}
