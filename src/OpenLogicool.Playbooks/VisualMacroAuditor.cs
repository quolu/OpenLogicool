using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Playbooks;

/// <summary>ObservedSceneだけを使って各stepの前後画面を判定する。AI・dispatch・retryは行わない。</summary>
public static class VisualMacroAuditor
{
    public static VisualMacroAuditResult AuditBefore(VisualMacroStep step, ObservedScene scene)
    {
        var result = Audit(step, scene, VisualMacroAuditPhase.Before, step.SourceStateId);
        return result.Status == VisualMacroAuditStatus.Confirmed && !HasRequiredTarget(step, scene)
            ? result with
            {
                Status = VisualMacroAuditStatus.Ambiguous,
                Message = "fresh frameで操作対象を一意に確認できません。",
            }
            : result;
    }

    public static VisualMacroAuditResult AuditTransition(
        VisualMacroStep step,
        SupervisedMacroTransitionObservation transition)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(transition);
        var final = transition.FinalScene;
        return transition.Comparison.Judgement switch
        {
            GameTransitionJudgement.Moved => new VisualMacroAuditResult(
                step.Sequence,
                VisualMacroAuditPhase.After,
                step.DestinationStateId,
                final!.ObservationId,
                VisualMacroAuditStatus.Confirmed,
                transition.DestinationMatched
                    ? "画面遷移と保存済みdestinationの一致を確認しました。"
                    : "画面遷移を確認しました。destination IDは診断上不一致です。"),
            GameTransitionJudgement.Stayed => new VisualMacroAuditResult(
                step.Sequence,
                VisualMacroAuditPhase.After,
                step.DestinationStateId,
                final!.ObservationId,
                VisualMacroAuditStatus.UnexpectedState,
                "10秒観測しても画面遷移を確認できませんでした。"),
            _ => new VisualMacroAuditResult(
                step.Sequence,
                VisualMacroAuditPhase.After,
                step.DestinationStateId,
                final!.ObservationId,
                VisualMacroAuditStatus.Ambiguous,
                "操作後の画面遷移を確定できませんでした。"),
        };
    }

    public static bool HasRequiredTarget(VisualMacroStep step, ObservedScene scene)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(scene);
        if (step.Primitive is not ("click" or "frame-bound pointer click"))
        {
            return true;
        }
        return scene.Affordances.Count(candidate =>
            candidate.CandidateId == step.AffordanceCandidateId
            && candidate.ObservationId == scene.ObservationId
            && candidate.FrameSequence == scene.Frame.Sequence
            && candidate.TransformRevision == scene.Frame.TransformRevision
            && candidate.TargetWindowSourceId == scene.Frame.SourceId
            && candidate.AllowedPrimitives.Contains(step.Primitive, StringComparer.Ordinal)) == 1;
    }

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
