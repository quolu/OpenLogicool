using System.Text.Json;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Playbooks;

public sealed record StructurePlaybookAction(
    string SemanticActionId,
    string StructureEdgeId,
    string SourceStateId,
    string DestinationStateId,
    string Primitive,
    string TargetWindowSourceId,
    long FrameSequence,
    long TransformRevision,
    AffordanceLocator Locator,
    IReadOnlyList<string> RiskTags,
    bool Reversible,
    string BeforeObservationId,
    string StructureRevisionId,
    string EnvironmentScope,
    string PolicyRevisionId,
    string ConsentRevisionId);

/// <summary>Playbookのstable structure-edge actionを、保存済みbefore Observationのframe-bound locatorへ戻す。</summary>
public static class StructurePlaybookActionResolver
{
    private const string Prefix = "structure-edge:";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static StructurePlaybookAction Resolve(
        StructurePlaybookCandidate playbookCandidate,
        GameStructureRevision structure,
        IReadOnlyList<StructureEvent> events,
        string semanticActionId,
        ExplorationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(playbookCandidate);
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticActionId);
        if (!semanticActionId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Semantic Actionはstructure-edge IDでなければなりません。", nameof(semanticActionId));
        }

        var edgeId = semanticActionId[Prefix.Length..];
        if (!string.Equals(playbookCandidate.StructureRevisionId, structure.RevisionId, StringComparison.Ordinal)
            || !string.Equals(playbookCandidate.EnvironmentScope, structure.EnvironmentScope, StringComparison.Ordinal)
            || !playbookCandidate.StructureEdgeIds.Contains(edgeId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Playbookがpinしたstructure revision／environment／edge列と一致しません。");
        }
        var edge = structure.ScreenGraph.Edges.SingleOrDefault(item =>
            !item.Retired && string.Equals(item.EdgeId, edgeId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Active structure edge '{edgeId}' が存在しません。");
        if (edge.DestinationStateId is null)
        {
            throw new InvalidOperationException($"Structure edge '{edgeId}' のdestinationが確定していません。");
        }
        var sourceNode = structure.ScreenGraph.Nodes.SingleOrDefault(node =>
            !node.Retired && string.Equals(node.StateId, edge.SourceStateId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Edge '{edgeId}' のactive source nodeが存在しません。");
        if (!string.Equals(sourceNode.EnvironmentScope, structure.EnvironmentScope, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Edge '{edgeId}' のsource nodeは別environment scopeです。");
        }
        if (!string.Equals(policy.EnvironmentScope, structure.EnvironmentScope, StringComparison.Ordinal)
            || !policy.AllowedPrimitives.Contains(edge.Primitive, StringComparer.Ordinal)
            || edge.RiskTags.Any(tag => policy.ProhibitedRiskTags.Contains(tag, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException($"Edge '{edgeId}' は現在のExploration Policyで許可されていません。");
        }

        var observationEvent = events.SingleOrDefault(item =>
            item.Kind == StructureEventKind.ObservationRecorded
            && item.PayloadType == StructureEventPayloadTypes.Observation
            && string.Equals(item.EnvironmentScope, structure.EnvironmentScope, StringComparison.Ordinal)
            && string.Equals(item.ObservationId, edge.BeforeObservationId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Edge '{edgeId}' のbefore Observation eventが存在しません。");
        var scene = JsonSerializer.Deserialize<ObservedScene>(observationEvent.PayloadJson, Json)
            ?? throw new InvalidOperationException($"Edge '{edgeId}' のbefore Observationを復元できません。");
        if (!string.Equals(scene.ObservationId, edge.BeforeObservationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Edge '{edgeId}' のObservation eventとpayloadが一致しません。");
        }
        var candidate = scene.Affordances.SingleOrDefault(item =>
            string.Equals(item.CandidateId, edge.AffordanceCandidateId, StringComparison.Ordinal)
            && string.Equals(item.Locator.LocatorRevision, edge.LocatorRevision, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Edge '{edgeId}' のframe-bound locatorを復元できません。");
        if (!string.Equals(candidate.ObservationId, scene.ObservationId, StringComparison.Ordinal)
            || !string.Equals(candidate.TargetWindowSourceId, scene.Frame.SourceId, StringComparison.Ordinal)
            || candidate.FrameSequence != scene.Frame.Sequence
            || candidate.TransformRevision != scene.Frame.TransformRevision)
        {
            throw new InvalidOperationException($"Edge '{edgeId}' のlocatorは保存frameへ束縛されていません。");
        }
        if (!candidate.AllowedPrimitives.Contains(edge.Primitive, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Edge '{edgeId}' のprimitiveは保存Observationで許可されていません。");
        }

        return new StructurePlaybookAction(
            semanticActionId,
            edge.EdgeId,
            edge.SourceStateId,
            edge.DestinationStateId,
            edge.Primitive,
            candidate.TargetWindowSourceId,
            candidate.FrameSequence,
            candidate.TransformRevision,
            candidate.Locator,
            edge.RiskTags,
            edge.Reversible,
            edge.BeforeObservationId,
            structure.RevisionId,
            structure.EnvironmentScope,
            policy.PolicyRevisionId,
            policy.ConsentRevisionId);
    }
}
