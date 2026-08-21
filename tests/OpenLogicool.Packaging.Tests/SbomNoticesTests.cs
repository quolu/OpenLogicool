using System.Text;
using OpenLogicool.Packaging;
using Xunit;

namespace OpenLogicool.Packaging.Tests;

public sealed class SbomNoticesTests
{
    [Fact]
    public void Current_development_bundle_carries_known_components_notices_and_unsigned_hashes()
    {
        var bundle = SbomNotices.CreateForCurrentDevelopment(
            [new ArtifactInput("OpenLogicool.Host.exe", Encoding.UTF8.GetBytes("abc"))]);

        Assert.Equal(PackagingEvidence.Unverified, bundle.PackageIdentity.PublicPackagingEvidence);
        Assert.Contains(bundle.Components, component => component.Name == "Microsoft.Data.Sqlite" && component.License == "MIT");
        Assert.Contains(bundle.Components, component => component.Name == "SQLitePCLRaw.lib.e_sqlite3" && component.License == "Apache-2.0");
        Assert.Equal(["sbom.json", "THIRD-PARTY-NOTICES.md"], bundle.IncludedFiles);
        Assert.False(bundle.SignatureCreated);
        Assert.Equal(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            Assert.Single(bundle.ArtifactHashes).Sha256);
    }

    [Fact]
    public void Duplicate_artifact_file_names_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => SbomNotices.Create(
            PackageIdentities.CurrentDevelopment(),
            [new SbomComponent("component", "1.0", "MIT", "source")],
            [
                new ArtifactInput("same.dll", Array.Empty<byte>()),
                new ArtifactInput("same.dll", Array.Empty<byte>()),
            ]));
    }

    [Fact]
    public void Components_without_license_or_notice_source_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => SbomNotices.Create(
            PackageIdentities.CurrentDevelopment(),
            [new SbomComponent("component", "1.0", string.Empty, "source")],
            []));
    }
}
