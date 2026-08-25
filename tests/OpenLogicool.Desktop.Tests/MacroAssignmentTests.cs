using OpenLogicool.Contracts.Playbooks;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class MacroAssignmentTests
{
    [Theory]
    [InlineData(MacroPlaybackMode.AiMonitored)]
    [InlineData(MacroPlaybackMode.AiFree)]
    public void Assignment_tracks_latest_revision_and_preserves_playback_mode(MacroPlaybackMode mode)
    {
        var macro = new MacroCatalogItem(
            "route:daily", "route:daily:v7", "game", "env", "日課", 7, 4, "保存済み");

        var parsed = MacroInvocationTokens.Parse(MacroAssignment.CreateToken(macro, mode));

        Assert.Equal("route:daily", parsed.RouteId);
        Assert.Null(parsed.VersionId);
        Assert.Equal(mode, parsed.PlaybackMode);
    }
}
