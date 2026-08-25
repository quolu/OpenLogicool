using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class MacroRouteComposerTests
{
    [Fact]
    public void Compose_concatenates_versions_in_order_without_mutating_sources()
    {
        var first = Route("macro-a", "version-a", ["edge-1"]);
        var second = Route("macro-b", "version-b", ["edge-2"]);
        var firstEdges = first.EdgeIds.ToArray();
        var secondEdges = second.EdgeIds.ToArray();

        var result = MacroRouteComposer.Compose(
            "macro-combined", "長い目的", [first, second], Structure(), DateTimeOffset.UnixEpoch);

        Assert.Equal(["edge-1", "edge-2"], result.EdgeIds);
        Assert.Equal(LearningRouteStatus.Compiled, result.Status);
        Assert.Equal(firstEdges, first.EdgeIds);
        Assert.Equal(secondEdges, second.EdgeIds);
    }

    [Fact]
    public void Compose_rejects_mixed_environment_or_non_contiguous_edges()
    {
        var mixed = Route("macro-b", "version-b", ["edge-2"]) with { EnvironmentScope = "other" };
        Assert.Throws<InvalidOperationException>(() => MacroRouteComposer.Compose(
            "combined", "goal", [Route("a", "v1", ["edge-1"]), mixed], Structure(), DateTimeOffset.UnixEpoch));

        Assert.ThrowsAny<Exception>(() => MacroRouteComposer.Compose(
            "combined", "goal", [Route("a", "v1", ["edge-2"]), Route("b", "v2", ["edge-1"])],
            Structure(), DateTimeOffset.UnixEpoch));
    }

    private static LearningRouteRevision Route(string id, string version, IReadOnlyList<string> edges) => new(
        ContractSchemaVersions.Revision03, id, 1, version, null, "game", "env", "structure:1",
        "goal", edges, LearningRouteAuthor.Ai, null, "test", LearningRouteStatus.Compiled, DateTimeOffset.UnixEpoch);

    private static GameStructureRevision Structure()
    {
        var nodes = new[] { Node("state-1"), Node("state-2"), Node("state-3") };
        var edges = new[] { Edge("edge-1", "state-1", "state-2"), Edge("edge-2", "state-2", "state-3") };
        return new GameStructureRevision(
            ContractSchemaVersions.Revision03, "structure:1", null, 1,
            new StructureScreenGraph(ContractSchemaVersions.Revision03, "graph:1", nodes, edges, [], "env"),
            [], [], "env", DateTimeOffset.UnixEpoch);
    }

    private static StructureScreenNode Node(string id) => new(
        ContractSchemaVersions.Revision03, id, "env", [], [], ["evidence"], id,
        StructureVerificationState.Replayed);

    private static StructureScreenEdge Edge(string id, string source, string destination) => new(
        ContractSchemaVersions.Revision03, id, source, destination, null, $"candidate:{id}", $"locator:{id}",
        "click", "guard", [], true, "before", "after",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 1)], ["evidence"],
        StructureVerificationState.Replayed,
        TargetSemanticKey: $"text|{id}|0|0", TargetNormalizedBounds: [0.1, 0.1, 0.1, 0.1]);
}
