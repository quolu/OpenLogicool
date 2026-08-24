using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Playbooks;

/// <summary>保存locatorを入力へ直結せず、現在frameの一意candidateへ再束縛する。</summary>
public static class StructurePlaybookActionRebinder
{
    public static StructurePlaybookAction Rebind(
        GameStructureRevision structure,
        StructurePlaybookAction action,
        ObservedScene freshScene,
        ExplorationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(freshScene);
        ArgumentNullException.ThrowIfNull(policy);
        if (!string.Equals(action.StructureRevisionId, structure.RevisionId, StringComparison.Ordinal)
            || freshScene.CaptureAvailability != CaptureAvailability.Available
            || !string.Equals(policy.EnvironmentScope, structure.EnvironmentScope, StringComparison.Ordinal)
            || !string.Equals(policy.PolicyRevisionId, action.PolicyRevisionId, StringComparison.Ordinal)
            || !string.Equals(policy.ConsentRevisionId, action.ConsentRevisionId, StringComparison.Ordinal)
            || !string.Equals(freshScene.Frame.SourceId, policy.TargetWindowSourceId, StringComparison.Ordinal)
            || freshScene.Frame.FreshnessMs > policy.StopPolicy.MaximumFrameFreshnessMilliseconds)
        {
            throw new InvalidOperationException("Pinned structure／policyの対象windowにfresh available frameではありません。");
        }
        var source = structure.ScreenGraph.Nodes.SingleOrDefault(node =>
            !node.Retired && string.Equals(node.StateId, action.SourceStateId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Playbook stepのsource nodeがactive structureにありません。");
        if (freshScene.StateHypothesisId is null
            || !source.SceneSignatureIds.Contains(freshScene.StateHypothesisId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Fresh frameをPlaybook stepのsource stateへ再同定できません。");
        }

        var matches = freshScene.Affordances
            .Where(candidate =>
                candidate.AllowedPrimitives.Contains(action.Primitive, StringComparer.Ordinal)
                && string.Equals(candidate.Locator.LocatorType, action.Locator.LocatorType, StringComparison.Ordinal))
            .Select(candidate => new { Candidate = candidate, Overlap = IntersectionOverUnion(action.Locator, candidate.Locator) })
            .Where(item => item.Overlap >= 0.5)
            .OrderByDescending(item => item.Overlap)
            .ToArray();
        if (matches.Length == 0 || (matches.Length > 1 && matches[0].Overlap - matches[1].Overlap < 0.15))
        {
            throw new InvalidOperationException("Fresh frameでPlaybook targetを一意に再束縛できません。");
        }
        var rebound = matches[0].Candidate;
        if (!string.Equals(rebound.ObservationId, freshScene.ObservationId, StringComparison.Ordinal)
            || !string.Equals(rebound.TargetWindowSourceId, freshScene.Frame.SourceId, StringComparison.Ordinal)
            || rebound.FrameSequence != freshScene.Frame.Sequence
            || rebound.TransformRevision != freshScene.Frame.TransformRevision)
        {
            throw new InvalidOperationException("Fresh candidateが現在frameへ束縛されていません。");
        }

        return action with
        {
            TargetWindowSourceId = rebound.TargetWindowSourceId,
            FrameSequence = rebound.FrameSequence,
            TransformRevision = rebound.TransformRevision,
            Locator = rebound.Locator,
            BeforeObservationId = freshScene.ObservationId,
        };
    }

    private static double IntersectionOverUnion(AffordanceLocator left, AffordanceLocator right)
    {
        if (left.NormalizedBounds.Count != 4 || right.NormalizedBounds.Count != 4)
        {
            return 0;
        }
        var leftX2 = left.NormalizedBounds[0] + left.NormalizedBounds[2];
        var leftY2 = left.NormalizedBounds[1] + left.NormalizedBounds[3];
        var rightX2 = right.NormalizedBounds[0] + right.NormalizedBounds[2];
        var rightY2 = right.NormalizedBounds[1] + right.NormalizedBounds[3];
        var intersectionWidth = Math.Max(0, Math.Min(leftX2, rightX2) - Math.Max(left.NormalizedBounds[0], right.NormalizedBounds[0]));
        var intersectionHeight = Math.Max(0, Math.Min(leftY2, rightY2) - Math.Max(left.NormalizedBounds[1], right.NormalizedBounds[1]));
        var intersection = intersectionWidth * intersectionHeight;
        var union = left.NormalizedBounds[2] * left.NormalizedBounds[3]
            + right.NormalizedBounds[2] * right.NormalizedBounds[3]
            - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}
