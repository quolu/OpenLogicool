using System.Text.Json;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Exploration;

public interface IDemonstrationRouteCompiler
{
    DemonstrationRouteCompilationResult Compile(DemonstrationSessionRecord session);
}

/// <summary>
/// 停止済みの操作デモ原本を、既存Game Structure／Transition Evidence／Learning Routeへ導出する。
/// 全操作を記録順で候補routeへ保持する。Movedだけを構造へ学習し、他は確認待ちにする。
/// 重複も独立した手順として残す。元sessionと既存route revisionは
/// 変更せず、goal単位のrouteへ新しいrevisionだけを追記する。
/// </summary>
public sealed class DemonstrationRouteCompiler(
    IGameStructureStore structures,
    IGameInteractionStructureCommitter committer,
    ILearningRouteStore routes,
    TimeProvider? timeProvider = null) : IDemonstrationRouteCompiler
{
    public static ExplorationWaitCondition ReplayWaitCondition { get; } = new(
        ContractSchemaVersions.Revision03, 2, 1_000, 10_000);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public DemonstrationRouteCompilationResult Compile(DemonstrationSessionRecord session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State != DemonstrationSessionState.Stopped)
        {
            throw new InvalidOperationException("記録停止前の操作デモ原本はLearning Routeへ導出できません。");
        }

        var operations = session.Events
            .Where(item => item.Kind == DemonstrationEventKind.Operation)
            .Select(item => item.Operation!)
            .ToArray();
        if (operations.Length == 0)
        {
            throw new InvalidOperationException("操作デモ原本に操作eventがありません。");
        }

        var decisions = new List<DemonstrationRouteDecision>();
        var acceptedEdgeIds = new List<string>();
        var recordedSteps = new List<DemonstrationRouteStep>();
        var latestStructureRevisionId = structures.LoadRevision(session.Session.GameId, session.Session.EnvironmentScope).RevisionId;

        foreach (var operation in operations)
        {
            var afterScene = operation.After.StableScene;
            if (operation.Comparison.Judgement != GameTransitionJudgement.Moved || afterScene is null)
            {
                var reason = operation.Comparison.Judgement == GameTransitionJudgement.Stayed
                    ? "画面変化なし。意図した結果か確認してください。"
                    : "操作後の画面を判定できませんでした。画像確認またはこの手順の再記録が必要です。";
                recordedSteps.Add(ToStep(session.Session.SessionId, operation, null, null, reason));
                decisions.Add(new DemonstrationRouteDecision(operation.OperationId,
                    DemonstrationRouteDecisionKind.PendingReview, reason, null));
                continue;
            }

            var commitResult = Commit(session.Session, operation, afterScene);
            latestStructureRevisionId = commitResult.Revision.RevisionId;
            var edgeId = commitResult.EdgeId
                ?? throw new InvalidOperationException(
                    $"操作 {operation.OperationId} のcommitでEdgeIdが得られませんでした。");

            acceptedEdgeIds.Add(edgeId);
            recordedSteps.Add(ToStep(session.Session.SessionId, operation, edgeId, GameTransitionJudgement.Moved,
                "記録の画面変化を確認しました。"));
            decisions.Add(new DemonstrationRouteDecision(
                operation.OperationId,
                DemonstrationRouteDecisionKind.Accepted,
                "Moved操作をrouteへ採用しました。",
                edgeId));
        }

        var routeId = DemonstrationGoalRouteIds.Create(
            session.Session.GameId, session.Session.EnvironmentScope, session.Session.Goal);
        var existingLatest = routes.LoadLatest(routeId);
        if (existingLatest is not null
            && (existingLatest.GameId != session.Session.GameId
                || existingLatest.EnvironmentScope != session.Session.EnvironmentScope
                || existingLatest.Goal != session.Session.Goal))
        {
            throw new InvalidOperationException("目的routeのscopeまたはgoalが一致しません。");
        }

        var revision = routes.Append(new LearningRouteDraft(
            ContractSchemaVersions.Revision03,
            routeId,
            existingLatest?.VersionId,
            session.Session.GameId,
            session.Session.EnvironmentScope,
            latestStructureRevisionId!,
            session.Session.Goal,
            acceptedEdgeIds,
            LearningRouteAuthor.User,
            null,
            $"操作デモ原本 {session.Session.SessionId} から導出",
            recordedSteps.Any(step => step.ExpectedJudgement is null) ? LearningRouteStatus.Draft : LearningRouteStatus.Compiled,
            time.GetUtcNow(), recordedSteps));

        return new DemonstrationRouteCompilationResult(session.Session.SessionId, revision, decisions);
    }

    private static DemonstrationRouteStep ToStep(string sessionId, DemonstrationOperation operation,
        string? edgeId, GameTransitionJudgement? expected, string reason) => new(
        sessionId, operation.OperationId, operation.Operation, operation.Target.NormalizedPoint,
        operation.KeyTokens, operation.VerticalScrollSteps, operation.HorizontalScrollSteps,
        operation.DragDestinationNormalized, edgeId, expected, reason, sessionId);

    public DemonstrationRouteStep CompileStep(DemonstrationSessionRecord session)
    {
        if (session.State != DemonstrationSessionState.Stopped)
            throw new InvalidOperationException("再記録を終了してください。");
        var operations = session.Events.Where(item => item.Operation is not null).Select(item => item.Operation!).ToArray();
        if (operations.Length != 1)
            throw new InvalidOperationException("差し替える一手だけを記録してください。原本と候補は変更していません。");
        var operation = operations[0];
        var edgeId = operation.Comparison.Judgement == GameTransitionJudgement.Moved && operation.After.StableScene is { } after
            ? Commit(session.Session, operation, after).EdgeId : null;
        return ToStep(session.Session.SessionId, operation, edgeId,
            edgeId is null ? null : GameTransitionJudgement.Moved,
            edgeId is null ? "再記録した手順も確認待ちです。前後画像を確認してください。" : "この一手を再記録して遷移を確認しました。");
    }

    private GameInteractionStructureCommitResult Commit(
        DemonstrationSessionDraft session, DemonstrationOperation operation, ObservedScene afterScene)
    {
        var candidateId = $"demo-candidate:{operation.OperationId}";
        var isPointer = operation.Target.NormalizedPoint is { Count: 2 };
        var bounds = isPointer
            ? new[] { operation.Target.NormalizedPoint![0], operation.Target.NormalizedPoint![1], 0d, 0d }
            : [0d, 0d, 0d, 0d];
        var candidate = new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            candidateId,
            operation.Target.ObservationId,
            operation.Target.FrameSequence,
            operation.Target.TransformRevision,
            operation.Target.TargetWindowSourceId,
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                isPointer ? "demonstration-point" : "demonstration-key",
                bounds,
                "demo-1"),
            EvidenceRegions: [],
            Confidence: 1.0,
            AllowedPrimitives: [operation.Operation],
            SemanticKind: "demonstration",
            KeyTokens: operation.KeyTokens,
            VerticalScrollSteps: operation.VerticalScrollSteps,
            HorizontalScrollSteps: operation.HorizontalScrollSteps,
            DragDestinationNormalized: operation.DragDestinationNormalized);

        var beforeWithCandidate = operation.Before with
        {
            Affordances = [.. operation.Before.Affordances, candidate],
        };

        var outcome = afterScene.StateIdentity == StateIdentityStatus.Known
            ? ExplorationOutcomeKind.Destination
            : ExplorationOutcomeKind.Novel;

        var evidence = new TransitionEvidence(
            SchemaVersion: ContractSchemaVersions.Revision03,
            EvidenceId: operation.TransitionEvidenceId,
            BeforeObservationId: operation.Before.ObservationId,
            AfterObservationId: afterScene.ObservationId,
            AttemptId: operation.OperationId,
            AffordanceCandidateId: candidateId,
            Primitive: operation.Operation,
            Outcome: outcome,
            EnvironmentScope: session.EnvironmentScope,
            DispatchMonotonicMilliseconds: operation.OperationMonotonicMilliseconds,
            ObservationCompletedMonotonicMilliseconds: operation.ObservationCompletedMonotonicMilliseconds,
            RecordedUtc: operation.OccurredUtc,
            ExplorationRunId: null,
            DispatchReceipt: null,
            Comparison: operation.Comparison,
            ObservationSequenceIds: operation.After.Observations.Select(scene => scene.ObservationId).ToArray());

        // 原本の操作間隔・観測回数は証拠として保持し、再生は既存基盤の10秒Compareを使う。
        var waitCondition = ReplayWaitCondition;

        // GameInteractionStructureLearner（StructureKnowledgeController）はdelta operationが
        // 参照するevidenceが既にStructure Event Storeに記録済みであることを要求する。AI探索は
        // ExplorationCoordinator.RecordOutcomeがdispatch probeの一部としてこれを書くが、
        // demonstrationは物理入力が既に完了した後の事後導出であり同じprobe機構を持たないため、
        // Actor=Userの同形eventをここで直接登録して既知evidenceにする。
        RegisterEvidence(session.GameId, evidence);

        return committer.Commit(
            beforeWithCandidate,
            afterScene,
            evidence,
            waitCondition,
            riskTags: [],
            reversible: false,
            recordedUtc: operation.OccurredUtc);
    }

    private void RegisterEvidence(string gameId, TransitionEvidence evidence)
    {
        // GameStructureProjector.ReplayはOutcomeRecordedの前に、同じAttemptId／CorrelationIdの
        // DispatchArmedがあることを要求する（AI探索のdispatch-then-outcomeと同じ台帳形）。
        // demonstrationはNano等の実dispatchを経ないため、ここでActor=Userとして両eventを直接記録する。
        var correlationId = $"demo-correlation:{evidence.AttemptId}";
        var armedEventId = $"demo-dispatch-armed:{evidence.EvidenceId}";
        Append(
            gameId,
            evidence.EnvironmentScope,
            armedEventId,
            StructureEventKind.DispatchArmed,
            correlationId,
            causationId: evidence.AttemptId,
            observationId: evidence.BeforeObservationId,
            attemptId: evidence.AttemptId,
            evidenceIds: [evidence.BeforeObservationId],
            payloadType: StructureEventPayloadTypes.None,
            payloadJson: "{}",
            outcome: null,
            occurredUtc: evidence.RecordedUtc);

        var outcomeEventId = $"demo-outcome-recorded:{evidence.EvidenceId}";
        Append(
            gameId,
            evidence.EnvironmentScope,
            outcomeEventId,
            StructureEventKind.OutcomeRecorded,
            correlationId,
            causationId: evidence.AttemptId,
            observationId: evidence.AfterObservationId,
            attemptId: evidence.AttemptId,
            evidenceIds: [evidence.EvidenceId, evidence.BeforeObservationId, evidence.AfterObservationId],
            payloadType: StructureEventPayloadTypes.TransitionEvidence,
            payloadJson: JsonSerializer.Serialize(evidence, JsonOptions),
            outcome: evidence.Outcome,
            occurredUtc: evidence.RecordedUtc);
    }

    private void Append(
        string gameId,
        string environmentScope,
        string eventId,
        StructureEventKind kind,
        string correlationId,
        string causationId,
        string? observationId,
        string attemptId,
        IReadOnlyList<string> evidenceIds,
        string payloadType,
        string payloadJson,
        ExplorationOutcomeKind? outcome,
        DateTimeOffset occurredUtc)
    {
        var current = structures.LoadRevision(gameId, environmentScope);
        var expectedParentRevisionId = current.RevisionId == "structure:root" ? null : current.RevisionId;
        _ = structures.Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03,
                eventId,
                gameId,
                environmentScope,
                kind,
                StructureEventActor.User,
                CorrelationId: correlationId,
                CausationId: causationId,
                ObservationId: observationId,
                ProposalId: null,
                AttemptId: attemptId,
                EvidenceIds: evidenceIds,
                PayloadType: payloadType,
                PayloadJson: payloadJson,
                Outcome: outcome,
                OccurredUtc: occurredUtc),
            expectedParentRevisionId,
            occurredUtc);
    }
}
