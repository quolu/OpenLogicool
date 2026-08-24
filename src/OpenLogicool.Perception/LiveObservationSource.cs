using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Perception;

/// <summary>Recognizer が frame から得た候補。未校正の confidence は Known に使えない。</summary>
public sealed record RecognitionResult(
    string RecognizerVersion,
    bool IsCalibrated,
    IReadOnlyList<StateCandidate> Candidates,
    string? UnavailableReason = null);

public interface IFrameRecognizer
{
    RecognitionResult Recognize(CapturedFrame frame);
}

/// <summary>
/// recorded と live のどちらの frame も、capture可否とstate同定を分離した ObservationResult へ正規化する。
/// Attempt は Playbooks の RunEvent が所有するため、ここでは扱わない。
/// </summary>
public sealed class LiveObservationSource(IFrameRecognizer recognizer) : IObservationSource
{
    private long sequence;

    public ObservationResult Observe(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var recognized = recognizer.Recognize(frame)
            ?? throw new InvalidOperationException("Recognizer が認識結果を返しませんでした。");
        Validate(recognized);

        var captureAvailability = recognized.UnavailableReason is null
            ? CaptureAvailability.Available
            : CaptureAvailability.Unavailable;
        var stateIdentity = StateIdentityOf(recognized);
        var candidates = stateIdentity is StateIdentityStatus.Known or StateIdentityStatus.Ambiguous
            ? recognized.Candidates
            : [];

        return new ObservationResult(
            "0.3.0",
            $"observation:{frame.SourceId}:{frame.Sequence}:{Interlocked.Increment(ref sequence)}",
            new CapturedFrameReference(
                "0.3.0",
                frame.SourceId,
                frame.Backend,
                frame.Sequence,
                frame.MonotonicMs,
                frame.WallClockUtc,
                frame.TransformRevision,
                frame.FreshnessMs,
                frame.LastChangeMs),
            captureAvailability,
            stateIdentity,
            candidates,
            recognized.RecognizerVersion,
            frame.FreshnessMs,
            captureAvailability == CaptureAvailability.Unavailable ? recognized.UnavailableReason : null);
    }

    public static bool AllowsAutomaticExecution(ObservationResult observation) =>
        observation.CaptureAvailability == CaptureAvailability.Available
        && observation.StateIdentity == StateIdentityStatus.Known;

    private static StateIdentityStatus StateIdentityOf(RecognitionResult result)
    {
        if (result.UnavailableReason is not null)
        {
            return StateIdentityStatus.InsufficientEvidence;
        }

        if (!result.IsCalibrated || result.Candidates.Count == 0)
        {
            return StateIdentityStatus.InsufficientEvidence;
        }

        return result.Candidates.Count == 1
            ? StateIdentityStatus.Known
            : StateIdentityStatus.Ambiguous;
    }

    private static void Validate(RecognitionResult result)
    {
        if (string.IsNullOrWhiteSpace(result.RecognizerVersion))
        {
            throw new InvalidOperationException("RecognizerVersion が空の認識結果は Observation にできません。");
        }

        if (result.Candidates is null)
        {
            throw new InvalidOperationException("Recognizer の候補集合がありません。");
        }

        if (result.UnavailableReason is not null && string.IsNullOrWhiteSpace(result.UnavailableReason))
        {
            throw new InvalidOperationException("Unavailable の認識結果には理由が必要です。");
        }

        foreach (var candidate in result.Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.StateId)
                || candidate.Confidence < 0
                || candidate.Confidence > 1
                || candidate.EvidenceRegions is null
                || candidate.EvidenceRegions.Count == 0)
            {
                throw new InvalidOperationException("Recognizer の候補は state、校正済み confidence、evidence region を持つ必要があります。");
            }
        }
    }
}

/// <summary>
/// 同一の Known state が同じ frame 系統で安定窓を満たしたかを判定する。
/// dispatch の前後をどこで比較するかは Attempt を所有する Playbooks の責務である。
/// </summary>
public sealed class ObservationStabilityWindow
{
    private readonly long requiredStableMs;
    private StableKnown? firstKnown;

    public ObservationStabilityWindow(long requiredStableMs)
    {
        if (requiredStableMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredStableMs), "安定窓は正の値で明示します。");
        }

        this.requiredStableMs = requiredStableMs;
    }

    public bool Observe(ObservationResult observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (observation.CaptureAvailability != CaptureAvailability.Available
            || observation.StateIdentity != StateIdentityStatus.Known
            || observation.StateCandidates.Count != 1)
        {
            firstKnown = null;
            return false;
        }

        var current = new StableKnown(
            observation.Frame.SourceId,
            observation.Frame.Backend,
            observation.Frame.TransformRevision,
            observation.StateCandidates[0].StateId,
            observation.Frame.MonotonicMs);
        if (firstKnown is null || !firstKnown.Matches(current) || current.MonotonicMs < firstKnown.MonotonicMs)
        {
            firstKnown = current;
            return false;
        }

        return current.MonotonicMs - firstKnown.MonotonicMs >= requiredStableMs;
    }

    private sealed record StableKnown(
        string SourceId,
        CaptureBackend Backend,
        long TransformRevision,
        string StateId,
        double MonotonicMs)
    {
        public bool Matches(StableKnown other) =>
            string.Equals(SourceId, other.SourceId, StringComparison.Ordinal)
            && Backend == other.Backend
            && TransformRevision == other.TransformRevision
            && string.Equals(StateId, other.StateId, StringComparison.Ordinal);
    }
}
