using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class ShadowCompareTests
{
    [Fact]
    public void fake_plannerのproposalと利用者操作を比較するだけで一致を返す()
    {
        var proposal = VerifiedProposal("action.open-menu");
        var planner = new FakePlanner(proposal);

        var comparison = ShadowCompare.Observe(planner, Context(), "action.open-menu");

        Assert.Equal(1, planner.CallCount);
        Assert.Same(proposal, comparison.Proposal);
        Assert.True(comparison.IsMatch);
    }

    [Fact]
    public void Teach_proposalは利用者のsemantic_actionと一致扱いにしない()
    {
        var proposal = VerifiedProposal("action.open-menu") with
        {
            Mode = ProposalMode.Teach,
            Action = new TeachAction(ContractSchemaVersions.Revision01, "target:menu", "click"),
        };

        var comparison = ShadowCompare.Compare(proposal, "action.open-menu");

        Assert.False(comparison.IsMatch);
    }

    private static PlannerContext Context() => new(
        ContractSchemaVersions.Revision01,
        "メニューを開く",
        "observation-1",
        ["action.open-menu"],
        "shadow context",
        new PlannerBudget(ContractSchemaVersions.Revision01, 1, 1m));

    private static NextActionProposal VerifiedProposal(string actionId) => new(
        ContractSchemaVersions.Revision01,
        "proposal-1",
        Context(),
        ProposalMode.VerifiedRun,
        new VerifiedRunAction(ContractSchemaVersions.Revision01, actionId),
        new ProposalPrecondition(ContractSchemaVersions.Revision01, "state:menu", 100),
        new ProposalExpectedOutcome(
            ContractSchemaVersions.Revision01,
            "state:opened",
            new StabilityWindow(ContractSchemaVersions.Revision01, 1, 1)),
        new ProposalStopCondition(ContractSchemaVersions.Revision01, 1_000, "pause"),
        new ProposalValidity(ContractSchemaVersions.Revision01, 1, 1));

    private sealed class FakePlanner(NextActionProposal proposal) : INextActionPlanner
    {
        public int CallCount { get; private set; }

        public NextActionProposal Propose(PlannerContext plannerContext)
        {
            CallCount++;
            return proposal;
        }
    }
}
