using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Conformance.Tests;

public sealed class PlannerProposalSchemaTests
{
    [Fact]
    public void 完全な学習提案を受理する()
    {
        PlannerProposalSchema.Validate(学習提案());
    }

    [Fact]
    public void ネストした未知のスキーマ版を拒否する()
    {
        var 元の提案 = 学習提案();
        var 提案 = 元の提案 with
        {
            ExpectedOutcome = 元の提案.ExpectedOutcome with
            {
                StabilityWindow = 元の提案.ExpectedOutcome.StabilityWindow with
                {
                    SchemaVersion = "0.2.0",
                },
            },
        };

        Assert.Throws<ArgumentException>(() => PlannerProposalSchema.Validate(提案));
    }

    [Fact]
    public void 種別と操作の不一致を拒否する()
    {
        var 提案 = 学習提案() with { Mode = ProposalMode.VerifiedRun };

        Assert.Throws<ArgumentException>(() => PlannerProposalSchema.Validate(提案));
    }

    private static NextActionProposal 学習提案() => new(
        ContractSchemaVersions.Revision01,
        "proposal-1",
        new PlannerContext(
            ContractSchemaVersions.Revision01,
            "メニューを開く",
            "observation-1",
            ["action.open-menu"],
            "入口画面を観測済み",
            new PlannerBudget(ContractSchemaVersions.Revision01, 3, 1.25m)),
        ProposalMode.Teach,
        new TeachAction(ContractSchemaVersions.Revision01, "target:menu", "click"),
        new ProposalPrecondition(ContractSchemaVersions.Revision01, "state:entry", 500),
        new ProposalExpectedOutcome(
            ContractSchemaVersions.Revision01,
            "state:menu",
            new StabilityWindow(ContractSchemaVersions.Revision01, 2, 100)),
        new ProposalStopCondition(ContractSchemaVersions.Revision01, 2_000, "pause"),
        new ProposalValidity(ContractSchemaVersions.Revision01, 4, 1));
}
