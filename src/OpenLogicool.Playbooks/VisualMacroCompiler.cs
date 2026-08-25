using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Playbooks;

/// <summary>Learning Routeを、locatorと前後画面の期待値を持つ有限Visual Macroへ変換するpure境界。</summary>
public static class VisualMacroCompiler
{
    public static VisualMacroProgram Compile(
        LearningRouteRevision route,
        GameStructureRevision structure,
        IReadOnlyCollection<string> prohibitedRiskTags)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(prohibitedRiskTags);
        if (route.Status == LearningRouteStatus.Retired)
        {
            throw new InvalidOperationException("非対応にした学習ルートはマクロへ変換できません。");
        }

        LearningRouteValidator.Validate(route, structure);
        var nodes = structure.ScreenGraph.Nodes.ToDictionary(item => item.StateId, StringComparer.Ordinal);
        var edges = structure.ScreenGraph.Edges.ToDictionary(item => item.EdgeId, StringComparer.Ordinal);
        var prohibited = prohibitedRiskTags.ToHashSet(StringComparer.Ordinal);
        var steps = route.EdgeIds.Select((edgeId, index) =>
        {
            var edge = edges[edgeId];
            var source = nodes[edge.SourceStateId];
            var destination = nodes[edge.DestinationStateId!];
            var blockedRisks = edge.RiskTags.Where(prohibited.Contains).ToArray();
            if (blockedRisks.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Step {index + 1} は禁止risk '{string.Join(", ", blockedRisks)}' を含みます。");
            }
            if (source.SceneSignatureIds.Count == 0 || destination.SceneSignatureIds.Count == 0)
            {
                throw new InvalidOperationException($"Step {index + 1} の前後画面signatureがありません。");
            }
            if (string.IsNullOrWhiteSpace(edge.LocatorRevision)
                || string.IsNullOrWhiteSpace(edge.AffordanceCandidateId)
                || string.IsNullOrWhiteSpace(edge.Primitive))
            {
                throw new InvalidOperationException($"Step {index + 1} の操作場所または入力が確定していません。");
            }
            return new VisualMacroStep(
                index + 1,
                edge.EdgeId,
                edge.SourceStateId,
                source.SceneSignatureIds.ToArray(),
                edge.AffordanceCandidateId,
                edge.LocatorRevision,
                edge.Primitive,
                edge.DestinationStateId!,
                destination.SceneSignatureIds.ToArray(),
                edge.WaitCondition,
                edge.RiskTags.ToArray(),
                edge.VerificationState);
        }).ToArray();

        var allVerified = steps.All(step => step.VerificationState == StructureVerificationState.Verified)
                          && steps.SelectMany(step => new[] { step.SourceStateId, step.DestinationStateId })
                              .Distinct(StringComparer.Ordinal)
                              .All(stateId => nodes[stateId].VerificationState == StructureVerificationState.Verified);
        var executionMode = allVerified
            ? VisualMacroExecutionMode.Verified
            : VisualMacroExecutionMode.Supervised;
        return new VisualMacroProgram(
            ContractSchemaVersions.Revision03,
            $"macro:{route.VersionId["route:".Length..]}",
            route.RouteId,
            route.VersionId,
            route.GameId,
            route.EnvironmentScope,
            structure.RevisionId,
            executionMode,
            steps);
    }
}
