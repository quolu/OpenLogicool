using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class DeviceMappingRuntimeTests
{
    private const string DeviceId = "device-1";

    private static long _sequence;

    private static PhysicalInput Edge(string controlId, PhysicalInputEdge edge, string deviceId = DeviceId) =>
        new(
            ContractSchemaVersions.Revision01,
            deviceId,
            controlId,
            edge,
            MonotonicMs: 0,
            ReportSequence: ++_sequence);

    private static MappingProfile Profile(
        string profileRevision = "profile-r1",
        string mappingRevision = "map-r1") =>
        new(
            profileRevision,
            mappingRevision,
            defaultLayerId: "base",
            layerIds: ["base", "m2", "shift"],
            latchSelectors: new Dictionary<string, string> { ["M2"] = "m2", ["M1"] = "base" },
            holdSelectors: new Dictionary<string, string> { ["G6"] = "shift" },
            bindings:
            [
                new MappingBinding("G1", "base", ["Key:A"]),
                new MappingBinding("G1", "m2", ["Key:B"]),
                new MappingBinding("G1", "shift", ["Key:C"]),
                new MappingBinding("G2", "base", ["Key:X", "Key:Y"]),
            ]);

    private static DeviceMappingRuntime CreateRuntime() => new(DeviceId, Profile());

    [Fact]
    public void Mapped_down_and_up_emit_the_bound_outputs()
    {
        var runtime = CreateRuntime();

        var downs = runtime.Process(Edge("G2", PhysicalInputEdge.Down));
        Assert.Equal(
            [new MappedOutputEdge("Key:X", PhysicalInputEdge.Down), new MappedOutputEdge("Key:Y", PhysicalInputEdge.Down)],
            downs);

        var ups = runtime.Process(Edge("G2", PhysicalInputEdge.Up));
        Assert.Equal(
            [new MappedOutputEdge("Key:X", PhysicalInputEdge.Up), new MappedOutputEdge("Key:Y", PhysicalInputEdge.Up)],
            ups);
    }

    [Fact]
    public void Unmapped_control_produces_no_outputs_and_no_error()
    {
        var runtime = CreateRuntime();

        Assert.Empty(runtime.Process(Edge("G20", PhysicalInputEdge.Down)));
        Assert.Empty(runtime.Process(Edge("G20", PhysicalInputEdge.Up)));
    }

    [Fact]
    public void Latch_selector_switches_layer_for_new_downs()
    {
        var runtime = CreateRuntime();

        Assert.Empty(runtime.Process(Edge("M2", PhysicalInputEdge.Down)));
        Assert.Empty(runtime.Process(Edge("M2", PhysicalInputEdge.Up)));
        Assert.Equal("m2", runtime.CurrentLayerId);

        var downs = runtime.Process(Edge("G1", PhysicalInputEdge.Down));
        Assert.Equal([new MappedOutputEdge("Key:B", PhysicalInputEdge.Down)], downs);
    }

    [Fact]
    public void Press_held_across_latch_change_releases_down_time_outputs()
    {
        var runtime = CreateRuntime();

        runtime.Process(Edge("G1", PhysicalInputEdge.Down));
        runtime.Process(Edge("M2", PhysicalInputEdge.Down));

        var ups = runtime.Process(Edge("G1", PhysicalInputEdge.Up));
        Assert.Equal([new MappedOutputEdge("Key:A", PhysicalInputEdge.Up)], ups);
    }

    [Fact]
    public void Hold_selector_overrides_layer_only_while_held()
    {
        var runtime = CreateRuntime();

        Assert.Empty(runtime.Process(Edge("G6", PhysicalInputEdge.Down)));
        Assert.Equal("shift", runtime.CurrentLayerId);
        Assert.Equal(
            [new MappedOutputEdge("Key:C", PhysicalInputEdge.Down)],
            runtime.Process(Edge("G1", PhysicalInputEdge.Down)));
        Assert.Equal(
            [new MappedOutputEdge("Key:C", PhysicalInputEdge.Up)],
            runtime.Process(Edge("G1", PhysicalInputEdge.Up)));

        Assert.Empty(runtime.Process(Edge("G6", PhysicalInputEdge.Up)));
        Assert.Equal("base", runtime.CurrentLayerId);
        Assert.Equal(
            [new MappedOutputEdge("Key:A", PhysicalInputEdge.Down)],
            runtime.Process(Edge("G1", PhysicalInputEdge.Down)));
    }

    [Fact]
    public void Press_held_across_hold_exit_releases_hold_layer_outputs()
    {
        var runtime = CreateRuntime();

        runtime.Process(Edge("G6", PhysicalInputEdge.Down));
        runtime.Process(Edge("G1", PhysicalInputEdge.Down));
        runtime.Process(Edge("G6", PhysicalInputEdge.Up));

        var ups = runtime.Process(Edge("G1", PhysicalInputEdge.Up));
        Assert.Equal([new MappedOutputEdge("Key:C", PhysicalInputEdge.Up)], ups);
    }

    [Fact]
    public void Profile_change_keeps_old_outputs_for_held_press_and_uses_new_profile_for_new_downs()
    {
        var runtime = CreateRuntime();
        runtime.Process(Edge("M2", PhysicalInputEdge.Down));
        runtime.Process(Edge("G1", PhysicalInputEdge.Down));

        var newProfile = new MappingProfile(
            "profile-r2",
            "map-r2",
            defaultLayerId: "base",
            layerIds: ["base"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string>(),
            bindings: [new MappingBinding("G1", "base", ["Key:Z"])]);
        runtime.ApplyProfile(newProfile);

        Assert.Equal("base", runtime.CurrentLayerId);
        Assert.Equal(
            [new MappedOutputEdge("Key:B", PhysicalInputEdge.Up)],
            runtime.Process(Edge("G1", PhysicalInputEdge.Up)));
        Assert.Equal(
            [new MappedOutputEdge("Key:Z", PhysicalInputEdge.Down)],
            runtime.Process(Edge("G1", PhysicalInputEdge.Down)));
    }

    [Fact]
    public void Hold_selector_up_after_profile_change_is_ignored()
    {
        var runtime = CreateRuntime();
        runtime.Process(Edge("G6", PhysicalInputEdge.Down));

        runtime.ApplyProfile(Profile("profile-r2", "map-r2"));

        Assert.Empty(runtime.Process(Edge("G6", PhysicalInputEdge.Up)));
        Assert.Equal("base", runtime.CurrentLayerId);
    }

    [Fact]
    public void Stop_releases_all_owned_outputs_and_suppresses_new_downs()
    {
        var runtime = CreateRuntime();
        runtime.Process(Edge("G1", PhysicalInputEdge.Down));
        runtime.Process(Edge("G2", PhysicalInputEdge.Down));

        var releases = runtime.StopAndReleaseAll();
        Assert.False(runtime.AcceptsNewDowns);
        Assert.Equal(
            new HashSet<MappedOutputEdge>
            {
                new("Key:A", PhysicalInputEdge.Up),
                new("Key:X", PhysicalInputEdge.Up),
                new("Key:Y", PhysicalInputEdge.Up),
            },
            releases.ToHashSet());

        Assert.Empty(runtime.Process(Edge("G1", PhysicalInputEdge.Down)));
        Assert.Empty(runtime.Process(Edge("G1", PhysicalInputEdge.Up)));
        Assert.Empty(runtime.Process(Edge("G2", PhysicalInputEdge.Up)));
    }

    [Fact]
    public void Input_from_another_device_instance_is_rejected()
    {
        var runtime = CreateRuntime();

        Assert.Throws<ArgumentException>(() =>
            runtime.Process(Edge("G1", PhysicalInputEdge.Down, deviceId: "device-2")));
    }
}
