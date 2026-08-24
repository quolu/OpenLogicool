using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Fakes;

/// <summary>
/// capture可否とstate同定を分離した fake ObservationResult を決定的に合成する（Phase 9 t06）。
/// Phase 4 の「現在 state」根拠は GameLab oracle とこの fake だけであり、実画面 capture を参照しない。
/// Perception は Attempt を知らない（§6.7 契約4）——この builder にも AttemptId を受け取る口が無い。
/// 状態の意味（Known への丸め禁止・Unavailable の理由必須）を破る結果は作れない。
/// </summary>
public static class FakeObservations
{
    private const string SchemaVersion = "0.3.0";
    public const string RecognizerVersion = "fake-recognizer-1";

    public static ObservationResult Known(string observationId, string stateId, long frameSequence = 1) =>
        Build(observationId, frameSequence, CaptureAvailability.Available, StateIdentityStatus.Known,
            [Candidate(stateId, confidence: 0.97)], captureFailureReason: null);

    /// <summary>複数候補の差が小さい観測（§6.9: Known へ丸めない）。</summary>
    public static ObservationResult Ambiguous(string observationId, string firstStateId, string secondStateId, long frameSequence = 1)
    {
        if (firstStateId == secondStateId)
        {
            throw new ArgumentException("Ambiguous には異なる state 候補が2つ必要です。", nameof(secondStateId));
        }

        return Build(observationId, frameSequence, CaptureAvailability.Available, StateIdentityStatus.Ambiguous,
            [Candidate(firstStateId, confidence: 0.51), Candidate(secondStateId, confidence: 0.49)], captureFailureReason: null);
    }

    /// <summary>どの state とも判定できない観測。候補を持たず、Known へ丸めない。</summary>
    public static ObservationResult Unknown(string observationId, long frameSequence = 1) =>
        Build(observationId, frameSequence, CaptureAvailability.Available, StateIdentityStatus.InsufficientEvidence, [], captureFailureReason: null);

    /// <summary>captureは成立したが、既知stateに一致しない新規scene hypothesis。</summary>
    public static ObservationResult Novel(string observationId, long frameSequence = 1) =>
        Build(observationId, frameSequence, CaptureAvailability.Available, StateIdentityStatus.Novel, [], captureFailureReason: null);

    /// <summary>観測そのものが成立しない状態。診断カテゴリの理由が必須。</summary>
    public static ObservationResult Unavailable(string observationId, string reason, long frameSequence = 1)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Unavailable には captureFailureReason が必要です。", nameof(reason));
        }

        return Build(observationId, frameSequence, CaptureAvailability.Unavailable, StateIdentityStatus.InsufficientEvidence, [], captureFailureReason: reason);
    }

    public static ObservationResult Stale(string observationId, string reason, long frameSequence = 1)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Stale には captureFailureReason が必要です。", nameof(reason));
        }

        return Build(observationId, frameSequence, CaptureAvailability.Stale, StateIdentityStatus.InsufficientEvidence, [], reason);
    }

    private static ObservationResult Build(
        string observationId,
        long frameSequence,
        CaptureAvailability captureAvailability,
        StateIdentityStatus stateIdentity,
        IReadOnlyList<StateCandidate> candidates,
        string? captureFailureReason)
    {
        if (string.IsNullOrWhiteSpace(observationId))
        {
            throw new ArgumentException("ObservationId が空です。", nameof(observationId));
        }

        return new ObservationResult(
            SchemaVersion,
            observationId,
            new CapturedFrameReference(
                SchemaVersion,
                "fake-source",
                CaptureBackend.WindowsGraphicsCapture,
                frameSequence,
                MonotonicMs: frameSequence * 100.0,
                WallClockUtc: new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(frameSequence * 100.0),
                TransformRevision: 1,
                FreshnessMs: 16,
                LastChangeMs: 0),
            captureAvailability,
            stateIdentity,
            candidates,
            RecognizerVersion,
            FreshnessMs: 16,
            captureFailureReason);
    }

    private static StateCandidate Candidate(string stateId, double confidence)
    {
        if (string.IsNullOrWhiteSpace(stateId))
        {
            throw new ArgumentException("StateId が空です。", nameof(stateId));
        }

        return new StateCandidate(
            SchemaVersion,
            stateId,
            confidence,
            [new EvidenceRegion(SchemaVersion, "rect", [0.25, 0.25, 0.5, 0.5], "fake-recognizer")]);
    }
}
