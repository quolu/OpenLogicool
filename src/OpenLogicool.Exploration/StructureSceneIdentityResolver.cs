using System.Text.Json;
using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Exploration;

/// <summary>観測の版と画面のidentityを分け、保存済みの画面証拠からnodeを再利用する。</summary>
public static class StructureSceneIdentityResolver
{
    public static StructureScreenNode? FindExistingNode(
        GameStructureRevision structure,
        IReadOnlyList<StructureEvent> events,
        ObservedScene scene)
    {
        var signatureId = GameSceneSemanticComparer.SignatureId(scene);
        return structure.ScreenGraph.Nodes
            .Where(node => !node.Retired && (node.SceneSignatureIds.Contains(signatureId, StringComparer.Ordinal)
                || NodeScenes(structure, events, node.StateId).Any(saved => SameScreen(saved, scene))))
            .OrderBy(node => node.StateId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static StructureDeltaCommitRequest? PlanRouteJoins(
        GameStructureRevision structure,
        IReadOnlyList<StructureEvent> events,
        IReadOnlyList<string> edgeIds,
        IExplorationIdSource ids,
        DateTimeOffset now)
    {
        var edges = edgeIds.Select(id => structure.ScreenGraph.Edges.Single(edge => edge.EdgeId == id)).ToArray();
        var groups = new List<HashSet<string>>();
        var evidence = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < edges.Length; index++)
        {
            var left = edges[index - 1];
            var right = edges[index];
            if (left.DestinationStateId is null || left.DestinationStateId == right.SourceStateId) continue;
            if (structure.ScreenGraph.Nodes.Any(node => node.Retired
                && (node.StateId == left.DestinationStateId || node.StateId == right.SourceStateId))) continue;
            var after = EdgeScene(events, left, left.AfterObservationId);
            var before = EdgeScene(events, right, right.BeforeObservationId);
            if (after is null || before is null || !SameScreen(after, before)) continue;
            var joined = new HashSet<string>([left.DestinationStateId, right.SourceStateId], StringComparer.Ordinal);
            foreach (var group in groups.Where(group => group.Overlaps(joined)).ToArray())
            {
                joined.UnionWith(group);
                groups.Remove(group);
            }
            // 近い観測の連鎖だけで別画面まで統合しない。各nodeの証拠を直接比較する。
            var samples = joined.Select(id => NodeScenes(structure, events, id).ToArray()).ToArray();
            if (samples.Any(sample => sample.Length == 0)
                || samples.Any(a => samples.Any(b => a.Any(x => b.Any(y => !SameScreen(x, y))))))
                throw new InvalidOperationException("統合境界の画面証拠が一致しません。");
            groups.Add(joined);
            evidence.Add(after.ObservationId);
            evidence.Add(before.ObservationId);
        }
        if (groups.Count == 0) return null;
        var materialized = groups.Select(group =>
        {
            var ordered = group.Order(StringComparer.Ordinal).ToArray();
            var operation = new StructureDeltaOperation(
                ContractSchemaVersions.Revision03, StructureDeltaKind.MergeNodes,
                ordered[0], null, null, null, null, ordered.Skip(1).ToArray());
            var mutation = new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.MergeNodes, StructureEntityKind.Node,
                ordered[0], ordered.Skip(1).ToArray(), null, null, null, null, null, null,
                evidence.ToArray(), "保存済みの前後画面が同一である証拠から画面identityを統合");
            return new MaterializedStructureDeltaOperation(operation, mutation);
        }).ToArray();
        return new StructureDeltaCommitRequest(
            ContractSchemaVersions.Revision03,
            new StructureDeltaProposal(ContractSchemaVersions.Revision03, ids.Next("delta"),
                structure.RevisionId, evidence.ToArray(), materialized.Select(item => item.ProposalOperation).ToArray()),
            new Dictionary<string, string>(StringComparer.Ordinal), materialized,
            ids.Next("correlation"), evidence.First(), now, now);
    }

    private static IEnumerable<ObservedScene> NodeScenes(
        GameStructureRevision structure, IReadOnlyList<StructureEvent> events, string stateId) =>
        structure.ScreenGraph.Edges.Where(edge => !edge.Retired).SelectMany(edge =>
        {
            var scenes = new List<ObservedScene>();
            if (edge.SourceStateId == stateId && EdgeScene(events, edge, edge.BeforeObservationId) is { } before)
                scenes.Add(before);
            if (edge.DestinationStateId == stateId && EdgeScene(events, edge, edge.AfterObservationId) is { } after)
                scenes.Add(after);
            return scenes;
        });

    private static ObservedScene? EdgeScene(
        IReadOnlyList<StructureEvent> events, StructureScreenEdge edge, string? observationId)
    {
        // process再起動後のObservationId再使用でも、edgeが作られた時点の観測を読む。
        var createdSequence = events.Single(item => item.ResultingStructureRevisionId == edge.CreatedRevisionId).Sequence;
        var recorded = events.LastOrDefault(item => item.Sequence < createdSequence
            && item.PayloadType == StructureEventPayloadTypes.Observation && item.ObservationId == observationId);
        return recorded is null ? null : JsonSerializer.Deserialize<ObservedScene>(recorded.PayloadJson)
            ?? throw new InvalidOperationException("保存済みの画面観測がnullです。");
    }

    private static bool SameScreen(ObservedScene left, ObservedScene right)
    {
        if (left.CaptureAvailability != CaptureAvailability.Available
            || right.CaptureAvailability != CaptureAvailability.Available
            || left.Frame.Backend != right.Frame.Backend
            || left.Frame.Artifact?.Width != right.Frame.Artifact?.Width
            || left.Frame.Artifact?.Height != right.Frame.Artifact?.Height) return false;
        if (left.SceneVisualPatch is not null && right.SceneVisualPatch is not null)
            return VisualPatchSignatureComparer.MeanAbsoluteDifference(left.SceneVisualPatch, right.SceneVisualPatch) < 6;
        var leftSignature = GameSceneSemanticComparer.Signature(left);
        var rightSignature = GameSceneSemanticComparer.Signature(right);
        return leftSignature.HasEvidence && rightSignature.HasEvidence
            && GameSceneSemanticComparer.StableEquivalent(leftSignature, rightSignature);
    }
}
