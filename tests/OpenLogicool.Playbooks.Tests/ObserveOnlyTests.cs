using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class ObserveOnlyTests
{
    [Fact]
    public void Observe_returns_the_planner_proposal_without_dispatching()
    {
        var proposal = Proposal();
        var planner = new RecordingPlanner(proposal);
        var observeOnly = new ObserveOnly(planner);
        var context = proposal.PlannerContextRef;

        var observed = observeOnly.Observe(context);

        Assert.Same(proposal, observed);
        Assert.Same(context, planner.ReceivedContext);
        Assert.Equal(1, planner.CallCount);
    }

    [Fact]
    public void Observe_only_surface_has_no_attempt_journal_dispatch_or_playbook_version_dependency()
    {
        var dependencyTypes = typeof(ObserveOnly)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .Append(typeof(ObserveOnly).GetConstructors().Single().GetParameters().Single().ParameterType);

        Assert.DoesNotContain(typeof(AttemptDispatchGate), dependencyTypes);
        Assert.DoesNotContain(typeof(RunJournal), dependencyTypes);
        Assert.DoesNotContain(typeof(PlaybookVersion), dependencyTypes);
    }

    private static NextActionProposal Proposal() => new(
        ContractSchemaVersions.Revision01,
        "proposal-1",
        new PlannerContext(
            ContractSchemaVersions.Revision01,
            "observe",
            "observation-1",
            ["action:advance"],
            "menu observed",
            new PlannerBudget(ContractSchemaVersions.Revision01, 1, 0)),
        ProposalMode.VerifiedRun,
        new VerifiedRunAction(ContractSchemaVersions.Revision01, "action:advance"),
        new ProposalPrecondition(ContractSchemaVersions.Revision01, "state:menu", 100),
        new ProposalExpectedOutcome(
            ContractSchemaVersions.Revision01,
            "state:done",
            new StabilityWindow(ContractSchemaVersions.Revision01, 2, 100)),
        new ProposalStopCondition(ContractSchemaVersions.Revision01, 1_000, "stop"),
        new ProposalValidity(ContractSchemaVersions.Revision01, 1, 1));

    private sealed class RecordingPlanner(NextActionProposal proposal) : INextActionPlanner
    {
        public int CallCount { get; private set; }

        public PlannerContext? ReceivedContext { get; private set; }

        public NextActionProposal Propose(PlannerContext plannerContext)
        {
            CallCount++;
            ReceivedContext = plannerContext;
            return proposal;
        }
    }
}
