using System.Text.Json;
using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G13;
using OpenLogicool.Fakes;
using Xunit;

namespace OpenLogicool.Devices.G13.Tests;

/// <summary>
/// 実機記録 fixture（recheck セッション 243 report）を recorded adapter で replay し、
/// fake adapter と同じ conformance 検査を通す。
/// 期待 edge 列は docs/probes/g13-input-map-2026-08-15.md の実測台帳から導出。
/// </summary>
public sealed class G13RecordedInputSourceTests
{
    private static readonly (string ControlId, PhysicalInputEdge Edge)[] ExpectedEdges =
    [
        ("G20", PhysicalInputEdge.Down),
        ("G20", PhysicalInputEdge.Up),
        ("STICK_PRESS", PhysicalInputEdge.Down),
        ("STICK_PRESS", PhysicalInputEdge.Up),
        ("STICK_PRESS", PhysicalInputEdge.Down),
        ("STICK_PRESS", PhysicalInputEdge.Up),
        ("G1", PhysicalInputEdge.Down),
        ("G1", PhysicalInputEdge.Up),
        ("G2", PhysicalInputEdge.Down),
        ("G1", PhysicalInputEdge.Down),
        ("G1", PhysicalInputEdge.Up),
        ("G2", PhysicalInputEdge.Up),
    ];

    [Fact]
    public void Recorded_fixture_replays_to_the_expected_edge_sequence()
    {
        var source = LoadRecordedSource();
        var inputs = PullAll(source);

        Assert.Equal(ExpectedEdges, inputs.Select(input => (input.ControlId, input.Edge)));
    }

    [Fact]
    public void Recorded_and_fake_adapters_pass_the_same_input_conformance_checks()
    {
        var recorded = LoadRecordedSource();
        var recordedInputs = PullAll(recorded);
        VerifyInputConformance(recorded.EnumerateDevices(), recordedInputs);

        var fake = new FakeDeviceInputSource(recorded.EnumerateDevices(), recordedInputs);
        VerifyInputConformance(fake.EnumerateDevices(), PullAll(fake));
    }

    [Fact]
    public void Stick_samples_come_from_the_same_replay_with_increasing_sequence()
    {
        var source = LoadRecordedSource();
        var samples = new List<G13StickSample>();
        while (source.TryPullStick(out var sample))
        {
            samples.Add(sample);
        }

        Assert.NotEmpty(samples);
        for (var i = 1; i < samples.Count; i++)
        {
            Assert.True(samples[i].ReportSequence > samples[i - 1].ReportSequence);
            Assert.True(samples[i].MonotonicMs >= samples[i - 1].MonotonicMs);
        }
    }

    private static void VerifyInputConformance(IReadOnlyList<DeviceInstance> devices, IReadOnlyList<PhysicalInput> inputs)
    {
        var device = Assert.Single(devices);
        Assert.Equal(G13DeviceIdentity.VendorId, device.VendorId);
        Assert.Equal(G13DeviceIdentity.ProductId, device.ProductId);

        var heldControls = new HashSet<string>();
        var lastMonotonicMs = double.NegativeInfinity;
        var lastSequence = long.MinValue;

        foreach (var input in inputs)
        {
            Assert.Equal("0.1.0", input.SchemaVersion);
            Assert.Equal(device.DeviceInstanceId, input.DeviceInstanceId);
            Assert.Contains(input.ControlId, G13Controls.Buttons);
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

    private static G13RecordedInputSource LoadRecordedSource()
    {
        var fixturePath = Path.Combine(RepositoryRoot(), "fixtures", "devices", "g13-input-report.recheck.v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = document.RootElement;

        var deviceElement = root.GetProperty("device");
        Assert.Equal(G13DeviceIdentity.VendorId, deviceElement.GetProperty("vendorId").GetInt32());
        Assert.Equal(G13DeviceIdentity.ProductId, deviceElement.GetProperty("productId").GetInt32());
        Assert.Equal(G13DeviceIdentity.InputReportId, deviceElement.GetProperty("reportId").GetInt32());
        Assert.Equal(G13DeviceIdentity.InputReportLength, deviceElement.GetProperty("reportLength").GetInt32());

        var devicePath = deviceElement.GetProperty("devicePath").GetString()!;
        var device = new DeviceInstance(
            root.GetProperty("schemaVersion").GetString()!,
            devicePath,
            G13DeviceIdentity.VendorId,
            G13DeviceIdentity.ProductId,
            devicePath,
            ContainerId: "recorded-fixture",
            Generation: 1,
            Capabilities: ["g13.buttons", "g13.stick"]);

        var reports = root.GetProperty("reports").EnumerateArray()
            .Select(report => new G13RecordedReport(
                report.GetProperty("monotonicMs").GetDouble(),
                Convert.FromHexString(report.GetProperty("dataHex").GetString()!)))
            .ToList();

        return new G13RecordedInputSource(device, reports);
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
