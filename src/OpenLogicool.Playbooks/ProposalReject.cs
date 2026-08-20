using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Domain;
using OpenLogicool.Domain;

namespace OpenLogicool.Playbooks;

public enum ProposalRejectReason
{
    Schema,
    Catalog,
    State,
    Risk,
}

public sealed record ProposalRejectDecision(
    bool IsAccepted,
    ProposalRejectReason? RejectionReason)
{
    public static ProposalRejectDecision Accept() => new(true, null);

    public static ProposalRejectDecision Reject(ProposalRejectReason reason) => new(false, reason);
}

/// <summary>
/// AI proposal を dispatch 前に照合する pure gate（AI-005）。
/// この型は input emitter や dispatch delegate を持たず、判定結果だけを返す。
/// </summary>
public static class ProposalReject
{
    public static ProposalRejectDecision Evaluate(
        NextActionProposal proposal,
        SemanticActionCatalog catalog,
        string currentStateId,
        RiskClass expectedRisk)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentStateId);

        try
        {
            PlannerProposalSchema.Validate(proposal);
        }
        catch (ArgumentException)
        {
            return ProposalRejectDecision.Reject(ProposalRejectReason.Schema);
        }

        if (proposal.Action is not VerifiedRunAction action
            || !proposal.PlannerContextRef.AllowedActionIds.Contains(action.SemanticActionId, StringComparer.Ordinal))
        {
            return ProposalRejectDecision.Reject(ProposalRejectReason.Catalog);
        }

        SemanticAction catalogAction;
        try
        {
            catalogAction = catalog.Get(action.SemanticActionId);
        }
        catch (KeyNotFoundException)
        {
            return ProposalRejectDecision.Reject(ProposalRejectReason.Catalog);
        }

        if (!string.Equals(proposal.Precondition.StateId, currentStateId, StringComparison.Ordinal))
        {
            return ProposalRejectDecision.Reject(ProposalRejectReason.State);
        }

        return catalogAction.RiskClass == expectedRisk
            ? ProposalRejectDecision.Accept()
            : ProposalRejectDecision.Reject(ProposalRejectReason.Risk);
    }
}
