using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;

namespace OpenLogicool.Playbooks;

/// <summary>再開時に Run が記録済みの対象を表す。不明値を既定へ補完しない。</summary>
public sealed record LiveResumeBinding(
    string RecordedAppIdentity,
    string RecordedTargetWindowId,
    string RecordedCaptureSourceId,
    string AdoptedPlaybookVersionId,
    string ResumePlaybookVersionId);

/// <summary>dispatch 直前に実環境から読み取った対象と、t06 が合成した Observation。</summary>
public sealed record LiveResumeContext(
    string? ObservedAppIdentity,
    string? ObservedTargetWindowId,
    string? ObservedCaptureSourceId,
    string? InputTargetWindowId,
    ObservationResult Observation);

public enum LiveResumeBlockReason
{
    TargetWindowMismatch,
    CaptureSourceMismatch,
    InputTargetMismatch,
}

/// <summary>
/// 実画面 Observation を Phase 4 の StateMatcher／ResumeGate へ接続する dispatch 前の pure gate。
/// InputEmitter は参照しない。許可結果だけを返し、呼び出し側は false の時に dispatch してはならない。
/// </summary>
public sealed record LiveResumeDecision(
    bool DispatchAllowed,
    StateMatchResult StateMatch,
    ResumeDecision ResumeDecision,
    IReadOnlyList<LiveResumeBlockReason> LiveBlockReasons);

public static class LiveResumeGate
{
    public static LiveResumeDecision Judge(
        LiveResumeBinding binding,
        LiveResumeContext context,
        IReadOnlyList<RunEvent> events,
        string expectedStateId,
        long freshnessBudgetMs,
        long stabilityWindowMs)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Observation);
        ArgumentNullException.ThrowIfNull(events);
        Validate(binding);

        var stateMatch = StateMatcher.Match(
            context.Observation,
            expectedStateId,
            freshnessBudgetMs,
            stabilityWindowMs);
        var resumeDecision = ResumeGate.Judge(new ResumeCheckInputs(
            binding.RecordedAppIdentity,
            context.ObservedAppIdentity,
            binding.RecordedCaptureSourceId,
            context.ObservedCaptureSourceId,
            binding.AdoptedPlaybookVersionId,
            binding.ResumePlaybookVersionId,
            stateMatch,
            ResumeReadiness.IsRunClosed(events),
            ResumeReadiness.SatisfiesReobservation(events, context.Observation.ObservationId)));

        var liveReasons = new List<LiveResumeBlockReason>();
        if (!string.Equals(binding.RecordedTargetWindowId, context.ObservedTargetWindowId, StringComparison.Ordinal))
        {
            liveReasons.Add(LiveResumeBlockReason.TargetWindowMismatch);
        }

        if (!string.Equals(binding.RecordedCaptureSourceId, context.ObservedCaptureSourceId, StringComparison.Ordinal)
            || !string.Equals(context.ObservedCaptureSourceId, context.Observation.Frame.SourceId, StringComparison.Ordinal))
        {
            liveReasons.Add(LiveResumeBlockReason.CaptureSourceMismatch);
        }

        if (!string.Equals(context.ObservedTargetWindowId, context.InputTargetWindowId, StringComparison.Ordinal))
        {
            liveReasons.Add(LiveResumeBlockReason.InputTargetMismatch);
        }

        return new LiveResumeDecision(
            resumeDecision.AutoResumeAllowed && liveReasons.Count == 0,
            stateMatch,
            resumeDecision,
            liveReasons);
    }

    private static void Validate(LiveResumeBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.RecordedAppIdentity)
            || string.IsNullOrWhiteSpace(binding.RecordedTargetWindowId)
            || string.IsNullOrWhiteSpace(binding.RecordedCaptureSourceId)
            || string.IsNullOrWhiteSpace(binding.AdoptedPlaybookVersionId)
            || string.IsNullOrWhiteSpace(binding.ResumePlaybookVersionId))
        {
            throw new ArgumentException("再開に記録した app、window、capture source、version は空にできません。", nameof(binding));
        }
    }
}
