using System.Xml;
using OpenLogicool.Profiles;
using Xunit;

namespace OpenLogicool.Profiles.Tests;

public sealed class LgsXmlDryRunTests
{
    [Fact]
    public void Fixture_splits_convertible_and_unsupported_without_importing_defaults_or_commands()
    {
        var report = LgsXmlDryRun.Analyze(File.ReadAllText(FixturePath()));

        var convertible = Assert.Single(report.Convertible);
        Assert.Equal(("Pilot", "G1"), (convertible.ProfileName, convertible.Source));
        Assert.Equal(LgsXmlDryRunLineKind.Convertible, convertible.Kind);

        Assert.Equal(3, report.Unsupported.Count);
        Assert.Contains(report.Unsupported, line => line.Source == "target" && line.Summary.Contains("path"));
        Assert.Contains(report.Unsupported, line => line.Source == "G2" && line.Summary.Contains("script"));
        Assert.Contains(report.Unsupported, line => line.Source == "G3" && line.Summary.Contains("original=true"));
        Assert.DoesNotContain(report.Unsupported, line => line.Summary.Contains("C:\\Games") || line.Summary.Contains("os.execute"));
    }

    [Fact]
    public void External_entities_are_rejected_before_dry_run()
    {
        const string xml = "<!DOCTYPE profiles [<!ENTITY outside SYSTEM 'file:///not-trusted'>]><profiles><profile name='P'>&outside;</profile></profiles>";

        Assert.Throws<XmlException>(() => LgsXmlDryRun.Analyze(xml));
    }

    private static string FixturePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "fixtures", "lgs", "profile-dry-run.v1.xml");
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("LGS dry-run fixture が見つかりません。");
    }
}
