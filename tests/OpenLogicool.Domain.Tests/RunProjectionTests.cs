using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Domain.Tests;

public sealed class RunProjectionTests
{
    private static RunEvent Event(
        long sequence,
        string payloadType,
        string runId = "run-1",
        string playbookId = "playbook-1",
        string versionId = "playbook-version-1",
        long epoch = 1,
        string? attemptId = null,
        string? commandId = null,
        string? observationId = null) =>
        new(
            "0.1.0",
            $"{runId}-event-{sequence}",
            runId,
            sequence,
            playbookId,
            versionId,
            null,
            commandId,
            attemptId,
            "cause-1",
            $"correlation-{sequence}",
            epoch,
            RunEventActorType.Automation,
            new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 0, 0, 1, TimeSpan.Zero),
            observationId,
            payloadType,
            """{"body":"x"}""");

    [Fact]
    public void FromFirstEvent_pins_playbook_version_and_starts_at_sequence_one()
    {
        var projection = RunProjection.FromFirstEvent(
            Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"));

        Assert.Equal("run-1", projection.RunId);
        Assert.Equal("playbook-1", projection.PlaybookId);
        Assert.Equal("playbook-version-1", projection.PinnedPlaybookVersionId);
        Assert.Equal(1, projection.LastSequence);
        Assert.Equal("observation-1", projection.LastObservationId);
        Assert.Equal(1, projection.Tally.Observations);
    }

    [Fact]
    public void FromFirstEvent_rejects_a_start_that_is_not_sequence_one()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RunProjection.FromFirstEvent(Event(2, RunEventPayloadTypes.Proposal)));
    }

    [Fact]
    public void Apply_accumulates_sequence_epoch_tally_and_latest_observation()
    {
        var projection = RunProjection
            .FromFirstEvent(Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"))
            .Apply(Event(2, RunEventPayloadTypes.Proposal))
            .Apply(Event(3, RunEventPayloadTypes.Dispatch, epoch: 2, attemptId: "attempt-1", commandId: "command-1"))
            .Apply(Event(4, RunEventPayloadTypes.Observation, epoch: 2, observationId: "observation-2"));

        Assert.Equal(4, projection.LastSequence);
        Assert.Equal(2, projection.CurrentExecutorEpoch);
        Assert.Equal("run-1-event-4", projection.LastEventId);
        // 観測 event だけが LastObservationId を進める（confirmation 等の observationId 併記では動かさない）。
        Assert.Equal("observation-2", projection.LastObservationId);
        Assert.Equal(new RunEventTally(2, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0), projection.Tally);
    }

    [Fact]
    public void Apply_keeps_last_observation_when_a_confirmation_carries_an_observation_id()
    {
        var projection = RunProjection
            .FromFirstEvent(Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"))
            .Apply(Event(2, RunEventPayloadTypes.Confirmation, attemptId: "attempt-1", observationId: "observation-9"));

        Assert.Equal("observation-1", projection.LastObservationId);
    }

    [Fact]
    public void Apply_rejects_a_sequence_gap()
    {
        var projection = RunProjection.FromFirstEvent(Event(1, RunEventPayloadTypes.Proposal));

        Assert.Throws<InvalidOperationException>(() => projection.Apply(Event(3, RunEventPayloadTypes.Proposal)));
    }

    [Fact]
    public void Apply_rejects_a_stale_executor_epoch()
    {
        var projection = RunProjection
            .FromFirstEvent(Event(1, RunEventPayloadTypes.Proposal, epoch: 2));

        Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(Event(2, RunEventPayloadTypes.Proposal, epoch: 1)));
    }

    [Fact]
    public void Apply_rejects_an_event_from_another_run()
    {
        var projection = RunProjection.FromFirstEvent(Event(1, RunEventPayloadTypes.Proposal));

        Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(Event(2, RunEventPayloadTypes.Proposal, runId: "run-2")));
    }

    [Fact]
    public void Apply_rejects_a_playbook_version_change()
    {
        var projection = RunProjection.FromFirstEvent(Event(1, RunEventPayloadTypes.Proposal));

        // PB-002: pin された version と異なる version を運ぶ event は黙って採用しない。
        Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(Event(2, RunEventPayloadTypes.Proposal, versionId: "playbook-version-2")));
        Assert.Equal("playbook-version-1", projection.PinnedPlaybookVersionId);
    }

    [Fact]
    public void Apply_repins_on_an_explicit_version_switch()
    {
        var projection = RunProjection
            .FromFirstEvent(Event(1, RunEventPayloadTypes.Proposal))
            .Apply(Event(2, RunEventPayloadTypes.VersionSwitch, versionId: "playbook-version-2"));

        Assert.Equal("playbook-version-2", projection.PinnedPlaybookVersionId);
        Assert.Equal(1, projection.Tally.VersionSwitches);
    }

    [Fact]
    public void Apply_rejects_a_playbook_id_change()
    {
        var projection = RunProjection.FromFirstEvent(Event(1, RunEventPayloadTypes.Proposal));

        Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(Event(2, RunEventPayloadTypes.Proposal, playbookId: "playbook-2")));
    }

    [Fact]
    public void Tally_rejects_an_unknown_payload_type()
    {
        Assert.Throws<ArgumentException>(() => RunEventTally.Empty.Increment("telemetry"));
    }

    [Fact]
    public void Replay_of_the_same_events_equals_the_incrementally_built_projection()
    {
        var events = new[]
        {
            Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"),
            Event(2, RunEventPayloadTypes.Proposal),
            Event(3, RunEventPayloadTypes.Approval),
            Event(4, RunEventPayloadTypes.Dispatch, attemptId: "attempt-1", commandId: "command-1"),
            Event(5, RunEventPayloadTypes.DispatchResult, attemptId: "attempt-1"),
            Event(6, RunEventPayloadTypes.Confirmation, attemptId: "attempt-1", observationId: "observation-2"),
        };

        var incremental = events.Skip(1)
            .Aggregate(RunProjection.FromFirstEvent(events[0]), (projection, e) => projection.Apply(e));
        var replayed = RunProjection.Replay(events);

        // 値等価: 逐次適用と replay が同じ event 列から同じ projection 値になる（OPS-008）。
        Assert.Equal(incremental, replayed);
    }

    [Fact]
    public void Replay_rejects_an_empty_event_list()
    {
        Assert.Throws<InvalidOperationException>(() => RunProjection.Replay([]));
    }
}
