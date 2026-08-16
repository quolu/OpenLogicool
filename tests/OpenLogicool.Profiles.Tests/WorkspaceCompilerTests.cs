using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Profiles;
using Xunit;

namespace OpenLogicool.Profiles.Tests;

public sealed class WorkspaceCompilerTests
{
    private static WorkspaceDocument Document(
        IReadOnlyList<WorkspaceActionEntry>? actions = null,
        IReadOnlyList<WorkspaceDeviceLayout>? devices = null,
        IReadOnlyList<WorkspaceActionBinding>? bindings = null,
        string schemaVersion = ContractSchemaVersions.Revision01) =>
        new(
            schemaVersion,
            WorkspaceId: "ws",
            ProfileRevision: "rev-1",
            MappingRevision: "map-1",
            actions ?? [new WorkspaceActionEntry("dodge", "回避", ["Key:Space"])],
            devices ?? [Layout("G13"), Layout("G600")],
            bindings ?? []);

    private static WorkspaceDeviceLayout Layout(
        string deviceKind,
        IReadOnlyList<string>? layerIds = null,
        IReadOnlyList<LayerSelectorEntry>? holdSelectors = null) =>
        new(
            deviceKind,
            DefaultLayerId: "base",
            layerIds ?? ["base"],
            LatchSelectors: [],
            holdSelectors ?? []);

    [Fact]
    public void One_action_compiles_to_bindings_on_both_devices()
    {
        var compilation = WorkspaceCompiler.Compile(Document(bindings:
        [
            new WorkspaceActionBinding("dodge", "G13", "G1", "base"),
            new WorkspaceActionBinding("dodge", "G600", "G9", "base"),
        ]));

        Assert.Equal(2, compilation.Profiles.Count);
        Assert.Empty(compilation.Warnings);

        var g13 = Assert.Single(compilation.Profiles, profile => profile.DeviceKind == "G13");
        Assert.Equal("ws-G13", g13.ProfileId);
        var g13Binding = Assert.Single(g13.Bindings);
        Assert.Equal(("G1", "base"), (g13Binding.ControlId, g13Binding.LayerId));
        Assert.Equal(["Key:Space"], g13Binding.Outputs);

        var g600 = Assert.Single(compilation.Profiles, profile => profile.DeviceKind == "G600");
        Assert.Equal("ws-G600", g600.ProfileId);
        Assert.Equal(["Key:Space"], Assert.Single(g600.Bindings).Outputs);
    }

    [Fact]
    public void Unassigned_action_is_a_warning_not_an_error()
    {
        var compilation = WorkspaceCompiler.Compile(Document());

        Assert.Equal(2, compilation.Profiles.Count);
        var warning = Assert.Single(compilation.Warnings);
        Assert.Contains("未割当", warning);
        Assert.Contains("dodge", warning);
    }

    [Fact]
    public void Unreachable_layer_is_a_warning()
    {
        var reachable = Layout(
            "G600",
            layerIds: ["base", "shift"],
            holdSelectors: [new LayerSelectorEntry("G6", "shift")]);
        var unreachable = Layout("G13", layerIds: ["base", "m2"]);

        var compilation = WorkspaceCompiler.Compile(Document(
            devices: [unreachable, reachable],
            bindings: [new WorkspaceActionBinding("dodge", "G13", "G1", "base")]));

        var warning = Assert.Single(compilation.Warnings);
        Assert.Contains("到達不能", warning);
        Assert.Contains("'m2'", warning);
    }

    [Fact]
    public void Binding_to_unknown_action_or_unknown_device_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceCompiler.Compile(Document(bindings:
            [new WorkspaceActionBinding("missing", "G13", "G1", "base")])));

        Assert.Throws<ArgumentException>(() => WorkspaceCompiler.Compile(Document(bindings:
            [new WorkspaceActionBinding("dodge", "G999", "G1", "base")])));
    }

    [Fact]
    public void Colliding_bindings_are_rejected_via_domain_validation()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceCompiler.Compile(Document(
            actions:
            [
                new WorkspaceActionEntry("dodge", "回避", ["Key:Space"]),
                new WorkspaceActionEntry("attack", "攻撃", ["Key:F"]),
            ],
            bindings:
            [
                new WorkspaceActionBinding("dodge", "G13", "G1", "base"),
                new WorkspaceActionBinding("attack", "G13", "G1", "base"),
            ])));
    }

    [Fact]
    public void Duplicate_actions_and_unknown_schema_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceCompiler.Compile(Document(actions:
            [
                new WorkspaceActionEntry("dodge", "回避", ["Key:Space"]),
                new WorkspaceActionEntry("dodge", "回避2", ["Key:X"]),
            ])));

        Assert.Throws<ArgumentException>(() => WorkspaceCompiler.Compile(Document(schemaVersion: "9.9.9")));
    }
}
