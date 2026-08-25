using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Playbooks;

/// <summary>Teachで得た一件のproposal。操作受付は10の基盤機能と明示Game Policyが所有する。</summary>
public sealed record TeachStepProposal(NextActionProposal Proposal);

public static class TeachSupervised
{
    public static TeachStepProposal Request(INextActionPlanner planner, PlannerContext context)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(context);
        return Stage(planner.Propose(context));
    }

    public static TeachStepProposal Stage(NextActionProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        PlannerProposalSchema.Validate(proposal);
        if (proposal.Mode != ProposalMode.Teach || proposal.Action is not TeachAction)
        {
            throw new ArgumentException("Teach／Supervised 口は Teach proposal だけを受理します。", nameof(proposal));
        }

        return new TeachStepProposal(proposal);
    }
}
