using System.Text.Json;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Exploration;

/// <summary>
/// AIから分離した再現性authority。別sessionのTransition EvidenceだけでCandidate→Replayed→Verifiedを一段ずつ進める。
/// </summary>
public sealed class StructureVerificationController(
    IGameStructureStore store,
    IExplorationIdSource eventIds)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly IGameStructureStore store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IExplorationIdSource eventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));

    public GameStructureRevision Promote(
        StructureVerificationRequest request,
        string gameId,
        string environmentScope)
    {
        ValidateRequest(request, gameId, environmentScope);
        var current = store.LoadRevision(gameId, environmentScope);
        var currentState = GetVerification(current, request.EntityKind, request.SubjectId);
        var required = currentState switch
        {
            StructureVerificationState.Candidate => StructureVerificationState.Replayed,
            StructureVerificationState.Replayed => StructureVerificationState.Verified,
            _ => throw new InvalidOperationException($"'{request.SubjectId}' は昇格可能なCandidate／Replayedではありません。"),
        };
        if (request.RequestedState != required)
        {
            throw new InvalidOperationException("verificationはCandidate→Replayed→Verifiedを一段ずつ進めます。");
        }

        var events = store.ReadEvents(gameId, environmentScope);
        var transitionById = events
            .Where(item => item.PayloadType == StructureEventPayloadTypes.TransitionEvidence)
            .Select(item => JsonSerializer.Deserialize<TransitionEvidence>(item.PayloadJson, Json))
            .Where(item => item is not null)
            .ToDictionary(item => item!.EvidenceId, item => item!, StringComparer.Ordinal);
        if (request.EvidenceIds.Count == 0
            || request.EvidenceIds.Any(evidenceId =>
                !transitionById.TryGetValue(evidenceId, out var evidence)
                || !string.Equals(evidence.ExplorationRunId, request.ReplaySessionId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("昇格evidenceは指定した別Exploration sessionのTransition Evidenceでなければなりません。");
        }

        var observations = events
            .Where(item => item.PayloadType == StructureEventPayloadTypes.Observation)
            .Select(item => JsonSerializer.Deserialize<ObservedScene>(item.PayloadJson, Json))
            .Where(item => item is not null)
            .ToDictionary(item => item!.ObservationId, item => item!, StringComparer.Ordinal);
        if (request.EvidenceIds
            .Select(evidenceId => transitionById[evidenceId])
            .Any(evidence => !SupportsSubject(current, request.EntityKind, request.SubjectId, evidence, observations)))
        {
            throw new InvalidOperationException("昇格evidenceが対象node／edgeの再同定または再遷移を証明していません。");
        }

        var priorPromotions = events
            .Where(item => item.Kind == StructureEventKind.VerificationAccepted
                && item.PayloadType == StructureEventPayloadTypes.StructureVerification)
            .Select(item => JsonSerializer.Deserialize<StructureVerificationRequest>(item.PayloadJson, Json))
            .Where(item => item is not null
                && item.EntityKind == request.EntityKind
                && string.Equals(item.SubjectId, request.SubjectId, StringComparison.Ordinal))
            .Select(item => item!)
            .ToArray();
        if (request.RequestedState == StructureVerificationState.Verified
            && priorPromotions.Any(item => string.Equals(item.ReplaySessionId, request.ReplaySessionId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Verified昇格にはReplayed昇格と異なる独立sessionが必要です。");
        }

        var accepted = store.Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03,
                eventIds.Next("structure-event"),
                gameId,
                environmentScope,
                StructureEventKind.VerificationAccepted,
                StructureEventActor.Controller,
                request.CorrelationId,
                request.CausationId,
                null,
                null,
                null,
                request.EvidenceIds,
                StructureEventPayloadTypes.StructureVerification,
                JsonSerializer.Serialize(request, Json),
                null,
                request.OccurredUtc),
            current.RevisionId == "structure:root" ? null : current.RevisionId,
            request.PersistedUtc);
        var mutation = new StructureMutation(
            ContractSchemaVersions.Revision03,
            StructureMutationKind.ChangeVerification,
            request.EntityKind,
            request.SubjectId,
            [],
            null,
            null,
            null,
            null,
            request.RequestedState,
            null,
            request.EvidenceIds,
            "独立Exploration sessionの再観測");
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
                null,
                null,
                request.EvidenceIds,
                StructureEventPayloadTypes.MutationBatch,
                JsonSerializer.Serialize(
                    new StructureMutationBatch(ContractSchemaVersions.Revision03, [mutation]),
                    Json),
                null,
                request.OccurredUtc),
            accepted.ResultingStructureRevisionId,
            request.PersistedUtc);
        return store.LoadRevision(gameId, environmentScope);
    }

    private static void ValidateRequest(
        StructureVerificationRequest request,
        string gameId,
        string environmentScope)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
        if (!string.Equals(request.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(request.SubjectId)
            || string.IsNullOrWhiteSpace(request.DiscoverySessionId)
            || string.IsNullOrWhiteSpace(request.ReplaySessionId)
            || string.Equals(request.DiscoverySessionId, request.ReplaySessionId, StringComparison.Ordinal)
            || request.RequestedState is not (StructureVerificationState.Replayed or StructureVerificationState.Verified)
            || string.IsNullOrWhiteSpace(request.CorrelationId)
            || string.IsNullOrWhiteSpace(request.CausationId))
        {
            throw new ArgumentException("Structure verification requestが不正です。", nameof(request));
        }
    }

    private static StructureVerificationState GetVerification(
        GameStructureRevision revision,
        StructureEntityKind entityKind,
        string subjectId) => entityKind switch
    {
        StructureEntityKind.Node => revision.ScreenGraph.Nodes.Single(item => item.StateId == subjectId).VerificationState,
        StructureEntityKind.Edge => revision.ScreenGraph.Edges.Single(item => item.EdgeId == subjectId).VerificationState,
        StructureEntityKind.Fact => revision.StateFacts.Single(item => item.FactId == subjectId).VerificationState,
        _ => throw new ArgumentOutOfRangeException(nameof(entityKind)),
    };

    private static bool SupportsSubject(
        GameStructureRevision revision,
        StructureEntityKind entityKind,
        string subjectId,
        TransitionEvidence evidence,
        IReadOnlyDictionary<string, ObservedScene> observations)
    {
        if (!string.Equals(evidence.EnvironmentScope, revision.EnvironmentScope, StringComparison.Ordinal)
            || !observations.TryGetValue(evidence.BeforeObservationId, out var before)
            || !observations.TryGetValue(evidence.AfterObservationId, out var after))
        {
            return false;
        }

        return entityKind switch
        {
            StructureEntityKind.Node => SupportsNode(
                revision.ScreenGraph.Nodes.Single(item => item.StateId == subjectId),
                before,
                after),
            StructureEntityKind.Edge => SupportsEdge(
                revision,
                revision.ScreenGraph.Edges.Single(item => item.EdgeId == subjectId),
                evidence,
                before,
                after),
            // Factの独立再抽出とTransition Evidenceの結び付けcontractは未定義なので、
            // node／edge用evidenceによる昇格を許可しない。
            StructureEntityKind.Fact => false,
            _ => false,
        };
    }

    private static bool SupportsNode(
        StructureScreenNode node,
        ObservedScene before,
        ObservedScene after) =>
        MatchesNode(node, before) || MatchesNode(node, after);

    private static bool SupportsEdge(
        GameStructureRevision revision,
        StructureScreenEdge edge,
        TransitionEvidence evidence,
        ObservedScene before,
        ObservedScene after)
    {
        if (!string.Equals(edge.AffordanceCandidateId, evidence.AffordanceCandidateId, StringComparison.Ordinal)
            || !string.Equals(edge.Primitive, evidence.Primitive, StringComparison.Ordinal)
            || !edge.OutcomeCounts.Any(item => item.Outcome == evidence.Outcome))
        {
            return false;
        }

        var source = revision.ScreenGraph.Nodes.Single(item => item.StateId == edge.SourceStateId);
        if (!MatchesNode(source, before))
        {
            return false;
        }

        if (edge.DestinationStateId is null)
        {
            return true;
        }

        var destination = revision.ScreenGraph.Nodes.Single(item => item.StateId == edge.DestinationStateId);
        return MatchesNode(destination, after);
    }

    private static bool MatchesNode(StructureScreenNode node, ObservedScene scene) =>
        scene.StateHypothesisId is not null
        && node.SceneSignatureIds.Contains(scene.StateHypothesisId, StringComparer.Ordinal);
}
