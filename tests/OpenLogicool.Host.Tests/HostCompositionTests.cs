using System.IO;
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

    private static AppProfileAssociation Association(
        string path, string deviceKind, string profileId, string matcherKind = AppMatcherKind.Path) =>
        new(ContractSchemaVersions.Revision01, path, deviceKind, profileId, matcherKind);

    private static ForegroundApplicationIdentity PathIdentity(string path) =>
        new(AppProfileResolver.NormalizePath(path), null, ProcessId: 0, ProcessStartTimeUtc: null);

    private static ForegroundApplicationIdentity PackageIdentity(string packageFamilyName, string? path = null) =>
        new(path is null ? null : AppProfileResolver.NormalizePath(path), packageFamilyName, ProcessId: 0, ProcessStartTimeUtc: null);

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
        Assert.Null(resolver.Resolve("G600", PathIdentity(@"c:\game\game.exe")));
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

        Assert.Equal("p-game", resolver.Resolve("G600", PathIdentity(@"C:\Game\GAME.EXE"))!.ProfileId);
        Assert.Equal("p-main", resolver.Resolve("G600", PathIdentity(@"c:\other\other.exe"))!.ProfileId);
        Assert.Equal("p-main", resolver.Resolve("G600", null)!.ProfileId);
    }

    [Fact]
    public void Package_match_takes_priority_over_path_match_and_is_case_insensitive()
    {
        var resolver = AppProfileResolver.Build(
            [Document("p-main", "G600"), Document("p-path", "G600"), Document("p-pkg", "G600")],
            [
                Association("*", "G600", "p-main"),
                Association(@"c:\game\game.exe", "G600", "p-path"),
                Association("chrome_8wekyb3d8bbwe", "G600", "p-pkg", AppMatcherKind.Package),
            ]);

        // path・package 両方一致する identity では package matcher を優先する
        Assert.Equal(
            "p-pkg",
            resolver.Resolve("G600", PackageIdentity("Chrome_8WeKyb3d8bbwe", @"c:\game\game.exe"))!.ProfileId);
        // package 不一致・path 一致は従来どおり path matcher で解決する
        Assert.Equal("p-path", resolver.Resolve("G600", PathIdentity(@"c:\game\game.exe"))!.ProfileId);
        // 未知 package・未知 path は既定へ落ちる
        Assert.Equal("p-main", resolver.Resolve("G600", PackageIdentity("other_pkg"))!.ProfileId);
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
    public void Workspace_catalog_leads_with_default_and_groups_per_app()
    {
        var associations = new[]
        {
            Association("*", "G600", "p-main"),
            Association(@"C:\Game\Game.exe", "G600", "p-game"),
            Association(@"c:\game\game.exe", "G13", "p-g13-game"),
        };
        var resolver = AppProfileResolver.Build(
            [Document("p-main", "G600"), Document("p-game", "G600"), Document("p-g13-game", "G13")],
            associations);

        var workspaces = ApplicationWorkspaceCatalog.Build(resolver, associations);

        Assert.Equal(2, workspaces.Count);
        Assert.Equal(AppProfileResolver.DefaultMarker, workspaces[0].ApplicationFullPath);
        // 既定行は resolver の既定（G13 は単一 profile 互換）を反映する
        Assert.Equal("p-main", workspaces[0].ProfileIdByKind["G600"]);
        Assert.Equal("p-g13-game", workspaces[0].ProfileIdByKind["G13"]);
        // 大文字小文字違いの path は同じ workspace 行へまとまる
        Assert.Equal(@"c:\game\game.exe", workspaces[1].ApplicationFullPath);
        Assert.Equal("p-game", workspaces[1].ProfileIdByKind["G600"]);
        Assert.Equal("p-g13-game", workspaces[1].ProfileIdByKind["G13"]);
    }

    [Fact]
    public void Workspace_catalog_without_profiles_has_only_an_empty_default()
    {
        var workspaces = ApplicationWorkspaceCatalog.Build(AppProfileResolver.Build([], []), []);

        var row = Assert.Single(workspaces);
        Assert.Equal(AppProfileResolver.DefaultMarker, row.ApplicationFullPath);
        Assert.Empty(row.ProfileIdByKind);
    }

    [Fact]
    public void Running_application_catalog_returns_deduped_full_paths()
    {
        var running = RunningApplicationCatalog.ListVisibleApplications();

        Assert.Equal(
            running.Select(app => AppProfileResolver.NormalizePath(app.FullPath)).Distinct().Count(),
            running.Count);
        Assert.All(running, app => Assert.True(Path.IsPathRooted(app.FullPath)));
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
