using OpenLogicool.Input;
using OpenLogicool.Probe;
using Xunit;

namespace OpenLogicool.Probe.Tests;

public sealed class LiveDiscoveryNanoCoordinateSmokeTests
{
    [Theory]
    [InlineData("0.001", 0.001)]
    [InlineData("0.5", 0.5)]
    [InlineData("0.999", 0.999)]
    public void NormalizedCoordinateAcceptsOnlyFiniteOpenUnitInterval(string value, double expected)
    {
        Assert.Equal(expected, LiveDiscoveryNanoCoordinateSmoke.ParseNormalizedCoordinate(value, "--x"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("abc")]
    public void NormalizedCoordinateRejectsUnsafeValues(string value)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => LiveDiscoveryNanoCoordinateSmoke.ParseNormalizedCoordinate(value, "--x"));
    }

    [Fact]
    public void CursorReadbackAllowsOnlyTwoPixelTolerance()
    {
        var expected = new SerialHidCursorPoint(100, 200);

        Assert.True(LiveDiscoveryNanoCoordinateSmoke.IsCursorAtTarget(
            expected,
            new SerialHidCursorPoint(102, 198)));
        Assert.False(LiveDiscoveryNanoCoordinateSmoke.IsCursorAtTarget(
            expected,
            new SerialHidCursorPoint(103, 200)));
    }

    [Fact]
    public void ScreenMappingStaysInsideExclusiveRightAndBottomEdges()
    {
        var rect = new LiveDiscoveryObserveSmoke.WindowRectangle(100, 200, 1100, 700);

        var point = LiveDiscoveryNanoCoordinateSmoke.MapToScreen(rect, 0.999999, 0.999999);

        Assert.Equal(new SerialHidCursorPoint(1099, 699), point);
    }

    [Theory]
    [InlineData(null, 40)]
    [InlineData("4", 4)]
    [InlineData("8", 8)]
    [InlineData("128", 128)]
    public void PatchRadiusUsesBoundedExplicitRegion(string? value, int expected)
    {
        Assert.Equal(expected, LiveDiscoveryNanoCoordinateSmoke.ParsePatchRadius(value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("129")]
    [InlineData("8.5")]
    [InlineData("abc")]
    public void PatchRadiusRejectsUnsafeValues(string value)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => LiveDiscoveryNanoCoordinateSmoke.ParsePatchRadius(value));
    }

    [Fact]
    public void DHashParsesAndMeasuresBoundedVisualDifference()
    {
        Assert.Null(LiveDiscoveryNanoCoordinateSmoke.ParseDHash(null));
        Assert.Equal(0UL, LiveDiscoveryNanoCoordinateSmoke.ParseDHash("0000000000000000"));
        Assert.Equal(64, LiveDiscoveryNanoCoordinateSmoke.DHashDistance(0UL, ulong.MaxValue));
        Assert.Equal(1, LiveDiscoveryNanoCoordinateSmoke.DHashDistance(0UL, 1UL));
        Assert.Throws<ArgumentException>(() => LiveDiscoveryNanoCoordinateSmoke.ParseDHash("abc"));
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData("0", 0)]
    [InlineData("16", 16)]
    public void DHashDistanceThresholdIsNarrowAndBounded(string? value, int expected)
    {
        Assert.Equal(expected, LiveDiscoveryNanoCoordinateSmoke.ParseMaxDHashDistance(value));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("17")]
    [InlineData("x")]
    public void DHashDistanceThresholdRejectsUnsafeValues(string value)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => LiveDiscoveryNanoCoordinateSmoke.ParseMaxDHashDistance(value));
    }

    [Fact]
    public void ExpectedPatchMustBeSha256Hex()
    {
        LiveDiscoveryNanoCoordinateSmoke.ValidateExpectedPatch(null);
        LiveDiscoveryNanoCoordinateSmoke.ValidateExpectedPatch(new string('a', 64));

        Assert.Throws<ArgumentException>(
            () => LiveDiscoveryNanoCoordinateSmoke.ValidateExpectedPatch("abc"));
        Assert.Throws<ArgumentException>(
            () => LiveDiscoveryNanoCoordinateSmoke.ValidateExpectedPatch(new string('z', 64)));
    }
}
