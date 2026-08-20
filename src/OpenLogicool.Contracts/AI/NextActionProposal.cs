using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using System.Text.Json.Serialization;

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
    [property: JsonPropertyName("plannerContext")]
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

/// <summary>Planner と dispatch 境界が共有する proposal wire schema の検証口。</summary>
public static class PlannerProposalSchema
{
    public static void Validate(PlannerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureSchema(context.SchemaVersion, nameof(PlannerContext));
        RequireText(context.Goal, "goal");
        ArgumentNullException.ThrowIfNull(context.AllowedActionIds);
        if (context.AllowedActionIds.Count == 0
            || context.AllowedActionIds.Any(string.IsNullOrWhiteSpace)
            || context.AllowedActionIds.Distinct(StringComparer.Ordinal).Count() != context.AllowedActionIds.Count)
        {
            throw new ArgumentException("allowed action は重複なしの非空集合でなければなりません。", nameof(context));
        }

        RequireText(context.HistorySummary, "history summary");
        ArgumentNullException.ThrowIfNull(context.Budget);
        EnsureSchema(context.Budget.SchemaVersion, nameof(PlannerBudget));
        if (context.Budget.RemainingProposals < 0 || context.Budget.RemainingCostUsd is < 0)
        {
            throw new ArgumentException("planner budget は負にできません。", nameof(context));
        }
    }

    public static void Validate(NextActionProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        EnsureSchema(proposal.SchemaVersion, nameof(NextActionProposal));
        RequireText(proposal.ProposalId, "proposal id");
        Validate(proposal.PlannerContextRef);
        if (!Enum.IsDefined(proposal.Mode))
        {
            throw new ArgumentException("proposal mode が未対応です。", nameof(proposal));
        }

        ValidateAction(proposal.Mode, proposal.Action);
        ValidatePrecondition(proposal.Precondition);
        ValidateExpectedOutcome(proposal.ExpectedOutcome);
        ValidateStopCondition(proposal.StopCondition);
        ArgumentNullException.ThrowIfNull(proposal.Validity);
        EnsureSchema(proposal.Validity.SchemaVersion, nameof(ProposalValidity));
        if (proposal.Validity.FrameSequence < 0 || proposal.Validity.TransformRevision < 0)
        {
            throw new ArgumentException("proposal validity は負にできません。", nameof(proposal));
        }
    }

    private static void ValidateAction(ProposalMode mode, ProposalAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureSchema(action.SchemaVersion, nameof(ProposalAction));
        switch (mode, action)
        {
            case (ProposalMode.VerifiedRun, VerifiedRunAction verified):
                RequireText(verified.SemanticActionId, "semantic action");
                return;
            case (ProposalMode.Teach, TeachAction teach):
                RequireText(teach.VisualTargetRef, "visual target");
                RequireText(teach.Primitive, "primitive");
                return;
            default:
                throw new ArgumentException("proposal mode と action の組合せが不正です。", nameof(action));
        }
    }

    private static void ValidatePrecondition(ProposalPrecondition precondition)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        EnsureSchema(precondition.SchemaVersion, nameof(ProposalPrecondition));
        RequireText(precondition.StateId, "precondition state");
        if (precondition.MaxObservationAgeMs < 0)
        {
            throw new ArgumentException("max observation age は負にできません。", nameof(precondition));
        }
    }

    private static void ValidateExpectedOutcome(ProposalExpectedOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        EnsureSchema(outcome.SchemaVersion, nameof(ProposalExpectedOutcome));
        RequireText(outcome.StateId, "expected outcome state");
        ArgumentNullException.ThrowIfNull(outcome.StabilityWindow);
        EnsureSchema(outcome.StabilityWindow.SchemaVersion, nameof(StabilityWindow));
        if (outcome.StabilityWindow.Frames <= 0 || outcome.StabilityWindow.MinDurationMs <= 0)
        {
            throw new ArgumentException("stability window は正の frame 数と duration を持つ必要があります。", nameof(outcome));
        }
    }

    private static void ValidateStopCondition(ProposalStopCondition stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        EnsureSchema(stop.SchemaVersion, nameof(ProposalStopCondition));
        RequireText(stop.OnObservationUnavailable, "observation unavailable stop");
        if (stop.TimeoutMs <= 0)
        {
            throw new ArgumentException("stop timeout は正でなければなりません。", nameof(stop));
        }
    }

    private static void EnsureSchema(string schemaVersion, string kind)
    {
        if (!string.Equals(schemaVersion, ContractSchemaVersions.Revision01, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{kind} の schema version '{schemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。", nameof(schemaVersion));
        }
    }

    private static void RequireText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{field} が空です。", nameof(value));
        }
    }
}
