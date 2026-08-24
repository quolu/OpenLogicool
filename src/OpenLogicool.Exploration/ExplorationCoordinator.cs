using System.Text.Json;
using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Exploration;

public interface IExplorationIdSource
{
    string Next(string prefix);
}

public sealed class GuidExplorationIdSource : IExplorationIdSource
{
    public string Next(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return $"{prefix}:{Guid.NewGuid():N}";
    }
}

/// <summary>
/// Observation→proposal→policy／approval→Durable Attempt→再観測→Transition Evidenceを順序付ける。
/// AI、Input、SQLite実装を参照せず、proposal data・Playbooks gate・store portだけを扱う。
/// </summary>
public sealed class ExplorationCoordinator
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly IGameStructureStore structureStore;
    private readonly RunJournal runJournal;
    private readonly AttemptDispatchGate attemptGate;
    private readonly ExplorationRunBinding binding;
    private readonly ExplorationPolicy policy;
    private readonly IExplorationIdSource ids;
    private readonly Dictionary<string, CompletedProbe> completed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> probeCounts = new(StringComparer.Ordinal);
    private readonly List<string> observedStateHistory = [];
    private ActiveProbe? active;
    private ObservedScene? currentScene;
    private string currentStructureRevisionId;
    private long nextRunSequence;
    private int remainingProbes;
    private int consecutiveNoProgress;
    private int oscillationCount;
    private ExplorationStopReason stopReason;

    public ExplorationCoordinator(
        IGameStructureStore structureStore,
        RunJournal runJournal,
        AttemptDispatchGate attemptGate,
        ExplorationRunBinding binding,
        ExplorationPolicy policy,
        IExplorationIdSource ids)
    {
        ArgumentNullException.ThrowIfNull(structureStore);
        ArgumentNullException.ThrowIfNull(runJournal);
        ArgumentNullException.ThrowIfNull(attemptGate);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(ids);
        ValidateRunContract(binding, policy);

        this.structureStore = structureStore;
        this.runJournal = runJournal;
        this.attemptGate = attemptGate;
        this.binding = binding;
        this.policy = policy;
        this.ids = ids;
        currentStructureRevisionId = structureStore.LoadRevision(binding.GameId, binding.EnvironmentScope).RevisionId;
        nextRunSequence = runJournal.ReadRun(binding.ExplorationRunId).LastOrDefault()?.RunSequence + 1 ?? 1;
        remainingProbes = policy.Budget.RemainingProbes;
    }

    public string CurrentStructureRevisionId => currentStructureRevisionId;
    public int RemainingProbes => remainingProbes;
    public ExplorationStopReason StopReason => stopReason;
    public bool HasActiveProbe => active is not null;

    public void CommitObservation(ObservedScene scene, DateTimeOffset persistedUtc)
    {
        ValidateScene(scene);
        AppendStructure(
            StructureEventKind.ObservationRecorded,
            StructureEventActor.Controller,
            ids.Next("correlation"),
            "capture-perception",
            scene.ObservationId,
            null,
            null,
            EvidenceIds(scene),
            StructureEventPayloadTypes.Observation,
            JsonSerializer.Serialize(scene, Json),
            null,
            persistedUtc,
            persistedUtc);
        AppendRun(
            RunEventPayloadTypes.Observation,
            RunEventActorType.System,
            ids.Next("correlation"),
            "capture-perception",
            scene.ObservationId,
            null,
            null,
            JsonSerializer.Serialize(scene, Json),
            persistedUtc,
            runJournal.Append);
        currentScene = scene;
        RecordState(scene);
    }

    public ExplorationAdmissionDecision Propose(
        ExplorationProposalAdmission admission,
        DateTimeOffset persistedUtc)
    {
        if (active is not null)
        {
            throw new InvalidOperationException("未完了のprobeがある間、新しいproposalは登録できません。");
        }

        var decision = Evaluate(admission, approval: null);
        var correlationId = ids.Next("correlation");
        AppendStructure(
            StructureEventKind.ProbeProposed,
            StructureEventActor.Automation,
            correlationId,
            admission.Context.CurrentScene.ObservationId,
            admission.Proposal.SourceObservationId,
            admission.Proposal.ProposalId,
            null,
            [admission.Proposal.SourceObservationId, admission.Proposal.AffordanceCandidateId],
            StructureEventPayloadTypes.ExplorationProposal,
            JsonSerializer.Serialize(admission.Proposal, Json),
            null,
            persistedUtc,
            persistedUtc);

        if (decision.Status is ExplorationAdmissionStatus.Rejected or ExplorationAdmissionStatus.Stopped)
        {
            return decision;
        }

        active = new ActiveProbe(admission, decision, correlationId, null, false, false, null);
        if (decision.Status == ExplorationAdmissionStatus.Allowed)
        {
            var automaticApproval = new ExplorationApproval(
                ContractSchemaVersions.Revision03,
                ids.Next("approval"),
                admission.Proposal.ProposalId,
                admission.Proposal.SourceObservationId,
                policy.PolicyRevisionId,
                admission.Proposal.SourceStructureRevisionId,
                "deterministic-policy",
                persistedUtc);
            Authorize(automaticApproval, RunEventActorType.System, persistedUtc);
        }
        return decision;
    }

    public ExplorationAdmissionDecision Approve(
        ExplorationApproval approval,
        DateTimeOffset persistedUtc)
    {
        var probe = RequireActive();
        var decision = Evaluate(probe.Admission, approval, proposalAlreadyRecorded: true);
        active = probe with { Decision = decision };
        if (decision.Status == ExplorationAdmissionStatus.Allowed)
        {
            Authorize(approval, RunEventActorType.User, persistedUtc);
        }
        return decision;
    }

    public void Dispatch(string proposalId, Action externalInput, DateTimeOffset persistedUtc)
    {
        ArgumentNullException.ThrowIfNull(externalInput);
        var probe = RequireActive(proposalId);
        if (!probe.Authorized || probe.AttemptId is null || probe.Decision.Status != ExplorationAdmissionStatus.Allowed)
        {
            throw new InvalidOperationException("policyと承認を通過していないproposalはdispatchできません。");
        }
        if (remainingProbes <= 0)
        {
            stopReason = ExplorationStopReason.BudgetExhausted;
            throw new InvalidOperationException("probe budgetを使い切っています。");
        }

        attemptGate.MarkPrepared(probe.AttemptId);
        var dispatchEvent = NewRunEvent(
            RunEventPayloadTypes.Dispatch,
            RunEventActorType.System,
            probe.CorrelationId,
            probe.Admission.Proposal.ProposalId,
            probe.Admission.Proposal.SourceObservationId,
            probe.Admission.Proposal.ProposalId,
            probe.AttemptId,
            JsonSerializer.Serialize(probe.Admission.Proposal, Json),
            persistedUtc);
        var inputCalled = false;
        try
        {
            attemptGate.ArmThenDispatch(dispatchEvent, () =>
            {
                AppendStructure(
                    StructureEventKind.DispatchArmed,
                    StructureEventActor.Controller,
                    probe.CorrelationId,
                    dispatchEvent.EventId,
                    probe.Admission.Proposal.SourceObservationId,
                    probe.Admission.Proposal.ProposalId,
                    probe.AttemptId,
                    [probe.Admission.Proposal.SourceObservationId, probe.Admission.Proposal.AffordanceCandidateId],
                    StructureEventPayloadTypes.ExplorationProposal,
                    JsonSerializer.Serialize(probe.Admission.Proposal, Json),
                    null,
                    persistedUtc,
                    persistedUtc);
                remainingProbes--;
                active = probe with { Dispatched = true };
                inputCalled = true;
                externalInput();
            });
            nextRunSequence++;
        }
        catch
        {
            if (attemptGate.Get(probe.AttemptId).State == AttemptState.DispatchArmed)
            {
                nextRunSequence++;
                if (!inputCalled)
                {
                    AppendRun(
                        RunEventPayloadTypes.Disarm,
                        RunEventActorType.System,
                        probe.CorrelationId,
                        dispatchEvent.EventId,
                        null,
                        null,
                        probe.AttemptId,
                        "{\"reason\":\"structure-event-append-failed-before-input\"}",
                        persistedUtc,
                        attemptGate.CommitDisarmed);
                }
            }
            throw;
        }
    }

    public TransitionEvidence RecordOutcome(ExplorationOutcomeReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var probe = RequireActive(report.ProposalId);
        if (!probe.Dispatched || probe.AttemptId is null)
        {
            throw new InvalidOperationException("dispatchしていないproposalへoutcomeを記録できません。");
        }
        ValidateOutcome(probe, report);

        AppendRun(
            RunEventPayloadTypes.DispatchResult,
            RunEventActorType.System,
            probe.CorrelationId,
            probe.Admission.Proposal.ProposalId,
            null,
            probe.Admission.Proposal.ProposalId,
            probe.AttemptId,
            "{\"reported\":true}",
            report.RecordedUtc,
            attemptGate.CommitReported);
        AppendRun(
            RunEventPayloadTypes.Observation,
            RunEventActorType.System,
            probe.CorrelationId,
            probe.Admission.Proposal.ProposalId,
            report.AfterScene.ObservationId,
            null,
            probe.AttemptId,
            JsonSerializer.Serialize(report.AfterScene, Json),
            report.RecordedUtc,
            attemptGate.CommitObserving);

        switch (report.Outcome)
        {
            case ExplorationOutcomeKind.Destination:
            case ExplorationOutcomeKind.Novel:
                AppendRun(
                    RunEventPayloadTypes.Confirmation,
                    RunEventActorType.System,
                    probe.CorrelationId,
                    report.AfterScene.ObservationId,
                    report.AfterScene.ObservationId,
                    null,
                    probe.AttemptId,
                    JsonSerializer.Serialize(new { report.Outcome }, Json),
                    report.RecordedUtc,
                    attemptGate.CommitConfirmed);
                break;
            case ExplorationOutcomeKind.NoChange:
                AppendRun(
                    RunEventPayloadTypes.Rejection,
                    RunEventActorType.System,
                    probe.CorrelationId,
                    report.AfterScene.ObservationId,
                    report.AfterScene.ObservationId,
                    null,
                    probe.AttemptId,
                    JsonSerializer.Serialize(new { report.Outcome }, Json),
                    report.RecordedUtc,
                    attemptGate.CommitRejected);
                break;
            default:
                attemptGate.ResolveLocally(probe.AttemptId, AttemptState.OutcomeUnknown);
                break;
        }

        AppendStructure(
            StructureEventKind.ObservationRecorded,
            StructureEventActor.Controller,
            probe.CorrelationId,
            probe.Admission.Proposal.SourceObservationId,
            report.AfterScene.ObservationId,
            probe.Admission.Proposal.ProposalId,
            probe.AttemptId,
            EvidenceIds(report.AfterScene),
            StructureEventPayloadTypes.Observation,
            JsonSerializer.Serialize(report.AfterScene, Json),
            null,
            report.RecordedUtc,
            report.RecordedUtc);
        var evidence = new TransitionEvidence(
            ContractSchemaVersions.Revision03,
            report.TransitionEvidenceId,
            probe.Admission.Proposal.SourceObservationId,
            report.AfterScene.ObservationId,
            probe.AttemptId,
            probe.Admission.Proposal.AffordanceCandidateId,
            probe.Admission.Proposal.Primitive,
            report.Outcome,
            binding.EnvironmentScope,
            report.DispatchMonotonicMilliseconds,
            report.ObservationCompletedMonotonicMilliseconds,
            report.RecordedUtc);
        AppendStructure(
            StructureEventKind.OutcomeRecorded,
            StructureEventActor.Controller,
            probe.CorrelationId,
            probe.AttemptId,
            report.AfterScene.ObservationId,
            probe.Admission.Proposal.ProposalId,
            probe.AttemptId,
            [evidence.EvidenceId, evidence.BeforeObservationId, evidence.AfterObservationId],
            StructureEventPayloadTypes.TransitionEvidence,
            JsonSerializer.Serialize(evidence, Json),
            report.Outcome,
            report.RecordedUtc,
            report.RecordedUtc);

        currentScene = report.AfterScene;
        RecordProgress(probe.Admission.Proposal, report.AfterScene, report.Outcome);
        completed[report.ProposalId] = new CompletedProbe(probe, evidence);
        active = null;
        return evidence;
    }

    public TransitionEvidence GetCompletedEvidence(string proposalId) =>
        completed.TryGetValue(proposalId, out var probe)
            ? probe.Evidence
            : throw new InvalidOperationException($"proposal '{proposalId}' の完了evidenceがありません。");

    private ExplorationAdmissionDecision Evaluate(
        ExplorationProposalAdmission admission,
        ExplorationApproval? approval,
        bool proposalAlreadyRecorded = false)
    {
        ArgumentNullException.ThrowIfNull(admission);
        var context = admission.Context;
        var proposal = admission.Proposal;
        var risk = admission.Risk;
        if (stopReason != ExplorationStopReason.None)
        {
            return Stopped(stopReason, "探索Runは停止済みです。");
        }
        if (!SchemasAreCurrent(context, proposal, risk))
        {
            return Reject(ExplorationStopReason.SchemaMismatch, "schema 0.3.0以外は受理しません。");
        }
        if (!string.Equals(context.Policy.PolicyRevisionId, policy.PolicyRevisionId, StringComparison.Ordinal)
            || !string.Equals(context.Policy.ConsentRevisionId, policy.ConsentRevisionId, StringComparison.Ordinal))
        {
            return Reject(ExplorationStopReason.PolicyMismatch, "Run開始時に固定したpolicy／consentと一致しません。");
        }
        if (!string.Equals(context.StructureRevisionId, proposal.SourceStructureRevisionId, StringComparison.Ordinal)
            || !proposalAlreadyRecorded
                && !string.Equals(proposal.SourceStructureRevisionId, currentStructureRevisionId, StringComparison.Ordinal))
        {
            return Reject(ExplorationStopReason.SourceRevisionMismatch, "proposalのsource revisionが現在revisionではありません。");
        }
        if (!admission.GamePolicyAllowsExplore)
        {
            return Reject(ExplorationStopReason.GamePolicyDisabled, "Game PolicyがExploreを許可していません。");
        }
        if (!admission.WithinExplorationScope)
        {
            return Reject(ExplorationStopReason.ScopeViolation, "proposalは許可した探索scope外です。");
        }
        if (context.CurrentScene.CaptureAvailability != CaptureAvailability.Available)
        {
            return StopRun(ExplorationStopReason.CaptureUnavailable, "captureがAvailableではありません。");
        }
        if (context.CurrentScene.Frame.FreshnessMs > policy.StopPolicy.MaximumFrameFreshnessMilliseconds)
        {
            return StopRun(ExplorationStopReason.StaleFrame, "frame freshness budgetを超えています。");
        }
        if (!string.Equals(proposal.SourceObservationId, context.CurrentScene.ObservationId, StringComparison.Ordinal)
            || currentScene is null
            || !string.Equals(currentScene.ObservationId, proposal.SourceObservationId, StringComparison.Ordinal)
            || !SceneBindingMatches(context.CurrentScene, currentScene))
        {
            return Reject(ExplorationStopReason.TargetNotCurrent, "proposalは現在Observationに束縛されていません。");
        }
        var target = context.CurrentScene.Affordances.SingleOrDefault(candidate =>
            string.Equals(candidate.CandidateId, proposal.AffordanceCandidateId, StringComparison.Ordinal));
        var committedTarget = currentScene.Affordances.SingleOrDefault(candidate =>
            string.Equals(candidate.CandidateId, proposal.AffordanceCandidateId, StringComparison.Ordinal));
        if (target is null
            || committedTarget is null
            || !CandidateBindingMatches(target, committedTarget)
            || !string.Equals(target.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !string.Equals(target.Locator.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !string.Equals(target.ObservationId, context.CurrentScene.ObservationId, StringComparison.Ordinal)
            || target.FrameSequence != context.CurrentScene.Frame.Sequence
            || target.TransformRevision != context.CurrentScene.Frame.TransformRevision
            || !IsWindowBoundLocator(target.Locator))
        {
            return Reject(ExplorationStopReason.TargetNotCurrent, "targetは現在Observation／frame／transform／window内の候補ではありません。");
        }
        if (!string.Equals(target.TargetWindowSourceId, policy.TargetWindowSourceId, StringComparison.Ordinal)
            || !string.Equals(context.CurrentScene.Frame.SourceId, policy.TargetWindowSourceId, StringComparison.Ordinal))
        {
            return Reject(ExplorationStopReason.TargetWindowMismatch, "capture source、target window、input targetが一致しません。");
        }
        if (!policy.AllowedPrimitives.Contains(proposal.Primitive, StringComparer.Ordinal)
            || !target.AllowedPrimitives.Contains(proposal.Primitive, StringComparer.Ordinal))
        {
            return Reject(ExplorationStopReason.PrimitiveNotAllowed, "primitiveはpolicyまたはtargetで許可されていません。");
        }
        if (!string.Equals(risk.AffordanceCandidateId, target.CandidateId, StringComparison.Ordinal)
            || risk.Level == ExplorationRiskLevel.Prohibited
            || risk.RiskTags.Intersect(policy.ProhibitedRiskTags, StringComparer.Ordinal).Any())
        {
            return Reject(ExplorationStopReason.RiskProhibited, "deterministic risk policyがprobeを禁止しました。");
        }
        if (risk.RecoveryEdgeIds.Any(edgeId =>
                !context.KnownReturnPathEdgeIds.Contains(edgeId, StringComparer.Ordinal)))
        {
            return StopRun(ExplorationStopReason.RecoveryLost, "deterministic policyが指定した復帰経路を現在構造で確認できません。");
        }
        if (remainingProbes <= 0
            || admission.ElapsedMilliseconds > policy.Budget.RemainingElapsedMilliseconds
            || admission.InferenceMilliseconds > policy.Budget.RemainingInferenceMilliseconds)
        {
            return StopRun(ExplorationStopReason.BudgetExhausted, "探索budgetを使い切りました。");
        }
        var requiresApproval = policy.OneStepApprovalRequired
            || risk.Level != ExplorationRiskLevel.Low
            || !risk.SideEffectFree
            || !risk.Reversible
            || risk.RecoveryEdgeIds.Count == 0;
        if (approval is null && requiresApproval)
        {
            return new ExplorationAdmissionDecision(
                ExplorationAdmissionStatus.NeedsApproval,
                ExplorationStopReason.ApprovalRequired,
                "Observation／proposal／policy／revisionへ束縛した一手承認が必要です。",
                false);
        }
        if (approval is not null && !ApprovalMatches(approval, proposal))
        {
            return Reject(ExplorationStopReason.ApprovalMismatch, "承認の束縛先がproposal前提と一致しません。");
        }
        if (!requiresApproval && (!risk.Reversible || risk.RecoveryEdgeIds.Count == 0))
        {
            return Reject(ExplorationStopReason.RecoveryMissing, "自動probeに必要な復帰経路がありません。");
        }
        return new ExplorationAdmissionDecision(ExplorationAdmissionStatus.Allowed, ExplorationStopReason.None, "dispatch可能です。", true);
    }

    private void Authorize(ExplorationApproval approval, RunEventActorType actor, DateTimeOffset persistedUtc)
    {
        var probe = RequireActive(approval.ProposalId);
        var attemptId = ids.Next("attempt");
        AppendRun(
            RunEventPayloadTypes.Proposal,
            RunEventActorType.Automation,
            probe.CorrelationId,
            probe.Admission.Proposal.SourceObservationId,
            probe.Admission.Proposal.SourceObservationId,
            probe.Admission.Proposal.ProposalId,
            attemptId,
            JsonSerializer.Serialize(probe.Admission.Proposal, Json),
            persistedUtc,
            attemptGate.CommitProposed);
        AppendRun(
            RunEventPayloadTypes.Approval,
            actor,
            probe.CorrelationId,
            approval.ApprovalId,
            approval.ObservationId,
            probe.Admission.Proposal.ProposalId,
            attemptId,
            JsonSerializer.Serialize(approval, Json),
            persistedUtc,
            attemptGate.CommitAuthorized);
        AppendStructure(
            StructureEventKind.ProbeApproved,
            actor == RunEventActorType.User ? StructureEventActor.User : StructureEventActor.Controller,
            probe.CorrelationId,
            approval.ApprovalId,
            approval.ObservationId,
            approval.ProposalId,
            attemptId,
            [approval.ApprovalId, approval.ObservationId, approval.ProposalId, approval.PolicyRevisionId],
            StructureEventPayloadTypes.ExplorationApproval,
            JsonSerializer.Serialize(approval, Json),
            null,
            persistedUtc,
            persistedUtc);
        active = probe with { AttemptId = attemptId, Authorized = true, Approval = approval, Decision = new(
            ExplorationAdmissionStatus.Allowed,
            ExplorationStopReason.None,
            "dispatch可能です。",
            true) };
    }

    private void ValidateOutcome(ActiveProbe probe, ExplorationOutcomeReport report)
    {
        if (!string.Equals(report.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !probe.Admission.Proposal.AllowedOutcomes.Contains(report.Outcome)
            || !string.Equals(report.AfterScene.Frame.SourceId, policy.TargetWindowSourceId, StringComparison.Ordinal)
            || report.AfterScene.Frame.Sequence <= probe.Admission.Context.CurrentScene.Frame.Sequence
            || report.AfterScene.CaptureAvailability != CaptureAvailability.Available && report.Outcome != ExplorationOutcomeKind.Unavailable
            || report.StableFramesObserved < probe.Admission.Proposal.WaitCondition.StableFrames
            || report.StableMillisecondsObserved < probe.Admission.Proposal.WaitCondition.MinimumStableMilliseconds)
        {
            stopReason = ExplorationStopReason.StabilityInsufficient;
            throw new InvalidOperationException("after Observationまたは安定窓がproposal契約を満たしません。");
        }
    }

    private void RecordProgress(ExplorationProposal proposal, ObservedScene scene, ExplorationOutcomeKind outcome)
    {
        var probeKey = $"{proposal.AffordanceCandidateId}\n{proposal.Primitive}";
        probeCounts.TryGetValue(probeKey, out var count);
        count++;
        probeCounts[probeKey] = count;
        if (count >= policy.StopPolicy.MaximumRepeatedProbeCount)
        {
            stopReason = ExplorationStopReason.RepeatedProbe;
        }

        consecutiveNoProgress = outcome == ExplorationOutcomeKind.NoChange ? consecutiveNoProgress + 1 : 0;
        if (consecutiveNoProgress >= policy.StopPolicy.MaximumConsecutiveNoProgressCount)
        {
            stopReason = ExplorationStopReason.NoProgress;
        }

        RecordState(scene);
        if (observedStateHistory.Count >= 4)
        {
            var tail = observedStateHistory.TakeLast(4).ToArray();
            if (tail[0] == tail[2] && tail[1] == tail[3] && tail[0] != tail[1])
            {
                oscillationCount++;
                if (oscillationCount >= policy.StopPolicy.MaximumOscillationCount)
                {
                    stopReason = ExplorationStopReason.Oscillation;
                }
            }
        }
    }

    private void RecordState(ObservedScene scene) =>
        observedStateHistory.Add(scene.StateHypothesisId ?? $"observation:{scene.ObservationId}");

    private void AppendStructure(
        StructureEventKind kind,
        StructureEventActor actor,
        string correlationId,
        string causationId,
        string? observationId,
        string? proposalId,
        string? attemptId,
        IReadOnlyList<string> evidenceIds,
        string payloadType,
        string payloadJson,
        ExplorationOutcomeKind? outcome,
        DateTimeOffset occurredUtc,
        DateTimeOffset persistedUtc)
    {
        var appended = structureStore.Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03,
                ids.Next("structure-event"),
                binding.GameId,
                binding.EnvironmentScope,
                kind,
                actor,
                correlationId,
                causationId,
                observationId,
                proposalId,
                attemptId,
                evidenceIds.Distinct(StringComparer.Ordinal).ToArray(),
                payloadType,
                payloadJson,
                outcome,
                occurredUtc),
            currentStructureRevisionId == "structure:root" ? null : currentStructureRevisionId,
            persistedUtc);
        currentStructureRevisionId = appended.ResultingStructureRevisionId;
    }

    private void AppendRun(
        string payloadType,
        RunEventActorType actor,
        string correlationId,
        string causationId,
        string? observationId,
        string? commandId,
        string? attemptId,
        string payloadJson,
        DateTimeOffset persistedUtc,
        Action<RunEvent> append)
    {
        var runEvent = NewRunEvent(
            payloadType,
            actor,
            correlationId,
            causationId,
            observationId,
            commandId,
            attemptId,
            payloadJson,
            persistedUtc);
        append(runEvent);
        nextRunSequence++;
    }

    private RunEvent NewRunEvent(
        string payloadType,
        RunEventActorType actor,
        string correlationId,
        string causationId,
        string? observationId,
        string? commandId,
        string? attemptId,
        string payloadJson,
        DateTimeOffset persistedUtc) =>
        new(
            ContractSchemaVersions.Revision01,
            ids.Next("run-event"),
            binding.ExplorationRunId,
            nextRunSequence,
            binding.PlaybookId,
            binding.PlaybookVersionId,
            null,
            commandId,
            attemptId,
            causationId,
            correlationId,
            binding.ExecutorEpoch,
            actor,
            persistedUtc,
            persistedUtc,
            observationId,
            payloadType,
            payloadJson);

    private ActiveProbe RequireActive(string? proposalId = null)
    {
        var probe = active ?? throw new InvalidOperationException("active proposalがありません。");
        if (proposalId is not null && !string.Equals(probe.Admission.Proposal.ProposalId, proposalId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"proposal '{proposalId}' はactive proposalではありません。");
        }
        return probe;
    }

    private bool ApprovalMatches(ExplorationApproval approval, ExplorationProposal proposal) =>
        string.Equals(approval.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
        && string.Equals(approval.ProposalId, proposal.ProposalId, StringComparison.Ordinal)
        && string.Equals(approval.ObservationId, proposal.SourceObservationId, StringComparison.Ordinal)
        && string.Equals(approval.PolicyRevisionId, policy.PolicyRevisionId, StringComparison.Ordinal)
        && string.Equals(approval.StructureRevisionId, proposal.SourceStructureRevisionId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(approval.ActorId);

    private static bool SchemasAreCurrent(
        ExplorationContext context,
        ExplorationProposal proposal,
        ExplorationRiskAssessment risk) =>
        string.Equals(context.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
        && string.Equals(context.Policy.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
        && string.Equals(context.Policy.StopPolicy.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
        && string.Equals(context.CurrentScene.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
        && string.Equals(proposal.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
        && string.Equals(proposal.WaitCondition.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
        && string.Equals(risk.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal);

    private void ValidateScene(ObservedScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!string.Equals(scene.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !string.Equals(scene.Frame.SourceId, policy.TargetWindowSourceId, StringComparison.Ordinal))
        {
            throw new ArgumentException("ObservedSceneのschemaまたはtarget windowがRun policyと一致しません。", nameof(scene));
        }
    }

    private static void ValidateRunContract(ExplorationRunBinding binding, ExplorationPolicy policy)
    {
        if (!string.Equals(binding.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !string.Equals(policy.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !string.Equals(policy.StopPolicy.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !string.Equals(binding.EnvironmentScope, policy.EnvironmentScope, StringComparison.Ordinal)
            || policy.Budget.RemainingProbes <= 0
            || policy.Budget.RemainingInferenceMilliseconds <= 0
            || policy.Budget.RemainingElapsedMilliseconds <= 0
            || policy.StopPolicy.MaximumFrameFreshnessMilliseconds < 0
            || policy.StopPolicy.MaximumRepeatedProbeCount <= 0
            || policy.StopPolicy.MaximumConsecutiveNoProgressCount <= 0
            || policy.StopPolicy.MaximumOscillationCount <= 0)
        {
            throw new ArgumentException("Exploration Run bindingまたはimmutable policyが不正です。");
        }
    }

    private static IReadOnlyList<string> EvidenceIds(ObservedScene scene) =>
        [scene.ObservationId, .. scene.Affordances.Select(affordance => affordance.CandidateId)];

    private static bool IsWindowBoundLocator(AffordanceLocator locator)
    {
        if (locator.NormalizedBounds is not { Count: 4 })
        {
            return false;
        }

        var x = locator.NormalizedBounds[0];
        var y = locator.NormalizedBounds[1];
        var width = locator.NormalizedBounds[2];
        var height = locator.NormalizedBounds[3];
        return double.IsFinite(x)
            && double.IsFinite(y)
            && double.IsFinite(width)
            && double.IsFinite(height)
            && x >= 0
            && y >= 0
            && width > 0
            && height > 0
            && x + width <= 1
            && y + height <= 1;
    }

    private static bool SceneBindingMatches(ObservedScene supplied, ObservedScene committed) =>
        string.Equals(supplied.ObservationId, committed.ObservationId, StringComparison.Ordinal)
        && string.Equals(supplied.Frame.SourceId, committed.Frame.SourceId, StringComparison.Ordinal)
        && supplied.Frame.Backend == committed.Frame.Backend
        && supplied.Frame.Sequence == committed.Frame.Sequence
        && supplied.Frame.TransformRevision == committed.Frame.TransformRevision
        && supplied.Frame.FreshnessMs == committed.Frame.FreshnessMs
        && supplied.CaptureAvailability == committed.CaptureAvailability;

    private static bool CandidateBindingMatches(AffordanceCandidate supplied, AffordanceCandidate committed) =>
        string.Equals(supplied.SchemaVersion, committed.SchemaVersion, StringComparison.Ordinal)
        && string.Equals(supplied.CandidateId, committed.CandidateId, StringComparison.Ordinal)
        && string.Equals(supplied.ObservationId, committed.ObservationId, StringComparison.Ordinal)
        && supplied.FrameSequence == committed.FrameSequence
        && supplied.TransformRevision == committed.TransformRevision
        && string.Equals(supplied.TargetWindowSourceId, committed.TargetWindowSourceId, StringComparison.Ordinal)
        && string.Equals(supplied.Locator.SchemaVersion, committed.Locator.SchemaVersion, StringComparison.Ordinal)
        && string.Equals(supplied.Locator.LocatorType, committed.Locator.LocatorType, StringComparison.Ordinal)
        && string.Equals(supplied.Locator.LocatorRevision, committed.Locator.LocatorRevision, StringComparison.Ordinal)
        && supplied.Locator.NormalizedBounds.SequenceEqual(committed.Locator.NormalizedBounds)
        && supplied.AllowedPrimitives.SequenceEqual(committed.AllowedPrimitives, StringComparer.Ordinal);

    private static ExplorationAdmissionDecision Reject(ExplorationStopReason reason, string detail) =>
        new(ExplorationAdmissionStatus.Rejected, reason, detail, false);

    private ExplorationAdmissionDecision StopRun(ExplorationStopReason reason, string detail)
    {
        stopReason = reason;
        return Stopped(reason, detail);
    }

    private static ExplorationAdmissionDecision Stopped(ExplorationStopReason reason, string detail) =>
        new(ExplorationAdmissionStatus.Stopped, reason, detail, false);

    private sealed record ActiveProbe(
        ExplorationProposalAdmission Admission,
        ExplorationAdmissionDecision Decision,
        string CorrelationId,
        string? AttemptId,
        bool Authorized,
        bool Dispatched,
        ExplorationApproval? Approval);

    private sealed record CompletedProbe(ActiveProbe Probe, TransitionEvidence Evidence);
}
