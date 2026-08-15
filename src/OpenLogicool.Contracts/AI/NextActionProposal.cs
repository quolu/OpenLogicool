using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Contracts.AI;

public enum ProposalMode
{
    VerifiedRun,
    Teach,
}

public abstract record ProposalAction(string SchemaVersion);

public sealed record VerifiedRunAction(
    string SchemaVersion,
    string SemanticActionId)
    : ProposalAction(SchemaVersion);

public sealed record TeachAction(
    string SchemaVersion,
    string VisualTargetRef,
    string Primitive)
    : ProposalAction(SchemaVersion);

public sealed record ProposalPrecondition(
    string SchemaVersion,
    string StateId,
    long MaxObservationAgeMs);

public sealed record StabilityWindow(
    string SchemaVersion,
    int Frames,
    long MinDurationMs);

public sealed record ProposalExpectedOutcome(
    string SchemaVersion,
    string StateId,
    StabilityWindow StabilityWindow);

public sealed record ProposalStopCondition(
    string SchemaVersion,
    long TimeoutMs,
    string OnObservationUnavailable);

public sealed record ProposalValidity(
    string SchemaVersion,
    long FrameSequence,
    long TransformRevision);

public sealed record NextActionProposal(
    string SchemaVersion,
    string ProposalId,
    PlannerContext PlannerContextRef,
    ProposalMode Mode,
    ProposalAction Action,
    ProposalPrecondition Precondition,
    ProposalExpectedOutcome ExpectedOutcome,
    ProposalStopCondition StopCondition,
    ProposalValidity Validity);

public interface INextActionPlanner
{
    NextActionProposal Propose(PlannerContext plannerContext);
}
