using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class InputStudioSupportMatrixTests
{
    [Fact]
    public void Public_claim_is_partial_replacement_not_lgs_parity()
    {
        Assert.Equal("Partial LGS Replacement", InputStudioSupportMatrix.PublicClaim);
        Assert.DoesNotContain("Parity", InputStudioSupportMatrix.PublicClaim, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Only_confirmed_rows_are_supported()
    {
        Assert.Equal(
            [
                "Windows 11 build 26200 / x64 での G13・G600 入力と profile 適用",
                "G600 side button の legacy 無害化",
                "G600 onboard slot の切替と退避",
            ],
            InputStudioSupportMatrix.SupportedEntries.Select(entry => entry.Capability));

        Assert.All(
            InputStudioSupportMatrix.SupportedEntries,
            entry => Assert.Contains("実機", entry.Evidence));
    }

    [Fact]
    public void G600_route_and_three_slot_constraint_are_publicly_stated()
    {
        var remap = Assert.Single(
            InputStudioSupportMatrix.Entries,
            entry => entry.Capability.Contains("legacy 無害化"));
        Assert.Equal(InputStudioSupportStatus.Supported, remap.Status);
        Assert.Contains("B変種", remap.Detail);
        Assert.Contains("F13〜F24", remap.Detail);

        var slots = Assert.Single(
            InputStudioSupportMatrix.Entries,
            entry => entry.Capability.Contains("slot"));
        Assert.Contains("3 slot", slots.Detail);
    }

    [Fact]
    public void F6_and_inventory_gaps_are_not_supported()
    {
        var f6 = Assert.Single(InputStudioSupportMatrix.Entries, entry => entry.Capability.Contains("F6"));
        var parity = Assert.Single(InputStudioSupportMatrix.Entries, entry => entry.Capability.Contains("全機能 parity"));

        Assert.Equal(InputStudioSupportStatus.Unsupported, f6.Status);
        Assert.Equal(InputStudioSupportStatus.Unverified, parity.Status);
    }
}
