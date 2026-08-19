using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Domain.Tests;

public sealed class ResumeGateTests
{
    private static ObservationResult Observation(
        ObservationStatus status,
        IReadOnlyList<StateCandidate> candidates,
        long freshnessMs = 100,
        long lastChangeMs = 5_000,
        string sourceId = "window-1",
        string? unavailableReason = null) =>
        new(
            "0.1.0",
            "observation-1",
            new CapturedFrameReference(
                "0.1.0", sourceId, CaptureBackend.WindowsGraphicsCapture,
                Sequence: 1, MonotonicMs: 1_000, WallClockUtc: new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
                TransformRevision: 1, FreshnessMs: freshnessMs, LastChangeMs: lastChangeMs),
            status,
            candidates,
            "recognizer-1",
            freshnessMs,
            unavailableReason);

    private static StateCandidate Candidate(string stateId, double confidence = 0.95) =>
        new("0.1.0", stateId, confidence, []);

    [Theory]
    [InlineData(ObservationStatus.Unknown)]
    [InlineData(ObservationStatus.Unavailable)]
    public void Match_returns_insufficient_evidence_when_the_screen_cannot_be_recognized(ObservationStatus status)
    {
        var observation = Observation(status, [], unavailableReason: status == ObservationStatus.Unavailable ? "capture-lost" : null);

        Assert.Equal(StateMatchResult.InsufficientEvidence, StateMatcher.Match(observation, "state-a", 1_000, 500));
    }

    [Fact]
    public void Match_returns_ambiguous_for_an_ambiguous_observation()
    {
        var observation = Observation(ObservationStatus.Ambiguous, [Candidate("state-a", 0.51), Candidate("state-b", 0.49)]);

        Assert.Equal(StateMatchResult.AmbiguousMatch, StateMatcher.Match(observation, "state-a", 1_000, 500));
    }

    [Fact]
    public void Match_returns_unique_match_only_for_the_expected_state()
    {
        var matching = Observation(ObservationStatus.Known, [Candidate("state-a")]);
        var other = Observation(ObservationStatus.Known, [Candidate("state-b")]);

        Assert.Equal(StateMatchResult.UniqueMatch, StateMatcher.Match(matching, "state-a", 1_000, 500));
        Assert.Equal(StateMatchResult.NoMatch, StateMatcher.Match(other, "state-a", 1_000, 500));
    }

    [Fact]
    public void Match_returns_stale_when_the_observation_exceeds_the_freshness_budget()
    {
        var observation = Observation(ObservationStatus.Known, [Candidate("state-a")], freshnessMs: 2_000);

        Assert.Equal(StateMatchResult.StaleObservation, StateMatcher.Match(observation, "state-a", 1_000, 500));
    }

    [Fact]
    public void Match_returns_insufficient_evidence_before_the_stability_window_is_met()
    {
        var observation = Observation(ObservationStatus.Known, [Candidate("state-a")], lastChangeMs: 100);

        Assert.Equal(StateMatchResult.InsufficientEvidence, StateMatcher.Match(observation, "state-a", 1_000, 500));
    }

    [Fact]
    public void Match_rejects_a_known_observation_without_a_single_candidate()
    {
        var none = Observation(ObservationStatus.Known, []);
        var two = Observation(ObservationStatus.Known, [Candidate("state-a"), Candidate("state-b")]);

        Assert.Throws<ArgumentException>(() => StateMatcher.Match(none, "state-a", 1_000, 500));
        Assert.Throws<ArgumentException>(() => StateMatcher.Match(two, "state-a", 1_000, 500));
    }

    private static ResumeCheckInputs AllSatisfied() => new(
        RecordedAppIdentity: @"c:\games\nikke.exe",
        ObservedAppIdentity: @"c:\games\nikke.exe",
        RecordedTargetSourceId: "window-1",
        ObservedTargetSourceId: "window-1",
        AdoptedVersionId: "version-1",
        ResumeVersionId: "version-1",
        StateMatch: StateMatchResult.UniqueMatch,
        RunClosed: false,
        ReobservationSatisfied: true);

    [Fact]
    public void Judge_allows_auto_resume_only_when_every_condition_holds()
    {
        var decision = ResumeGate.Judge(AllSatisfied());

        Assert.True(decision.AutoResumeAllowed);
        Assert.Empty(decision.BlockReasons);
    }

    [Theory]
    [InlineData(StateMatchResult.NoMatch)]
    [InlineData(StateMatchResult.AmbiguousMatch)]
    [InlineData(StateMatchResult.InsufficientEvidence)]
    [InlineData(StateMatchResult.StaleObservation)]
    public void Judge_blocks_auto_resume_for_anything_but_unique_match(StateMatchResult match)
    {
        var decision = ResumeGate.Judge(AllSatisfied() with { StateMatch = match });

        Assert.False(decision.AutoResumeAllowed);
        Assert.Equal([ResumeBlockReason.StateNotUniqueMatch], decision.BlockReasons);
    }

    [Fact]
    public void Judge_blocks_when_the_app_identity_is_unknown_or_different()
    {
        var unknown = ResumeGate.Judge(AllSatisfied() with { ObservedAppIdentity = null });
        var different = ResumeGate.Judge(AllSatisfied() with { ObservedAppIdentity = @"c:\games\other.exe" });

        Assert.Equal([ResumeBlockReason.AppIdentityMismatch], unknown.BlockReasons);
        Assert.Equal([ResumeBlockReason.AppIdentityMismatch], different.BlockReasons);
    }

    [Fact]
    public void Judge_enumerates_every_failed_condition_at_once()
    {
        var decision = ResumeGate.Judge(new ResumeCheckInputs(
            RecordedAppIdentity: @"c:\games\nikke.exe",
            ObservedAppIdentity: null,
            RecordedTargetSourceId: "window-1",
            ObservedTargetSourceId: "window-2",
            AdoptedVersionId: "version-2",
            ResumeVersionId: "version-1",
            StateMatch: StateMatchResult.AmbiguousMatch,
            RunClosed: true,
            ReobservationSatisfied: false));

        Assert.False(decision.AutoResumeAllowed);
        Assert.Equal(
            [
                ResumeBlockReason.RunClosed,
                ResumeBlockReason.AppIdentityMismatch,
                ResumeBlockReason.TargetWindowMismatch,
                ResumeBlockReason.PlaybookVersionMismatch,
                ResumeBlockReason.StateNotUniqueMatch,
                ResumeBlockReason.ReobservationRequired,
            ],
            decision.BlockReasons);
    }

    [Fact]
    public void Judge_blocks_a_closed_run_and_a_version_mismatch()
    {
        var closed = ResumeGate.Judge(AllSatisfied() with { RunClosed = true });
        var versionDrift = ResumeGate.Judge(AllSatisfied() with { ResumeVersionId = "version-9" });

        Assert.Equal([ResumeBlockReason.RunClosed], closed.BlockReasons);
        Assert.Equal([ResumeBlockReason.PlaybookVersionMismatch], versionDrift.BlockReasons);
    }

    [Fact]
    public void Judge_blocks_until_a_new_observation_follows_the_manual_intervention()
    {
        var decision = ResumeGate.Judge(AllSatisfied() with { ReobservationSatisfied = false });

        Assert.Equal([ResumeBlockReason.ReobservationRequired], decision.BlockReasons);
    }
}
