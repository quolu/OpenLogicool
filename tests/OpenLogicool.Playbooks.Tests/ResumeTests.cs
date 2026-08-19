using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class ResumeTests
{
    private static RunEvent Event(
        long sequence,
        string payloadType,
        string versionId = "version-1",
        string? attemptId = null,
        string? observationId = null) =>
        new(
            "0.1.0",
            $"event-{sequence}",
            "run-1",
            sequence,
            "playbook-1",
            versionId,
            null,
            null,
            attemptId,
            "cause-1",
            $"correlation-{sequence}",
            1,
            RunEventActorType.Automation,
            new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 0, 0, 1, TimeSpan.Zero),
            observationId,
            payloadType,
            """{"body":"x"}""");

    // t05 の interface 決定（room [90]）どおりの wire 文字列。定数の正本は t05 着地後の RunEventPayloadTypes。
    private const string Abandon = "abandon";
    private const string VersionSwitch = "version-switch";

    [Fact]
    public void A_run_with_an_abandon_event_is_closed()
    {
        var open = new[] { Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1") };
        var closed = new[]
        {
            Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"),
            Event(2, Abandon),
        };

        Assert.False(ResumeReadiness.IsRunClosed(open));
        Assert.True(ResumeReadiness.IsRunClosed(closed));
    }

    [Fact]
    public void Adopted_version_is_the_pin_until_a_version_switch_moves_it()
    {
        var pinned = new[]
        {
            Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"),
            Event(2, RunEventPayloadTypes.Proposal),
        };
        var switched = new[]
        {
            Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"),
            Event(2, VersionSwitch, versionId: "version-2"),
            Event(3, RunEventPayloadTypes.Observation, versionId: "version-2", observationId: "observation-2"),
        };

        Assert.Equal("version-1", ResumeReadiness.AdoptedVersionId(pinned));
        Assert.Equal("version-2", ResumeReadiness.AdoptedVersionId(switched));
        Assert.Throws<InvalidOperationException>(() => ResumeReadiness.AdoptedVersionId([]));
    }

    [Fact]
    public void Reobservation_is_satisfied_without_any_manual_intervention()
    {
        var events = new[] { Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1") };

        Assert.True(ResumeReadiness.SatisfiesReobservation(events, "observation-9"));
    }

    [Fact]
    public void Reobservation_requires_a_new_observation_after_the_last_manual_intervention()
    {
        var events = new[]
        {
            Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"),
            Event(2, RunEventPayloadTypes.ManualIntervention),
            Event(3, RunEventPayloadTypes.ManualIntervention),
            Event(4, RunEventPayloadTypes.Observation, observationId: "observation-2"),
        };

        // 介入終了後に commit された新しい Observation だけが照合に使える。
        Assert.True(ResumeReadiness.SatisfiesReobservation(events, "observation-2"));
        // 介入前の Observation では進めない（§6.8）。
        Assert.False(ResumeReadiness.SatisfiesReobservation(events, "observation-1"));
    }

    [Fact]
    public void Reobservation_fails_safe_when_the_intervention_has_no_following_observation()
    {
        // 開始 event だけで crash した run（[97]）——観測が続かないので再開不可の側に落ちる。
        var events = new[]
        {
            Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"),
            Event(2, RunEventPayloadTypes.ManualIntervention),
        };

        Assert.False(ResumeReadiness.SatisfiesReobservation(events, "observation-1"));
        Assert.False(ResumeReadiness.SatisfiesReobservation(events, "observation-9"));
    }

    private static ObservationResult KnownObservation(string observationId, string stateId) =>
        new(
            "0.1.0",
            observationId,
            new CapturedFrameReference(
                "0.1.0", "window-1", CaptureBackend.WindowsGraphicsCapture,
                Sequence: 1, MonotonicMs: 1_000, WallClockUtc: new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
                TransformRevision: 1, FreshnessMs: 100, LastChangeMs: 5_000),
            ObservationStatus.Known,
            [new StateCandidate("0.1.0", stateId, 0.95, [])],
            "recognizer-1",
            100,
            null);

    private static PlaybookGraph Graph(string versionId = "version-1") => new(
        versionId,
        parentVersionId: null,
        changeReason: "initial",
        nodes:
        [
            new PlaybookGraphNode("node-entry", IsEntry: true, StateId: "state-a", Preconditions: [], SemanticActionId: "action-advance", ExpectedOutcomes: []),
            new PlaybookGraphNode("node-goal", IsEntry: false, StateId: "state-b", Preconditions: [], SemanticActionId: null, ExpectedOutcomes: []),
        ],
        edges: [new PlaybookGraphEdge("edge-1", "node-entry", "node-goal", BranchCondition: null)]);

    private static readonly ResumeDecision AllowedDecision = new(true, []);

    [Fact]
    public void Report_shows_the_five_ux005_items_from_the_journal_and_the_current_observation()
    {
        var events = new[]
        {
            Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"),
            Event(2, RunEventPayloadTypes.Dispatch, attemptId: "attempt-1"),
            Event(3, RunEventPayloadTypes.Confirmation, attemptId: "attempt-1", observationId: "observation-1"),
        };
        var current = KnownObservation("observation-2", "state-a");

        var view = ResumeReport.Build(
            events,
            new Dictionary<string, string> { ["observation-1"] = "state-b" },
            current,
            Graph(),
            StateMatchResult.UniqueMatch,
            AllowedDecision);

        Assert.Equal("observation-1", view.LastConfirmedObservationId);
        Assert.Equal("state-b", view.LastConfirmedStateId);
        Assert.Equal("observation-2", view.CurrentObservationId);
        Assert.Equal("state-a", view.CurrentStateId);
        Assert.Equal(ResumeStateDifference.Different, view.Difference);
        Assert.Equal("version-1", view.AdoptedVersionId);
        Assert.Equal("action-advance", view.NextSemanticActionId);
    }

    [Fact]
    public void Report_marks_the_difference_unknown_instead_of_guessing()
    {
        var events = new[] { Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1") };
        var current = KnownObservation("observation-2", "state-a");

        // confirmation が無い run: confirmed state は null のまま・差分は Unknown（補完しない）。
        var view = ResumeReport.Build(
            events, new Dictionary<string, string>(), current, Graph(),
            StateMatchResult.UniqueMatch, AllowedDecision);

        Assert.Null(view.LastConfirmedObservationId);
        Assert.Null(view.LastConfirmedStateId);
        Assert.Equal(ResumeStateDifference.Unknown, view.Difference);
    }

    [Fact]
    public void Report_offers_no_next_action_when_the_state_has_no_unique_node()
    {
        var events = new[] { Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1") };
        var current = KnownObservation("observation-2", "state-z");

        var view = ResumeReport.Build(
            events, new Dictionary<string, string>(), current, Graph(),
            StateMatchResult.NoMatch, new ResumeDecision(false, [ResumeBlockReason.StateNotUniqueMatch]));

        Assert.Null(view.NextSemanticActionId);
    }

    [Fact]
    public void Report_uses_the_post_switch_version_and_rejects_a_mismatched_graph()
    {
        var events = new[]
        {
            Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"),
            Event(2, VersionSwitch, versionId: "version-2"),
        };
        var current = KnownObservation("observation-2", "state-a");

        var view = ResumeReport.Build(
            events, new Dictionary<string, string>(), current, Graph("version-2"),
            StateMatchResult.UniqueMatch, AllowedDecision);
        Assert.Equal("version-2", view.AdoptedVersionId);

        Assert.Throws<ArgumentException>(() => ResumeReport.Build(
            events, new Dictionary<string, string>(), current, Graph("version-1"),
            StateMatchResult.UniqueMatch, AllowedDecision));
    }
}
