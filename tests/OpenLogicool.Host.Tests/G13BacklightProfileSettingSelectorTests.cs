using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class G13BacklightProfileSettingSelectorTests
{
    [Fact]
    public void Explicit_app_match_decides_by_the_workspace_even_when_the_match_is_on_g600()
    {
        var defaultG13 = Profile("common-G13", "G13", followsAudio: false);
        var gameG600 = Profile("game-G600", "G600", followsAudio: true);
        var documents = Documents(defaultG13, gameG600);

        Assert.True(G13BacklightProfileSettingSelector.FollowsAudio(
            Decision(new ProfileSwitchKindOutcome("G600", "path", gameG600.ProfileId, defaultG13.ProfileId, true)),
            documents,
            defaultG13));
    }

    [Fact]
    public void A_matched_workspace_that_is_off_stays_off_even_when_the_common_setting_is_on()
    {
        var defaultG13 = Profile("common-G13", "G13", followsAudio: true);
        var game = Profile("game-G13", "G13", followsAudio: false);
        var documents = Documents(defaultG13, game);

        Assert.False(G13BacklightProfileSettingSelector.FollowsAudio(
            Decision(new ProfileSwitchKindOutcome("G13", "package", game.ProfileId, defaultG13.ProfileId, true)),
            documents,
            defaultG13));
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("default", false)]
    [InlineData("identity-unavailable", true)]
    [InlineData("identity-unavailable", false)]
    public void Without_an_app_match_the_common_setting_decides(string matchKind, bool commonFollowsAudio)
    {
        var defaultG13 = Profile("common-G13", "G13", commonFollowsAudio);
        var documents = Documents(defaultG13);

        Assert.Equal(commonFollowsAudio, G13BacklightProfileSettingSelector.FollowsAudio(
            Decision(new ProfileSwitchKindOutcome("G13", matchKind, defaultG13.ProfileId, null, false)),
            documents,
            defaultG13));
    }

    [Fact]
    public void Without_any_g13_setting_the_backlight_is_left_alone()
    {
        Assert.False(G13BacklightProfileSettingSelector.FollowsAudio(Decision(), Documents(), null));
    }

    private static Dictionary<string, MappingProfileDocument> Documents(params MappingProfileDocument[] documents) =>
        documents.ToDictionary(document => document.ProfileId, StringComparer.Ordinal);

    private static MappingProfileDocument Profile(string profileId, string deviceKind, bool followsAudio) =>
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
            G13BacklightFollowsAudio: followsAudio);

    private static ProfileSwitchDecision Decision(params ProfileSwitchKindOutcome[] outcomes) =>
        new(1, null, null, null, null, outcomes, false);
}
