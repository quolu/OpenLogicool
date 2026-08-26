using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostGameIndexRediscoveryTests
{
    [Fact]
    public void Failed_saved_action_expands_to_a_bounded_local_rediscovery_crop()
    {
        var region = HostGameIndexCommand.RediscoveryRegion([0.16, 0.81, 0.24, 0.05]);

        Assert.Equal([0d, 0.5, 1d, 0.5], region);
        Assert.True(region[0] <= 0.16);
        Assert.True(region[1] <= 0.81);
        Assert.True(region[0] + region[2] >= 0.40);
        Assert.True(region[1] + region[3] >= 0.86);
    }
}
