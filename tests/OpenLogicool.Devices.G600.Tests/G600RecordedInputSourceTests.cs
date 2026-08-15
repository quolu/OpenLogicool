using System.Text.Json;
using OpenLogicool.Contracts.Devices.G600;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G600;
using OpenLogicool.Fakes;
using Xunit;

namespace OpenLogicool.Devices.G600.Tests;

/// <summary>
/// 実機記録 fixture 2件を recorded adapter で replay し、fake adapter と同じ conformance 検査を通す。
/// 期待列は一次データ（probe-output の jsonl）の report byte 列から
/// docs/probes/g600-input-map-2026-08-15.md の bit 対応で導出した。
/// </summary>
public sealed class G600RecordedInputSourceTests
{
    /// <summary>g9-g20 セッション: サイド12ボタンを印字順に押下 → 最後に G1 クリック。</summary>
    private static readonly (string ControlId, PhysicalInputEdge Edge)[] ExpectedSideEdges =
        Enumerable.Range(9, 12)
            .SelectMany(g => new[]
            {
                ($"G{g}", PhysicalInputEdge.Down),
                ($"G{g}", PhysicalInputEdge.Up),
            })
            .Concat([("G1", PhysicalInputEdge.Down), ("G1", PhysicalInputEdge.Up)])
            .ToArray();

    /// <summary>normal セッションの button 列（ホイールは別途 tick として検査）。</summary>
    private static readonly (string ControlId, PhysicalInputEdge Edge)[] ExpectedNormalEdges =
        new[]
        {
            "G1", "G1", "G1", "G2", "G3", "G4", "G5",
            "G8", "G7", "G3", "G3",
            "G11", "G14", "G17", "G20", "G10", "G16", "G19", "G9", "G12", "G15", "G18",
            "G6", "G1",
        }
        .SelectMany(controlId => new[]
        {
            (controlId, PhysicalInputEdge.Down),
            (controlId, PhysicalInputEdge.Up),
        })
        .ToArray();

    /// <summary>normal セッションのホイール: 上4・下5（うち3ノッチは1 WM_INPUT に pack）→ （ボタン操作）→ 上3・下3。</summary>
    private static readonly int[] ExpectedNormalWheelDeltas =
        [1, 1, 1, 1, -1, -1, -1, -1, -1, 1, 1, 1, -1, -1, -1];

    [Fact]
    public void Side_button_fixture_replays_to_the_expected_edge_sequence()
    {
        var source = LoadRecordedSource("g600-input-report.g9-g20.v1.json");
        Assert.Equal(ExpectedSideEdges, PullAll(source).Select(input => (input.ControlId, input.Edge)));

        Assert.False(source.TryPullWheel(out _));
    }

    [Fact]
    public void Normal_fixture_replays_to_the_expected_edge_and_wheel_sequence()
    {
        var source = LoadRecordedSource("g600-input-report.normal.v1.json");
        Assert.Equal(ExpectedNormalEdges, PullAll(source).Select(input => (input.ControlId, input.Edge)));

        var ticks = new List<G600WheelTick>();
        while (source.TryPullWheel(out var tick))
        {
            ticks.Add(tick);
        }

        Assert.Equal(ExpectedNormalWheelDeltas, ticks.Select(tick => tick.Delta));
        for (var i = 1; i < ticks.Count; i++)
        {
            Assert.True(ticks[i].ReportSequence > ticks[i - 1].ReportSequence);
            Assert.True(ticks[i].MonotonicMs >= ticks[i - 1].MonotonicMs);
        }
    }

    [Fact]
    public void Recorded_and_fake_adapters_pass_the_same_input_conformance_checks()
    {
        var recorded = LoadRecordedSource("g600-input-report.normal.v1.json");
        var recordedInputs = PullAll(recorded);
        VerifyInputConformance(recorded.EnumerateDevices(), recordedInputs);

        var fake = new FakeDeviceInputSource(recorded.EnumerateDevices(), recordedInputs);
        VerifyInputConformance(fake.EnumerateDevices(), PullAll(fake));
    }

    private static void VerifyInputConformance(IReadOnlyList<DeviceInstance> devices, IReadOnlyList<PhysicalInput> inputs)
    {
        var device = Assert.Single(devices);
        Assert.Equal(G600DeviceIdentity.VendorId, device.VendorId);
        Assert.Equal(G600DeviceIdentity.ProductId, device.ProductId);

        var heldControls = new HashSet<string>();
        var lastMonotonicMs = double.NegativeInfinity;
        var lastSequence = long.MinValue;

        foreach (var input in inputs)
        {
            Assert.Equal("0.1.0", input.SchemaVersion);
            Assert.Equal(device.DeviceInstanceId, input.DeviceInstanceId);
            Assert.Contains(input.ControlId, G600Controls.Buttons);
            Assert.True(input.MonotonicMs >= lastMonotonicMs, "MonotonicMs が逆行しています。");
            Assert.True(input.ReportSequence >= lastSequence, "ReportSequence が逆行しています。");
            lastMonotonicMs = input.MonotonicMs;
            lastSequence = input.ReportSequence;

            if (input.Edge == PhysicalInputEdge.Down)
            {
                Assert.True(heldControls.Add(input.ControlId), $"{input.ControlId} の二重 Down です。");
            }
            else
            {
                Assert.True(heldControls.Remove(input.ControlId), $"{input.ControlId} の Down なし Up です。");
            }
        }

        Assert.Empty(heldControls);
    }

    private static List<PhysicalInput> PullAll(IDeviceInputSource source)
    {
        var inputs = new List<PhysicalInput>();
        while (source.TryPull(out var input))
        {
            inputs.Add(input);
        }

        return inputs;
    }

    private static G600RecordedInputSource LoadRecordedSource(string fixtureFileName)
    {
        var fixturePath = Path.Combine(RepositoryRoot(), "fixtures", "devices", fixtureFileName);
        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = document.RootElement;

        var deviceElement = root.GetProperty("device");
        Assert.Equal(G600DeviceIdentity.VendorId, deviceElement.GetProperty("vendorId").GetInt32());
        Assert.Equal(G600DeviceIdentity.ProductId, deviceElement.GetProperty("productId").GetInt32());
        Assert.Equal(G600DeviceIdentity.InputReportId, deviceElement.GetProperty("reportId").GetInt32());
        Assert.Equal(G600DeviceIdentity.InputReportLength, deviceElement.GetProperty("reportLength").GetInt32());

        var devicePath = deviceElement.GetProperty("devicePath").GetString()!;
        var device = new DeviceInstance(
            root.GetProperty("schemaVersion").GetString()!,
            devicePath,
            G600DeviceIdentity.VendorId,
            G600DeviceIdentity.ProductId,
            devicePath,
            ContainerId: "recorded-fixture",
            Generation: 1,
            Capabilities: ["g600.buttons", "g600.wheel"]);

        var reports = root.GetProperty("reports").EnumerateArray()
            .Select(report => new G600RecordedReport(
                report.GetProperty("monotonicMs").GetDouble(),
                Convert.FromHexString(report.GetProperty("dataHex").GetString()!)))
            .ToList();

        return new G600RecordedInputSource(device, reports);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenLogicool.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("OpenLogicool.sln を含む repository root を特定できません。");
    }
}
