using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class MappingProfileTests
{
    private static readonly string[] Layers = ["base", "m2", "shift"];

    private static MappingProfile Create(
        string defaultLayer = "base",
        IReadOnlyDictionary<string, string>? latch = null,
        IReadOnlyDictionary<string, string>? hold = null,
        IEnumerable<MappingBinding>? bindings = null) =>
        new(
            profileRevision: "profile-r1",
            mappingRevision: "map-r1",
            defaultLayerId: defaultLayer,
            layerIds: Layers,
            latchSelectors: latch ?? new Dictionary<string, string>(),
            holdSelectors: hold ?? new Dictionary<string, string>(),
            bindings: bindings ?? []);

    [Fact]
    public void Default_layer_must_exist()
    {
        Assert.Throws<ArgumentException>(() => Create(defaultLayer: "missing"));
    }

    [Fact]
    public void Selector_target_layer_must_exist()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(latch: new Dictionary<string, string> { ["M2"] = "missing" }));
        Assert.Throws<ArgumentException>(() =>
            Create(hold: new Dictionary<string, string> { ["G6"] = "missing" }));
    }

    [Fact]
    public void Control_cannot_be_both_latch_and_hold_selector()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(
                latch: new Dictionary<string, string> { ["M2"] = "m2" },
                hold: new Dictionary<string, string> { ["M2"] = "shift" }));
    }

    [Fact]
    public void Selector_control_cannot_have_output_binding()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(
                latch: new Dictionary<string, string> { ["M2"] = "m2" },
                bindings: [new MappingBinding("M2", "base", ["Key:A"])]));
        Assert.Throws<ArgumentException>(() =>
            Create(
                hold: new Dictionary<string, string> { ["G6"] = "shift" },
                bindings: [new MappingBinding("G6", "base", ["Key:A"])]));
    }

    [Fact]
    public void Binding_layer_must_exist()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(bindings: [new MappingBinding("G1", "missing", ["Key:A"])]));
    }

    [Fact]
    public void Binding_outputs_must_not_be_empty()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(bindings: [new MappingBinding("G1", "base", [])]));
    }

    [Fact]
    public void Duplicate_binding_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Create(bindings:
            [
                new MappingBinding("G1", "base", ["Key:A"]),
                new MappingBinding("G1", "base", ["Key:B"]),
            ]));
    }

    [Fact]
    public void Resolve_returns_bound_outputs_per_layer()
    {
        var profile = Create(bindings:
        [
            new MappingBinding("G1", "base", ["Key:A"]),
            new MappingBinding("G1", "m2", ["Key:B", "Key:C"]),
        ]);

        Assert.True(profile.TryResolve("G1", "base", out var baseOutputs));
        Assert.Equal(["Key:A"], baseOutputs);
        Assert.True(profile.TryResolve("G1", "m2", out var m2Outputs));
        Assert.Equal(["Key:B", "Key:C"], m2Outputs);
        Assert.False(profile.TryResolve("G1", "shift", out _));
        Assert.False(profile.TryResolve("G2", "base", out _));
    }
}
