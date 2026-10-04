using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class RecordedMacroResultVerifierTests
{
    [Fact]
    public void Same_state_id_cannot_turn_different_local_layout_into_a_match()
    {
        var verifier = new RecordedMacroResultVerifier(_ => Session(Scene("reward", "REWARD")));
        var result = verifier.Verify(Step(), 3, Scene("actual", "50ジュエル消費確認"));
        Assert.Equal(RecordedMacroResultStatus.Different, result.Status);
        Assert.Equal("reward", result.ExpectedObservationId);
    }

    [Fact]
    public void Different_session_state_ids_do_not_reject_the_same_local_layout()
    {
        var expected = Scene("recorded", "REWARD");
        var actual = Scene("actual", "REWARD") with
        { StateHypothesisId = "another-session", StateCandidates = [new(Version, "another-session", 1, [])] };
        Assert.True(RecordedMacroResultVerifier.MatchesLocalLayout(expected, actual));
    }

    [Fact]
    public void Compared_recorded_observation_is_used_even_when_a_later_stable_scene_is_different()
    {
        var expected = Scene("recorded", "REWARD");
        var session = Session(expected, Scene("later", "確認"));
        var original = System.Text.Json.JsonSerializer.Serialize(session);
        RecordedMacroResultVerification? published = null;
        var verifier = new RecordedMacroResultVerifier(_ => session, result => published = result);
        var result = verifier.Verify(Step(), 3, Scene("actual", "REWARD"));
        Assert.Equal(RecordedMacroResultStatus.Matched, result.Status);
        Assert.Equal("recorded", result.ExpectedObservationId);
        Assert.Equal(result, published);
        Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(session));
    }

    [Fact]
    public void Missing_record_or_current_evidence_is_explicitly_unavailable()
    {
        Assert.Equal(RecordedMacroResultStatus.Unavailable,
            new RecordedMacroResultVerifier(_ => null).Verify(Step(), 3, Scene("actual", "REWARD")).Status);
        Assert.Equal(RecordedMacroResultStatus.Unavailable,
            new RecordedMacroResultVerifier(_ => Session(Scene("recorded", "REWARD"))).Verify(Step(), 3, null).Status);
    }

    private const string Version = ContractSchemaVersions.Revision03;
    private static DemonstrationRouteStep Step() => new("demo", "op", GameInteractionOperations.Click,
        [0.5, 0.5], null, null, null, null, "edge", GameTransitionJudgement.Moved, "test", "demo");

    private static ObservedScene Scene(string id, string label) => new(Version, id, id,
        new(Version, "window", CaptureBackend.WindowsGraphicsCapture, 1, 1, DateTimeOffset.UnixEpoch, 1, 0, 0),
        CaptureAvailability.Available, StateIdentityStatus.Known, "shared-state", [new(Version, "shared-state", 1, [])],
        [new(Version, "candidate", id, 1, 1, "window", new(Version, "ocr", [0.4, 0.4, 0.2, 0.1], "local"),
            [], 1, [GameInteractionOperations.Click], "ocr-text", label)], "test");

    private static DemonstrationSessionRecord Session(ObservedScene expected, ObservedScene? later = null)
    {
        var before = Scene("before", "案内");
        var after = new GameInteractionStabilityResult(Version, GameInteractionStabilityStatus.Stable,
            later is null ? [expected] : [expected, later], later ?? expected, 2, 1_000, 10_000, null);
        var operation = new DemonstrationOperation(Version, "op", GameInteractionOperations.Click,
            DemonstrationInputSource.Mouse, new(Version, "before", 1, 1, "window", [0.5, 0.5]), before, after,
            new(Version, "before", expected.ObservationId, GameTransitionJudgement.Moved, [], []),
            "evidence", 0, 1_000, DateTimeOffset.UnixEpoch);
        return new(new(Version, "demo", "game", "env", "goal", "game.exe", "window", "test", DateTimeOffset.UnixEpoch),
            DemonstrationSessionState.Stopped, "revision",
            [new(Version, "demo", 1, "event", null, "revision", DemonstrationEventKind.Operation,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, operation, null, null)]);
    }
}
