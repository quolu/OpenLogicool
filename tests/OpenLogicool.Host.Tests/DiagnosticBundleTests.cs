using System.IO;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class DiagnosticBundleTests : IDisposable
{
    private readonly string _directoryPath = Path.Combine(Path.GetTempPath(), $"openlogicool-diagnostic-{Guid.NewGuid():N}");

    [Fact]
    public void Preview_is_a_fixed_manifest_without_collected_screen_secret_or_personal_data()
    {
        var preview = DiagnosticBundle.Preview(_directoryPath, new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(Path.Combine(_directoryPath, "openlogicool-diagnostic-20260821-000000.json"), preview.BundlePath);
        Assert.Contains("OpenLogicool", preview.ManifestJson);
        Assert.Contains("bundle schema", preview.ManifestJson);
        Assert.Contains("screen", preview.ManifestJson);
        Assert.Contains("personal data", preview.ManifestJson);
        Assert.DoesNotContain("DeviceInstanceId", preview.ManifestJson);
        Assert.DoesNotContain("prompt text", preview.ManifestJson);
        Assert.False(Directory.Exists(_directoryPath));
    }

    [Fact]
    public void Create_writes_previewed_bundle_and_delete_removes_only_that_file()
    {
        var preview = DiagnosticBundle.Preview(_directoryPath, new DateTimeOffset(2026, 8, 21, 1, 2, 3, TimeSpan.Zero));

        DiagnosticBundle.Create(preview);

        Assert.True(File.Exists(preview.BundlePath));
        Assert.Equal(preview.ManifestJson, File.ReadAllText(preview.BundlePath));
        Assert.Single(Directory.GetFiles(_directoryPath));

        DiagnosticBundle.Delete(preview);

        Assert.False(File.Exists(preview.BundlePath));
        Assert.Empty(Directory.GetFiles(_directoryPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
