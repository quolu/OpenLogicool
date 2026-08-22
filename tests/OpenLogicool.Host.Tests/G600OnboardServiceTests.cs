using System.IO;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Devices.G600;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class G600OnboardServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"olc-onboard-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static byte[] CleanF3()
    {
        var report = new byte[G600SideRemap.ReportLength];
        for (var i = 0; i < report.Length; i++)
        {
            report[i] = (byte)(i & 0xFF);
        }

        report[0] = 0xF3;
        return report;
    }

    private static MappingProfileDocument Document() =>
        new(
            "1", "ws-G600", "G600", "r1", "m1", "base", ["base", "shift"],
            [],
            [new LayerSelectorEntry("G6", "shift")],
            [new MappingBindingEntry("G11", "base", ["Key:A"])]);

    private sealed class FakeDeviceAccess(byte[] initialF3) : IG600FeatureAccess
    {
        public byte[] StoredF3 = initialF3;

        public bool TryOpen(out IG600FeatureHandle? handle)
        {
            handle = new FakeHandle(this);
            return true;
        }

        private sealed class FakeHandle(FakeDeviceAccess owner) : IG600FeatureHandle
        {
            public void SetFeature(byte[] report) => owner.StoredF3 = report.ToArray();

            public byte[] GetFeature(byte reportId) => owner.StoredF3.ToArray();

            public void Dispose()
            {
            }
        }
    }

    private (G600OnboardService Service, FakeDeviceAccess Device, G600OnboardModeStore Mode, FileG600OnboardBaselineStore Baseline)
        CreateService(byte[] initialF3, bool coexistence = false)
    {
        var device = new FakeDeviceAccess(initialF3);
        var mode = new G600OnboardModeStore(_directory);
        var baseline = new FileG600OnboardBaselineStore(_directory);
        var service = new G600OnboardService(device, baseline, mode, () => coexistence, sleep: _ => { }, settleMs: 0);
        return (service, device, mode, baseline);
    }

    [Fact]
    public void Apply_saves_baseline_writes_payload_and_sets_mode()
    {
        var clean = CleanF3();
        var (service, device, mode, baseline) = CreateService(clean);

        var result = service.Apply("ws", Document());

        Assert.True(result.Success);
        Assert.Equal(clean, baseline.LoadF3());
        Assert.NotNull(mode.Load());
        Assert.Equal("ws", mode.Load()!.WorkspaceId);
        // G11 base = A（usage 0x04）・G1 = 左クリック・G6 = G-Shift
        Assert.Equal(0x04, device.StoredF3[G600SideRemap.NormalLayerBaseOffset + 10 * 3 + 2]);
        Assert.Equal(0x01, device.StoredF3[G600SideRemap.NormalLayerBaseOffset]);
        Assert.Equal(0x17, device.StoredF3[G600SideRemap.NormalLayerBaseOffset + 5 * 3]);
    }

    [Fact]
    public void Apply_builds_from_existing_baseline_not_current_state()
    {
        var clean = CleanF3();
        var (service, device, _, baseline) = CreateService(G600SideRemap.Build(clean));
        baseline.SaveF3(clean); // 残置が先に確保した出荷状態

        var result = service.Apply("ws", Document());

        Assert.True(result.Success);
        // baseline 起点なので、残置の中間 usage（F13〜F24）は payload に残らない
        // （G9 base cell は明示 00 00 00 になる——G9 未割当のため）
        Assert.Equal(0x00, device.StoredF3[G600SideRemap.NormalLayerBaseOffset + 8 * 3 + 2]);
        Assert.Equal(clean, baseline.LoadF3()); // baseline は出荷状態のまま
    }

    [Fact]
    public void Restore_writes_baseline_back_and_clears_mode()
    {
        var clean = CleanF3();
        var (service, device, mode, _) = CreateService(clean);
        Assert.True(service.Apply("ws", Document()).Success);

        var result = service.Restore();

        Assert.True(result.Success);
        Assert.Equal(clean, device.StoredF3);
        Assert.Null(mode.Load());
    }

    [Fact]
    public void Coexistence_refuses_apply_and_restore_without_writing()
    {
        var clean = CleanF3();
        var (service, device, mode, _) = CreateService(clean, coexistence: true);

        Assert.False(service.Apply("ws", Document()).Success);
        Assert.False(service.Restore().Success);
        Assert.Equal(clean, device.StoredF3);
        Assert.Null(mode.Load());
    }

    [Fact]
    public void Inexpressible_workspace_is_refused_before_any_write()
    {
        var clean = CleanF3();
        var (service, device, mode, _) = CreateService(clean);
        var document = Document() with
        {
            Bindings = [new MappingBindingEntry("G11", "base", ["Tap:Key:A"])],
        };

        var result = service.Apply("ws", document);

        Assert.False(result.Success);
        Assert.Contains("表現できない", result.Message);
        Assert.Equal(clean, device.StoredF3);
        Assert.Null(mode.Load());
    }
}
