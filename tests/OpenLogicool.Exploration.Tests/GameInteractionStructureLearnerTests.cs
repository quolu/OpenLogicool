using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using Xunit;

namespace OpenLogicool.Exploration.Tests;

public sealed class GameInteractionStructureLearnerTests
{
    [Fact]
    public void Exact_duplicate_scene_signatures_reuse_one_deterministic_active_node()
    {
        var nodes = new[]
        {
            Node("state:z", "signature:same"),
            Node("state:a", "signature:same"),
            Node("state:retired", "signature:same") with { Retired = true },
            Node("state:other", "signature:other"),
        };

        var method = typeof(GameInteractionStructureLearner).GetMethod(
            "SelectExistingNode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var selected = (StructureScreenNode?)method!.Invoke(null, [nodes, "signature:same"]);

        Assert.Equal("state:a", selected?.StateId);
    }

    private static StructureScreenNode Node(string stateId, string signatureId) => new(
        ContractSchemaVersions.Revision03,
        stateId,
        "env",
        [signatureId],
        [],
        ["evidence"],
        null,
        StructureVerificationState.Candidate);
}
