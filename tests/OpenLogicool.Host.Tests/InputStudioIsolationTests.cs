using OpenLogicool.Host;

namespace OpenLogicool.Host.Tests;

public sealed class InputStudioIsolationTests
{
    [Theory]
    [InlineData(GameOperatorDependency.Ai)]
    [InlineData(GameOperatorDependency.Network)]
    [InlineData(GameOperatorDependency.Capture)]
    public void Each_external_dependency_fault_preserves_every_input_studio_operation(
        GameOperatorDependency failedDependency)
    {
        var status = InputStudioIsolation.Assess([failedDependency]);

        Assert.True(status.IsGameOperatorDegraded);
        Assert.Equal([failedDependency], status.FailedDependencies);
        Assert.Equal(
            [
                InputStudioOperation.EditMappings,
                InputStudioOperation.SaveProfiles,
                InputStudioOperation.RunMappings,
            ],
            status.AvailableOperations);
        Assert.All(status.AvailableOperations, operation => Assert.True(status.CanUse(operation)));
    }

    [Fact]
    public void Simultaneous_external_faults_remain_isolated_and_are_not_hidden()
    {
        var status = InputStudioIsolation.Assess(
        [
            GameOperatorDependency.Capture,
            GameOperatorDependency.Ai,
            GameOperatorDependency.Network,
            GameOperatorDependency.Ai,
        ]);

        Assert.True(status.IsGameOperatorDegraded);
        Assert.Equal(
            [
                GameOperatorDependency.Ai,
                GameOperatorDependency.Network,
                GameOperatorDependency.Capture,
            ],
            status.FailedDependencies);
        Assert.True(status.CanUse(InputStudioOperation.EditMappings));
        Assert.True(status.CanUse(InputStudioOperation.SaveProfiles));
        Assert.True(status.CanUse(InputStudioOperation.RunMappings));
    }

    [Fact]
    public void Unknown_dependency_is_rejected_instead_of_being_silently_isolated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InputStudioIsolation.Assess([(GameOperatorDependency)999]));
    }
}
