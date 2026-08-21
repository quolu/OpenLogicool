using OpenLogicool.Packaging;
using Xunit;

namespace OpenLogicool.Packaging.Tests;

public sealed class ActiveRunUpdateHoldTests
{
    [Fact]
    public void Active_run_holds_update_before_it_starts()
    {
        var decision = ActiveRunUpdateHold.DecideUpdateStart(hasActiveRun: true);

        Assert.Equal(UpdateStartDisposition.HeldForActiveRun, decision.Disposition);
        Assert.False(decision.CanStartUpdate);
    }

    [Fact]
    public void Update_can_start_only_without_an_active_run()
    {
        var decision = ActiveRunUpdateHold.DecideUpdateStart(hasActiveRun: false);

        Assert.Equal(UpdateStartDisposition.Allowed, decision.Disposition);
        Assert.True(decision.CanStartUpdate);
    }

    [Fact]
    public void Resume_is_compatible_only_with_the_exact_pinned_artifact_version()
    {
        var decision = ActiveRunUpdateHold.DecideResume("2.0.0", "2.0.0");

        Assert.Equal(RunResumeCompatibility.Compatible, decision.Compatibility);
        Assert.True(decision.CanResume);
    }

    [Fact]
    public void Resume_does_not_guess_compatibility_across_an_update()
    {
        var decision = ActiveRunUpdateHold.DecideResume("2.0.0", "2.0.1");

        Assert.Equal(RunResumeCompatibility.Incompatible, decision.Compatibility);
        Assert.False(decision.CanResume);
    }

    [Theory]
    [InlineData(null, "2.0.0")]
    [InlineData("2.0.0", "")]
    public void Resume_requires_both_artifact_versions(string? pinnedArtifactVersion, string? installedArtifactVersion)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ActiveRunUpdateHold.DecideResume(pinnedArtifactVersion!, installedArtifactVersion!));
    }
}
