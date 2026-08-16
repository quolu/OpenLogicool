using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostCompositionTests
{
    private static MappingProfileDocument Document(string profileId, string deviceKind) =>
        new(
            ContractSchemaVersions.Revision01,
            profileId,
            deviceKind,
            ProfileRevision: "rev-1",
            MappingRevision: "map-1",
            DefaultLayerId: "base",
            LayerIds: ["base"],
            LatchSelectors: [],
            HoldSelectors: [],
            Bindings: [new MappingBindingEntry("G9", "base", ["Key:F13"])]);

    [Fact]
    public void Selection_returns_one_profile_per_device_kind()
    {
        var selected = HostProfileSelection.SelectByDeviceKind(
            [Document("p-g13", "G13"), Document("p-g600", "G600")]);

        Assert.Equal("p-g13", selected["G13"].ProfileId);
        Assert.Equal("p-g600", selected["G600"].ProfileId);
    }

    [Fact]
    public void Selection_with_no_documents_is_empty()
    {
        Assert.Empty(HostProfileSelection.SelectByDeviceKind([]));
    }

    [Fact]
    public void Two_profiles_for_the_same_device_kind_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            HostProfileSelection.SelectByDeviceKind(
                [Document("p-1", "G600"), Document("p-2", "G600")]));
    }

    [Fact]
    public void Single_instance_guard_rejects_a_second_owner()
    {
        var name = $"Local\\OpenLogicool.HostTests.{Guid.NewGuid():N}";
        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.IsOwner);
        Assert.False(second.IsOwner);
    }
}
