using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Fakes;
using System.Diagnostics;
using Xunit;

namespace OpenLogicool.Input.Tests;

/// <summary>
/// fast path の trace（test field・Journey A-6）の focused test。
/// trace off 時に enqueue 自体を行わない構成を検証し、既存 fast path 挙動（emit・fault・release）
/// への影響がないことは既存 FastPathPumpTests が引き続き保証する。
/// </summary>
public sealed class InputTraceTests
{
    private sealed class RecordingEmitter : IOutputEmitter
    {
        public List<MappedOutputEdge> Emitted { get; } = [];

        public void Emit(IReadOnlyList<MappedOutputEdge> edges) => Emitted.AddRange(edges);
    }

    private static DeviceInstance Device(string id) =>
        new(ContractSchemaVersions.Revision01, id, 0x046D, 0xC24A, id, "{00000000-0000-0000-0000-000000000000}", 1, []);

    private static PhysicalInput Edge(string deviceId, string controlId, PhysicalInputEdge edge, long sequence) =>
        new(ContractSchemaVersions.Revision01, deviceId, controlId, edge, MonotonicMilliseconds(), ReportSequence: sequence);

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
    public void Trace_records_down_and_up_entries_in_order_when_enabled()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [
                Edge("dev-a", "G9", PhysicalInputEdge.Down, 1),
                Edge("dev-a", "G9", PhysicalInputEdge.Up, 2),
            ]);
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            new RecordingEmitter(),
            enableTrace: true);

        pump.RunOnce();
        var trace = pump.DrainTrace();

        Assert.Equal(2, trace.Count);
        Assert.Equal("dev-a", trace[0].DeviceInstanceId);
        Assert.Equal("G9", trace[0].ControlId);
        Assert.Equal(PhysicalInputEdge.Down, trace[0].Edge);
        Assert.Equal("base", trace[0].LayerId);
        Assert.Equal(["Key:F13"], trace[0].OutputTokens);
        Assert.True(trace[0].Emitted);
        Assert.True(trace[0].DispatchCompletedMonotonicMs >= trace[0].InputMonotonicMs);
        Assert.True(trace[0].DispatchLatencyMs >= 0);

        Assert.Equal(PhysicalInputEdge.Up, trace[1].Edge);
        Assert.Equal(["Key:F13"], trace[1].OutputTokens);
        Assert.True(trace[1].Emitted);
        Assert.True(trace[1].Sequence > trace[0].Sequence);
    }

    [Fact]
    public void Unbound_control_is_recorded_with_empty_outputs_and_not_emitted()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [Edge("dev-a", "G10", PhysicalInputEdge.Down, 1)]);
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            new RecordingEmitter(),
            enableTrace: true);

        pump.RunOnce();
        var trace = pump.DrainTrace();

        var entry = Assert.Single(trace);
        Assert.Equal("G10", entry.ControlId);
        Assert.Empty(entry.OutputTokens);
        Assert.False(entry.Emitted);
    }

    [Fact]
    public void Trace_buffer_drops_oldest_beyond_capacity()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            Enumerable.Range(1, 5).Select(i => Edge(
                "dev-a", "G9", i % 2 == 1 ? PhysicalInputEdge.Down : PhysicalInputEdge.Up, i)));
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            new RecordingEmitter(),
            enableTrace: true,
            traceCapacity: 2);

        pump.RunOnce();
        var trace = pump.DrainTrace();

        Assert.True(trace.Count <= 2, $"buffer は上限（2件）以下であるべき: 実際 {trace.Count} 件");
        // drop-oldest なので残るのは最新の sequence 側
        Assert.All(trace, entry => Assert.True(entry.Sequence >= 4));
    }

    [Fact]
    public void Trace_is_empty_when_disabled()
    {
        var source = new FakeDeviceInputSource(
            [Device("dev-a")],
            [
                Edge("dev-a", "G9", PhysicalInputEdge.Down, 1),
                Edge("dev-a", "G9", PhysicalInputEdge.Up, 2),
            ]);
        using var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { ["dev-a"] = Runtime("dev-a", "Key:F13") },
            new RecordingEmitter());

        pump.RunOnce();

        Assert.Empty(pump.DrainTrace());
    }

    private static double MonotonicMilliseconds() =>
        Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
}
