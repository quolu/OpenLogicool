using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class PlaybookGraphTests
{
    [Fact]
    public void Valid_branching_graph_materializes_with_entry_action_and_outcomes()
    {
        var graph = PlaybookMaterializer.ToGraph(ValidDocument());

        Assert.Equal("ver-1", graph.VersionId);
        Assert.Null(graph.ParentVersionId);
        Assert.Equal(["menu"], graph.EntryNodeIds);
        Assert.Equal(4, graph.Nodes.Count);
        Assert.Equal(3, graph.Edges.Count);

        var open = graph.Nodes.Single(node => node.NodeId == "open-menu");
        Assert.Equal("action.open-menu", open.SemanticActionId);
        Assert.Equal(["state:menu"], open.Preconditions);
        Assert.Equal(["state:lobby"], open.ExpectedOutcomes);

        var failEdge = graph.Edges.Single(edge => edge.EdgeId == "open-to-error");
        Assert.Equal("rejected", failEdge.BranchCondition);
    }

    [Fact]
    public void Domain_constructor_accepts_the_same_valid_graph()
    {
        var graph = new PlaybookGraph(
            "ver-1",
            parentVersionId: null,
            "initial",
            [
                new PlaybookGraphNode("menu", IsEntry: true, "state:menu", [], null, []),
                new PlaybookGraphNode("done", IsEntry: false, "state:lobby", [], null, []),
            ],
            [new PlaybookGraphEdge("menu-to-done", "menu", "done", null)]);

        Assert.Equal(["menu"], graph.EntryNodeIds);
        Assert.Equal("done", graph.Edges[0].ToNodeId);
    }

    [Fact]
    public void Unknown_schema_version_is_rejected()
    {
        var document = ValidDocument() with { SchemaVersion = "9.9.9" };

        var exception = Assert.Throws<ArgumentException>(() => PlaybookMaterializer.ToGraph(document));
        Assert.Contains("9.9.9", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_node_id_is_rejected()
    {
        var document = ValidDocument() with
        {
            Nodes =
            [
                Entry("menu", "state:menu"),
                Entry("menu", "state:other"),
            ],
            Edges = [],
        };

        Assert.Throws<ArgumentException>(() => PlaybookMaterializer.ToGraph(document));
    }

    [Fact]
    public void Duplicate_edge_id_is_rejected()
    {
        var document = ValidDocument() with
        {
            Edges =
            [
                Edge("dup", "menu", "open-menu", "go"),
                Edge("dup", "open-menu", "lobby", "ok"),
            ],
        };

        Assert.Throws<ArgumentException>(() => PlaybookMaterializer.ToGraph(document));
    }

    [Fact]
    public void Edge_endpoints_must_exist()
    {
        var missingFrom = ValidDocument() with
        {
            Edges = [Edge("ghost", "no-such-from", "lobby", null)],
        };
        var missingTo = ValidDocument() with
        {
            Edges = [Edge("ghost", "menu", "no-such-to", null)],
        };

        Assert.Throws<ArgumentException>(() => PlaybookMaterializer.ToGraph(missingFrom));
        Assert.Throws<ArgumentException>(() => PlaybookMaterializer.ToGraph(missingTo));
    }

    [Fact]
    public void Missing_entry_node_is_rejected()
    {
        var document = ValidDocument() with
        {
            Nodes =
            [
                Node("menu", isEntry: false, "state:menu", [], null, []),
            ],
            Edges = [],
        };

        var exception = Assert.Throws<ArgumentException>(() => PlaybookMaterializer.ToGraph(document));
        Assert.Contains("入口", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_semantic_action_id_is_rejected()
    {
        var document = ValidDocument() with
        {
            Nodes =
            [
                Entry("menu", "state:menu"),
                Node("open-menu", isEntry: false, "state:menu", ["state:menu"], "", ["state:lobby"]),
            ],
            Edges = [Edge("menu-to-open", "menu", "open-menu", null)],
        };

        var exception = Assert.Throws<ArgumentException>(() => PlaybookMaterializer.ToGraph(document));
        Assert.Contains("SemanticActionId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unreachable_nodes_are_enumerated_and_rejected()
    {
        var document = ValidDocument() with
        {
            Nodes =
            [
                Entry("menu", "state:menu"),
                Node("orphan-b", isEntry: false, "state:b", [], null, []),
                Node("orphan-a", isEntry: false, "state:a", [], null, []),
            ],
            Edges = [],
        };

        var exception = Assert.Throws<ArgumentException>(() => PlaybookMaterializer.ToGraph(document));
        Assert.Contains("orphan-a", exception.Message, StringComparison.Ordinal);
        Assert.Contains("orphan-b", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("menu", exception.Message.Split(':')[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Cycle_from_an_entry_is_reachable()
    {
        var document = new PlaybookVersion(
            ContractSchemaVersions.Revision01,
            "ver-cycle",
            null,
            [
                Entry("retry", "state:retry"),
                Node("work", isEntry: false, "state:work", [], "action.work", []),
            ],
            [
                Edge("retry-to-work", "retry", "work", null),
                Edge("work-to-retry", "work", "retry", "again"),
            ],
            "cycle");

        var graph = PlaybookMaterializer.ToGraph(document);

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Equal(2, graph.Edges.Count);
    }

    [Fact]
    public void Empty_graph_is_rejected()
    {
        var document = new PlaybookVersion(
            ContractSchemaVersions.Revision01,
            "ver-empty",
            null,
            [],
            [],
            "empty");

        Assert.Throws<ArgumentException>(() => PlaybookMaterializer.ToGraph(document));
    }

    internal static PlaybookVersion ValidDocument() =>
        new(
            ContractSchemaVersions.Revision01,
            "ver-1",
            null,
            [
                Entry("menu", "state:menu"),
                Node("open-menu", isEntry: false, "state:menu", ["state:menu"], "action.open-menu", ["state:lobby"]),
                Node("lobby", isEntry: false, "state:lobby", [], null, []),
                Node("error", isEntry: false, "state:error", [], null, []),
            ],
            [
                Edge("menu-to-open", "menu", "open-menu", null),
                Edge("open-to-lobby", "open-menu", "lobby", "confirmed"),
                Edge("open-to-error", "open-menu", "error", "rejected"),
            ],
            "initial");

    internal static PlaybookNode Entry(string nodeId, string stateId) =>
        Node(nodeId, isEntry: true, stateId, [], null, []);

    internal static PlaybookNode Node(
        string nodeId,
        bool isEntry,
        string? stateId,
        IReadOnlyList<string> preconditions,
        string? semanticActionId,
        IReadOnlyList<string> expectedOutcomes) =>
        new(
            ContractSchemaVersions.Revision01,
            nodeId,
            isEntry,
            stateId,
            preconditions,
            semanticActionId,
            expectedOutcomes);

    internal static PlaybookEdge Edge(string edgeId, string from, string to, string? branch) =>
        new(ContractSchemaVersions.Revision01, edgeId, from, to, branch);
}
