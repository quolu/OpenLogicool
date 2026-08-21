using OpenLogicool.Packaging;
using Xunit;

namespace OpenLogicool.Packaging.Tests;

public sealed class InstallLifecycleTests
{
    [Fact]
    public void Every_lifecycle_operation_does_not_start_device_write()
    {
        var steps = InstallLifecycle.DefaultSteps();

        Assert.Equal(5, steps.Count);
        Assert.All(steps, step => Assert.False(step.StartsDeviceWrite));
    }

    [Fact]
    public void Rollback_and_uninstall_require_existing_leftover_restore_route()
    {
        var steps = InstallLifecycle.DefaultSteps();

        Assert.True(Assert.Single(steps, step => step.Action == InstallLifecycleAction.Rollback).RequiresLeftoverRestore);
        Assert.True(Assert.Single(steps, step => step.Action == InstallLifecycleAction.Uninstall).RequiresLeftoverRestore);
        Assert.All(
            steps.Where(step => step.Action is not (InstallLifecycleAction.Rollback or InstallLifecycleAction.Uninstall)),
            step => Assert.False(step.RequiresLeftoverRestore));
    }
}
