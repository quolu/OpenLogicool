using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Playbooks;

/// <summary>
/// Observe Only の proposal 取得口。
/// Attempt、journal、dispatch、Playbook version を参照しないため、外部入力も Playbook の変更も起こさない。
/// </summary>
public sealed class ObserveOnly
{
    private readonly INextActionPlanner _planner;

    public ObserveOnly(INextActionPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(planner);
        _planner = planner;
    }

    public NextActionProposal Observe(PlannerContext plannerContext)
    {
        ArgumentNullException.ThrowIfNull(plannerContext);
        return _planner.Propose(plannerContext);
    }
}
