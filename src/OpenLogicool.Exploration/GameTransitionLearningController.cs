using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Exploration;

public interface IExplorationOutcomeRecorder
{
    TransitionEvidence RecordOutcome(ExplorationOutcomeReport report);
}

public sealed class ExplorationCoordinatorOutcomeRecorder(ExplorationCoordinator coordinator)
    : IExplorationOutcomeRecorder
{
    public TransitionEvidence RecordOutcome(ExplorationOutcomeReport report) =>
        coordinator.RecordOutcome(report);
}

/// <summary>意味判定を既存Durable Attemptのoutcomeへ変換する唯一の境界。</summary>
public interface IGameTransitionLearner
{
    GameTransitionLearningResult Learn(GameTransitionLearningRequest request);
}

public sealed class GameTransitionLearningController(IExplorationOutcomeRecorder recorder)
    : IGameTransitionLearner
{
    public GameTransitionLearningResult Learn(GameTransitionLearningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        if (request.Dispatch.Status == GameInteractionDispatchStatus.DispatchFailed)
        {
            return new GameTransitionLearningResult(
                GameTransitionLearningStatus.DispatchFailed,
                null,
                request.Dispatch.FailureReason ?? "Nano dispatch failed");
        }
        var after = request.Stability.StableScene
            ?? request.Stability.Observations.LastOrDefault()
            ?? throw new InvalidOperationException("Transition Evidenceにはafter Observationが必要です。");
        var outcome = Outcome(request.Comparison.Judgement, after.StateIdentity);
        var report = new ExplorationOutcomeReport(
            ContractSchemaVersions.Revision03,
            request.ProposalId,
            after,
            outcome,
            request.Stability.StableFramesObserved,
            request.Stability.StableMillisecondsObserved,
            request.TransitionEvidenceId,
            request.DispatchMonotonicMilliseconds,
            request.ObservationCompletedMonotonicMilliseconds,
            request.RecordedUtc,
            request.Dispatch,
            request.Comparison,
            request.Stability.Observations.Select(scene => scene.ObservationId).ToArray());
        var evidence = recorder.RecordOutcome(report);
        if (!string.Equals(evidence.AttemptId, request.AttemptId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Transition EvidenceのAttempt IDが学習要求と一致しません。");
        }
        return new GameTransitionLearningResult(
            GameTransitionLearningStatus.Learned,
            evidence,
            $"outcome:{outcome}");
    }

    private static ExplorationOutcomeKind Outcome(
        GameTransitionJudgement judgement,
        StateIdentityStatus afterIdentity) => judgement switch
        {
            GameTransitionJudgement.Stayed => ExplorationOutcomeKind.NoChange,
            GameTransitionJudgement.Undetermined => ExplorationOutcomeKind.OutcomeUnknown,
            GameTransitionJudgement.Moved when afterIdentity == StateIdentityStatus.Novel =>
                ExplorationOutcomeKind.Novel,
            GameTransitionJudgement.Moved when afterIdentity == StateIdentityStatus.Known =>
                ExplorationOutcomeKind.Destination,
            GameTransitionJudgement.Moved => ExplorationOutcomeKind.OutcomeUnknown,
            _ => throw new ArgumentOutOfRangeException(nameof(judgement)),
        };

    private static void Validate(GameTransitionLearningRequest request)
    {
        if (!string.Equals(request.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(request.ProposalId)
            || string.IsNullOrWhiteSpace(request.AttemptId)
            || string.IsNullOrWhiteSpace(request.TransitionEvidenceId)
            || string.IsNullOrWhiteSpace(request.EnvironmentScope)
            || !string.Equals(request.Dispatch.ObservationId, request.Before.ObservationId, StringComparison.Ordinal)
            || !string.Equals(request.Comparison.BeforeObservationId, request.Before.ObservationId, StringComparison.Ordinal)
            || request.Dispatch.Status == GameInteractionDispatchStatus.Dispatched
                && request.Dispatch.DispatchCount != 1)
        {
            throw new ArgumentException("transition learning requestのschema／identity／dispatchが不正です。", nameof(request));
        }
        var after = request.Stability.StableScene ?? request.Stability.Observations.LastOrDefault();
        if (after is not null
            && request.Comparison.AfterObservationId is not null
            && !string.Equals(request.Comparison.AfterObservationId, after.ObservationId, StringComparison.Ordinal))
        {
            throw new ArgumentException("comparisonとafter Observationが一致しません。", nameof(request));
        }
    }
}
