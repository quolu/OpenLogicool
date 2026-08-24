using System.Text.Json;
using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Exploration;

public interface IStableStructureIdRegistry
{
    string Issue(StructureEntityKind entityKind);

    bool IsIssued(string id, StructureEntityKind entityKind);
}

public sealed class InMemoryStableStructureIdRegistry : IStableStructureIdRegistry
{
    private readonly HashSet<string> issued = new(StringComparer.Ordinal);

    public string Issue(StructureEntityKind entityKind)
    {
        var prefix = entityKind switch
        {
            StructureEntityKind.Node => "state",
            StructureEntityKind.Edge => "edge",
            StructureEntityKind.Fact => "fact",
            _ => throw new ArgumentOutOfRangeException(nameof(entityKind)),
        };
        var id = $"{prefix}:{Guid.NewGuid():N}";
        issued.Add($"{entityKind}:{id}");
        return id;
    }

    public bool IsIssued(string id, StructureEntityKind entityKind) =>
        issued.Contains($"{entityKind}:{id}");
}

/// <summary>
/// AIのStructureDeltaProposalをStructure Eventへ変換できる唯一のauthority。
/// proposalとmaterializationを照合し、evidence、revision、stable ID、candidate状態を検証してからappendする。
/// </summary>
public sealed class StructureKnowledgeController(
    IGameStructureStore store,
    IStableStructureIdRegistry idRegistry,
    IExplorationIdSource eventIds)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly IGameStructureStore store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IStableStructureIdRegistry idRegistry = idRegistry ?? throw new ArgumentNullException(nameof(idRegistry));
    private readonly IExplorationIdSource eventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));

    public GameStructureRevision Commit(StructureDeltaCommitRequest request, string gameId, string environmentScope)
    {
        ValidateRequest(request, gameId, environmentScope);
        var current = store.LoadRevision(gameId, environmentScope);
        if (!string.Equals(request.Proposal.SourceStructureRevisionId, current.RevisionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("StructureDeltaProposalのsource revisionが現在revisionではありません。");
        }

        var events = store.ReadEvents(gameId, environmentScope);
        var knownEvidence = events
            .SelectMany(structureEvent => structureEvent.EvidenceIds
                .Append(structureEvent.EventId)
                .Append(structureEvent.ObservationId)
                .Append(structureEvent.ProposalId)
                .Append(structureEvent.AttemptId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        if (request.Proposal.EvidenceIds.Count == 0
            || request.Proposal.EvidenceIds.Any(evidenceId => !knownEvidence.Contains(evidenceId)))
        {
            throw new InvalidOperationException("StructureDeltaProposalが参照するevidenceはStructure Event Storeに存在しません。");
        }

        for (var index = 0; index < request.Proposal.Operations.Count; index++)
        {
            ValidateMaterialization(
                request.Proposal.Operations[index],
                request.Operations[index],
                request.StableIdByProposalAlias,
                current,
                environmentScope,
                request.Proposal.EvidenceIds);
        }

        var accepted = store.Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03,
                eventIds.Next("structure-event"),
                gameId,
                environmentScope,
                StructureEventKind.DeltaAccepted,
                StructureEventActor.Controller,
                request.CorrelationId,
                request.CausationId,
                null,
                request.Proposal.ProposalId,
                null,
                request.Proposal.EvidenceIds,
                StructureEventPayloadTypes.StructureDelta,
                JsonSerializer.Serialize(request.Proposal, Json),
                null,
                request.OccurredUtc),
            current.RevisionId == "structure:root" ? null : current.RevisionId,
            request.PersistedUtc);

        var mutations = request.Operations.Select(operation => operation.Mutation).ToArray();
        _ = store.Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03,
                eventIds.Next("structure-event"),
                gameId,
                environmentScope,
                StructureEventKind.MutationApplied,
                StructureEventActor.Controller,
                request.CorrelationId,
                accepted.EventId,
                null,
                request.Proposal.ProposalId,
                null,
                request.Proposal.EvidenceIds,
                StructureEventPayloadTypes.MutationBatch,
                JsonSerializer.Serialize(
                    new StructureMutationBatch(ContractSchemaVersions.Revision03, mutations),
                    Json),
                null,
                request.OccurredUtc),
            accepted.ResultingStructureRevisionId,
            request.PersistedUtc);
        return store.LoadRevision(gameId, environmentScope);
    }

    private static void ValidateRequest(
        StructureDeltaCommitRequest request,
        string gameId,
        string environmentScope)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
        if (!string.Equals(request.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !string.Equals(request.Proposal.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || request.Proposal.Operations is null
            || request.Proposal.Operations.Count == 0
            || request.Operations is null
            || request.Operations.Count != request.Proposal.Operations.Count
            || request.StableIdByProposalAlias is null
            || string.IsNullOrWhiteSpace(request.CorrelationId)
            || string.IsNullOrWhiteSpace(request.CausationId))
        {
            throw new ArgumentException("StructureDelta commit requestのschemaまたは必須fieldが不正です。", nameof(request));
        }
    }

    private void ValidateMaterialization(
        StructureDeltaOperation proposalOperation,
        MaterializedStructureDeltaOperation materialized,
        IReadOnlyDictionary<string, string> aliases,
        GameStructureRevision current,
        string environmentScope,
        IReadOnlyList<string> proposalEvidenceIds)
    {
        if (materialized.ProposalOperation != proposalOperation
            || !string.Equals(proposalOperation.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !string.Equals(materialized.Mutation.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || materialized.Mutation.EvidenceIds.Any(id => !proposalEvidenceIds.Contains(id, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("proposal operationとmaterialized mutationが一致しません。");
        }

        var mutation = materialized.Mutation;
        var expectedKind = proposalOperation.Kind switch
        {
            StructureDeltaKind.CreateNode => StructureMutationKind.UpsertNode,
            StructureDeltaKind.AttributeEdge => StructureMutationKind.UpsertEdge,
            StructureDeltaKind.ExtractFact => StructureMutationKind.UpsertFact,
            StructureDeltaKind.MergeNodes => StructureMutationKind.MergeNodes,
            StructureDeltaKind.SplitNode => StructureMutationKind.SplitNode,
            StructureDeltaKind.Relabel => StructureMutationKind.RelabelNode,
            StructureDeltaKind.Retire => StructureMutationKind.RetireEntity,
            _ => throw new InvalidOperationException($"delta kind '{proposalOperation.Kind}' は未対応です。"),
        };
        if (mutation.Kind != expectedKind)
        {
            throw new InvalidOperationException("delta operationとmutation kindが一致しません。");
        }

        var subjectId = Resolve(proposalOperation.SubjectId, aliases);
        var relatedIds = (proposalOperation.RelatedIds ??
                (proposalOperation.RelatedId is null ? [] : [proposalOperation.RelatedId]))
            .Select(id => Resolve(id, aliases))
            .ToArray();
        switch (proposalOperation.Kind)
        {
            case StructureDeltaKind.CreateNode:
                if (mutation.Node is null
                    || !string.Equals(mutation.SubjectId, subjectId, StringComparison.Ordinal)
                    || !string.Equals(mutation.Node.StateId, subjectId, StringComparison.Ordinal)
                    || !string.Equals(mutation.Node.EnvironmentScope, environmentScope, StringComparison.Ordinal)
                    || mutation.Node.VerificationState != StructureVerificationState.Candidate
                    || current.ScreenGraph.Nodes.Any(node => node.StateId == subjectId)
                    || !idRegistry.IsIssued(subjectId, StructureEntityKind.Node))
                {
                    throw new InvalidOperationException("CreateNodeのstable ID、environment、candidate状態が不正です。");
                }
                break;
            case StructureDeltaKind.AttributeEdge:
                if (mutation.Edge is null
                    || !string.Equals(mutation.SubjectId, mutation.Edge.EdgeId, StringComparison.Ordinal)
                    || mutation.Edge.VerificationState != StructureVerificationState.Candidate
                    || !idRegistry.IsIssued(mutation.Edge.EdgeId, StructureEntityKind.Edge)
                    || !string.Equals(mutation.Edge.SourceStateId, subjectId, StringComparison.Ordinal)
                    || relatedIds.Length != 1
                    || !string.Equals(mutation.Edge.DestinationStateId, relatedIds[0], StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("AttributeEdgeのstable ID、node帰属、candidate状態が不正です。");
                }
                break;
            case StructureDeltaKind.ExtractFact:
                if (mutation.Fact is null
                    || !string.Equals(mutation.SubjectId, mutation.Fact.FactId, StringComparison.Ordinal)
                    || mutation.Fact.VerificationState != StructureVerificationState.Candidate
                    || !idRegistry.IsIssued(mutation.Fact.FactId, StructureEntityKind.Fact)
                    || !string.Equals(mutation.Fact.FactType, proposalOperation.FactType, StringComparison.Ordinal)
                    || !string.Equals(mutation.Fact.Value, proposalOperation.FactValue, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("ExtractFactのstable ID、値、candidate状態が不正です。");
                }
                break;
            case StructureDeltaKind.Relabel:
                if (!string.Equals(mutation.SubjectId, subjectId, StringComparison.Ordinal)
                    || !string.Equals(mutation.Label, proposalOperation.ProposedLabel, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Relabelのsubjectまたはlabelが一致しません。");
                }
                break;
            case StructureDeltaKind.MergeNodes:
            case StructureDeltaKind.SplitNode:
                if (!string.Equals(mutation.SubjectId, subjectId, StringComparison.Ordinal)
                    || !mutation.RelatedIds.SequenceEqual(relatedIds, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException("merge／splitのidentity対応が一致しません。");
                }
                break;
            case StructureDeltaKind.Retire:
                if (!string.Equals(mutation.SubjectId, subjectId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Retireのsubjectが一致しません。");
                }
                break;
        }

        if (mutation.VerificationState is StructureVerificationState.Replayed or StructureVerificationState.Verified)
        {
            throw new InvalidOperationException("AI deltaはverificationを昇格できません。");
        }
    }

    private static string Resolve(string id, IReadOnlyDictionary<string, string> aliases) =>
        aliases.TryGetValue(id, out var stableId) ? stableId : id;
}
