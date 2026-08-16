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

    private static AppProfileAssociation Association(string path, string deviceKind, string profileId) =>
        new(ContractSchemaVersions.Revision01, path, deviceKind, profileId);

    [Fact]
    public void Single_profile_per_kind_is_the_default_without_associations()
    {
        var resolver = AppProfileResolver.Build(
            [Document("p-g13", "G13"), Document("p-g600", "G600")],
            []);

        Assert.Equal("p-g13", resolver.DefaultByKind["G13"].ProfileId);
        Assert.Equal("p-g600", resolver.DefaultByKind["G600"].ProfileId);
        Assert.False(resolver.HasAppAssociations);
    }

    [Fact]
    public void No_documents_resolve_to_nothing()
    {
        var resolver = AppProfileResolver.Build([], []);

        Assert.Empty(resolver.DefaultByKind);
        Assert.Null(resolver.Resolve("G600", @"c:\game\game.exe"));
    }

    [Fact]
    public void Foreground_app_match_overrides_default_and_is_case_insensitive()
    {
        var resolver = AppProfileResolver.Build(
            [Document("p-main", "G600"), Document("p-game", "G600")],
            [
                Association("*", "G600", "p-main"),
                Association(@"c:\game\game.exe", "G600", "p-game"),
            ]);

        Assert.Equal("p-game", resolver.Resolve("G600", @"C:\Game\GAME.EXE")!.ProfileId);
        Assert.Equal("p-main", resolver.Resolve("G600", @"c:\other\other.exe")!.ProfileId);
        Assert.Equal("p-main", resolver.Resolve("G600", null)!.ProfileId);
    }

    [Fact]
    public void Multiple_profiles_without_default_association_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AppProfileResolver.Build(
                [Document("p-1", "G600"), Document("p-2", "G600")],
                []));
    }

    [Fact]
    public void Association_to_unknown_profile_or_wrong_kind_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AppProfileResolver.Build(
                [Document("p-1", "G600")],
                [Association(@"c:\game\game.exe", "G600", "p-missing")]));

        Assert.Throws<InvalidOperationException>(() =>
            AppProfileResolver.Build(
                [Document("p-1", "G600")],
                [Association(@"c:\game\game.exe", "G13", "p-1")]));
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
