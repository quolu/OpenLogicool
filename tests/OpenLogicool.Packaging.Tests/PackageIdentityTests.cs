using OpenLogicool.Packaging;
using Xunit;

namespace OpenLogicool.Packaging.Tests;

public sealed class PackageIdentityTests
{
    [Fact]
    public void Development_layout_is_unpackaged_without_claiming_a_public_packaging_choice()
    {
        var identity = PackageIdentities.CurrentDevelopment();

        Assert.Equal("OpenLogicool", identity.ProductName);
        Assert.Equal(PackagingFormat.Unpackaged, identity.DevelopmentFormat);
        Assert.Equal(PackagingEvidence.Unverified, identity.PublicPackagingEvidence);
        Assert.Contains("MSIX", identity.PublicPackagingDecision);
        Assert.Contains("未決定", identity.PublicPackagingDecision);
        Assert.Equal(
            ["OpenLogicool.Host.exe", "OpenLogicool.Watchdog.exe", "OpenLogicool.Host.dll と runtime dependencies"],
            identity.DevelopmentLayout.ApplicationFiles);
    }

    [Fact]
    public void Autostart_and_update_remain_unverified_and_never_start_device_write()
    {
        var identity = PackageIdentities.CurrentDevelopment();

        Assert.Equal(PackagingEvidence.Unverified, identity.AutostartEvidence);
        Assert.Equal(PackagingEvidence.Unverified, identity.UpdateManifestEvidence);
        Assert.False(identity.DevelopmentLayout.AutostartConfigured);
        Assert.False(identity.DevelopmentLayout.UpdateManifestPresent);
        Assert.False(identity.DevelopmentLayout.StartsDeviceWriteDuringInstallOrUpdate);
    }
}
