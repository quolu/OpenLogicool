using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;

namespace OpenLogicool.Playbooks;

/// <summary>最後の confirmed state と現在 state の差分（UX-005「差分」）。どちらかが不明なら Unknown。</summary>
public enum ResumeStateDifference
{
    Same,
    Different,
    Unknown,
}

/// <summary>
/// UX-005 の再開表示材料: 最後の confirmed state・現在 state・差分・採用 version・次の操作。
/// 表示のための値だけを持つ pure record であり、表示面（GameLab 等）はこれを描画する。
/// </summary>
public sealed record ResumeReportView(
    string? LastConfirmedObservationId,
    string? LastConfirmedStateId,
    string CurrentObservationId,
    CaptureAvailability CurrentCaptureAvailability,
    StateIdentityStatus CurrentStateIdentity,
    string? CurrentStateId,
    ResumeStateDifference Difference,
    string AdoptedVersionId,
    string? NextSemanticActionId,
    StateMatchResult StateMatch,
    ResumeDecision Decision);

/// <summary>
/// UX-005 の pure builder。journal の event 列・現在 Observation・照合結果から表示材料を組む。
/// 最後の confirmed state は最後の confirmation event の ObservationId を observedStateIds
/// （observationId→stateId。呼び出し側が commit 済み Observation から供給）で解決する——解決できなければ
/// null のまま表示し、勝手に補完しない。次の操作は採用 version の graph で現在 state と唯一対応する
/// node の SemanticActionId だけを出す（対応 node が無い・複数・action なしは null＝提示しない）。
/// </summary>
public static class ResumeReport
{
    public static ResumeReportView Build(
        IReadOnlyList<RunEvent> events,
        IReadOnlyDictionary<string, string> observedStateIds,
        ObservationResult currentObservation,
        PlaybookGraph adoptedGraph,
        StateMatchResult stateMatch,
        ResumeDecision decision)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(observedStateIds);
        ArgumentNullException.ThrowIfNull(currentObservation);
        ArgumentNullException.ThrowIfNull(adoptedGraph);
        ArgumentNullException.ThrowIfNull(decision);

        var adoptedVersionId = ResumeReadiness.AdoptedVersionId(events);
        if (!string.Equals(adoptedGraph.VersionId, adoptedVersionId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"渡された graph の version '{adoptedGraph.VersionId}' は採用 version '{adoptedVersionId}' と一致しません。",
                nameof(adoptedGraph));
        }

        var lastConfirmation = events.LastOrDefault(e =>
            string.Equals(e.PayloadType, RunEventPayloadTypes.Confirmation, StringComparison.Ordinal));
        var lastConfirmedObservationId = lastConfirmation?.ObservationId;
        string? lastConfirmedStateId = null;
        if (lastConfirmedObservationId is not null
            && observedStateIds.TryGetValue(lastConfirmedObservationId, out var confirmedStateId))
        {
            lastConfirmedStateId = confirmedStateId;
        }

        var currentStateId = currentObservation.CaptureAvailability == CaptureAvailability.Available
            && currentObservation.StateIdentity == StateIdentityStatus.Known
            && currentObservation.StateCandidates.Count == 1
            ? currentObservation.StateCandidates[0].StateId
            : null;

        var difference = lastConfirmedStateId is null || currentStateId is null
            ? ResumeStateDifference.Unknown
            : string.Equals(lastConfirmedStateId, currentStateId, StringComparison.Ordinal)
                ? ResumeStateDifference.Same
                : ResumeStateDifference.Different;

        string? nextSemanticActionId = null;
        if (currentStateId is not null)
        {
            var matchingNodes = adoptedGraph.Nodes
                .Where(node => string.Equals(node.StateId, currentStateId, StringComparison.Ordinal))
                .ToArray();
            if (matchingNodes.Length == 1)
            {
                nextSemanticActionId = matchingNodes[0].SemanticActionId;
            }
        }

        return new ResumeReportView(
            lastConfirmedObservationId,
            lastConfirmedStateId,
            currentObservation.ObservationId,
            currentObservation.CaptureAvailability,
            currentObservation.StateIdentity,
            currentStateId,
            difference,
            adoptedVersionId,
            nextSemanticActionId,
            stateMatch,
            decision);
    }
}
