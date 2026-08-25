using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using Xunit;

namespace OpenLogicool.Exploration.Tests;

public sealed class GameTransitionLearningControllerTests
{
    [Theory]
    [InlineData(GameTransitionJudgement.Moved, StateIdentityStatus.Novel, ExplorationOutcomeKind.Novel)]
    [InlineData(GameTransitionJudgement.Moved, StateIdentityStatus.Known, ExplorationOutcomeKind.Destination)]
    [InlineData(GameTransitionJudgement.Moved, StateIdentityStatus.Ambiguous, ExplorationOutcomeKind.Novel)]
    [InlineData(GameTransitionJudgement.Moved, StateIdentityStatus.InsufficientEvidence, ExplorationOutcomeKind.Novel)]
    [InlineData(GameTransitionJudgement.Stayed, StateIdentityStatus.Novel, ExplorationOutcomeKind.NoChange)]
    [InlineData(GameTransitionJudgement.Undetermined, StateIdentityStatus.Novel, ExplorationOutcomeKind.OutcomeUnknown)]
    public void Meaning_judgement_maps_to_durable_outcome(
        GameTransitionJudgement judgement,
        StateIdentityStatus afterIdentity,
        ExplorationOutcomeKind expected)
    {
        var recorder = new Recorder();
        var controller = new GameTransitionLearningController(recorder);

        var result = controller.Learn(Request(judgement, afterIdentity));

        Assert.Equal(GameTransitionLearningStatus.Learned, result.Status);
        Assert.Equal(expected, recorder.Report!.Outcome);
        Assert.Equal("nano-1", recorder.Report.DispatchReceipt!.TransportReceiptId);
        Assert.Equal(judgement, recorder.Report.Comparison!.Judgement);
        Assert.Equal(["after-1"], recorder.Report.ObservationSequenceIds);
        Assert.Equal("attempt-1", result.Evidence!.AttemptId);
    }

    [Fact]
    public void Dispatch_failure_does_not_create_transition_outcome()
    {
        var recorder = new Recorder();
        var controller = new GameTransitionLearningController(recorder);
        var request = Request(GameTransitionJudgement.Undetermined, StateIdentityStatus.Novel) with
        {
            Dispatch = Dispatch(GameInteractionDispatchStatus.DispatchFailed, "nano fault"),
            Stability = new GameInteractionStabilityResult(
                ContractSchemaVersions.Revision03,
                GameInteractionStabilityStatus.Fault,
                [],
                null,
                0,
                0,
                0,
                "not observed"),
            Comparison = new GameTransitionComparison(
                ContractSchemaVersions.Revision03,
                "before-1",
                null,
                GameTransitionJudgement.Undetermined,
                [],
                ["dispatch failed"]),
        };

        var result = controller.Learn(request);

        Assert.Equal(GameTransitionLearningStatus.DispatchFailed, result.Status);
        Assert.Null(result.Evidence);
        Assert.Null(recorder.Report);
    }

    [Fact]
    public void Mismatched_after_observation_is_rejected_before_store_call()
    {
        var recorder = new Recorder();
        var controller = new GameTransitionLearningController(recorder);
        var request = Request(GameTransitionJudgement.Moved, StateIdentityStatus.Novel) with
        {
            Comparison = new GameTransitionComparison(
                ContractSchemaVersions.Revision03,
                "before-1",
                "after-other",
                GameTransitionJudgement.Moved,
                [],
                ["changed"]),
        };

        Assert.Throws<ArgumentException>(() => controller.Learn(request));

        Assert.Null(recorder.Report);
    }

    private static GameTransitionLearningRequest Request(
        GameTransitionJudgement judgement,
        StateIdentityStatus afterIdentity)
    {
        var before = Scene("before-1", 1, StateIdentityStatus.Novel);
        var after = Scene("after-1", 2, afterIdentity);
        return new GameTransitionLearningRequest(
            ContractSchemaVersions.Revision03,
            "proposal-1",
            before,
            Dispatch(GameInteractionDispatchStatus.Dispatched, null),
            new GameInteractionStabilityResult(
                ContractSchemaVersions.Revision03,
                GameInteractionStabilityStatus.Stable,
                [after],
                after,
                3,
                300,
                300,
                null),
            new GameTransitionComparison(
                ContractSchemaVersions.Revision03,
                before.ObservationId,
                after.ObservationId,
                judgement,
                after.Affordances.SelectMany(candidate => candidate.EvidenceRegions).ToArray(),
                ["test"]),
            "attempt-1",
            "transition-1",
            "nikke:test",
            100,
            500,
            DateTimeOffset.UnixEpoch,
            "run-1");
    }

    private static GameInteractionDispatchReceipt Dispatch(
        GameInteractionDispatchStatus status,
        string? failure) => new(
        ContractSchemaVersions.Revision03,
        GameInteractionOperations.Click,
        status,
        "before-1",
        "window:game",
        "NanoSerialHid",
        1,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        "candidate-1",
        status == GameInteractionDispatchStatus.Dispatched ? "nano-1" : null,
        failure);

    private static ObservedScene Scene(
        string id,
        long sequence,
        StateIdentityStatus identity) => new(
        ContractSchemaVersions.Revision03,
        $"scene-{id}",
        id,
        new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            "window:game",
            CaptureBackend.WindowsGraphicsCapture,
            sequence,
            sequence * 100,
            DateTimeOffset.UnixEpoch,
            3,
            10,
            250),
        CaptureAvailability.Available,
        identity,
        identity == StateIdentityStatus.Known ? "state-1" : $"hypothesis:{id}",
        identity == StateIdentityStatus.Known
            ? [new StateCandidate(
                ContractSchemaVersions.Revision03,
                "state-1",
                0.9,
                [new EvidenceRegion(
                    ContractSchemaVersions.Revision03,
                    "rect",
                    [0, 0, 1, 1],
                    "state")])]
            : [],
        [new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            $"candidate-{id}",
            id,
            sequence,
            3,
            "window:game",
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "foundry-local-text-region",
                [0.1, 0.2, 0.1, 0.1],
                $"locator-{id}"),
            [new EvidenceRegion(
                ContractSchemaVersions.Revision03,
                "rect",
                [0.1, 0.2, 0.1, 0.1],
                "foundry-local")],
            0.5,
            [GameInteractionOperations.Click],
            "text",
            "部隊")],
        "foundry-local-controls");

    private sealed class Recorder : IExplorationOutcomeRecorder
    {
        public ExplorationOutcomeReport? Report { get; private set; }

        public TransitionEvidence RecordOutcome(ExplorationOutcomeReport report)
        {
            Report = report;
            return new TransitionEvidence(
                ContractSchemaVersions.Revision03,
                report.TransitionEvidenceId,
                "before-1",
                report.AfterScene.ObservationId,
                "attempt-1",
                "candidate-1",
                GameInteractionOperations.Click,
                report.Outcome,
                "nikke:test",
                report.DispatchMonotonicMilliseconds,
                report.ObservationCompletedMonotonicMilliseconds,
                report.RecordedUtc,
                "run-1",
                report.DispatchReceipt,
                report.Comparison,
                report.ObservationSequenceIds);
        }
    }
}
