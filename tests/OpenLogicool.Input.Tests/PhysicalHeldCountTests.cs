using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Fakes;
using Xunit;

namespace OpenLogicool.Input.Tests;

/// <summary>
/// 物理押下数（PhysicalHeldCount）: Botの手入力監視へ渡す「いま物理的に押されている control の数」。
/// Down で増え、Up で減り、切断・停止・fault 停止で 0 に戻る。
/// </summary>
public sealed class PhysicalHeldCountTests
{
    private sealed class RecordingEmitter : IOutputEmitter
    {
        public List<MappedOutputEdge> Emitted { get; } = [];

        public void Emit(IReadOnlyList<MappedOutputEdge> edges) => Emitted.AddRange(edges);
    }

    private static DeviceInstance Device(string id) =>
        new(ContractSchemaVersions.Revision01, id, 0x046D, 0xC24A, id, "{00000000-0000-0000-0000-000000000000}", 1, []);

    private static PhysicalInput Edge(string deviceId, string controlId, PhysicalInputEdge edge, long sequence) =>
        new(ContractSchemaVersions.Revision01, deviceId, controlId, edge, MonotonicMs: 0, ReportSequence: sequence);

    private static DeviceChange Change(string deviceId, DeviceChangeKind kind) =>
        new(ContractSchemaVersions.Revision01, deviceId, kind, MonotonicMs: 0);

    private static DeviceMappingRuntime Runtime(string deviceId) =>
        new(deviceId, new MappingProfile(
            "profile-r1",
            "map-r1",
            defaultLayerId: "base",
            layerIds: ["base"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string>(),
            bindings:
            [
                new MappingBinding("G9", "base", ["Key:F13"]),
                new MappingBinding("G10", "base", ["Key:F14"]),
            ]));

    private static (FakeDeviceInputSource Source, FastPathPump Pump) Setup(params string[] deviceIds)
    {
        var source = new FakeDeviceInputSource(deviceIds.Select(Device).ToArray(), []);
        var pump = new FastPathPump(
            [new FastPathSource(source)],
            deviceIds.ToDictionary(id => id, Runtime),
            new RecordingEmitter());
        return (source, pump);
    }

    [Fact]
    public void Down_increases_and_Up_decreases_the_count()
    {
        var (source, pump) = Setup("dev-a");
        Assert.Equal(0, pump.PhysicalHeldCount);

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 1));
        pump.RunOnce();
        Assert.Equal(1, pump.PhysicalHeldCount);

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Up, 2));
        pump.RunOnce();
        Assert.Equal(0, pump.PhysicalHeldCount);
    }

    [Fact]
    public void Two_pressed_and_one_released_leaves_one_held()
    {
        var (source, pump) = Setup("dev-a");

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 1));
        source.EnqueueInput(Edge("dev-a", "G10", PhysicalInputEdge.Down, 2));
        pump.RunOnce();
        Assert.Equal(2, pump.PhysicalHeldCount);

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Up, 3));
        pump.RunOnce();
        Assert.Equal(1, pump.PhysicalHeldCount);
    }

    [Fact]
    public void Ghost_up_does_not_make_the_count_negative()
    {
        var (source, pump) = Setup("dev-a");

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Up, 1));
        pump.RunOnce();
        Assert.Equal(0, pump.PhysicalHeldCount);

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 2));
        pump.RunOnce();
        Assert.Equal(1, pump.PhysicalHeldCount);
    }

    [Fact]
    public void Same_control_on_two_devices_counts_separately()
    {
        var (source, pump) = Setup("dev-a", "dev-b");

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 1));
        source.EnqueueInput(Edge("dev-b", "G9", PhysicalInputEdge.Down, 2));
        pump.RunOnce();
        Assert.Equal(2, pump.PhysicalHeldCount);

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Up, 3));
        pump.RunOnce();
        Assert.Equal(1, pump.PhysicalHeldCount);
    }

    [Fact]
    public void Removal_clears_only_that_device()
    {
        var (source, pump) = Setup("dev-a", "dev-b");

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 1));
        source.EnqueueInput(Edge("dev-a", "G10", PhysicalInputEdge.Down, 2));
        source.EnqueueInput(Edge("dev-b", "G9", PhysicalInputEdge.Down, 3));
        pump.RunOnce();
        Assert.Equal(3, pump.PhysicalHeldCount);

        source.EnqueueChange(Change("dev-a", DeviceChangeKind.Removal));
        pump.RunOnce();
        Assert.Equal(1, pump.PhysicalHeldCount);

        // 切断前に押していた control の up（幽霊 up）は数を変えない。
        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Up, 4));
        pump.RunOnce();
        Assert.Equal(1, pump.PhysicalHeldCount);
    }

    [Fact]
    public void Removal_clears_a_device_without_a_mapping_runtime()
    {
        var source = new FakeDeviceInputSource([Device("dev-a")], []);
        var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime>(),
            new RecordingEmitter());

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 1));
        pump.RunOnce();
        Assert.Equal(1, pump.PhysicalHeldCount);

        source.EnqueueChange(Change("dev-a", DeviceChangeKind.Removal));
        pump.RunOnce();
        Assert.Equal(0, pump.PhysicalHeldCount);
    }

    [Fact]
    public void Stop_resets_the_count_to_zero()
    {
        var (source, pump) = Setup("dev-a");
        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 1));
        source.EnqueueInput(Edge("dev-a", "G10", PhysicalInputEdge.Down, 2));

        pump.Start();
        Assert.True(SpinWait.SpinUntil(() => pump.ProcessedCount == 2, TimeSpan.FromSeconds(2)));
        Assert.Equal(2, pump.PhysicalHeldCount);

        pump.Stop();
        Assert.Equal(0, pump.PhysicalHeldCount);
    }

    [Fact]
    public void Fault_stop_resets_the_count_to_zero()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [
                Edge("dev-a", "G9", PhysicalInputEdge.Down, 1),
                // 列挙されていない device の入力は fault 停止になる。
                Edge("dev-unknown", "G9", PhysicalInputEdge.Down, 2),
            ]);
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a") },
            new RecordingEmitter());

        pump.Start();
        Assert.True(SpinWait.SpinUntil(() => pump.Failure is not null, TimeSpan.FromSeconds(2)));

        Assert.IsType<FastPathFaultException>(pump.Failure);
        Assert.Equal(1, pump.ProcessedCount);
        Assert.Equal(0, pump.PhysicalHeldCount);
    }
}
