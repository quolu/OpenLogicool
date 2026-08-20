using System.Text.Json;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class UnknownBranchAppendTests
{
    [Fact]
    public void Unknown_branchは訂正版だけへ追記し旧verified版を変えない()
    {
        var verified = PlaybookGraphTests.ValidDocument();
        var verifiedBytes = JsonSerializer.Serialize(verified);
        var unknown = new PlaybookNode(
            ContractSchemaVersions.Revision01,
            "unknown-reward",
            IsEntry: false,
            "state:unknown-reward",
            [],
            null,
            []);
        var branch = new PlaybookEdge(
            ContractSchemaVersions.Revision01,
            "open-to-unknown-reward",
            "open-menu",
            unknown.NodeId,
            "unknown");

        var revised = UnknownBranchAppend.Append(
            verified,
            "ver-2",
            unknown,
            branch,
            "未知報酬 branch を追記");

        Assert.Equal("ver-1", revised.ParentVersionId);
        Assert.DoesNotContain(verified.Nodes, node => node.NodeId == unknown.NodeId);
        Assert.DoesNotContain(verified.Edges, edge => edge.EdgeId == branch.EdgeId);
        Assert.Equal(verifiedBytes, JsonSerializer.Serialize(verified));
        Assert.Contains(revised.Nodes, node => node.NodeId == unknown.NodeId);
        Assert.Contains(revised.Edges, edge => edge.EdgeId == branch.EdgeId);
        _ = PlaybookMaterializer.ToGraph(revised);
    }

    [Fact]
    public void Unknown_branchは追加nodeを終点にしなければならない()
    {
        var unknown = PlaybookGraphTests.Node("unknown", isEntry: false, "state:unknown", [], null, []);
        var invalidBranch = PlaybookGraphTests.Edge("bad-branch", "open-menu", "lobby", "unknown");

        Assert.Throws<ArgumentException>(() =>
            UnknownBranchAppend.Append(
                PlaybookGraphTests.ValidDocument(),
                "ver-2",
                unknown,
                invalidBranch,
                "未知 branch"));
    }
}
