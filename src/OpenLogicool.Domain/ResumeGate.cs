using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Domain;

/// <summary>State match の結果（計画 §6.8 の閉集合5値）。</summary>
public enum StateMatchResult
{
    UniqueMatch,
    NoMatch,
    AmbiguousMatch,
    InsufficientEvidence,
    StaleObservation,
}

/// <summary>
/// 現在 Observation と期待 state の照合（PB-009／§6.8）。pure であり、Observation の出所
/// （Phase 4 では GameLab oracle／fake だけ）を知らない。実画面 UniqueMatch の成立主張は Phase 5 の面。
/// </summary>
public static class StateMatcher
{
    /// <summary>
    /// CaptureAvailability／StateIdentityStatus→StateMatchResult の写像:
    /// Unavailable は照合の証拠にならず InsufficientEvidence、Stale は StaleObservation。
    /// Novel／InsufficientEvidence は InsufficientEvidence、Ambiguous は AmbiguousMatch。
    /// Known は鮮度予算超過で StaleObservation、安定窓（frame の LastChangeMs）未達で InsufficientEvidence、
    /// その上で唯一候補の StateId が期待と一致した時だけ UniqueMatch、不一致は NoMatch。
    /// </summary>
    public static StateMatchResult Match(
        ObservationResult observation,
        string expectedStateId,
        long freshnessBudgetMs,
        long stabilityWindowMs)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (string.IsNullOrWhiteSpace(expectedStateId))
        {
            throw new ArgumentException("期待 state が空です。", nameof(expectedStateId));
        }

        if (freshnessBudgetMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(freshnessBudgetMs), "鮮度予算は正の値で明示します。");
        }

        if (stabilityWindowMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stabilityWindowMs), "安定窓は正の値で明示します。");
        }

        switch (observation.CaptureAvailability)
        {
            case CaptureAvailability.Unavailable:
                return StateMatchResult.InsufficientEvidence;
            case CaptureAvailability.Stale:
                return StateMatchResult.StaleObservation;
            case CaptureAvailability.Available:
                break;
            default:
                throw new ArgumentException(
                    $"未知の CaptureAvailability '{observation.CaptureAvailability}' です。",
                    nameof(observation));
        }

        switch (observation.StateIdentity)
        {
            case StateIdentityStatus.Novel:
            case StateIdentityStatus.InsufficientEvidence:
                return StateMatchResult.InsufficientEvidence;
            case StateIdentityStatus.Ambiguous:
                return StateMatchResult.AmbiguousMatch;
            case StateIdentityStatus.Known:
                break;
            default:
                throw new ArgumentException(
                    $"未知の StateIdentityStatus '{observation.StateIdentity}' です。",
                    nameof(observation));
        }

        if (observation.StateCandidates.Count != 1)
        {
            throw new ArgumentException(
                $"Known の Observation は唯一候補を持たなければなりません（候補 {observation.StateCandidates.Count} 件）。",
                nameof(observation));
        }

        if (observation.FreshnessMs > freshnessBudgetMs)
        {
            return StateMatchResult.StaleObservation;
        }

        if (observation.Frame.LastChangeMs < stabilityWindowMs)
        {
            return StateMatchResult.InsufficientEvidence;
        }

        return string.Equals(observation.StateCandidates[0].StateId, expectedStateId, StringComparison.Ordinal)
            ? StateMatchResult.UniqueMatch
            : StateMatchResult.NoMatch;
    }
}

/// <summary>自動再開を拒む理由（PB-009 の照合条件と §6.8 の再開規則）。</summary>
public enum ResumeBlockReason
{
    RunClosed,
    AppIdentityMismatch,
    TargetWindowMismatch,
    PlaybookVersionMismatch,
    StateNotUniqueMatch,
    ReobservationRequired,
}

/// <summary>
/// 再開判定の入力（pure data）。照合材料は呼び出し側が journal・Observation から供給し、
/// 本 record は「何を照合したか」を値として固定する。
/// </summary>
public sealed record ResumeCheckInputs(
    string RecordedAppIdentity,
    string? ObservedAppIdentity,
    string RecordedTargetSourceId,
    string? ObservedTargetSourceId,
    string AdoptedVersionId,
    string ResumeVersionId,
    StateMatchResult StateMatch,
    bool RunClosed,
    bool ReobservationSatisfied);

/// <summary>再開判定。許可は全条件成立の時だけで、拒否理由は全列挙する。</summary>
public sealed record ResumeDecision(bool AutoResumeAllowed, IReadOnlyList<ResumeBlockReason> BlockReasons);

/// <summary>
/// PB-009: 再開前に対象 app・target window・Playbook version・現在 Observation を照合し、
/// UniqueMatch 以外では自動再開しない。§6.8: manual intervention 終了後は必ず新 Observation から照合する。
/// 落ちた条件は一つで止めず全列挙する（利用者が一度で全部直せるように）。
/// </summary>
public static class ResumeGate
{
    public static ResumeDecision Judge(ResumeCheckInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (string.IsNullOrWhiteSpace(inputs.RecordedAppIdentity)
            || string.IsNullOrWhiteSpace(inputs.RecordedTargetSourceId)
            || string.IsNullOrWhiteSpace(inputs.AdoptedVersionId)
            || string.IsNullOrWhiteSpace(inputs.ResumeVersionId))
        {
            throw new ArgumentException("記録側の照合材料（app・target window・version）は空にできません。", nameof(inputs));
        }

        var reasons = new List<ResumeBlockReason>();

        if (inputs.RunClosed)
        {
            reasons.Add(ResumeBlockReason.RunClosed);
        }

        if (!string.Equals(inputs.ObservedAppIdentity, inputs.RecordedAppIdentity, StringComparison.Ordinal))
        {
            reasons.Add(ResumeBlockReason.AppIdentityMismatch);
        }

        if (!string.Equals(inputs.ObservedTargetSourceId, inputs.RecordedTargetSourceId, StringComparison.Ordinal))
        {
            reasons.Add(ResumeBlockReason.TargetWindowMismatch);
        }

        if (!string.Equals(inputs.ResumeVersionId, inputs.AdoptedVersionId, StringComparison.Ordinal))
        {
            reasons.Add(ResumeBlockReason.PlaybookVersionMismatch);
        }

        if (inputs.StateMatch != StateMatchResult.UniqueMatch)
        {
            reasons.Add(ResumeBlockReason.StateNotUniqueMatch);
        }

        if (!inputs.ReobservationSatisfied)
        {
            reasons.Add(ResumeBlockReason.ReobservationRequired);
        }

        return new ResumeDecision(reasons.Count == 0, reasons);
    }
}
