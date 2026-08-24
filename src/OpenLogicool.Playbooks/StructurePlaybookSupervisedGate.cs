using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Playbooks;

public sealed record StructurePlaybookStepApproval(
    string SchemaVersion,
    string PlaybookVersionId,
    string StructureRevisionId,
    string SemanticActionId,
    string SourceStateId,
    string ObservationId,
    long FrameSequence,
    long TransformRevision,
    string PolicyRevisionId,
    string ConsentRevisionId,
    bool Approved);

/// <summary>Supervised stepをPlaybook、構造revision、現在state、policy／consentへ束縛するpure gate。</summary>
public static class StructurePlaybookSupervisedGate
{
    public static StructurePlaybookAction Authorize(
        StructurePlaybookCandidate candidate,
        StructurePlaybookAction action,
        string currentStateId,
        StructurePlaybookStepApproval approval)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentStateId);
        if (candidate.ExecutionMode != StructurePlaybookExecutionMode.Supervised
            || approval.SchemaVersion != ContractSchemaVersions.Revision03
            || !approval.Approved
            || !string.Equals(approval.PlaybookVersionId, candidate.Playbook.VersionId, StringComparison.Ordinal)
            || !string.Equals(approval.StructureRevisionId, candidate.StructureRevisionId, StringComparison.Ordinal)
            || !string.Equals(approval.StructureRevisionId, action.StructureRevisionId, StringComparison.Ordinal)
            || !string.Equals(approval.SemanticActionId, action.SemanticActionId, StringComparison.Ordinal)
            || !string.Equals(approval.SourceStateId, currentStateId, StringComparison.Ordinal)
            || !string.Equals(approval.SourceStateId, action.SourceStateId, StringComparison.Ordinal)
            || !string.Equals(approval.ObservationId, action.BeforeObservationId, StringComparison.Ordinal)
            || approval.FrameSequence != action.FrameSequence
            || approval.TransformRevision != action.TransformRevision
            || !string.Equals(approval.PolicyRevisionId, action.PolicyRevisionId, StringComparison.Ordinal)
            || !string.Equals(approval.ConsentRevisionId, action.ConsentRevisionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Supervised approvalがPlaybook stepの現在契約へ束縛されていません。");
        }

        return action;
    }
}
