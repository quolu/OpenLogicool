using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class ResidentInputSourceSelectionTests
{
    [Fact]
    public void Profileがあるdevice種別のsourceだけをfast_pathへ配線する()
    {
        var g13 = new FakeSource();
        var g600 = new FakeSource();
        var selected = ResidentInputSourceSelection.Select(
        [
            new ResidentInputSourceCandidate("G13", new FastPathSource(g13)),
            new ResidentInputSourceCandidate("G600", new FastPathSource(g600)),
        ],
        ["G13"]);

        var source = Assert.Single(selected);
        Assert.Same(g13, source.Source);
    }

    [Fact]
    public void 両deviceにprofileがあれば両sourceを配線する()
    {
        var selected = ResidentInputSourceSelection.Select(
        [
            new ResidentInputSourceCandidate("G13", new FastPathSource(new FakeSource())),
            new ResidentInputSourceCandidate("G600", new FastPathSource(new FakeSource())),
        ],
        ["G13", "G600"]);

        Assert.Equal(2, selected.Count);
    }

    private sealed class FakeSource : IDeviceInputSource
    {
        public IReadOnlyList<DeviceInstance> EnumerateDevices() => [];

        public bool TryPull(out PhysicalInput input)
        {
            input = null!;
            return false;
        }
    }
}
