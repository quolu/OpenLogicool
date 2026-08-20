using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Playbooks;

/// <summary>利用者の実操作と planner proposal を比較するだけの shadow 結果。</summary>
public sealed record ShadowComparison(
    NextActionProposal Proposal,
    string UserSemanticActionId,
    bool IsMatch);

/// <summary>
/// Shadow 比較の口。proposal を取得・比較するだけで、dispatch や外部入力は行わない。
/// </summary>
public static class ShadowCompare
{
    public static ShadowComparison Observe(
        INextActionPlanner planner,
        PlannerContext plannerContext,
        string userSemanticActionId)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(plannerContext);
        return Compare(planner.Propose(plannerContext), userSemanticActionId);
    }

    public static ShadowComparison Compare(NextActionProposal proposal, string userSemanticActionId)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentException.ThrowIfNullOrWhiteSpace(userSemanticActionId);
        PlannerProposalSchema.Validate(proposal);

        var isMatch = proposal.Action is VerifiedRunAction action
            && string.Equals(action.SemanticActionId, userSemanticActionId, StringComparison.Ordinal);
        return new ShadowComparison(proposal, userSemanticActionId, isMatch);
    }
}
