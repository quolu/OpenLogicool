using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Playbooks;

public enum StructurePlaybookExecutionMode
{
    Supervised,
    Verified,
}

public sealed record StructurePlaybookCandidate(
    PlaybookVersion Playbook,
    StructurePlaybookExecutionMode ExecutionMode,
    StructureVerificationState WeakestEvidence,
    IReadOnlyList<string> StructureEdgeIds,
    string StructureRevisionId,
    string EnvironmentScope);

/// <summary>
/// Screen Graphの連続edge列をimmutable Playbookへ変換するpure境界。
/// candidate／replayed構造はSupervisedだけ、Verified実行は全node／edgeがVerifiedの時だけ許可する。
/// </summary>
public static class StructurePlaybookSynthesizer
{
    public static StructurePlaybookCandidate Synthesize(
        GameStructureRevision structure,
        IReadOnlyList<string> routeEdgeIds,
        string playbookVersionId,
        StructurePlaybookExecutionMode executionMode)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(routeEdgeIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(playbookVersionId);
        if (routeEdgeIds.Count == 0)
        {
            throw new ArgumentException("Playbookへ変換するstructure edgeがありません。", nameof(routeEdgeIds));
        }
        if (!string.Equals(
                structure.EnvironmentScope,
                structure.ScreenGraph.EnvironmentScope,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Structure revisionとScreen Graphのenvironment scopeが一致しません。", nameof(structure));
        }

        var nodes = structure.ScreenGraph.Nodes
            .ToDictionary(item => item.StateId, StringComparer.Ordinal);
        var edges = structure.ScreenGraph.Edges
            .ToDictionary(item => item.EdgeId, StringComparer.Ordinal);
        var route = routeEdgeIds.Select(edgeId =>
            edges.TryGetValue(edgeId, out var edge)
                ? edge
                : throw new ArgumentException($"Structure edge '{edgeId}' が存在しません。", nameof(routeEdgeIds)))
            .ToArray();

        for (var index = 0; index < route.Length; index++)
        {
            var edge = route[index];
            if (edge.Retired || edge.VerificationState == StructureVerificationState.Retired)
            {
                throw new InvalidOperationException($"Retired edge '{edge.EdgeId}' はPlaybookへ変換できません。");
            }
            if (edge.DestinationStateId is null
                || !nodes.TryGetValue(edge.SourceStateId, out var source)
                || !nodes.TryGetValue(edge.DestinationStateId, out var destination))
            {
                throw new InvalidOperationException($"Edge '{edge.EdgeId}' のsource／destination nodeが確定していません。");
            }
            if (source.Retired || destination.Retired
                || source.VerificationState == StructureVerificationState.Retired
                || destination.VerificationState == StructureVerificationState.Retired)
            {
                throw new InvalidOperationException($"Edge '{edge.EdgeId}' はRetired nodeを参照しています。");
            }
            if (!string.Equals(source.EnvironmentScope, structure.EnvironmentScope, StringComparison.Ordinal)
                || !string.Equals(destination.EnvironmentScope, structure.EnvironmentScope, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Edge '{edge.EdgeId}' は別environment scopeのnodeを参照しています。");
            }
            if (index > 0
                && !string.Equals(route[index - 1].DestinationStateId, edge.SourceStateId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Structure edge列が連続していません。");
            }
        }

        var evidenceStates = route
            .SelectMany(edge => new[]
            {
                edge.VerificationState,
                nodes[edge.SourceStateId].VerificationState,
                nodes[edge.DestinationStateId!].VerificationState,
            })
            .ToArray();
        var weakest = evidenceStates.Contains(StructureVerificationState.Candidate)
            ? StructureVerificationState.Candidate
            : evidenceStates.All(state => state == StructureVerificationState.Verified)
                ? StructureVerificationState.Verified
                : StructureVerificationState.Replayed;
        if (executionMode == StructurePlaybookExecutionMode.Verified
            && weakest != StructureVerificationState.Verified)
        {
            throw new InvalidOperationException("Verified Playbookには全node／edgeのVerified根拠が必要です。");
        }

        var playbookNodes = route.Select((edge, index) => new PlaybookNode(
                ContractSchemaVersions.Revision01,
                $"step:{index + 1}",
                index == 0,
                edge.SourceStateId,
                [
                    $"environment:{structure.EnvironmentScope}",
                    $"structure-revision:{structure.RevisionId}",
                    $"verification:{edge.VerificationState}",
                ],
                $"structure-edge:{edge.EdgeId}",
                [$"state:{edge.DestinationStateId}"]))
            .Append(new PlaybookNode(
                ContractSchemaVersions.Revision01,
                "complete",
                false,
                route[^1].DestinationStateId,
                [$"environment:{structure.EnvironmentScope}"],
                null,
                ["route-complete"]))
            .ToArray();
        var playbookEdges = Enumerable.Range(0, route.Length)
            .Select(index => new PlaybookEdge(
                ContractSchemaVersions.Revision01,
                $"flow:{index + 1}",
                $"step:{index + 1}",
                index + 1 < route.Length ? $"step:{index + 2}" : "complete",
                null))
            .ToArray();
        var playbook = new PlaybookVersion(
            ContractSchemaVersions.Revision01,
            playbookVersionId,
            null,
            playbookNodes,
            playbookEdges,
            $"structure revision {structure.RevisionId}から合成");
        _ = PlaybookMaterializer.ToGraph(playbook);

        return new StructurePlaybookCandidate(
            playbook,
            executionMode,
            weakest,
            route.Select(edge => edge.EdgeId).ToArray(),
            structure.RevisionId,
            structure.EnvironmentScope);
    }
}
