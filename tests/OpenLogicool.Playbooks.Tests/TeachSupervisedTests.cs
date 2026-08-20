using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class TeachSupervisedTests
{
    [Fact]
    public void fake_plannerのTeach提案は明示承認まで承認待ちに留まる()
    {
        var planner = new FakePlanner(TeachProposal());

        var pending = TeachSupervised.Request(planner, Context());
        var approved = TeachSupervised.Approve(pending, "approval-1");

        Assert.Equal(1, planner.CallCount);
        Assert.Equal(ProposalMode.Teach, pending.Proposal.Mode);
        Assert.Equal("approval-1", approved.ApprovalId);
        Assert.Same(pending.Proposal, approved.Proposal);
    }

    [Fact]
    public void Teach以外のproposalを拒否する()
    {
        var verified = TeachProposal() with
        {
            Mode = ProposalMode.VerifiedRun,
            Action = new VerifiedRunAction(ContractSchemaVersions.Revision01, "action.open-menu"),
        };

        Assert.Throws<ArgumentException>(() => TeachSupervised.Stage(verified));
    }

    private static PlannerContext Context() => new(
        ContractSchemaVersions.Revision01,
        "次の画面へ進む",
        "phase5:frozen-menu",
        ["action.open-menu"],
        "frozen corpus context",
        new PlannerBudget(ContractSchemaVersions.Revision01, 1, 1m));

    private static NextActionProposal TeachProposal() => new(
        ContractSchemaVersions.Revision01,
        "proposal-teach-1",
        Context(),
        ProposalMode.Teach,
        new TeachAction(ContractSchemaVersions.Revision01, "target:menu", "click"),
        new ProposalPrecondition(ContractSchemaVersions.Revision01, "state:entry", 100),
        new ProposalExpectedOutcome(
            ContractSchemaVersions.Revision01,
            "state:menu",
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
