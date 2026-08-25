using OpenLogicool.Contracts.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class MacroInvocationTokensTests
{
    [Theory]
    [InlineData(MacroPlaybackMode.AiFree)]
    [InlineData(MacroPlaybackMode.AiMonitored)]
    public void Token_roundtrips_route_version_and_mode(MacroPlaybackMode mode)
    {
        var expected = new MacroVersionReference("purpose:route/日本語", "route:version+1", mode);

        var token = MacroInvocationTokens.Create(expected);

        Assert.StartsWith(MacroInvocationTokens.Prefix, token, StringComparison.Ordinal);
        Assert.Equal(expected, MacroInvocationTokens.Parse(token));
    }

    [Theory]
    [InlineData(MacroPlaybackMode.AiFree)]
    [InlineData(MacroPlaybackMode.AiMonitored)]
    public void Latest_reference_roundtrips_without_pinning_an_old_version(MacroPlaybackMode mode)
    {
        var expected = new MacroVersionReference("purpose:route", null, mode);
        Assert.Equal(expected, MacroInvocationTokens.Parse(MacroInvocationTokens.Create(expected)));
    }

    [Theory]
    [InlineData("Macro:")]
    [InlineData("Macro:free:not-base64:also:not")]
    [InlineData("Macro:unknown:cm91dGU:dmVyc2lvbg")]
    [InlineData("Key:A")]
    public void Invalid_token_is_rejected(string token)
    {
        Assert.ThrowsAny<ArgumentException>(() => MacroInvocationTokens.Parse(token));
    }
}
