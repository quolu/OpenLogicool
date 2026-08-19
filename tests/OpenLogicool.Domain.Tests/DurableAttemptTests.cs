using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Domain.Tests;

public sealed class DurableAttemptTests
{
    private static DurableAttempt Armed() =>
        DurableAttempt.Propose("attempt-1")
            .TransitionTo(AttemptState.Authorized)
            .TransitionTo(AttemptState.Prepared)
            .TransitionTo(AttemptState.DispatchArmed);

    [Fact]
    public void Happy_path_reaches_confirmed_with_its_observation()
    {
        var confirmed = Armed()
            .TransitionTo(AttemptState.DispatchReported)
            .TransitionTo(AttemptState.Observing, "observation-1")
            .TransitionTo(AttemptState.Confirmed, "observation-1");

        Assert.Equal(AttemptState.Confirmed, confirmed.State);
        Assert.Equal("observation-1", confirmed.ObservationId);
        Assert.True(confirmed.IsTerminal);
    }

    [Fact]
    public void Confirmed_requires_an_observation()
    {
        var observing = Armed()
            .TransitionTo(AttemptState.DispatchReported)
            .TransitionTo(AttemptState.Observing, "observation-1");

        Assert.Throws<InvalidOperationException>(() => observing.TransitionTo(AttemptState.Confirmed));
    }

    [Fact]
    public void Observing_requires_an_observation()
    {
        var reported = Armed().TransitionTo(AttemptState.DispatchReported);

        Assert.Throws<InvalidOperationException>(() => reported.TransitionTo(AttemptState.Observing));
    }

    [Fact]
    public void Confirmed_rejects_a_different_observation_than_observing()
    {
        var observing = Armed()
            .TransitionTo(AttemptState.DispatchReported)
            .TransitionTo(AttemptState.Observing, "observation-1");

        Assert.Throws<InvalidOperationException>(() => observing.TransitionTo(AttemptState.Confirmed, "observation-2"));
    }

    [Fact]
    public void Observation_id_is_accepted_only_on_observing_and_confirmed()
    {
        var proposed = DurableAttempt.Propose("attempt-1");

        Assert.Throws<ArgumentException>(() => proposed.TransitionTo(AttemptState.Authorized, "observation-1"));
    }

    [Theory]
    [InlineData(AttemptState.Confirmed)]
    [InlineData(AttemptState.Rejected)]
    [InlineData(AttemptState.Cancelled)]
    [InlineData(AttemptState.Disarmed)]
    [InlineData(AttemptState.UserResolvedSuccess)]
    [InlineData(AttemptState.UserResolvedFailure)]
    [InlineData(AttemptState.Abandoned)]
    public void Terminal_states_allow_no_transition(AttemptState terminal)
    {
        var attempt = DurableAttempt.Restore("attempt-1", terminal,
            terminal == AttemptState.Confirmed ? "observation-1" : null);

        Assert.True(attempt.IsTerminal);
        foreach (var next in Enum.GetValues<AttemptState>())
        {
            Assert.ThrowsAny<Exception>(() => attempt.TransitionTo(next,
                next == AttemptState.Confirmed ? "observation-1" : null));
        }
    }

    [Fact]
    public void Skipping_states_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => DurableAttempt.Propose("attempt-1").TransitionTo(AttemptState.DispatchArmed));
        Assert.Throws<InvalidOperationException>(
            () => Armed().TransitionTo(AttemptState.Confirmed, "observation-1"));
    }

    [Fact]
    public void Unknown_outcome_resolves_through_reconciling_and_user_decision()
    {
        var reconciled = Armed()
            .TransitionTo(AttemptState.OutcomeUnknown)
            .TransitionTo(AttemptState.Reconciling)
            .TransitionTo(AttemptState.NeedsUserDecision)
            .TransitionTo(AttemptState.UserResolvedFailure);

        Assert.True(reconciled.IsTerminal);
        // 利用者判断の終端は Observation を持たない（学習昇格の根拠にしない——§6.7）。
        Assert.Null(reconciled.ObservationId);
    }

    [Fact]
    public void Disarmed_is_reachable_only_from_dispatch_armed()
    {
        Assert.Equal(AttemptState.Disarmed, Armed().TransitionTo(AttemptState.Disarmed).State);
        Assert.Throws<InvalidOperationException>(
            () => DurableAttempt.Propose("attempt-1").TransitionTo(AttemptState.Disarmed));
    }

    [Theory]
    [InlineData(AttemptState.Proposed, AttemptState.Cancelled)]
    [InlineData(AttemptState.Authorized, AttemptState.Cancelled)]
    [InlineData(AttemptState.Prepared, AttemptState.Cancelled)]
    [InlineData(AttemptState.DispatchArmed, AttemptState.OutcomeUnknown)]
    [InlineData(AttemptState.DispatchReported, AttemptState.OutcomeUnknown)]
    [InlineData(AttemptState.Observing, AttemptState.OutcomeUnknown)]
    [InlineData(AttemptState.OutcomeUnknown, AttemptState.OutcomeUnknown)]
    [InlineData(AttemptState.Reconciling, AttemptState.OutcomeUnknown)]
    [InlineData(AttemptState.NeedsUserDecision, AttemptState.OutcomeUnknown)]
    [InlineData(AttemptState.Confirmed, AttemptState.Confirmed)]
    [InlineData(AttemptState.Cancelled, AttemptState.Cancelled)]
    [InlineData(AttemptState.Disarmed, AttemptState.Disarmed)]
    public void Recovery_classification_follows_contract_two(AttemptState atCrash, AttemptState expected)
    {
        // §6.7 契約2: DispatchArmed 以降の未解決は実際に未送信でも OutcomeUnknown、Prepared 以前は Cancelled。
        Assert.Equal(expected, DurableAttempt.RecoveryStateFor(atCrash));
    }

    [Fact]
    public void Restore_of_confirmed_requires_the_observation()
    {
        Assert.Throws<ArgumentException>(
            () => DurableAttempt.Restore("attempt-1", AttemptState.Confirmed, observationId: null));
    }

    [Theory]
    [InlineData(AttemptState.Proposed, false)]
    [InlineData(AttemptState.Prepared, false)]
    [InlineData(AttemptState.DispatchArmed, true)]
    [InlineData(AttemptState.Observing, true)]
    [InlineData(AttemptState.OutcomeUnknown, true)]
    [InlineData(AttemptState.Confirmed, false)]
    [InlineData(AttemptState.Disarmed, false)]
    public void Unresolved_after_arm_tracks_dispatchable_exposure(AttemptState state, bool expected)
    {
        var attempt = DurableAttempt.Restore("attempt-1", state,
            state == AttemptState.Confirmed ? "observation-1" : null);

        Assert.Equal(expected, attempt.IsUnresolvedAfterArm);
    }
}
