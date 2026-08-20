using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Playbooks;

/// <summary>利用者の一手承認を待つ Teach proposal。外部入力を実行する能力は持たない。</summary>
public sealed record PendingTeachStep(NextActionProposal Proposal);

/// <summary>承認済みの一手を下流の dispatch 境界へ渡すための値。</summary>
public sealed record ApprovedTeachStep(NextActionProposal Proposal, string ApprovalId);

public static class TeachSupervised
{
    public static PendingTeachStep Request(INextActionPlanner planner, PlannerContext context)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(context);
        return Stage(planner.Propose(context));
    }

    public static PendingTeachStep Stage(NextActionProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        PlannerProposalSchema.Validate(proposal);
        if (proposal.Mode != ProposalMode.Teach || proposal.Action is not TeachAction)
        {
            throw new ArgumentException("Teach／Supervised 口は Teach proposal だけを受理します。", nameof(proposal));
        }

        return new PendingTeachStep(proposal);
    }

    public static ApprovedTeachStep Approve(PendingTeachStep pendingStep, string approvalId)
    {
        ArgumentNullException.ThrowIfNull(pendingStep);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        return new ApprovedTeachStep(pendingStep.Proposal, approvalId);
    }
}
