using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Exploration;

public interface IGameInteractionStructureCommitter
{
    GameInteractionStructureCommitResult Commit(
        ObservedScene before,
        ObservedScene after,
        TransitionEvidence evidence,
        ExplorationWaitCondition waitCondition,
        IReadOnlyList<string> riskTags,
        bool reversible,
        DateTimeOffset recordedUtc);
}

public sealed record GameInteractionStructureCommitResult(GameStructureRevision Revision, string? EdgeId);

/// <summary>Transition Evidenceからcandidate node／edgeだけを作るcontroller。</summary>
public sealed class GameInteractionStructureLearner(
    IGameStructureStore store,
    StructureKnowledgeController knowledge,
    IStableStructureIdRegistry stableIds,
    IExplorationIdSource eventIds,
    ExplorationCoordinator coordinator,
    string gameId,
    string environmentScope) : IGameInteractionStructureCommitter
{
    public GameInteractionStructureCommitResult Commit(
        ObservedScene before,
        ObservedScene after,
        TransitionEvidence evidence,
        ExplorationWaitCondition waitCondition,
        IReadOnlyList<string> riskTags,
        bool reversible,
        DateTimeOffset recordedUtc)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(evidence);
        var sourceId = EnsureNode(before, evidence.EvidenceId, recordedUtc);
        if (evidence.Outcome == ExplorationOutcomeKind.OutcomeUnknown)
        {
            return new(store.LoadRevision(gameId, environmentScope), null);
        }
        var destinationId = evidence.Outcome == ExplorationOutcomeKind.NoChange
            ? sourceId
            : EnsureNode(after, evidence.EvidenceId, recordedUtc);
        var current = store.LoadRevision(gameId, environmentScope);
        var candidate = before.Affordances.Single(item =>
            string.Equals(item.CandidateId, evidence.AffordanceCandidateId, StringComparison.Ordinal));
        var edgeId = stableIds.Issue(StructureEntityKind.Edge);
        var operation = new StructureDeltaOperation(
            ContractSchemaVersions.Revision03,
            StructureDeltaKind.AttributeEdge,
            sourceId,
            destinationId,
            null,
            null,
            null);
        var edge = new StructureScreenEdge(
            ContractSchemaVersions.Revision03,
            edgeId,
            sourceId,
            destinationId,
            GameSceneSemanticComparer.SignatureId(before),
            candidate.CandidateId,
            candidate.Locator.LocatorRevision,
            evidence.Primitive,
            "owner-delegated-exploration",
            riskTags.ToArray(),
            reversible,
            before.ObservationId,
            after.ObservationId,
            waitCondition,
            [new StructureOutcomeCount(evidence.Outcome, 1)],
            [evidence.EvidenceId],
            StructureVerificationState.Candidate,
            TargetSemanticKey: GameSceneSemanticComparer.TargetKey(candidate),
            TargetNormalizedBounds: candidate.Locator.NormalizedBounds.ToArray(),
            KeyTokens: candidate.KeyTokens?.ToArray(),
            VerticalScrollSteps: candidate.VerticalScrollSteps,
            HorizontalScrollSteps: candidate.HorizontalScrollSteps,
            DragDestinationNormalized: candidate.DragDestinationNormalized?.ToArray());
        var mutation = new StructureMutation(
            ContractSchemaVersions.Revision03,
            StructureMutationKind.UpsertEdge,
            StructureEntityKind.Edge,
            edgeId,
            [],
            null,
            edge,
            null,
            null,
            null,
            null,
            [evidence.EvidenceId],
            "前後Observationの意味判定からedge candidateを作成");
        _ = knowledge.Commit(
            Delta(
                current.RevisionId,
                [operation],
                new Dictionary<string, string>(StringComparer.Ordinal),
                [new MaterializedStructureDeltaOperation(operation, mutation)],
                [evidence.EvidenceId],
                recordedUtc),
            gameId,
            environmentScope);
        _ = coordinator.SynchronizeStructureRevision();
        return new(store.LoadRevision(gameId, environmentScope), edgeId);
    }

    private string EnsureNode(ObservedScene scene, string evidenceId, DateTimeOffset recordedUtc)
    {
        var signatureId = GameSceneSemanticComparer.SignatureId(scene);
        var current = store.LoadRevision(gameId, environmentScope);
        var existing = current.ScreenGraph.Nodes.SingleOrDefault(node =>
            node.SceneSignatureIds.Contains(signatureId, StringComparer.Ordinal));
        if (existing is not null)
        {
            return existing.StateId;
        }
        var alias = $"candidate:{signatureId}";
        var stateId = stableIds.Issue(StructureEntityKind.Node);
        var operation = new StructureDeltaOperation(
            ContractSchemaVersions.Revision03,
            StructureDeltaKind.CreateNode,
            alias,
            null,
            null,
            null,
            null);
        var node = new StructureScreenNode(
            ContractSchemaVersions.Revision03,
            stateId,
            environmentScope,
            [signatureId],
            [],
            [evidenceId],
            scene.Affordances.FirstOrDefault()?.SemanticLabel,
            StructureVerificationState.Candidate);
        var mutation = new StructureMutation(
            ContractSchemaVersions.Revision03,
            StructureMutationKind.UpsertNode,
            StructureEntityKind.Node,
            stateId,
            [],
            node,
            null,
            null,
            null,
            null,
            null,
            [evidenceId],
            "意味構造signatureからnode candidateを作成");
        _ = knowledge.Commit(
            Delta(
                current.RevisionId,
                [operation],
                new Dictionary<string, string>(StringComparer.Ordinal) { [alias] = stateId },
                [new MaterializedStructureDeltaOperation(operation, mutation)],
                [evidenceId],
                recordedUtc),
            gameId,
            environmentScope);
        _ = coordinator.SynchronizeStructureRevision();
        return stateId;
    }

    private StructureDeltaCommitRequest Delta(
        string revisionId,
        IReadOnlyList<StructureDeltaOperation> operations,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyList<MaterializedStructureDeltaOperation> materialized,
        IReadOnlyList<string> evidenceIds,
        DateTimeOffset recordedUtc) =>
        new(
            ContractSchemaVersions.Revision03,
            new StructureDeltaProposal(
                ContractSchemaVersions.Revision03,
                eventIds.Next("delta"),
                revisionId,
                evidenceIds,
                operations),
            aliases,
            materialized,
            eventIds.Next("correlation"),
            evidenceIds[0],
            recordedUtc,
            recordedUtc);
}
