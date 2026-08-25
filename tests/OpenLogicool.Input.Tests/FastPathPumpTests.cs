using System.Collections.Concurrent;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Fakes;
using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class FastPathPumpTests
{
    private sealed class SignaledInputSource(DeviceInstance device) : IDeviceInputSource, IDeviceInputSignalSource, IDisposable
    {
        private readonly ConcurrentQueue<PhysicalInput> inputs = new();
        private readonly AutoResetEvent inputAvailable = new(false);

        public WaitHandle InputAvailable => inputAvailable;

        public IReadOnlyList<DeviceInstance> EnumerateDevices() => [device];

        public void Enqueue(PhysicalInput input)
        {
            inputs.Enqueue(input);
            inputAvailable.Set();
        }

        public bool TryPull(out PhysicalInput input) => inputs.TryDequeue(out input!);

        public void Dispose() => inputAvailable.Dispose();
    }

    private sealed class RecordingEmitter : IOutputEmitter
    {
        public List<MappedOutputEdge> Emitted { get; } = [];

        public void Emit(IReadOnlyList<MappedOutputEdge> edges) => Emitted.AddRange(edges);
    }

    private sealed class FailingReleaseEmitter : IOutputEmitter
    {
        public void Emit(IReadOnlyList<MappedOutputEdge> edges)
        {
            if (edges.Any(edge => edge.Edge == PhysicalInputEdge.Up))
            {
                throw new InvalidOperationException("release failed");
            }
        }
    }

    private sealed class RecordingMacroSink(MacroInvocationEnqueueResult result) : IMacroInvocationSink
    {
        public List<MacroVersionReference> Invocations { get; } = [];

        public MacroInvocationEnqueueResult TryEnqueue(MacroVersionReference invocation)
        {
            Invocations.Add(invocation);
            return result;
        }
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
    public void Macro_down_is_enqueued_once_and_never_reaches_the_physical_emitter()
    {
        var reference = new MacroVersionReference("route:daily", null, MacroPlaybackMode.AiMonitored);
        var token = MacroInvocationTokens.Create(reference);
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [
                Edge("dev-a", "G9", PhysicalInputEdge.Down, 1),
                Edge("dev-a", "G9", PhysicalInputEdge.Up, 2),
            ]);
        var emitter = new RecordingEmitter();
        var sink = new RecordingMacroSink(MacroInvocationEnqueueResult.Accepted);
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", token) },
            emitter,
            macroInvocations: sink);

        Assert.Equal(2, pump.RunOnce());
        Assert.Equal([reference], sink.Invocations);
        Assert.Empty(emitter.Emitted);
        Assert.Equal(1, pump.AcceptedMacroInvocations);
        Assert.Equal(0, pump.RejectedMacroInvocations);
    }

    [Fact]
    public void Rejected_macro_is_observable_and_does_not_fault_normal_fast_path()
    {
        var reference = new MacroVersionReference("route:daily", null, MacroPlaybackMode.AiFree);
        var token = MacroInvocationTokens.Create(reference);
        var source = new FakeDeviceInputSource(
            [Device("dev-a"), Device("dev-b")],
            [
                Edge("dev-a", "G9", PhysicalInputEdge.Down, 1),
                Edge("dev-b", "G9", PhysicalInputEdge.Down, 2),
            ]);
        var emitter = new RecordingEmitter();
        var sink = new RecordingMacroSink(MacroInvocationEnqueueResult.QueueFull);
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime>
            {
                ["dev-a"] = Runtime("dev-a", token),
                ["dev-b"] = Runtime("dev-b", "Key:F13"),
            },
            emitter,
            enableTrace: true,
            macroInvocations: sink);

        Assert.Equal(2, pump.RunOnce());
        Assert.Null(pump.Failure);
        Assert.Equal(MacroInvocationEnqueueResult.QueueFull, pump.LastMacroRejection);
        Assert.Equal(1, pump.RejectedMacroInvocations);
        Assert.Equal([new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down)], emitter.Emitted);
        Assert.Equal([false, true], pump.DrainTrace().Select(entry => entry.Emitted).ToArray());
    }

    [Fact]
    public void Macro_queue_is_bounded_and_preserves_order()
    {
        using var queue = new MacroInvocationQueue(capacity: 2);
        var first = new MacroVersionReference("route:1", null, MacroPlaybackMode.AiFree);
        var second = new MacroVersionReference("route:2", "v2", MacroPlaybackMode.AiMonitored);

        Assert.Equal(MacroInvocationEnqueueResult.Accepted, queue.TryEnqueue(first));
        Assert.Equal(MacroInvocationEnqueueResult.Accepted, queue.TryEnqueue(second));
        Assert.Equal(MacroInvocationEnqueueResult.QueueFull,
            queue.TryEnqueue(new MacroVersionReference("route:3", null, MacroPlaybackMode.AiFree)));
        Assert.True(queue.TryDequeue(out var dequeuedFirst));
        Assert.True(queue.TryDequeue(out var dequeuedSecond));
        Assert.Equal(first, dequeuedFirst);
        Assert.Equal(second, dequeuedSecond);
        Assert.False(queue.TryDequeue(out _));
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
    public void Requested_profile_change_applies_before_next_inputs_and_held_press_releases_old_outputs()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [Edge("dev-a", "G9", PhysicalInputEdge.Down, 1)]);
        var emitter = new RecordingEmitter();
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            emitter);

        pump.RunOnce();

        pump.RequestProfileChange("dev-a", new MappingProfile(
            "profile-r2",
            "map-r2",
            defaultLayerId: "base",
            layerIds: ["base"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string>(),
            bindings: [new MappingBinding("G9", "base", ["Key:F14"])]));
        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Up, 2));
        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 3));
        pump.RunOnce();

        // 保持中 press は down 時の outputs（旧 profile）で解放され、新規 down から新 profile が効く
        Assert.Equal(
            [
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down),
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up),
                new MappedOutputEdge("Key:F14", PhysicalInputEdge.Down),
            ],
            emitter.Emitted);
    }

    [Fact]
    public void Profile_change_for_unknown_device_is_a_fault()
    {
        var source = new FakeDeviceInputSource([Device("dev-a")], []);
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            new RecordingEmitter());

        pump.RequestProfileChange("dev-unknown", new MappingProfile(
            "profile-r2",
            "map-r2",
            defaultLayerId: "base",
            layerIds: ["base"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string>(),
            bindings: [new MappingBinding("G9", "base", ["Key:F14"])]));

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
    public void Worker_waiting_on_live_source_signal_processes_later_input()
    {
        using var source = new SignaledInputSource(Device("dev-a"));
        var emitter = new RecordingEmitter();
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            emitter);

        pump.Start();
        Assert.True(SpinWait.SpinUntil(() => pump.IsRunning, TimeSpan.FromSeconds(1)));
        source.Enqueue(Edge("dev-a", "G9", PhysicalInputEdge.Down, 1));
        Assert.True(SpinWait.SpinUntil(() => pump.ProcessedCount == 1, TimeSpan.FromSeconds(1)));
        Assert.Equal([new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down)], emitter.Emitted);
        pump.Stop();

        Assert.Null(pump.Failure);
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

    [Fact]
    public void Worker_release_failure_uses_output_route_neutral_diagnostic()
    {
        var dropAfterDown = 0L;
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [Edge("dev-a", "G9", PhysicalInputEdge.Down, 1)]);
        var pump = new FastPathPump(
            [new FastPathSource(source, DroppedCountProbe: () => Interlocked.Read(ref dropAfterDown))],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            new FailingReleaseEmitter());

        pump.Start();
        Assert.True(SpinWait.SpinUntil(() => pump.ProcessedCount == 1, TimeSpan.FromSeconds(1)));
        Interlocked.Exchange(ref dropAfterDown, 1);
        Assert.True(SpinWait.SpinUntil(() => pump.Failure is AggregateException, TimeSpan.FromSeconds(1)));

        var failure = Assert.IsType<AggregateException>(pump.Failure);
        Assert.Contains("出力経路側の独立した解放機構", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("watchdog", failure.Message, StringComparison.OrdinalIgnoreCase);
    }
}
