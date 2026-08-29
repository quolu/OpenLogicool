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
/// Moved操作はStructureへcommitし、Stayed／Undetermined／寄り道（既に経由した状態へ戻る）／
/// 重複（同じ状態遷移の再発生）はroute本体から除外して理由を残す。元sessionと既存route revisionは
/// 変更せず、goal単位のrouteへ新しいrevisionだけを追記する。
/// </summary>
public sealed class DemonstrationRouteCompiler(
    IGameStructureStore structures,
    IGameInteractionStructureCommitter committer,
    ILearningRouteStore routes,
    TimeProvider? timeProvider = null) : IDemonstrationRouteCompiler
{
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
        var visitedSignatures = new HashSet<string>(StringComparer.Ordinal)
        {
            GameSceneSemanticComparer.SignatureId(operations[0].Before),
        };
        var seenTransitionKeys = new HashSet<string>(StringComparer.Ordinal);
        string? latestStructureRevisionId = null;

        foreach (var operation in operations)
        {
            if (operation.Comparison.Judgement == GameTransitionJudgement.Stayed)
            {
                decisions.Add(new DemonstrationRouteDecision(
                    operation.OperationId,
                    DemonstrationRouteDecisionKind.ExcludedStayed,
                    "状態が変化しませんでした（Stayed）。",
                    null));
                continue;
            }

            if (operation.Comparison.Judgement == GameTransitionJudgement.Undetermined)
            {
                decisions.Add(new DemonstrationRouteDecision(
                    operation.OperationId,
                    DemonstrationRouteDecisionKind.ExcludedUndetermined,
                    "遷移を判定できませんでした（Undetermined）。",
                    null));
                continue;
            }

            var afterScene = operation.After.StableScene;
            if (afterScene is null)
            {
                decisions.Add(new DemonstrationRouteDecision(
                    operation.OperationId,
                    DemonstrationRouteDecisionKind.ExcludedUndetermined,
                    "Movedと判定されましたが、安定後の観測がありません。",
                    null));
                continue;
            }

            var commitResult = Commit(session.Session, operation, afterScene);
            latestStructureRevisionId = commitResult.Revision.RevisionId;
            var edgeId = commitResult.EdgeId
                ?? throw new InvalidOperationException(
                    $"操作 {operation.OperationId} のcommitでEdgeIdが得られませんでした。");

            var beforeSignature = GameSceneSemanticComparer.SignatureId(operation.Before);
            var afterSignature = GameSceneSemanticComparer.SignatureId(afterScene);
            var transitionKey = string.Join('', beforeSignature, afterSignature, operation.Operation);

            if (!seenTransitionKeys.Add(transitionKey))
            {
                decisions.Add(new DemonstrationRouteDecision(
                    operation.OperationId,
                    DemonstrationRouteDecisionKind.ExcludedDuplicate,
                    "同じ状態遷移が既にrouteにあります（重複）。",
                    edgeId));
                continue;
            }

            if (!visitedSignatures.Add(afterSignature))
            {
                decisions.Add(new DemonstrationRouteDecision(
                    operation.OperationId,
                    DemonstrationRouteDecisionKind.ExcludedDetour,
                    "既に経由した状態へ戻る操作のため、routeから除外しました（寄り道）。",
                    edgeId));
                continue;
            }

            acceptedEdgeIds.Add(edgeId);
            decisions.Add(new DemonstrationRouteDecision(
                operation.OperationId,
                DemonstrationRouteDecisionKind.Accepted,
                "Moved操作をrouteへ採用しました。",
                edgeId));
        }

        if (acceptedEdgeIds.Count == 0)
        {
            throw new InvalidOperationException("routeへ採用できる操作がありませんでした。");
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
            LearningRouteStatus.Compiled,
            time.GetUtcNow()));

        return new DemonstrationRouteCompilationResult(session.Session.SessionId, revision, decisions);
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

        // 記録時のwait条件そのものは原本に残らないため、実測済みの安定観測から
        // 再生時の待機条件を復元する（timeoutは観測にかかった実測msをそのまま使う）。
        var waitCondition = new ExplorationWaitCondition(
            ContractSchemaVersions.Revision03,
            Math.Max(operation.After.StableFramesObserved, 1),
            operation.After.StableMillisecondsObserved,
            Math.Max(operation.After.ElapsedMilliseconds, operation.After.StableMillisecondsObserved));

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
