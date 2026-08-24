using System.Text.Json;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Domain;

public static class GameStructureProjector
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = false };

    public static GameStructureRevision Replay(
        string gameId,
        string environmentScope,
        IReadOnlyList<StructureEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
        ArgumentNullException.ThrowIfNull(events);

        var nodes = new Dictionary<string, StructureScreenNode>(StringComparer.Ordinal);
        var edges = new Dictionary<string, StructureScreenEdge>(StringComparer.Ordinal);
        var facts = new Dictionary<string, GameStateFact>(StringComparer.Ordinal);
        var contradictions = new Dictionary<string, StructureContradiction>(StringComparer.Ordinal);
        var dispatches = new Dictionary<string, StructureDispatchProjection>(StringComparer.Ordinal);
        string? previousRevision = null;
        long expectedSequence = 1;

        foreach (var structureEvent in events)
        {
            ValidateEnvelope(structureEvent, gameId, environmentScope, expectedSequence, previousRevision);

            if (structureEvent.Kind == StructureEventKind.DispatchArmed)
            {
                if (string.IsNullOrWhiteSpace(structureEvent.AttemptId))
                {
                    throw new InvalidOperationException($"DispatchArmed event '{structureEvent.EventId}' にAttemptIdがありません。");
                }

                if (dispatches.ContainsKey(structureEvent.AttemptId))
                {
                    throw new InvalidOperationException($"attempt '{structureEvent.AttemptId}' は既に記録されています。");
                }
                dispatches.Add(structureEvent.AttemptId, new StructureDispatchProjection(
                    structureEvent.AttemptId,
                    structureEvent.CorrelationId,
                    ExplorationOutcomeKind.OutcomeUnknown,
                    null));
            }
            else if (structureEvent.Kind == StructureEventKind.OutcomeRecorded)
            {
                if (string.IsNullOrWhiteSpace(structureEvent.AttemptId) || structureEvent.Outcome is null)
                {
                    throw new InvalidOperationException($"OutcomeRecorded event '{structureEvent.EventId}' のAttemptIdまたはOutcomeがありません。");
                }

                if (!dispatches.TryGetValue(structureEvent.AttemptId, out var armed)
                    || armed.Outcome != ExplorationOutcomeKind.OutcomeUnknown
                    || !string.Equals(armed.CorrelationId, structureEvent.CorrelationId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"attempt '{structureEvent.AttemptId}' の未解決DispatchArmedがありません。");
                }
                dispatches[structureEvent.AttemptId] = new StructureDispatchProjection(
                    structureEvent.AttemptId,
                    structureEvent.CorrelationId,
                    structureEvent.Outcome.Value,
                    structureEvent.EvidenceIds.FirstOrDefault());
            }

            if (structureEvent.Kind is StructureEventKind.MutationApplied or StructureEventKind.CorrectionApplied)
            {
                if (!string.Equals(
                        structureEvent.PayloadType,
                        StructureEventPayloadTypes.MutationBatch,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"mutation event '{structureEvent.EventId}' のpayload typeが不正です。");
                }

                var batch = JsonSerializer.Deserialize<StructureMutationBatch>(structureEvent.PayloadJson, Json)
                    ?? throw new InvalidOperationException($"mutation event '{structureEvent.EventId}' のpayloadがnullです。");
                if (!string.Equals(batch.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"mutation event '{structureEvent.EventId}' のschemaは未対応です。");
                }

                foreach (var mutation in batch.Mutations)
                {
                    ApplyMutation(
                        mutation,
                        structureEvent,
                        nodes,
                        edges,
                        facts,
                        contradictions);
                }
            }

            previousRevision = structureEvent.ResultingStructureRevisionId;
            expectedSequence++;
        }

        var revisionId = previousRevision ?? "structure:root";
        var parentRevisionId = events.Count == 0 ? null : events[^1].ParentStructureRevisionId;
        var createdUtc = events.Count == 0 ? DateTimeOffset.UnixEpoch : events[^1].PersistedUtc;
        return new GameStructureRevision(
            ContractSchemaVersions.Revision03,
            revisionId,
            parentRevisionId,
            events.Count == 0 ? 0 : events[^1].Sequence,
            new StructureScreenGraph(
                ContractSchemaVersions.Revision03,
                revisionId,
                nodes.Values.OrderBy(node => node.StateId, StringComparer.Ordinal).ToArray(),
                edges.Values.OrderBy(edge => edge.EdgeId, StringComparer.Ordinal).ToArray(),
                contradictions.Values.OrderBy(item => item.ContradictionId, StringComparer.Ordinal).ToArray(),
                environmentScope),
            facts.Values.OrderBy(fact => fact.FactId, StringComparer.Ordinal).ToArray(),
            dispatches.Values.OrderBy(dispatch => dispatch.AttemptId, StringComparer.Ordinal).ToArray(),
            environmentScope,
            createdUtc);
    }

    private static void ValidateEnvelope(
        StructureEvent structureEvent,
        string gameId,
        string environmentScope,
        long expectedSequence,
        string? previousRevision)
    {
        if (!string.Equals(structureEvent.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !string.Equals(structureEvent.GameId, gameId, StringComparison.Ordinal)
            || !string.Equals(structureEvent.EnvironmentScope, environmentScope, StringComparison.Ordinal)
            || structureEvent.Sequence != expectedSequence
            || !string.Equals(structureEvent.ParentStructureRevisionId, previousRevision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"structure event '{structureEvent.EventId}' のchain envelopeが不正です。");
        }

        var expectedRevision = StructureRevisionIds.Next(previousRevision, structureEvent.EventId, structureEvent.Sequence);
        if (!string.Equals(expectedRevision, structureEvent.ResultingStructureRevisionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"structure event '{structureEvent.EventId}' のresulting revisionが不正です。");
        }
    }

    private static void ApplyMutation(
        StructureMutation mutation,
        StructureEvent structureEvent,
        IDictionary<string, StructureScreenNode> nodes,
        IDictionary<string, StructureScreenEdge> edges,
        IDictionary<string, GameStateFact> facts,
        IDictionary<string, StructureContradiction> contradictions)
    {
        if (!string.Equals(mutation.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(mutation.SubjectId)
            || mutation.RelatedIds is null
            || mutation.RelatedIds.Any(string.IsNullOrWhiteSpace)
            || mutation.RelatedIds.Distinct(StringComparer.Ordinal).Count() != mutation.RelatedIds.Count
            || mutation.EvidenceIds is null
            || string.IsNullOrWhiteSpace(mutation.Reason))
        {
            throw new InvalidOperationException($"event '{structureEvent.EventId}' のmutation contractが不正です。");
        }

        var revisionId = structureEvent.ResultingStructureRevisionId;
        var isCorrection = structureEvent.Kind == StructureEventKind.CorrectionApplied;
        switch (mutation.Kind)
        {
            case StructureMutationKind.UpsertNode:
            {
                var incoming = mutation.Node
                    ?? throw new InvalidOperationException("UpsertNodeにNodeがありません。");
                if (!string.Equals(incoming.StateId, mutation.SubjectId, StringComparison.Ordinal)
                    || !string.Equals(incoming.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
                    || !string.Equals(incoming.EnvironmentScope, structureEvent.EnvironmentScope, StringComparison.Ordinal)
                    || incoming.SceneSignatureIds is null
                    || incoming.VariantStateIds is null
                    || incoming.EvidenceIds is null)
                {
                    throw new InvalidOperationException("UpsertNodeのidentityまたはenvironmentが一致しません。");
                }

                nodes.TryGetValue(incoming.StateId, out var existing);
                nodes[incoming.StateId] = incoming with
                {
                    VerificationState = UpsertVerification(incoming.VerificationState, existing?.VerificationState, isCorrection),
                    CreatedRevisionId = existing?.CreatedRevisionId ?? revisionId,
                    UpdatedRevisionId = revisionId,
                    Retired = existing?.Retired ?? false,
                    EvidenceIds = Union(existing?.EvidenceIds, incoming.EvidenceIds, mutation.EvidenceIds),
                };
                break;
            }

            case StructureMutationKind.UpsertEdge:
            {
                var incoming = mutation.Edge
                    ?? throw new InvalidOperationException("UpsertEdgeにEdgeがありません。");
                if (!string.Equals(incoming.EdgeId, mutation.SubjectId, StringComparison.Ordinal)
                    || !string.Equals(incoming.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
                    || !nodes.ContainsKey(incoming.SourceStateId)
                    || incoming.DestinationStateId is not null && !nodes.ContainsKey(incoming.DestinationStateId)
                    || incoming.RiskTags is null
                    || incoming.OutcomeCounts is null
                    || incoming.EvidenceIds is null)
                {
                    throw new InvalidOperationException("UpsertEdgeのidentityまたはnode参照が不正です。");
                }

                edges.TryGetValue(incoming.EdgeId, out var existing);
                edges[incoming.EdgeId] = incoming with
                {
                    VerificationState = UpsertVerification(incoming.VerificationState, existing?.VerificationState, isCorrection),
                    CreatedRevisionId = existing?.CreatedRevisionId ?? revisionId,
                    UpdatedRevisionId = revisionId,
                    Retired = existing?.Retired ?? false,
                    EvidenceIds = Union(existing?.EvidenceIds, incoming.EvidenceIds, mutation.EvidenceIds),
                };
                break;
            }

            case StructureMutationKind.UpsertFact:
            {
                var incoming = mutation.Fact
                    ?? throw new InvalidOperationException("UpsertFactにFactがありません。");
                if (!string.Equals(incoming.FactId, mutation.SubjectId, StringComparison.Ordinal)
                    || !string.Equals(incoming.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
                    || incoming.EvidenceIds is null)
                {
                    throw new InvalidOperationException("UpsertFactのidentityが一致しません。");
                }

                facts.TryGetValue(incoming.FactId, out var existing);
                facts[incoming.FactId] = incoming with
                {
                    VerificationState = UpsertVerification(incoming.VerificationState, existing?.VerificationState, isCorrection),
                    EnvironmentScope = structureEvent.EnvironmentScope,
                    CreatedRevisionId = existing?.CreatedRevisionId ?? revisionId,
                    UpdatedRevisionId = revisionId,
                    Retired = existing?.Retired ?? false,
                    EvidenceIds = Union(existing?.EvidenceIds, incoming.EvidenceIds, mutation.EvidenceIds),
                };
                break;
            }

            case StructureMutationKind.RelabelNode:
            {
                var node = Require(nodes, mutation.SubjectId, "node");
                if (string.IsNullOrWhiteSpace(mutation.Label))
                {
                    throw new InvalidOperationException("RelabelNodeにlabelがありません。");
                }
                nodes[node.StateId] = node with
                {
                    ProvisionalLabel = mutation.Label,
                    UpdatedRevisionId = revisionId,
                    EvidenceIds = Union(node.EvidenceIds, mutation.EvidenceIds),
                };
                break;
            }

            case StructureMutationKind.MergeNodes:
                MergeNodes(mutation, revisionId, nodes, edges);
                break;

            case StructureMutationKind.SplitNode:
                SplitNode(mutation, revisionId, nodes);
                break;

            case StructureMutationKind.ReattributeEdge:
                ReattributeEdge(mutation, revisionId, nodes, edges);
                break;

            case StructureMutationKind.RetireEntity:
                RetireEntity(mutation, revisionId, nodes, edges, facts);
                break;

            case StructureMutationKind.ChangeVerification:
                ChangeVerification(mutation, revisionId, isCorrection, nodes, edges, facts);
                break;

            case StructureMutationKind.RecordContradiction:
                RecordContradiction(mutation, revisionId, nodes, edges, facts, contradictions);
                break;

            default:
                throw new InvalidOperationException($"mutation kind '{mutation.Kind}' は未対応です。");
        }
    }

    private static void MergeNodes(
        StructureMutation mutation,
        string revisionId,
        IDictionary<string, StructureScreenNode> nodes,
        IDictionary<string, StructureScreenEdge> edges)
    {
        var target = Require(nodes, mutation.SubjectId, "merge target node");
        if (target.Retired || mutation.RelatedIds.Count == 0 || mutation.RelatedIds.Contains(target.StateId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("MergeNodesのtargetまたはsourceが不正です。");
        }

        var sources = mutation.RelatedIds.Select(id => Require(nodes, id, "merge source node")).ToArray();
        nodes[target.StateId] = target with
        {
            SceneSignatureIds = Union([target.SceneSignatureIds, .. sources.Select(source => source.SceneSignatureIds)]),
            VariantStateIds = Union(target.VariantStateIds, sources.Select(source => source.StateId).ToArray()),
            EvidenceIds = Union([target.EvidenceIds, mutation.EvidenceIds, .. sources.Select(source => source.EvidenceIds)]),
            VerificationState = StructureVerificationState.Candidate,
            UpdatedRevisionId = revisionId,
        };
        foreach (var source in sources)
        {
            nodes[source.StateId] = source with
            {
                VerificationState = StructureVerificationState.Retired,
                Retired = true,
                UpdatedRevisionId = revisionId,
                EvidenceIds = Union(source.EvidenceIds, mutation.EvidenceIds),
            };
        }

        foreach (var edge in edges.Values.ToArray())
        {
            var sourceId = sources.Any(source => source.StateId == edge.SourceStateId)
                ? target.StateId
                : edge.SourceStateId;
            var destinationId = sources.Any(source => source.StateId == edge.DestinationStateId)
                ? target.StateId
                : edge.DestinationStateId;
            if (sourceId != edge.SourceStateId || destinationId != edge.DestinationStateId)
            {
                edges[edge.EdgeId] = edge with
                {
                    SourceStateId = sourceId,
                    DestinationStateId = destinationId,
                    VerificationState = StructureVerificationState.Candidate,
                    UpdatedRevisionId = revisionId,
                    EvidenceIds = Union(edge.EvidenceIds, mutation.EvidenceIds),
                };
            }
        }
    }

    private static void SplitNode(
        StructureMutation mutation,
        string revisionId,
        IDictionary<string, StructureScreenNode> nodes)
    {
        var source = Require(nodes, mutation.SubjectId, "split source node");
        if (mutation.RelatedIds.Count < 2)
        {
            throw new InvalidOperationException("SplitNodeには2件以上のreplacementが必要です。");
        }
        foreach (var replacementId in mutation.RelatedIds)
        {
            var replacement = Require(nodes, replacementId, "split replacement node");
            nodes[replacement.StateId] = replacement with
            {
                VariantStateIds = Union(replacement.VariantStateIds, [source.StateId]),
                VerificationState = StructureVerificationState.Candidate,
                UpdatedRevisionId = revisionId,
                EvidenceIds = Union(replacement.EvidenceIds, mutation.EvidenceIds),
            };
        }
        nodes[source.StateId] = source with
        {
            VerificationState = StructureVerificationState.Retired,
            Retired = true,
            UpdatedRevisionId = revisionId,
            EvidenceIds = Union(source.EvidenceIds, mutation.EvidenceIds),
        };
    }

    private static void ReattributeEdge(
        StructureMutation mutation,
        string revisionId,
        IDictionary<string, StructureScreenNode> nodes,
        IDictionary<string, StructureScreenEdge> edges)
    {
        var edge = Require(edges, mutation.SubjectId, "edge");
        if (mutation.RelatedIds.Count is < 1 or > 2)
        {
            throw new InvalidOperationException("ReattributeEdgeはsourceと任意のdestinationを要求します。");
        }
        _ = Require(nodes, mutation.RelatedIds[0], "edge source node");
        if (mutation.RelatedIds.Count == 2)
        {
            _ = Require(nodes, mutation.RelatedIds[1], "edge destination node");
        }
        edges[edge.EdgeId] = edge with
        {
            SourceStateId = mutation.RelatedIds[0],
            DestinationStateId = mutation.RelatedIds.Count == 2 ? mutation.RelatedIds[1] : null,
            VerificationState = StructureVerificationState.Candidate,
            UpdatedRevisionId = revisionId,
            EvidenceIds = Union(edge.EvidenceIds, mutation.EvidenceIds),
        };
    }

    private static void RetireEntity(
        StructureMutation mutation,
        string revisionId,
        IDictionary<string, StructureScreenNode> nodes,
        IDictionary<string, StructureScreenEdge> edges,
        IDictionary<string, GameStateFact> facts)
    {
        switch (mutation.EntityKind)
        {
            case StructureEntityKind.Node:
                var node = Require(nodes, mutation.SubjectId, "node");
                nodes[node.StateId] = node with { VerificationState = StructureVerificationState.Retired, Retired = true, UpdatedRevisionId = revisionId };
                foreach (var dependent in edges.Values
                             .Where(edge => edge.SourceStateId == node.StateId || edge.DestinationStateId == node.StateId)
                             .ToArray())
                {
                    edges[dependent.EdgeId] = dependent with
                    {
                        VerificationState = StructureVerificationState.Retired,
                        Retired = true,
                        UpdatedRevisionId = revisionId,
                    };
                }
                break;
            case StructureEntityKind.Edge:
                var edge = Require(edges, mutation.SubjectId, "edge");
                edges[edge.EdgeId] = edge with { VerificationState = StructureVerificationState.Retired, Retired = true, UpdatedRevisionId = revisionId };
                break;
            case StructureEntityKind.Fact:
                var fact = Require(facts, mutation.SubjectId, "fact");
                facts[fact.FactId] = fact with { VerificationState = StructureVerificationState.Retired, Retired = true, UpdatedRevisionId = revisionId };
                break;
            default:
                throw new InvalidOperationException($"entity kind '{mutation.EntityKind}' は未対応です。");
        }
    }

    private static void ChangeVerification(
        StructureMutation mutation,
        string revisionId,
        bool isCorrection,
        IDictionary<string, StructureScreenNode> nodes,
        IDictionary<string, StructureScreenEdge> edges,
        IDictionary<string, GameStateFact> facts)
    {
        var requested = mutation.VerificationState
            ?? throw new InvalidOperationException("ChangeVerificationにstateがありません。");
        switch (mutation.EntityKind)
        {
            case StructureEntityKind.Node:
                var node = Require(nodes, mutation.SubjectId, "node");
                nodes[node.StateId] = node with { VerificationState = CorrectedVerification(requested, node.VerificationState, isCorrection), UpdatedRevisionId = revisionId };
                break;
            case StructureEntityKind.Edge:
                var edge = Require(edges, mutation.SubjectId, "edge");
                edges[edge.EdgeId] = edge with { VerificationState = CorrectedVerification(requested, edge.VerificationState, isCorrection), UpdatedRevisionId = revisionId };
                break;
            case StructureEntityKind.Fact:
                var fact = Require(facts, mutation.SubjectId, "fact");
                facts[fact.FactId] = fact with { VerificationState = CorrectedVerification(requested, fact.VerificationState, isCorrection), UpdatedRevisionId = revisionId };
                break;
            default:
                throw new InvalidOperationException($"entity kind '{mutation.EntityKind}' は未対応です。");
        }
    }

    private static void RecordContradiction(
        StructureMutation mutation,
        string revisionId,
        IDictionary<string, StructureScreenNode> nodes,
        IDictionary<string, StructureScreenEdge> edges,
        IDictionary<string, GameStateFact> facts,
        IDictionary<string, StructureContradiction> contradictions)
    {
        var contradiction = mutation.Contradiction
            ?? throw new InvalidOperationException("RecordContradictionにcontradictionがありません。");
        if (!string.Equals(contradiction.ContradictionId, mutation.SubjectId, StringComparison.Ordinal)
            || contradiction.SubjectIds.Count == 0)
        {
            throw new InvalidOperationException("contradiction identityまたはsubjectが不正です。");
        }
        contradictions.Add(contradiction.ContradictionId, contradiction with
        {
            EvidenceIds = Union(contradiction.EvidenceIds, mutation.EvidenceIds),
        });
        foreach (var subjectId in contradiction.SubjectIds)
        {
            if (nodes.TryGetValue(subjectId, out var node))
            {
                nodes[subjectId] = node with { VerificationState = StructureVerificationState.Candidate, UpdatedRevisionId = revisionId };
                foreach (var dependent in edges.Values
                             .Where(edge => edge.SourceStateId == subjectId || edge.DestinationStateId == subjectId)
                             .ToArray())
                {
                    edges[dependent.EdgeId] = dependent with
                    {
                        VerificationState = StructureVerificationState.Candidate,
                        UpdatedRevisionId = revisionId,
                    };
                }
            }
            else if (edges.TryGetValue(subjectId, out var edge))
            {
                edges[subjectId] = edge with { VerificationState = StructureVerificationState.Candidate, UpdatedRevisionId = revisionId };
            }
            else if (facts.TryGetValue(subjectId, out var fact))
            {
                facts[subjectId] = fact with { VerificationState = StructureVerificationState.Candidate, UpdatedRevisionId = revisionId };
            }
            else
            {
                throw new InvalidOperationException($"contradiction subject '{subjectId}' が存在しません。");
            }
        }
    }

    private static StructureVerificationState CorrectedVerification(
        StructureVerificationState requested,
        StructureVerificationState? current,
        bool isCorrection)
    {
        if (!isCorrection)
        {
            return requested;
        }
        if (current is null)
        {
            return StructureVerificationState.Candidate;
        }
        if (current == StructureVerificationState.Retired || requested == StructureVerificationState.Retired)
        {
            return StructureVerificationState.Retired;
        }
        return (int)requested <= (int)current.Value ? requested : current.Value;
    }

    private static StructureVerificationState UpsertVerification(
        StructureVerificationState requested,
        StructureVerificationState? current,
        bool isCorrection) =>
        isCorrection
            ? current == StructureVerificationState.Retired
                ? StructureVerificationState.Retired
                : StructureVerificationState.Candidate
            : requested;

    private static TValue Require<TValue>(IDictionary<string, TValue> values, string id, string label) =>
        values.TryGetValue(id, out var value)
            ? value
            : throw new InvalidOperationException($"{label} '{id}' が存在しません。");

    private static IReadOnlyList<string> Union(params IEnumerable<string>?[] sources) =>
        sources
            .Where(source => source is not null)
            .SelectMany(source => source!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
