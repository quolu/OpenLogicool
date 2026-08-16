using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Fakes;
using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class FastPathPumpTests
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

    private static DeviceMappingRuntime Runtime(string deviceId, string output) =>
        new(deviceId, new MappingProfile(
            "profile-r1",
            "map-r1",
            defaultLayerId: "base",
            layerIds: ["base"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string>(),
            bindings: [new MappingBinding("G9", "base", [output])]));

    [Fact]
    public void Inputs_are_routed_to_the_runtime_of_their_device_instance()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a"), Device("dev-b")],
            [
                Edge("dev-a", "G9", PhysicalInputEdge.Down, 1),
                Edge("dev-b", "G9", PhysicalInputEdge.Down, 2),
                Edge("dev-a", "G9", PhysicalInputEdge.Up, 3),
                Edge("dev-b", "G9", PhysicalInputEdge.Up, 4),
            ]);
        var emitter = new RecordingEmitter();
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime>
            {
                ["dev-a"] = Runtime("dev-a", "Key:F13"),
                ["dev-b"] = Runtime("dev-b", "Key:F14"),
            },
            emitter);

        var processed = pump.RunOnce();

        Assert.Equal(4, processed);
        Assert.Equal(
            [
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down),
                new MappedOutputEdge("Key:F14", PhysicalInputEdge.Down),
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up),
                new MappedOutputEdge("Key:F14", PhysicalInputEdge.Up),
            ],
            emitter.Emitted);
    }

    [Fact]
    public void Unknown_device_instance_is_a_fault()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [Edge("dev-unknown", "G9", PhysicalInputEdge.Down, 1)]);
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            new RecordingEmitter());

        Assert.Throws<FastPathFaultException>(() => pump.RunOnce());
    }

    [Fact]
    public void Detected_drop_is_a_fault()
    {
        var source = new FakeDeviceInputSource([Device("dev-a")], []);
        using var pump = new FastPathPump(
            [new FastPathSource(source, DroppedCountProbe: () => 3)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            new RecordingEmitter());

        Assert.Throws<FastPathFaultException>(() => pump.RunOnce());
    }

    [Fact]
    public void Stop_releases_owned_outputs()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [Edge("dev-a", "G9", PhysicalInputEdge.Down, 1)]);
        var emitter = new RecordingEmitter();
        var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            emitter);

        pump.RunOnce();
        pump.Stop();

        Assert.Equal(
            [
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down),
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up),
            ],
            emitter.Emitted);
        Assert.Null(pump.Failure);
    }

    [Fact]
    public void Worker_thread_processes_inputs_and_stops_cleanly()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [
                Edge("dev-a", "G9", PhysicalInputEdge.Down, 1),
                Edge("dev-a", "G9", PhysicalInputEdge.Up, 2),
            ]);
        var emitter = new RecordingEmitter();
        var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            emitter);

        pump.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (pump.ProcessedCount < 2 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        pump.Stop();

        Assert.Null(pump.Failure);
        Assert.Equal(2, pump.ProcessedCount);
        Assert.Equal(
            [
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down),
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up),
            ],
            emitter.Emitted);
    }

    [Fact]
    public void Worker_fault_releases_owned_outputs_and_records_failure()
    {
        var dropAfterDown = 0L;
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [Edge("dev-a", "G9", PhysicalInputEdge.Down, 1)]);
        var emitter = new RecordingEmitter();
        var pump = new FastPathPump(
            [new FastPathSource(source, DroppedCountProbe: () => Interlocked.Read(ref dropAfterDown))],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            emitter);

        pump.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (pump.ProcessedCount < 1 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Interlocked.Exchange(ref dropAfterDown, 1);
        while (pump.Failure is null && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.IsType<FastPathFaultException>(pump.Failure);
        Assert.Contains(new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up), emitter.Emitted);
        Assert.False(pump.IsRunning);
    }
}
