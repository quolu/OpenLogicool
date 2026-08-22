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

    private static byte[] Cell(byte[] report, int button, bool shift)
    {
        var layerBase = shift
            ? G600SideRemap.ShiftLayerBaseOffset
            : G600SideRemap.NormalLayerBaseOffset;
        var offset = layerBase + (button - 1) * G600SideRemap.BytesPerButton;
        return report.AsSpan(offset, G600SideRemap.BytesPerButton).ToArray();
    }

    private sealed class FakeDeviceAccess(byte[] initialF3, int initialSlot = 0) : IG600FeatureAccess
    {
        public byte[] StoredF3 = initialF3;
        public int ActiveSlot = initialSlot;

        public bool TryOpen(out IG600FeatureHandle? handle)
        {
            handle = new FakeHandle(this);
            return true;
        }

        private sealed class FakeHandle(FakeDeviceAccess owner) : IG600FeatureHandle
        {
            public void SetFeature(byte[] report)
            {
                if (report[0] == G600ActiveSlot.ReportId)
                {
                    owner.ActiveSlot = G600ActiveSlot.ReadIndex(report[1]);
                    return;
                }

                owner.StoredF3 = report.ToArray();
            }

            public byte[] GetFeature(byte reportId)
            {
                if (reportId == G600ActiveSlot.ReportId)
                {
                    var f0 = new byte[G600SideRemap.ReportLength];
                    f0[0] = G600ActiveSlot.ReportId;
                    f0[1] = (byte)((owner.ActiveSlot << 4) | 0x0B); // 下位 nibble は状態 flags（揺れる）
                    return f0;
                }

                return owner.StoredF3.ToArray();
            }

            public void Dispose()
            {
            }
        }
    }

    private (G600OnboardService Service, FakeDeviceAccess Device, G600OnboardModeStore Mode, FileG600OnboardBaselineStore Baseline)
        CreateService(byte[] initialF3, bool coexistence = false, int initialSlot = 0)
    {
        var device = new FakeDeviceAccess(initialF3, initialSlot);
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
        Assert.Contains("USB", result.Message);
        Assert.Contains("挿し直", result.Message);

        // 未割当 G2〜G5 は baseline の右・中クリック等を両層とも保持する。
        foreach (var button in Enumerable.Range(2, 4))
        {
            Assert.Equal(Cell(clean, button, shift: false), Cell(device.StoredF3, button, shift: false));
            Assert.Equal(Cell(clean, button, shift: true), Cell(device.StoredF3, button, shift: true));
        }

        // G1 は左クリック、G6 は G-Shift に固定する。
        Assert.Equal([0x01, 0x00, 0x00], Cell(device.StoredF3, 1, shift: false));
        Assert.Equal([0x01, 0x00, 0x00], Cell(device.StoredF3, 1, shift: true));
        Assert.Equal([0x17, 0x00, 0x00], Cell(device.StoredF3, 6, shift: false));
        Assert.Equal([0x17, 0x00, 0x00], Cell(device.StoredF3, 6, shift: true));

        // G11 base だけは A（usage 0x04）。それ以外の G6〜G20 未割当層は無動作にする。
        Assert.Equal([0x00, 0x00, 0x04], Cell(device.StoredF3, 11, shift: false));
        foreach (var button in Enumerable.Range(7, 14))
        {
            if (button != 11)
            {
                Assert.Equal([0x00, 0x00, 0x00], Cell(device.StoredF3, button, shift: false));
            }

            Assert.Equal([0x00, 0x00, 0x00], Cell(device.StoredF3, button, shift: true));
        }
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
    public void Apply_switches_active_slot_to_zero_before_writing()
    {
        var clean = CleanF3();
        var (service, device, mode, _) = CreateService(clean, initialSlot: 2);

        var result = service.Apply("ws", Document());

        Assert.True(result.Success);
        Assert.Equal(0, device.ActiveSlot); // F3（slot 0）が生きる状態にしてから書く
        Assert.NotNull(mode.Load());
        Assert.Equal(0x04, device.StoredF3[G600SideRemap.NormalLayerBaseOffset + 10 * 3 + 2]);
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
