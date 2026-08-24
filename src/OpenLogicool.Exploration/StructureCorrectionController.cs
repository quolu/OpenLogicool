using System.Text.Json;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Exploration;

public sealed record StructureLabelCorrectionRequest(
    string StateId,
    string NewLabel,
    string Reason,
    DateTimeOffset OccurredUtc,
    DateTimeOffset PersistedUtc);

/// <summary>
/// 利用者の明示訂正だけをUser actorのStructure Eventとして追記するauthority。
/// 現段階のUIは既存nodeの表示名訂正に限定し、identityや検証段階は変更しない。
/// </summary>
public sealed class StructureCorrectionController(
    IGameStructureStore store,
    IExplorationIdSource eventIds)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly IGameStructureStore store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IExplorationIdSource eventIds = eventIds ?? throw new ArgumentNullException(nameof(eventIds));

    public GameStructureRevision RelabelNode(
        string gameId,
        string environmentScope,
        StructureLabelCorrectionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NewLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);

        var current = store.LoadRevision(gameId, environmentScope);
        var node = current.ScreenGraph.Nodes.SingleOrDefault(candidate =>
            string.Equals(candidate.StateId, request.StateId, StringComparison.Ordinal));
        if (node is null || node.Retired)
        {
            throw new InvalidOperationException($"訂正対象の画面状態 '{request.StateId}' は現在の構造にありません。");
        }

        if (string.Equals(node.ProvisionalLabel, request.NewLabel, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("新しい名前が現在の名前と同じです。");
        }

        var evidenceIds = node.EvidenceIds.Distinct(StringComparer.Ordinal).ToArray();
        var mutation = new StructureMutation(
            ContractSchemaVersions.Revision03,
            StructureMutationKind.RelabelNode,
            StructureEntityKind.Node,
            node.StateId,
            [],
            null,
            null,
            null,
            request.NewLabel,
            null,
            null,
            evidenceIds,
            request.Reason);
        var batch = new StructureMutationBatch(ContractSchemaVersions.Revision03, [mutation]);
        _ = store.Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03,
                eventIds.Next("structure-event"),
                gameId,
                environmentScope,
                StructureEventKind.CorrectionApplied,
                StructureEventActor.User,
                eventIds.Next("correlation"),
                current.RevisionId,
                null,
                null,
                null,
                evidenceIds,
                StructureEventPayloadTypes.MutationBatch,
                JsonSerializer.Serialize(batch, Json),
                null,
                request.OccurredUtc),
            current.RevisionId == "structure:root" ? null : current.RevisionId,
            request.PersistedUtc);
        return store.LoadRevision(gameId, environmentScope);
    }
}
