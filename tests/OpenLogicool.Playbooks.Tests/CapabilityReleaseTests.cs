using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class CapabilityReleaseTests
{
    private static readonly CapabilityReleaseSettings AllEnabled = new(true, true, true, true);

    [Fact]
    public void Every_capability_requires_its_release_setting()
    {
        var settings = AllEnabled with { TeachEnabled = false };

        var decision = CapabilityRelease.Evaluate(settings, GameOperatorCapability.Teach, Policy(GamePolicyReviewStatus.Confirmed));

        Assert.False(decision.IsReleased);
        Assert.Equal(CapabilityReleaseGateReason.DisabledByRelease, decision.Reason);
    }

    [Fact]
    public void Observe_and_supervised_require_their_corresponding_game_policy_mode()
    {
        var observeDenied = CapabilityRelease.Evaluate(
            AllEnabled,
            GameOperatorCapability.ObserveOnly,
            Policy(GamePolicyReviewStatus.Confirmed, [GameAutomationMode.Assist]));
        var supervisedDenied = CapabilityRelease.Evaluate(
            AllEnabled,
            GameOperatorCapability.Supervised,
            Policy(GamePolicyReviewStatus.Unverified, [GameAutomationMode.Observe]));

        Assert.Equal(CapabilityReleaseGateReason.GamePolicyDenied, observeDenied.Reason);
        Assert.Equal(CapabilityReleaseGateReason.GamePolicyDenied, supervisedDenied.Reason);
    }

    [Fact]
    public void Verification_scope_is_claim_metadata_and_does_not_block_an_enabled_auto_mode()
    {
        var policy = Policy(GamePolicyReviewStatus.Confirmed);
        var decision = CapabilityRelease.Evaluate(AllEnabled, GameOperatorCapability.Verified, policy);

        Assert.True(decision.IsReleased);
        Assert.Equal(CapabilityReleaseGateReason.Released, decision.Reason);
    }

    private static GamePolicyRecord Policy(
        GamePolicyReviewStatus reviewStatus,
        IReadOnlyList<GameAutomationMode>? allowedModes = null) => new(
        ContractSchemaVersions.Revision01,
        "gamelab:scenario-01",
        reviewStatus,
        allowedModes ?? [GameAutomationMode.Observe, GameAutomationMode.Assist, GameAutomationMode.Auto]);
}
