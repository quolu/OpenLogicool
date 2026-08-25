using System.Text.Json;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Playbooks;

/// <summary>
/// Visual Macroを一stepずつ進める決定的runner。画面監査とjournal済みNano dispatchの順序だけを所有し、
/// capture、認識、入力方式、retry、AI修復は所有しない。
/// </summary>
public sealed class SupervisedVisualMacroRunner
{
    private readonly VisualMacroProgram program;
    private readonly RunJournal journal;
    private readonly AttemptDispatchGate attemptGate;
    private readonly Func<string, string> nextId;
    private readonly TimeProvider timeProvider;
    private readonly long executorEpoch;
    private readonly List<SupervisedMacroStepHistory> history = [];
    private long nextSequence;
    private int stepIndex;
    private SupervisedMacroRunState state = SupervisedMacroRunState.AwaitingBeforeAudit;
    private SupervisedMacroStopReason stopReason;
    private string statusMessage = "操作前の画面を確認してください。";

    public SupervisedVisualMacroRunner(
        VisualMacroProgram program,
        string runId,
        RunJournal journal,
        AttemptDispatchGate attemptGate,
        Func<string, string> nextId,
        TimeProvider? timeProvider = null,
        long executorEpoch = 1)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(attemptGate);
        ArgumentNullException.ThrowIfNull(nextId);
        if (program.Steps.Count == 0)
        {
            throw new ArgumentException("Visual Macroにstepがありません。", nameof(program));
        }
        if (executorEpoch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(executorEpoch));
        }

        this.program = program;
        RunId = runId;
        this.journal = journal;
        this.attemptGate = attemptGate;
        this.nextId = nextId;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.executorEpoch = executorEpoch;
        nextSequence = journal.ReadRun(runId).LastOrDefault()?.RunSequence + 1 ?? 1;
    }

    public string RunId { get; }

    public VisualMacroStep CurrentStep => program.Steps[stepIndex];

    public SupervisedMacroRunSnapshot Snapshot => new(
        RunId,
        new SupervisedMacroRunPin(
            program.ProgramId,
            program.RouteVersionId,
            program.StructureRevisionId,
            program.GameId,
            program.EnvironmentScope),
        state,
        stopReason,
        CurrentSequence,
        program.Steps.Count,
        statusMessage,
        history.ToArray());

    public VisualMacroAuditResult AuditBefore(ObservedScene scene)
    {
        RequireState(SupervisedMacroRunState.AwaitingBeforeAudit);
        var result = VisualMacroAuditor.AuditBefore(CurrentStep, scene);
        journal.Append(Event(
            RunEventPayloadTypes.Observation,
            RunEventActorType.System,
            observationId: scene.ObservationId,
            payload: new { Phase = "before", Audit = result, Scene = scene }));
        history.Add(new SupervisedMacroStepHistory(CurrentStep.Sequence, result, null, false, false, null));
        if (!result.CanContinue)
        {
            Stop(SupervisedMacroStopReason.BeforeAuditFailed, result.Message);
            return result;
        }

        state = SupervisedMacroRunState.ReadyToDispatch;
        statusMessage = "操作前の画面が一致しました。次の一手を送信できます。";
        return result;
    }

    /// <summary>
    /// 利用者が画面確認後に待った時間でframeが古くなるため、dispatch直前に同じstepを再観測する。
    /// 再観測もjournalへ残し、ConfirmedでなければAttemptを作る前に停止する。
    /// </summary>
    public VisualMacroAuditResult ReauditBefore(ObservedScene scene)
    {
        RequireState(SupervisedMacroRunState.ReadyToDispatch);
        var result = VisualMacroAuditor.AuditBefore(CurrentStep, scene);
        journal.Append(Event(
            RunEventPayloadTypes.Observation,
            RunEventActorType.System,
            observationId: scene.ObservationId,
            payload: new { Phase = "before-dispatch", Audit = result, Scene = scene }));
        ReplaceHistory(history[^1] with { BeforeAudit = result });
        if (!result.CanContinue)
        {
            Stop(SupervisedMacroStopReason.BeforeAuditFailed, result.Message);
            return result;
        }

        statusMessage = "送信直前の画面が一致しました。";
        return result;
    }

    public void DispatchOnce(Action nanoSerialHidInput)
    {
        DispatchOnce(nanoSerialHidInput, RunEventActorType.User, "interactive-user");
    }

    public void DispatchOnce(
        Action nanoSerialHidInput,
        RunEventActorType authorizationActor,
        string authorizationMode)
    {
        ArgumentNullException.ThrowIfNull(nanoSerialHidInput);
        if (authorizationActor is not (RunEventActorType.User or RunEventActorType.Automation))
        {
            throw new ArgumentOutOfRangeException(nameof(authorizationActor));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationMode);
        RequireState(SupervisedMacroRunState.ReadyToDispatch);
        var attemptId = nextId("attempt");
        var commandId = nextId("command");
        var causationId = history[^1].BeforeAudit!.ObservationId;
        attemptGate.CommitProposed(Event(
            RunEventPayloadTypes.Proposal,
            RunEventActorType.Automation,
            attemptId,
            commandId,
            causationId: causationId,
            payload: new { CurrentStep.Sequence, CurrentStep.Primitive, CurrentStep.AffordanceCandidateId }));
        attemptGate.CommitAuthorized(Event(
            RunEventPayloadTypes.Authorization,
            authorizationActor,
            attemptId,
            commandId,
            causationId: causationId,
            payload: new { Mode = authorizationMode }));
        attemptGate.MarkPrepared(attemptId);
        ReplaceHistory(history[^1] with { AttemptId = attemptId });

        try
        {
            attemptGate.ArmThenDispatch(
                Event(
                    RunEventPayloadTypes.Dispatch,
                    RunEventActorType.Automation,
                    attemptId,
                    commandId,
                    causationId: causationId,
                    payload: new { Route = "nano-serial-hid", CurrentStep.Primitive }),
                nanoSerialHidInput);
            ReplaceHistory(history[^1] with { DispatchArmed = true });
            attemptGate.CommitReported(Event(
                RunEventPayloadTypes.DispatchResult,
                RunEventActorType.System,
                attemptId,
                commandId,
                causationId: causationId,
                payload: new { Handoff = "reported", SuccessIsNotConfirmed = true }));
            ReplaceHistory(history[^1] with { DispatchReported = true });
            state = SupervisedMacroRunState.AwaitingAfterAudit;
            statusMessage = "入力を一回送信しました。結果画面を確認してください。";
        }
        catch (SupervisedMacroDispatchNotStartedException exception)
        {
            ReplaceHistory(history[^1] with { DispatchArmed = true });
            attemptGate.CommitDisarmed(Event(
                RunEventPayloadTypes.Disarm,
                RunEventActorType.System,
                attemptId,
                commandId,
                causationId: causationId,
                payload: new { Reason = "dispatch-not-started", exception.Message }));
            state = SupervisedMacroRunState.Stopped;
            stopReason = SupervisedMacroStopReason.DispatchNotStarted;
            statusMessage = $"Nano入力を送信せず停止しました: {exception.Message}";
            throw;
        }
        catch (Exception exception)
        {
            ReplaceHistory(history[^1] with { DispatchArmed = true });
            state = SupervisedMacroRunState.OutcomeUnknown;
            stopReason = SupervisedMacroStopReason.DispatchFault;
            statusMessage = $"入力結果を確定できないため停止しました: {exception.Message}";
            throw;
        }
    }

    public VisualMacroAuditResult AuditAfterTransition(
        SupervisedMacroTransitionObservation transition)
    {
        RequireState(SupervisedMacroRunState.AwaitingAfterAudit);
        var attemptId = history[^1].AttemptId!;
        var result = VisualMacroAuditor.AuditTransition(CurrentStep, transition);
        attemptGate.CommitObserving(Event(
            RunEventPayloadTypes.Observation,
            RunEventActorType.System,
            attemptId,
            observationId: transition.FinalScene?.ObservationId,
            payload: new
            {
                Phase = "after",
                Audit = result,
                transition.Stability,
                transition.Comparison,
                transition.DestinationMatched,
            }));
        ReplaceHistory(history[^1] with { AfterAudit = result });
        if (!result.CanContinue)
        {
            if (result.Status == VisualMacroAuditStatus.UnexpectedState)
            {
                attemptGate.CommitRejected(Event(
                    RunEventPayloadTypes.Rejection,
                    RunEventActorType.System,
                    attemptId,
                    observationId: transition.FinalScene?.ObservationId,
                    payload: new { Phase = "after", Audit = result, transition.Comparison }));
            }
            else
            {
                attemptGate.ResolveLocally(attemptId, AttemptState.OutcomeUnknown);
            }
            Stop(SupervisedMacroStopReason.AfterAuditFailed, result.Message);
            return result;
        }

        attemptGate.CommitConfirmed(Event(
            RunEventPayloadTypes.Confirmation,
            RunEventActorType.System,
            attemptId,
            observationId: transition.FinalScene!.ObservationId,
            payload: new
            {
                Phase = "after",
                Audit = result,
                transition.Stability,
                transition.Comparison,
                transition.DestinationMatched,
            }));
        stepIndex++;
        if (stepIndex == program.Steps.Count)
        {
            state = SupervisedMacroRunState.Completed;
            statusMessage = "全stepの画面遷移を確認し、マクロを完了しました。";
        }
        else
        {
            state = SupervisedMacroRunState.AwaitingBeforeAudit;
            statusMessage = "次stepの操作前画面を確認してください。";
        }
        return result;
    }

    public void StopByUser()
    {
        if (state is SupervisedMacroRunState.Completed or SupervisedMacroRunState.Stopped)
        {
            return;
        }
        journal.Append(Event(
            RunEventPayloadTypes.Abandon,
            RunEventActorType.User,
            payload: new { Reason = "user-stop", State = state.ToString() }));
        Stop(SupervisedMacroStopReason.UserStopped, "利用者が実行を停止しました。");
    }

    public void StopBeforeDispatchUnavailable(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (state is not (SupervisedMacroRunState.AwaitingBeforeAudit or SupervisedMacroRunState.ReadyToDispatch))
        {
            throw new InvalidOperationException($"現在の状態は{state}であり、入力前のruntime停止にはできません。");
        }
        journal.Append(Event(
            RunEventPayloadTypes.RuntimeUnavailable,
            RunEventActorType.System,
            payload: new { Reason = "runtime-unavailable-before-dispatch", Message = message }));
        Stop(SupervisedMacroStopReason.RuntimeUnavailable, message);
    }

    public void MarkAfterObservationUnknown(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        RequireState(SupervisedMacroRunState.AwaitingAfterAudit);
        attemptGate.ResolveLocally(history[^1].AttemptId!, AttemptState.OutcomeUnknown);
        state = SupervisedMacroRunState.OutcomeUnknown;
        stopReason = SupervisedMacroStopReason.ObservationFault;
        statusMessage = $"入力後の画面を確認できないため、結果不明として停止しました: {message}";
    }

    private int CurrentSequence => state == SupervisedMacroRunState.Completed
        ? program.Steps.Count
        : CurrentStep.Sequence;

    private void Stop(SupervisedMacroStopReason reason, string message)
    {
        state = SupervisedMacroRunState.Stopped;
        stopReason = reason;
        statusMessage = message;
    }

    private RunEvent Event(
        string payloadType,
        RunEventActorType actor,
        string? attemptId = null,
        string? commandId = null,
        string? observationId = null,
        string? causationId = null,
        object? payload = null)
    {
        var now = timeProvider.GetUtcNow();
        return new RunEvent(
            ContractSchemaVersions.Revision01,
            nextId("event"),
            RunId,
            nextSequence++,
            program.ProgramId,
            program.RouteVersionId,
            CurrentStep.StructureEdgeId,
            commandId,
            attemptId,
            causationId ?? nextId("cause"),
            nextId("correlation"),
            executorEpoch,
            actor,
            now,
            now,
            observationId,
            payloadType,
            JsonSerializer.Serialize(payload ?? new { }));
    }

    private void RequireState(SupervisedMacroRunState expected)
    {
        if (state != expected)
        {
            throw new InvalidOperationException($"現在の状態は{state}であり、{expected}の操作はできません。");
        }
    }

    private void ReplaceHistory(SupervisedMacroStepHistory value) => history[^1] = value;
}
