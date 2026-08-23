using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class G13LcdProfileSettingSelectorTests
{
    [Fact]
    public void Explicit_app_match_uses_workspace_setting_even_when_the_match_is_on_g600()
    {
        var common = Setting("common");
        var game = Setting("game");
        var defaultG13 = Profile("common-G13", "G13", common);
        var gameG600 = Profile("game-G600", "G600", game);
        var decision = Decision(new ProfileSwitchKindOutcome("G600", "path", gameG600.ProfileId, defaultG13.ProfileId, true));

        var selected = G13LcdProfileSettingSelector.Select(
            decision,
            new Dictionary<string, MappingProfileDocument>(StringComparer.Ordinal)
            {
                [defaultG13.ProfileId] = defaultG13,
                [gameG600.ProfileId] = gameG600,
            },
            defaultG13);

        Assert.Equal(game, selected);
    }

    [Fact]
    public void Default_unknown_and_match_without_setting_use_common_setting()
    {
        var common = Setting("common");
        var defaultG13 = Profile("common-G13", "G13", common);
        var game = Profile("game-G13", "G13", null);
        var documents = new Dictionary<string, MappingProfileDocument>(StringComparer.Ordinal)
        {
            [defaultG13.ProfileId] = defaultG13,
            [game.ProfileId] = game,
        };

        Assert.Equal(common, G13LcdProfileSettingSelector.Select(
            Decision(new ProfileSwitchKindOutcome("G13", "default", defaultG13.ProfileId, null, true)),
            documents,
            defaultG13));
        Assert.Equal(common, G13LcdProfileSettingSelector.Select(
            Decision(new ProfileSwitchKindOutcome("G13", "identity-unavailable", defaultG13.ProfileId, null, false)),
            documents,
            defaultG13));
        Assert.Equal(common, G13LcdProfileSettingSelector.Select(
            Decision(new ProfileSwitchKindOutcome("G13", "package", game.ProfileId, defaultG13.ProfileId, true)),
            documents,
            defaultG13));
    }

    private static WorkspaceG13LcdSetting Setting(string name) =>
        new(WorkspaceG13LcdContentKind.Text, Convert.ToBase64String(new byte[960]), null, name);

    private static MappingProfileDocument Profile(
        string profileId,
        string deviceKind,
        WorkspaceG13LcdSetting? setting) =>
        new(
            ContractSchemaVersions.Revision01,
            profileId,
            deviceKind,
            "rev-1",
            "map-1",
            "base",
            ["base"],
            [],
            [],
            [],
            setting);

    private static ProfileSwitchDecision Decision(params ProfileSwitchKindOutcome[] outcomes) =>
        new(1, null, null, null, null, outcomes, false);
}
