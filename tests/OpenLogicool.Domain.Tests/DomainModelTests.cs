using OpenLogicool.Contracts.Domain;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Domain.Tests;

public sealed class DomainModelTests
{
    [Fact]
    public void Catalog_looks_up_a_registered_stable_id_and_rejects_duplicates()
    {
        var action = Action("action.open-menu");
        var catalog = new SemanticActionCatalog(new[] { action });

        Assert.Same(action, catalog.Get("action.open-menu"));
        Assert.Throws<ArgumentException>(() => catalog.With(Action("action.open-menu")));
    }

    [Fact]
    public void Physical_up_releases_the_outputs_owned_at_down_after_profile_change()
    {
        var initial = PressOwnershipState.Create("profile-1", "base", "mapping-1");
        var down = initial.Down(Input(PhysicalInputEdge.Down, "G1"), new[] { "output-A", "output-B" });
        var changed = down.State.ChangeProfile("profile-2", "alternate", "mapping-2");

        var up = changed.Up(Input(PhysicalInputEdge.Up, "G1"));

        Assert.Equal(new[] { "output-A", "output-B" }, up.Release.Outputs);
        Assert.Equal("profile-1", down.Ownership.ProfileRevision);
        Assert.Equal("mapping-1", down.Ownership.MappingRevision);
    }

    [Fact]
    public void Profile_change_applies_a_new_generation_to_subsequent_down()
    {
        var initial = PressOwnershipState.Create("profile-1", "base", "mapping-1");
        var first = initial.Down(Input(PhysicalInputEdge.Down, "G1"), new[] { "output-A" });
        var changed = first.State.ChangeProfile("profile-2", "alternate", "mapping-2");

        var second = changed.Down(Input(PhysicalInputEdge.Down, "G2"), new[] { "output-B" });

        Assert.Equal(0, first.Ownership.Key.PressGeneration);
        Assert.Equal(1, second.Ownership.Key.PressGeneration);
        Assert.Equal("profile-2", second.Ownership.ProfileRevision);
    }

    [Fact]
    public void Physical_up_without_a_matching_down_is_rejected()
    {
        var state = PressOwnershipState.Create("profile-1", "base", "mapping-1");

        Assert.Throws<InvalidOperationException>(() => state.Up(Input(PhysicalInputEdge.Up, "G1")));
    }

    [Fact]
    public void Stop_releases_all_owned_outputs_and_rejects_future_down()
    {
        var first = PressOwnershipState.Create("profile-1", "base", "mapping-1")
            .Down(Input(PhysicalInputEdge.Down, "G1"), new[] { "output-A" });
        var second = first.State.Down(Input(PhysicalInputEdge.Down, "G2"), new[] { "output-B", "output-C" });

        var stopped = second.State.StopAndReleaseAll();

        Assert.False(stopped.State.AcceptsNewDowns);
        Assert.Equal(2, stopped.Releases.Count);
        Assert.Contains(stopped.Releases, release => release.Outputs.SequenceEqual(new[] { "output-A" }));
        Assert.Contains(stopped.Releases, release => release.Outputs.SequenceEqual(new[] { "output-B", "output-C" }));
        Assert.Throws<InvalidOperationException>(() => stopped.State.Down(Input(PhysicalInputEdge.Down, "G3"), new[] { "output-D" }));
    }

    [Fact]
    public void Run_event_sequence_rejects_gaps_and_regressions()
    {
        var sequence = new RunEventSequenceModel().Append(Event(runSequence: 1, executorEpoch: 1));

        Assert.Throws<InvalidOperationException>(() => sequence.Append(Event(runSequence: 3, executorEpoch: 1)));
        Assert.Throws<InvalidOperationException>(() => sequence.Append(Event(runSequence: 1, executorEpoch: 1)));
    }

    [Fact]
    public void Run_event_sequence_rejects_a_stale_executor_epoch()
    {
        var sequence = new RunEventSequenceModel().Append(Event(runSequence: 1, executorEpoch: 2));

        Assert.Throws<InvalidOperationException>(() => sequence.Append(Event(runSequence: 2, executorEpoch: 1)));
    }

    private static SemanticAction Action(string actionId) =>
        new("0.1.0", actionId, "Open menu", RiskClass.Low, "{}");

    private static PhysicalInput Input(PhysicalInputEdge edge, string controlId) =>
        new("0.1.0", "device-G13", controlId, edge, 0.0, 0);

    private static RunEvent Event(long runSequence, long executorEpoch) =>
        new(
            "0.1.0",
            $"event-{runSequence}-{executorEpoch}",
            "run-1",
            runSequence,
            "playbook-1",
            "playbook-version-1",
            null,
            null,
            null,
            "cause-1",
            "correlation-1",
            executorEpoch,
            RunEventActorType.System,
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            null,
            "test",
            "{}");
}
