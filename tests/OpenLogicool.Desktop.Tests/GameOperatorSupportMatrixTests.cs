using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class GameOperatorSupportMatrixTests
{
    [Fact]
    public void Public_claim_does_not_claim_verified_autonomous_playbook()
    {
        Assert.Equal("Game Operator Preview", GameOperatorSupportMatrix.PublicClaim);
        Assert.DoesNotContain("Verified", GameOperatorSupportMatrix.PublicClaim, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Only_confirmed_capabilities_are_supported()
    {
        Assert.Equal(
            [
                "GameLab での crash boundary、停止、修正、再開を含む Durable Automation",
                "AI proposal の schema、catalog、state、risk の dispatch 前拒否",
                "Data Flow contract による frame、OCR、journal、evidence crop の保存・送信境界",
                "game ごとの policy record による Assist／Auto の gate",
            ],
            GameOperatorSupportMatrix.SupportedEntries.Select(entry => entry.Capability));
    }

    [Fact]
    public void Provider_and_real_game_rows_remain_unverified()
    {
        var provider = Assert.Single(GameOperatorSupportMatrix.Entries, entry => entry.Capability.Contains("provider"));
        var live = Assert.Single(GameOperatorSupportMatrix.Entries, entry => entry.Capability.Contains("Observe Only"));
        var verified = Assert.Single(GameOperatorSupportMatrix.Entries, entry => entry.Capability.Contains("Verified Autonomous"));

        Assert.Equal(GameOperatorSupportStatus.Unverified, provider.Status);
        Assert.Equal(GameOperatorSupportStatus.Unverified, live.Status);
        Assert.Equal(GameOperatorSupportStatus.Unverified, verified.Status);
        Assert.Contains("未選定", provider.Evidence);
    }

    [Fact]
    public void Data_flow_and_policy_boundaries_are_publicly_stated()
    {
        var dataFlow = Assert.Single(GameOperatorSupportMatrix.Entries, entry => entry.Capability.StartsWith("Data Flow"));
        var policy = Assert.Single(GameOperatorSupportMatrix.Entries, entry => entry.Capability.StartsWith("game ごと"));

        Assert.Contains("既定 OFF", dataFlow.Detail);
        Assert.Contains("Unverified", policy.Detail);
        Assert.Contains("解釈や許可を意味しない", policy.Detail);
    }
}
