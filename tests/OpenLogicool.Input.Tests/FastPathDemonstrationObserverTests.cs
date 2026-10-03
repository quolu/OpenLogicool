using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Fakes;
using Xunit;

namespace OpenLogicool.Input.Tests;

/// <summary>
/// 操作デモ記録へのG13／G600 edge fan-out（t02）。fast pathは観測へ渡すだけで待たない。
/// </summary>
public sealed class FastPathDemonstrationObserverTests
{
    [Fact]
    public void 保存済み割当がなくても両デバイスの押下を観測でき出力はしない()
    {
        var inputs = new[]
        {
            Edge("g13", "G1", PhysicalInputEdge.Down, 1),
            Edge("g13", "G1", PhysicalInputEdge.Up, 2),
            Edge("g600", "G9", PhysicalInputEdge.Down, 3),
            Edge("g600", "G9", PhysicalInputEdge.Up, 4),
        };
        var source = new FakeDeviceInputSource([Device("g13"), Device("g600")], inputs);
        var emitter = new RecordingEmitter();
        var observer = new RecordingInputObserver();
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime>(),
            emitter,
            enableTrace: true,
            inputObserver: observer);

        Assert.Equal(4, pump.RunOnce());
        Assert.Equal(inputs, observer.Observed);
        Assert.Empty(emitter.Emitted);
        var trace = pump.DrainTrace();
        Assert.Equal(inputs.Select(input => (input.DeviceInstanceId, input.ControlId, input.Edge)),
            trace.Select(entry => (entry.DeviceInstanceId, entry.ControlId, entry.Edge)));
        Assert.All(trace, entry =>
        {
            Assert.Empty(entry.OutputTokens);
            Assert.False(entry.Emitted);
        });
    }

    [Fact]
    public void 初回保存後は再起動なしで送出を開始し割当用の押下には後から出力しない()
    {
        var source = new FakeDeviceInputSource(
            [Device("g600")], [Edge("g600", "G9", PhysicalInputEdge.Down, 1)]);
        var emitter = new RecordingEmitter();
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime>(),
            emitter,
            enableTrace: true);

        pump.RunOnce();
        Assert.Empty(emitter.Emitted);
        pump.RequestProfileChange("g600", new MappingProfile(
            "profile-r1", "map-r1", "base", ["base"],
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            [new MappingBinding("G9", "base", ["Key:A"])]));
        source.EnqueueInput(Edge("g600", "G9", PhysicalInputEdge.Up, 2));
        source.EnqueueInput(Edge("g600", "G9", PhysicalInputEdge.Down, 3));
        source.EnqueueInput(Edge("g600", "G9", PhysicalInputEdge.Up, 4));
        pump.RunOnce();

        Assert.Equal(
            [new MappedOutputEdge("Key:A", PhysicalInputEdge.Down), new MappedOutputEdge("Key:A", PhysicalInputEdge.Up)],
            emitter.Emitted);
        Assert.Equal([false, false, true, true], pump.DrainTrace().Select(entry => entry.Emitted));
    }

    [Fact]
    public void Every_input_reaches_the_observer_in_order_without_changing_what_is_emitted()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [
                Edge("dev-a", "G9", PhysicalInputEdge.Down, 1),
                Edge("dev-a", "G9", PhysicalInputEdge.Up, 2),
            ]);
        var emitter = new RecordingEmitter();
        var observer = new RecordingInputObserver();
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            emitter,
            inputObserver: observer);

        var processed = pump.RunOnce();

        Assert.Equal(2, processed);
        Assert.Equal(
            [("G9", PhysicalInputEdge.Down), ("G9", PhysicalInputEdge.Up)],
            observer.Observed.Select(input => (input.ControlId, input.Edge)));
        Assert.Equal(
            [
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down),
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up),
            ],
            emitter.Emitted);
    }

    [Fact]
    public void Without_an_observer_the_pump_behaves_exactly_as_before()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [Edge("dev-a", "G9", PhysicalInputEdge.Down, 1)]);
        var emitter = new RecordingEmitter();
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            emitter);

        Assert.Equal(1, pump.RunOnce());
        Assert.Equal([new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down)], emitter.Emitted);
    }

    [Fact]
    public void An_unknown_device_faults_the_pump_and_that_input_never_reaches_the_observer()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [
                Edge("dev-unknown", "G9", PhysicalInputEdge.Down, 1),
                Edge("dev-a", "G9", PhysicalInputEdge.Down, 2),
            ]);
        var observer = new RecordingInputObserver();
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            new RecordingEmitter(),
            inputObserver: observer);

        Assert.Throws<FastPathFaultException>(() => pump.RunOnce());
        // fan-outはmapping解決の後にある。未知deviceのfaultはそのまま止まり、
        // 記録側は処理されなかった入力を受け取らない。
        Assert.Empty(observer.Observed);
    }

    private sealed class RecordingInputObserver : IPhysicalInputObserver
    {
        public List<PhysicalInput> Observed { get; } = [];

        public void OnInput(PhysicalInput input) => Observed.Add(input);
    }

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
}
