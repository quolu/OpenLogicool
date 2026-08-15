using System.Text.Json;
using OpenLogicool.Desktop.SmokeApp;
using Xunit;

namespace OpenLogicool.Desktop.SmokeApp.Tests;

public class EnvironmentReportTests
{
    [Fact]
    public void Capture_ReturnsNonEmptyFrameworkDescription()
    {
        var report = EnvironmentReport.Capture();

        Assert.False(string.IsNullOrWhiteSpace(report.FrameworkDescription));
    }

    [Fact]
    public void Capture_RoundTripsThroughJson()
    {
        var report = EnvironmentReport.Capture();

        var json = JsonSerializer.Serialize(report);
        var deserialized = JsonSerializer.Deserialize<EnvironmentReport>(json);

        Assert.Equal(report, deserialized);
    }
}
