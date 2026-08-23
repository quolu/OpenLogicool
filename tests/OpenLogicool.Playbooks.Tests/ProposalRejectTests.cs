using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Domain;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class ProposalRejectTests
{
    [Fact]
    public void Matching_verified_proposal_is_accepted_without_dispatch()
    {
        var decision = ProposalReject.Evaluate(
            Proposal(),
            Catalog(),
            currentStateId: "state:menu",
            expectedRisk: RiskClass.Medium);

        Assert.True(decision.IsAccepted);
        Assert.Null(decision.RejectionReason);
    }

    [Fact]
    public void Schema_mismatch_is_rejected_before_catalog_lookup()
    {
        var proposal = Proposal() with { SchemaVersion = "future" };

        var decision = ProposalReject.Evaluate(
            proposal,
            Catalog(),
            currentStateId: "state:menu",
            expectedRisk: RiskClass.Medium);

        Assert.Equal(ProposalRejectReason.Schema, decision.RejectionReason);
    }

    [Fact]
    public void Action_outside_the_catalog_is_rejected()
    {
        var proposal = Proposal("action:unknown");

        var decision = ProposalReject.Evaluate(
            proposal,
            Catalog(),
            currentStateId: "state:menu",
            expectedRisk: RiskClass.Medium);

        Assert.Equal(ProposalRejectReason.Catalog, decision.RejectionReason);
    }

    [Fact]
    public void State_mismatch_is_rejected()
    {
        var decision = ProposalReject.Evaluate(
            Proposal(),
            Catalog(),
            currentStateId: "state:other",
            expectedRisk: RiskClass.Medium);

        Assert.Equal(ProposalRejectReason.State, decision.RejectionReason);
    }

    [Fact]
    public void Risk_mismatch_is_rejected()
    {
        var decision = ProposalReject.Evaluate(
            Proposal(),
            Catalog(),
            currentStateId: "state:menu",
            expectedRisk: RiskClass.Low);

        Assert.Equal(ProposalRejectReason.Risk, decision.RejectionReason);
    }

    private static NextActionProposal Proposal(string actionId = "action:advance") => new(
        ContractSchemaVersions.Revision01,
        "proposal-1",
        new PlannerContext(
            ContractSchemaVersions.Revision01,
            "advance",
            "observation-1",
            [actionId],
            "menu observed",
            new PlannerBudget(ContractSchemaVersions.Revision01, 1, 0)),
        ProposalMode.VerifiedRun,
        new VerifiedRunAction(ContractSchemaVersions.Revision01, actionId),
        new ProposalPrecondition(ContractSchemaVersions.Revision01, "state:menu", 100),
        new ProposalExpectedOutcome(
            ContractSchemaVersions.Revision01,
            "state:done",
            new StabilityWindow(ContractSchemaVersions.Revision01, 2, 100)),
        new ProposalStopCondition(ContractSchemaVersions.Revision01, 1_000, "stop"),
        new ProposalValidity(ContractSchemaVersions.Revision01, 1, 1));

    private static SemanticActionCatalog Catalog() => new(
        [new SemanticAction(
            ContractSchemaVersions.Revision01,
            "action:advance",
            "Advance",
            RiskClass.Medium,
            "{}")] );
}
