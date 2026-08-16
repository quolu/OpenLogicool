using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Fakes;
using Xunit;

namespace OpenLogicool.Input.Tests;

/// <summary>
/// hotplug suite（Phase 2 Exit 条件2）: device 切断・再接続で
/// 「切断時に所有 output を全 release・新規 down 停止（DEV-008）」
/// 「再接続で新規 down 再開・切断前の layer／所有状態を持ち越さない」を fake source で検証する。
/// </summary>
public sealed class HotplugTests
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

    private static MappingProfile Profile() =>
        new(
            "profile-r1",
            "map-r1",
            defaultLayerId: "base",
            layerIds: ["base", "shift"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string> { ["G6"] = "shift" },
            bindings:
            [
                new MappingBinding("G9", "base", ["Key:F13"]),
                new MappingBinding("G10", "base", ["Key:F14"]),
                new MappingBinding("G9", "shift", ["Key:F20"]),
            ]);

    private static (FakeDeviceInputSource Source, DeviceMappingRuntime Runtime, RecordingEmitter Emitter, FastPathPump Pump) Setup(
        string deviceId = "dev-a")
    {
        var source = new FakeDeviceInputSource([Device(deviceId)], []);
        var runtime = new DeviceMappingRuntime(deviceId, Profile());
        var emitter = new RecordingEmitter();
        var pump = new FastPathPump(
            [new FastPathSource(source)],
            new Dictionary<string, DeviceMappingRuntime> { [deviceId] = runtime },
            emitter);
        return (source, runtime, emitter, pump);
    }

    [Fact]
    public void Removal_while_held_releases_owned_outputs_and_stops_new_downs()
    {
        var (source, runtime, emitter, pump) = Setup();

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 1));
        source.EnqueueInput(Edge("dev-a", "G10", PhysicalInputEdge.Down, 2));
        pump.RunOnce();
        source.EnqueueChange(Change("dev-a", DeviceChangeKind.Removal));
        pump.RunOnce();

        Assert.Equal(
            [
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down),
                new MappedOutputEdge("Key:F14", PhysicalInputEdge.Down),
                // StopAndReleaseAll の release 順は ControlId の ordinal 順（"G10" < "G9"）
                new MappedOutputEdge("Key:F14", PhysicalInputEdge.Up),
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up),
            ],
            emitter.Emitted);
        Assert.False(runtime.AcceptsNewDowns);

        // 切断後の down は output を生まない
        emitter.Emitted.Clear();
        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 3));
        pump.RunOnce();
        Assert.Empty(emitter.Emitted);
        Assert.Null(pump.Failure);
    }

    [Fact]
    public void Removal_with_nothing_held_emits_nothing()
    {
        var (source, runtime, emitter, pump) = Setup();

        source.EnqueueChange(Change("dev-a", DeviceChangeKind.Removal));
        pump.RunOnce();

        Assert.Empty(emitter.Emitted);
        Assert.False(runtime.AcceptsNewDowns);
    }

    [Fact]
    public void Rearrival_resumes_new_downs()
    {
        var (source, runtime, emitter, pump) = Setup();

        source.EnqueueChange(Change("dev-a", DeviceChangeKind.Removal));
        pump.RunOnce();
        source.EnqueueChange(Change("dev-a", DeviceChangeKind.Arrival));
        pump.RunOnce();

        Assert.True(runtime.AcceptsNewDowns);

        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 1));
        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Up, 2));
        pump.RunOnce();

        Assert.Equal(
            [
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down),
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up),
            ],
            emitter.Emitted);
    }

    [Fact]
    public void Rearrival_resets_hold_layer_and_ignores_ghost_ups()
    {
        var (source, runtime, emitter, pump) = Setup();

        // hold layer に入り G9 を押したまま切断
        source.EnqueueInput(Edge("dev-a", "G6", PhysicalInputEdge.Down, 1));
        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 2));
        pump.RunOnce();
        source.EnqueueChange(Change("dev-a", DeviceChangeKind.Removal));
        pump.RunOnce();
        source.EnqueueChange(Change("dev-a", DeviceChangeKind.Arrival));
        pump.RunOnce();
        emitter.Emitted.Clear();

        // 切断前に押していた control の up（幽霊 up）は何も送出しない
        source.EnqueueInput(Edge("dev-a", "G6", PhysicalInputEdge.Up, 3));
        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Up, 4));
        pump.RunOnce();
        Assert.Empty(emitter.Emitted);

        // 再接続後の新規 down は default layer で解決される（shift の F20 ではなく F13）
        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, 5));
        source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Up, 6));
        pump.RunOnce();
        Assert.Equal(
            [
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down),
                new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up),
            ],
            emitter.Emitted);
    }

    [Fact]
    public void Changes_for_unconfigured_devices_are_ignored()
    {
        var (source, runtime, emitter, pump) = Setup();

        source.EnqueueChange(Change("dev-unknown", DeviceChangeKind.Removal));
        source.EnqueueChange(Change("dev-unknown", DeviceChangeKind.Arrival));
        pump.RunOnce();

        Assert.Empty(emitter.Emitted);
        Assert.True(runtime.AcceptsNewDowns);
        Assert.Null(pump.Failure);
    }

    [Fact]
    public void Thousand_hotplug_cycles_leave_no_stuck_output_and_no_wrong_release()
    {
        var (source, runtime, emitter, pump) = Setup();

        // 固定 LCG（million replay suite と同系）で押下 pattern を変えながら 1,000 回抜挿する
        var lcgState = 20260816UL;
        ulong NextLcg() => lcgState = lcgState * 6364136223846793005UL + 1442695040888963407UL;

        var liveDowns = new HashSet<string>(StringComparer.Ordinal);
        var sequence = 0L;
        var emittedTotal = 0;

        void DrainAndVerify()
        {
            pump.RunOnce();
            foreach (var edge in emitter.Emitted)
            {
                emittedTotal++;
                if (edge.Edge == PhysicalInputEdge.Down)
                {
                    Assert.True(liveDowns.Add(edge.Output), $"二重 down: {edge.Output}");
                }
                else
                {
                    Assert.True(liveDowns.Remove(edge.Output), $"wrong release: {edge.Output}");
                }
            }

            emitter.Emitted.Clear();
        }

        for (var cycle = 0; cycle < 1_000; cycle++)
        {
            var bits = NextLcg() >> 32;
            var heldControls = new List<string>();
            if ((bits & 1) != 0)
            {
                source.EnqueueInput(Edge("dev-a", "G6", PhysicalInputEdge.Down, ++sequence));
                heldControls.Add("G6");
            }

            if ((bits & 2) != 0)
            {
                source.EnqueueInput(Edge("dev-a", "G9", PhysicalInputEdge.Down, ++sequence));
                heldControls.Add("G9");
            }

            if ((bits & 4) != 0)
            {
                source.EnqueueInput(Edge("dev-a", "G10", PhysicalInputEdge.Down, ++sequence));
                heldControls.Add("G10");
            }

            DrainAndVerify();

            source.EnqueueChange(Change("dev-a", DeviceChangeKind.Removal));
            DrainAndVerify();
            Assert.Empty(liveDowns);
            Assert.False(runtime.AcceptsNewDowns);

            source.EnqueueChange(Change("dev-a", DeviceChangeKind.Arrival));
            DrainAndVerify();
            Assert.True(runtime.AcceptsNewDowns);

            // 切断前に押していた control の幽霊 up は無送出でなければならない
            foreach (var control in heldControls)
            {
                source.EnqueueInput(Edge("dev-a", control, PhysicalInputEdge.Up, ++sequence));
            }

            pump.RunOnce();
            Assert.Empty(emitter.Emitted);
        }

        Assert.Empty(liveDowns);
        Assert.True(emittedTotal > 0);
        Assert.Null(pump.Failure);
    }
}
