using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class StructurePlaybookSynthesizerTests
{
    [Fact]
    public void ReplayedLoopBecomesSupervisedCandidatePlaybook()
    {
        var result = StructurePlaybookSynthesizer.Synthesize(
            Revision(),
            ["edge:open", "edge:back"],
            "playbook:live-loop:v1",
            StructurePlaybookExecutionMode.Supervised);

        Assert.Equal(StructureVerificationState.Replayed, result.WeakestEvidence);
        Assert.Equal(["edge:open", "edge:back"], result.StructureEdgeIds);
        Assert.Equal(3, result.Playbook.Nodes.Count);
        Assert.Equal("structure-edge:edge:open", result.Playbook.Nodes[0].SemanticActionId);
        Assert.Equal("state:source", result.Playbook.Nodes[^1].StateId);
        _ = PlaybookMaterializer.ToGraph(result.Playbook);
    }

    [Fact]
    public void ReplayedRouteCannotClaimVerifiedExecution()
    {
        Assert.Throws<InvalidOperationException>(() => StructurePlaybookSynthesizer.Synthesize(
            Revision(),
            ["edge:open"],
            "playbook:invalid",
            StructurePlaybookExecutionMode.Verified));
    }

    [Fact]
    public void DiscontinuousRouteIsRejected()
    {
        var revision = Revision() with
        {
            ScreenGraph = Revision().ScreenGraph with
            {
                Edges = [Edge("edge:open", "state:source", "state:destination"), Edge("edge:other", "state:source", "state:destination")],
            },
        };

        Assert.Throws<InvalidOperationException>(() => StructurePlaybookSynthesizer.Synthesize(
            revision,
            ["edge:open", "edge:other"],
            "playbook:invalid",
            StructurePlaybookExecutionMode.Supervised));
    }

    [Fact]
    public void RetiredEdgeIsRejected()
    {
        var revision = Revision() with
        {
            ScreenGraph = Revision().ScreenGraph with
            {
                Edges = [Edge("edge:open", "state:source", "state:destination") with { Retired = true }],
            },
        };

        Assert.Throws<InvalidOperationException>(() => StructurePlaybookSynthesizer.Synthesize(
            revision,
            ["edge:open"],
            "playbook:invalid",
            StructurePlaybookExecutionMode.Supervised));
    }

    [Fact]
    public void CrossEnvironmentNodeIsRejected()
    {
        var revision = Revision();
        var foreign = revision.ScreenGraph.Nodes[1] with { EnvironmentScope = "different-environment" };
        revision = revision with
        {
            ScreenGraph = revision.ScreenGraph with { Nodes = [revision.ScreenGraph.Nodes[0], foreign] },
        };

        Assert.Throws<InvalidOperationException>(() => StructurePlaybookSynthesizer.Synthesize(
            revision,
            ["edge:open"],
            "playbook:invalid",
            StructurePlaybookExecutionMode.Supervised));
    }

    private static GameStructureRevision Revision()
    {
        var now = DateTimeOffset.UnixEpoch;
        return new GameStructureRevision(
            ContractSchemaVersions.Revision03,
            "structure:live:v2",
            "structure:live:v1",
            12,
            new StructureScreenGraph(
                ContractSchemaVersions.Revision03,
                "graph:live:v2",
                [Node("state:source"), Node("state:destination")],
                [Edge("edge:open", "state:source", "state:destination"), Edge("edge:back", "state:destination", "state:source")],
                [],
                "windows11-ja/nikke-live"),
            [],
            [],
            "windows11-ja/nikke-live",
            now);
    }

    private static StructureScreenNode Node(string id) => new(
        ContractSchemaVersions.Revision03,
        id,
        "windows11-ja/nikke-live",
        [$"signature:{id}"],
        [],
        [$"evidence:{id}"],
        null,
        StructureVerificationState.Replayed);

    private static StructureScreenEdge Edge(string id, string source, string destination) => new(
        ContractSchemaVersions.Revision03,
        id,
        source,
        destination,
        null,
        $"affordance:{id}",
        "locator:v1",
        "click",
        "supervised",
        [],
        true,
        $"before:{id}",
        $"after:{id}",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 300, 10000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 2)],
        [$"evidence:{id}"],
        StructureVerificationState.Replayed);
}
