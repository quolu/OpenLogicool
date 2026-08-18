using System.Reflection;
using System.Text.Json;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class PlaybookVersioningTests
{
    [Fact]
    public void Start_pins_the_version_id_and_does_not_expose_a_swap()
    {
        var version = PlaybookMaterializer.ToGraph(PlaybookGraphTests.ValidDocument());
        var run = PlaybookRun.Start("playbook-1", version);

        Assert.Equal("playbook-1", run.PlaybookId);
        Assert.Equal("ver-1", run.PinnedVersionId);
        Assert.Same(version, run.PinnedVersion);

        var swap = typeof(PlaybookRun).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(PlaybookRun))
            .Where(method =>
                method.Name.Contains("Version", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(method.Name, "Start", StringComparison.Ordinal)
                && !string.Equals(method.Name, "get_PinnedVersion", StringComparison.Ordinal)
                && !string.Equals(method.Name, "get_PinnedVersionId", StringComparison.Ordinal));
        Assert.Empty(swap);
    }

    [Fact]
    public void Start_rejects_an_empty_playbook_id()
    {
        var version = PlaybookMaterializer.ToGraph(PlaybookGraphTests.ValidDocument());

        Assert.Throws<ArgumentException>(() => PlaybookRun.Start(" ", version));
    }

    [Fact]
    public void Correction_creates_a_new_version_without_changing_the_old_bytes()
    {
        var current = PlaybookGraphTests.ValidDocument();
        var nodesBefore = JsonSerializer.Serialize(current.Nodes);
        var edgesBefore = JsonSerializer.Serialize(current.Edges);
        var versionBefore = JsonSerializer.Serialize(current);

        var revisedNodes = new List<PlaybookNode>
        {
            PlaybookGraphTests.Entry("menu", "state:menu"),
            PlaybookGraphTests.Node(
                "open-menu",
                isEntry: false,
                "state:menu",
                ["state:menu"],
                "action.open-menu",
                ["state:lobby", "state:ready"]),
            PlaybookGraphTests.Node("lobby", isEntry: false, "state:lobby", [], null, []),
        };
        var revisedEdges = new List<PlaybookEdge>
        {
            PlaybookGraphTests.Edge("menu-to-open", "menu", "open-menu", null),
            PlaybookGraphTests.Edge("open-to-lobby", "open-menu", "lobby", "confirmed"),
        };

        var revised = PlaybookCorrection.Revise(current, "ver-2", revisedNodes, revisedEdges, "lobby 期待を追加");

        Assert.Equal("ver-2", revised.VersionId);
        Assert.Equal("ver-1", revised.ParentVersionId);
        Assert.Equal("lobby 期待を追加", revised.ChangeReason);
        Assert.Equal(3, revised.Nodes.Count);
        Assert.DoesNotContain(revised.Nodes, node => node.NodeId == "error");

        Assert.Equal(nodesBefore, JsonSerializer.Serialize(current.Nodes));
        Assert.Equal(edgesBefore, JsonSerializer.Serialize(current.Edges));
        Assert.Equal(versionBefore, JsonSerializer.Serialize(current));

        var graph = PlaybookMaterializer.ToGraph(revised);
        Assert.Equal("ver-1", graph.ParentVersionId);
        Assert.Equal(["state:lobby", "state:ready"], graph.Nodes.Single(node => node.NodeId == "open-menu").ExpectedOutcomes);
    }

    [Fact]
    public void Correction_does_not_reuse_the_caller_node_list()
    {
        var current = PlaybookGraphTests.ValidDocument();
        var revisedNodes = new List<PlaybookNode>
        {
            PlaybookGraphTests.Entry("menu", "state:menu"),
        };
        var revisedEdges = new List<PlaybookEdge>();

        var revised = PlaybookCorrection.Revise(current, "ver-2", revisedNodes, revisedEdges, "単一入口へ縮小");
        revisedNodes.Add(PlaybookGraphTests.Node("ghost", isEntry: false, "state:ghost", [], null, []));

        Assert.Single(revised.Nodes);
        var graph = PlaybookMaterializer.ToGraph(revised);
        Assert.Equal(["menu"], graph.EntryNodeIds);
    }

    [Fact]
    public void Correction_rejects_reusing_the_same_version_id()
    {
        var current = PlaybookGraphTests.ValidDocument();

        Assert.Throws<ArgumentException>(() =>
            PlaybookCorrection.Revise(current, current.VersionId, current.Nodes, current.Edges, "same id"));
    }

    [Fact]
    public void Correction_rejects_an_invalid_revised_graph()
    {
        var current = PlaybookGraphTests.ValidDocument();
        var invalidNodes = new List<PlaybookNode>
        {
            PlaybookGraphTests.Node("no-entry", isEntry: false, "state:x", [], null, []),
        };

        Assert.Throws<ArgumentException>(() =>
            PlaybookCorrection.Revise(current, "ver-2", invalidNodes, [], "invalid"));
    }

    [Fact]
    public void Playbooks_module_does_not_expose_run_event_mutation()
    {
        var mutators = typeof(PlaybookCorrection).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method =>
                method.GetParameters().Any(parameter => parameter.ParameterType == typeof(RunEvent))
                && (method.ReturnType == typeof(RunEvent) || method.Name.Contains("Mutat", StringComparison.OrdinalIgnoreCase)));

        Assert.Empty(mutators);
    }

    [Fact]
    public void Domain_playbook_run_has_no_run_event_append_or_mutate_surface()
    {
        var eventTouching = typeof(PlaybookRun).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(RunEvent)));

        Assert.Empty(eventTouching);
    }
}
