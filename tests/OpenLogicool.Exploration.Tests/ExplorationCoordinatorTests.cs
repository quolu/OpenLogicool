using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Exploration.Tests;

public sealed class ExplorationCoordinatorTests
{
    [Fact]
    public void One_step_approval_is_bound_before_durable_dispatch_and_outcome_evidence()
    {
        var fixture = Fixture(oneStepApproval: true);
        var scene = Scene("observation-before", sequence: 1, hypothesis: "hypothesis-a");
        fixture.Coordinator.CommitObservation(scene, Time(1));
        var admission = Admission(fixture, scene, "proposal-1");

        var decision = fixture.Coordinator.Propose(admission, Time(2));

        Assert.Equal(ExplorationAdmissionStatus.NeedsApproval, decision.Status);
        Assert.Throws<InvalidOperationException>(() =>
            fixture.Coordinator.Dispatch("proposal-1", () => { }, Time(3)));

        var approval = Approval(admission.Proposal, fixture.Policy, "approval-1");
        Assert.True(fixture.Coordinator.Approve(approval, Time(3)).DispatchAllowed);

        var inputCalls = 0;
        fixture.Coordinator.Dispatch("proposal-1", () =>
        {
            inputCalls++;
            Assert.Equal(StructureEventKind.DispatchArmed, fixture.StructureStore.Events[^1].Kind);
            Assert.Equal(RunEventPayloadTypes.Dispatch, fixture.RunStore.Events[^1].PayloadType);
        }, Time(4));
        var after = Scene("observation-after", sequence: 2, hypothesis: "hypothesis-b");
        var dispatchReceipt = new GameInteractionDispatchReceipt(
            ContractSchemaVersions.Revision03,
            GameInteractionOperations.Click,
            GameInteractionDispatchStatus.Dispatched,
            scene.ObservationId,
            scene.Frame.SourceId,
            "NanoSerialHid",
            1,
            Time(4),
            Time(4),
            scene.Affordances[0].CandidateId,
            "nano-1",
            null);
        var comparison = new GameTransitionComparison(
            ContractSchemaVersions.Revision03,
            scene.ObservationId,
            after.ObservationId,
            GameTransitionJudgement.Moved,
            after.Affordances.SelectMany(candidate => candidate.EvidenceRegions).ToArray(),
            ["actionable structure changed"]);
        var evidence = fixture.Coordinator.RecordOutcome(
            Outcome("proposal-1", after, ExplorationOutcomeKind.Novel) with
            {
                DispatchReceipt = dispatchReceipt,
                Comparison = comparison,
                ObservationSequenceIds = [after.ObservationId],
            });

        Assert.Equal(1, inputCalls);
        Assert.Equal("observation-before", evidence.BeforeObservationId);
        Assert.Equal("observation-after", evidence.AfterObservationId);
        Assert.Equal("nano-1", evidence.DispatchReceipt!.TransportReceiptId);
        Assert.Equal(GameTransitionJudgement.Moved, evidence.Comparison!.Judgement);
        Assert.Equal(["observation-after"], evidence.ObservationSequenceIds);
        Assert.Contains(
            "\"TransportReceiptId\":\"nano-1\"",
            fixture.StructureStore.Events[^1].PayloadJson,
            StringComparison.Ordinal);
        Assert.Equal(AttemptState.Confirmed, Recover(fixture).Attempts.Single().State);
        Assert.Equal(
            [
                RunEventPayloadTypes.Observation,
                RunEventPayloadTypes.Proposal,
                RunEventPayloadTypes.Approval,
                RunEventPayloadTypes.Dispatch,
                RunEventPayloadTypes.DispatchResult,
                RunEventPayloadTypes.Observation,
                RunEventPayloadTypes.Confirmation,
            ],
            fixture.RunStore.Events.Select(runEvent => runEvent.PayloadType));
        Assert.Equal(
            ExplorationOutcomeKind.Novel,
            fixture.StructureStore.LoadRevision("game-1", "env-1").Dispatches.Single().Outcome);
    }

    [Fact]
    public void Coordinator_synchronizes_an_external_structure_commit_only_between_probes()
    {
        var fixture = Fixture(oneStepApproval: false);
        var scene = Scene("observation-before", 1, "state-a");
        fixture.Coordinator.CommitObservation(scene, Time(1));
        var previous = fixture.Coordinator.CurrentStructureRevisionId;
        _ = fixture.StructureStore.Append(
            Draft("external-observation", StructureEventKind.ObservationRecorded, [scene.ObservationId]),
            previous,
            Time(2));

        var synchronized = fixture.Coordinator.SynchronizeStructureRevision();

        Assert.NotEqual(previous, synchronized);
        Assert.Equal(fixture.StructureStore.LoadRevision("game-1", "env-1").RevisionId, synchronized);

        _ = fixture.Coordinator.Propose(Admission(fixture, scene, "proposal-1"), Time(3));
        Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.SynchronizeStructureRevision());
    }

    [Fact]
    public void Product_structure_learner_creates_candidate_nodes_and_edge_from_transition_evidence()
    {
        var fixture = Fixture(oneStepApproval: true);
        var before = Scene("observation-before", 1, "hypothesis-a");
        var afterBase = Scene("observation-after", 2, "hypothesis-b");
        var after = afterBase with
        {
            Affordances = afterBase.Affordances.Select(candidate => candidate with
            {
                SemanticKind = "text",
                SemanticLabel = "設定",
                Locator = candidate.Locator with { NormalizedBounds = [0.7, 0.7, 0.2, 0.1] },
            }).ToArray(),
        };
        fixture.Coordinator.CommitObservation(before, Time(1));
        var admission = Admission(fixture, before, "proposal-structure");
        _ = fixture.Coordinator.Propose(admission, Time(2));
        _ = fixture.Coordinator.Approve(Approval(admission.Proposal, fixture.Policy, "approval-structure"), Time(3));
        fixture.Coordinator.Dispatch("proposal-structure", () => { }, Time(4));
        var evidence = fixture.Coordinator.RecordOutcome(
            Outcome("proposal-structure", after, ExplorationOutcomeKind.Novel));
        var stableIds = new InMemoryStableStructureIdRegistry();
        var eventIds = new SequenceIds();
        var knowledge = new StructureKnowledgeController(fixture.StructureStore, stableIds, eventIds);
        var learner = new GameInteractionStructureLearner(
            fixture.StructureStore,
            knowledge,
            stableIds,
            eventIds,
            fixture.Coordinator,
            "game-1",
            "env-1");

        var revision = learner.Commit(
            before,
            after,
            evidence,
            admission.Proposal.WaitCondition,
            ["unknown-side-effect"],
            false,
            Time(6));

        Assert.Equal(2, revision.ScreenGraph.Nodes.Count);
        var edge = Assert.Single(revision.ScreenGraph.Edges);
        Assert.NotEqual(edge.SourceStateId, edge.DestinationStateId);
        Assert.Equal(ExplorationOutcomeKind.Novel, Assert.Single(edge.OutcomeCounts).Outcome);
        Assert.Equal("unknown-side-effect", Assert.Single(edge.RiskTags));
        Assert.False(edge.Reversible);
        Assert.False(string.IsNullOrWhiteSpace(edge.TargetSemanticKey));
        Assert.Contains(evidence.EvidenceId, edge.EvidenceIds);
    }

    [Fact]
    public void Stale_frame_scope_violation_and_prohibited_risk_never_create_an_attempt()
    {
        AssertRejected(
            (fixture, scene) => Admission(fixture, scene, "proposal-stale") with
            {
                Context = Context(fixture, scene with { Frame = scene.Frame with { FreshnessMs = 999 } }),
            },
            ExplorationStopReason.StaleFrame);
        AssertRejected(
            (fixture, scene) => Admission(fixture, scene, "proposal-scope") with { WithinExplorationScope = false },
            ExplorationStopReason.ScopeViolation);
        AssertRejected(
            (fixture, scene) => Admission(fixture, scene, "proposal-risk") with
            {
                Risk = Risk("affordance-1") with { Level = ExplorationRiskLevel.Prohibited },
            },
            ExplorationStopReason.RiskProhibited);
    }

    [Fact]
    public void Rejected_proposal_does_not_occupy_the_active_probe_slot()
    {
        var fixture = Fixture(oneStepApproval: false);
        var scene = Scene("observation-before", 1, "state-a");
        fixture.Coordinator.CommitObservation(scene, Time(1));
        var rejected = Admission(fixture, scene, "proposal-rejected") with
        {
            Risk = Risk("affordance-1") with { Level = ExplorationRiskLevel.Prohibited },
        };

        Assert.Equal(
            ExplorationAdmissionStatus.Rejected,
            fixture.Coordinator.Propose(rejected, Time(2)).Status);
        Assert.False(fixture.Coordinator.HasActiveProbe);

        var allowed = fixture.Coordinator.Propose(Admission(fixture, scene, "proposal-allowed"), Time(3));
        Assert.Equal(ExplorationAdmissionStatus.Allowed, allowed.Status);
        Assert.True(fixture.Coordinator.HasActiveProbe);
    }

    [Fact]
    public void Stopped_condition_is_persisted_for_the_rest_of_the_run()
    {
        var fixture = Fixture(oneStepApproval: false);
        var scene = Scene("observation-before", 1, "state-a");
        fixture.Coordinator.CommitObservation(scene, Time(1));
        var stale = Admission(fixture, scene, "proposal-stale") with
        {
            Context = Context(fixture, scene with { Frame = scene.Frame with { FreshnessMs = 999 } }),
        };

        Assert.Equal(ExplorationAdmissionStatus.Stopped, fixture.Coordinator.Propose(stale, Time(2)).Status);
        Assert.Equal(ExplorationStopReason.StaleFrame, fixture.Coordinator.StopReason);
        Assert.False(fixture.Coordinator.HasActiveProbe);

        var later = fixture.Coordinator.Propose(Admission(fixture, scene, "proposal-later"), Time(3));
        Assert.Equal(ExplorationAdmissionStatus.Stopped, later.Status);
        Assert.Equal(ExplorationStopReason.StaleFrame, later.Reason);
        Assert.Empty(fixture.AttemptGate.Attempts);
    }

    [Fact]
    public void Target_must_belong_to_the_current_observation_and_window_bounds()
    {
        var wrongObservation = Scene("observation-before", 1, "state-a");
        wrongObservation = wrongObservation with
        {
            Affordances =
            [
                wrongObservation.Affordances[0] with { ObservationId = "observation-other" },
            ],
        };
        AssertTargetRejected(wrongObservation, "proposal-wrong-observation");

        var outsideWindow = Scene("observation-before", 1, "state-a");
        outsideWindow = outsideWindow with
        {
            Affordances =
            [
                outsideWindow.Affordances[0] with
                {
                    Locator = outsideWindow.Affordances[0].Locator with
                    {
                        NormalizedBounds = [0.9, 0.9, 0.2, 0.2],
                    },
                },
            ],
        };
        AssertTargetRejected(outsideWindow, "proposal-outside-window");
    }

    [Fact]
    public void Supplied_context_cannot_replace_the_committed_candidate_locator()
    {
        var fixture = Fixture(oneStepApproval: false);
        var committed = Scene("observation-before", 1, "state-a");
        fixture.Coordinator.CommitObservation(committed, Time(1));
        var replaced = committed with
        {
            Affordances =
            [
                committed.Affordances[0] with
                {
                    Locator = committed.Affordances[0].Locator with
                    {
                        NormalizedBounds = [0.6, 0.6, 0.2, 0.2],
                    },
                },
            ],
        };
        var admission = Admission(fixture, committed, "proposal-replaced-locator") with
        {
            Context = Context(fixture, replaced),
        };

        var decision = fixture.Coordinator.Propose(admission, Time(2));

        Assert.Equal(ExplorationAdmissionStatus.Rejected, decision.Status);
        Assert.Equal(ExplorationStopReason.TargetNotCurrent, decision.Reason);
        Assert.Empty(fixture.AttemptGate.Attempts);
    }

    [Fact]
    public void Lost_recovery_edge_stops_before_attempt_creation()
    {
        var fixture = Fixture(oneStepApproval: false);
        var scene = Scene("observation-before", 1, "state-a");
        fixture.Coordinator.CommitObservation(scene, Time(1));
        var admission = Admission(fixture, scene, "proposal-recovery-lost") with
        {
            Risk = Risk("affordance-1") with { RecoveryEdgeIds = ["edge-missing"] },
        };

        var decision = fixture.Coordinator.Propose(admission, Time(2));

        Assert.Equal(ExplorationAdmissionStatus.Stopped, decision.Status);
        Assert.Equal(ExplorationStopReason.RecoveryLost, decision.Reason);
        Assert.Equal(ExplorationStopReason.RecoveryLost, fixture.Coordinator.StopReason);
        Assert.Empty(fixture.AttemptGate.Attempts);
    }

    [Fact]
    public void Capture_loss_stops_before_attempt_creation()
    {
        var fixture = Fixture(oneStepApproval: false);
        var scene = Scene("observation-before", 1, "state-a") with
        {
            CaptureAvailability = CaptureAvailability.Unavailable,
        };
        fixture.Coordinator.CommitObservation(scene, Time(1));

        var decision = fixture.Coordinator.Propose(Admission(fixture, scene, "proposal-capture-lost"), Time(2));

        Assert.Equal(ExplorationAdmissionStatus.Stopped, decision.Status);
        Assert.Equal(ExplorationStopReason.CaptureUnavailable, decision.Reason);
        Assert.Empty(fixture.AttemptGate.Attempts);
    }

    [Fact]
    public void Elapsed_budget_exhaustion_stops_before_attempt_creation()
    {
        var fixture = Fixture(oneStepApproval: false);
        var scene = Scene("observation-before", 1, "state-a");
        fixture.Coordinator.CommitObservation(scene, Time(1));
        var admission = Admission(fixture, scene, "proposal-budget") with
        {
            ElapsedMilliseconds = fixture.Policy.Budget.RemainingElapsedMilliseconds + 1,
        };

        var decision = fixture.Coordinator.Propose(admission, Time(2));

        Assert.Equal(ExplorationAdmissionStatus.Stopped, decision.Status);
        Assert.Equal(ExplorationStopReason.BudgetExhausted, decision.Reason);
        Assert.Empty(fixture.AttemptGate.Attempts);
    }

    [Fact]
    public void Mismatched_approval_cannot_authorize_or_dispatch()
    {
        var fixture = Fixture(oneStepApproval: true);
        var scene = Scene("observation-before", 1, "hypothesis-a");
        fixture.Coordinator.CommitObservation(scene, Time(1));
        var admission = Admission(fixture, scene, "proposal-1");
        _ = fixture.Coordinator.Propose(admission, Time(2));

        var decision = fixture.Coordinator.Approve(
            Approval(admission.Proposal, fixture.Policy, "approval-1") with { ObservationId = "other-observation" },
            Time(3));

        Assert.Equal(ExplorationAdmissionStatus.Rejected, decision.Status);
        Assert.Equal(ExplorationStopReason.ApprovalMismatch, decision.Reason);
        Assert.Empty(fixture.AttemptGate.Attempts);
    }

    [Fact]
    public void Repeated_no_change_stops_before_a_third_probe()
    {
        var fixture = Fixture(oneStepApproval: false);
        var scene = Scene("observation-1", 1, "state-a");
        fixture.Coordinator.CommitObservation(scene, Time(1));

        scene = RunNoChange(fixture, scene, "proposal-1", 2);
        scene = RunNoChange(fixture, scene, "proposal-2", 3);

        Assert.Contains(
            fixture.Coordinator.StopReason,
            new[] { ExplorationStopReason.RepeatedProbe, ExplorationStopReason.NoProgress });
        var stopped = fixture.Coordinator.Propose(Admission(fixture, scene, "proposal-3"), Time(10));
        Assert.Equal(ExplorationAdmissionStatus.Stopped, stopped.Status);
        Assert.False(stopped.DispatchAllowed);
    }

    [Fact]
    public void Repeated_probe_limit_one_allows_the_first_probe_and_stops_only_after_repetition()
    {
        var fixture = Fixture(
            oneStepApproval: false,
            new ExplorationStopPolicy(ContractSchemaVersions.Revision03, 500, 1, 3, 3));
        var before = Scene("observation-before", 1, "state-a");
        fixture.Coordinator.CommitObservation(before, Time(1));
        var first = Admission(fixture, before, "proposal-1");
        Assert.True(fixture.Coordinator.Propose(first, Time(2)).DispatchAllowed);
        fixture.Coordinator.Dispatch("proposal-1", () => { }, Time(3));
        var after = Scene("observation-after", 2, "state-b");
        _ = fixture.Coordinator.RecordOutcome(
            Outcome("proposal-1", after, ExplorationOutcomeKind.Novel));

        Assert.Equal(ExplorationStopReason.None, fixture.Coordinator.StopReason);
        Assert.True(fixture.Coordinator.Propose(
            Admission(fixture, after, "proposal-2"),
            Time(4)).DispatchAllowed);
    }

    [Fact]
    public void Insufficient_stability_stops_without_false_confirmation()
    {
        var fixture = Fixture(oneStepApproval: false);
        var scene = Scene("observation-before", 1, "state-a");
        fixture.Coordinator.CommitObservation(scene, Time(1));
        _ = fixture.Coordinator.Propose(Admission(fixture, scene, "proposal-1"), Time(2));
        fixture.Coordinator.Dispatch("proposal-1", () => { }, Time(3));
        var report = Outcome("proposal-1", Scene("observation-after", 2, "state-b"), ExplorationOutcomeKind.Novel) with
        {
            StableFramesObserved = 1,
        };

        Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.RecordOutcome(report));
        Assert.Equal(ExplorationStopReason.StabilityInsufficient, fixture.Coordinator.StopReason);
        Assert.DoesNotContain(
            fixture.RunStore.Events,
            runEvent => runEvent.PayloadType == RunEventPayloadTypes.Confirmation);
    }

    [Fact]
    public void External_input_fault_is_not_retried_and_recovers_as_outcome_unknown()
    {
        var fixture = Fixture(oneStepApproval: false);
        var scene = Scene("observation-before", 1, "state-a");
        fixture.Coordinator.CommitObservation(scene, Time(1));
        _ = fixture.Coordinator.Propose(Admission(fixture, scene, "proposal-1"), Time(2));
        var calls = 0;

        Assert.Throws<InvalidOperationException>(() =>
            fixture.Coordinator.Dispatch("proposal-1", () =>
            {
                calls++;
                throw new InvalidOperationException("input fault");
            }, Time(3)));

        Assert.Equal(1, calls);
        Assert.Equal(AttemptState.OutcomeUnknown, Recover(fixture).Attempts.Single().State);
        Assert.Single(fixture.RunStore.Events, runEvent => runEvent.PayloadType == RunEventPayloadTypes.Dispatch);
    }

    [Fact]
    public void Structure_controller_accepts_only_existing_evidence_and_controller_issued_candidate_ids()
    {
        var store = new MemoryStructureStore();
        var ids = new SequenceIds();
        var registry = new InMemoryStableStructureIdRegistry();
        _ = store.Append(
            Draft("observation-event", StructureEventKind.ObservationRecorded, ["transition-1"]),
            null,
            Time(1));
        var current = store.LoadRevision("game-1", "env-1");
        var stateId = registry.Issue(StructureEntityKind.Node);
        var operation = new StructureDeltaOperation(
            ContractSchemaVersions.Revision03,
            StructureDeltaKind.CreateNode,
            "candidate-a",
            null,
            "仮画面",
            null,
            null);
        var mutation = new StructureMutation(
            ContractSchemaVersions.Revision03,
            StructureMutationKind.UpsertNode,
            StructureEntityKind.Node,
            stateId,
            [],
            Node(stateId, "env-1", StructureVerificationState.Candidate),
            null,
            null,
            null,
            null,
            null,
            ["transition-1"],
            "frame evidenceからcandidateを作成");
        var request = DeltaRequest(current.RevisionId, operation, mutation, stateId, ["transition-1"]);
        var controller = new StructureKnowledgeController(store, registry, ids);

        var revision = controller.Commit(request, "game-1", "env-1");

        Assert.Equal(stateId, Assert.Single(revision.ScreenGraph.Nodes).StateId);
        Assert.Equal(StructureVerificationState.Candidate, revision.ScreenGraph.Nodes[0].VerificationState);
        Assert.Equal(
            [StructureEventKind.ObservationRecorded, StructureEventKind.DeltaAccepted, StructureEventKind.MutationApplied],
            store.Events.Select(structureEvent => structureEvent.Kind));
    }

    [Fact]
    public void Structure_controller_rejects_unknown_evidence_and_ai_verification_promotion()
    {
        var store = new MemoryStructureStore();
        var registry = new InMemoryStableStructureIdRegistry();
        var ids = new SequenceIds();
        _ = store.Append(Draft("observation-event", StructureEventKind.ObservationRecorded, ["known-evidence"]), null, Time(1));
        var current = store.LoadRevision("game-1", "env-1");
        var stateId = registry.Issue(StructureEntityKind.Node);
        var operation = new StructureDeltaOperation(
            ContractSchemaVersions.Revision03,
            StructureDeltaKind.CreateNode,
            "candidate-a",
            null,
            null,
            null,
            null);
        var verified = new StructureMutation(
            ContractSchemaVersions.Revision03,
            StructureMutationKind.UpsertNode,
            StructureEntityKind.Node,
            stateId,
            [],
            Node(stateId, "env-1", StructureVerificationState.Verified),
            null,
            null,
            null,
            null,
            null,
            ["known-evidence"],
            "AIが昇格を要求");
        var controller = new StructureKnowledgeController(store, registry, ids);

        Assert.Throws<InvalidOperationException>(() =>
            controller.Commit(
                DeltaRequest(current.RevisionId, operation, verified, stateId, ["missing-evidence"]),
                "game-1",
                "env-1"));
        Assert.Throws<InvalidOperationException>(() =>
            controller.Commit(
                DeltaRequest(current.RevisionId, operation, verified, stateId, ["known-evidence"]),
                "game-1",
                "env-1"));
        Assert.Single(store.Events);
    }

    private static void AssertRejected(
        Func<FixtureState, ObservedScene, ExplorationProposalAdmission> change,
        ExplorationStopReason expected)
    {
        var fixture = Fixture(oneStepApproval: true);
        var scene = Scene("observation-before", 1, "state-a");
        fixture.Coordinator.CommitObservation(scene, Time(1));
        var decision = fixture.Coordinator.Propose(change(fixture, scene), Time(2));
        Assert.Equal(expected == ExplorationStopReason.StaleFrame
            ? ExplorationAdmissionStatus.Stopped
            : ExplorationAdmissionStatus.Rejected, decision.Status);
        Assert.Equal(expected, decision.Reason);
        Assert.Empty(fixture.AttemptGate.Attempts);
    }

    private static void AssertTargetRejected(ObservedScene scene, string proposalId)
    {
        var fixture = Fixture(oneStepApproval: false);
        fixture.Coordinator.CommitObservation(scene, Time(1));

        var decision = fixture.Coordinator.Propose(Admission(fixture, scene, proposalId), Time(2));

        Assert.Equal(ExplorationAdmissionStatus.Rejected, decision.Status);
        Assert.Equal(ExplorationStopReason.TargetNotCurrent, decision.Reason);
        Assert.False(fixture.Coordinator.HasActiveProbe);
        Assert.Empty(fixture.AttemptGate.Attempts);
    }

    private static ObservedScene RunNoChange(
        FixtureState fixture,
        ObservedScene before,
        string proposalId,
        long nextSequence)
    {
        var admission = Admission(fixture, before, proposalId);
        Assert.True(fixture.Coordinator.Propose(admission, Time(nextSequence * 2)).DispatchAllowed);
        fixture.Coordinator.Dispatch(proposalId, () => { }, Time(nextSequence * 2 + 1));
        var after = Scene($"observation-{nextSequence}", nextSequence, before.StateHypothesisId!);
        _ = fixture.Coordinator.RecordOutcome(Outcome(proposalId, after, ExplorationOutcomeKind.NoChange));
        return after;
    }

    private static FixtureState Fixture(
        bool oneStepApproval,
        ExplorationStopPolicy? stopPolicy = null)
    {
        var structureStore = new MemoryStructureStore();
        var runStore = new MemoryRunStore();
        var journal = new RunJournal(runStore, new NoopLog());
        var gate = new AttemptDispatchGate(journal);
        var policy = Policy(oneStepApproval, stopPolicy);
        var coordinator = new ExplorationCoordinator(
            structureStore,
            journal,
            gate,
            new ExplorationRunBinding(
                ContractSchemaVersions.Revision03,
                "explore-run-1",
                "game-1",
                "env-1",
                "exploration",
                "exploration-v1",
                1),
            policy,
            new SequenceIds());
        return new FixtureState(coordinator, structureStore, runStore, gate, policy, journal);
    }

    private static ExplorationPolicy Policy(
        bool oneStepApproval,
        ExplorationStopPolicy? stopPolicy = null) => new(
        ContractSchemaVersions.Revision03,
        "policy-1",
        "app:game",
        "window:game",
        "env-1",
        "safe-slice",
        ["click", "back"],
        ["purchase", "delete", "account-change"],
        new ExplorationBudget(ContractSchemaVersions.Revision03, 4, 5_000, 60_000),
        oneStepApproval,
        "consent-1",
        "back",
        stopPolicy ?? new ExplorationStopPolicy(ContractSchemaVersions.Revision03, 500, 2, 2, 2),
        ["budget-exhausted", "no-progress", "recovery-lost"]);

    private static ExplorationProposalAdmission Admission(
        FixtureState fixture,
        ObservedScene scene,
        string proposalId)
    {
        var context = Context(fixture, scene);
        return new ExplorationProposalAdmission(
            context,
            new ExplorationProposal(
                ContractSchemaVersions.Revision03,
                proposalId,
                scene.ObservationId,
                fixture.Coordinator.CurrentStructureRevisionId,
                "affordance-1",
                "click",
                "画面変化を観測する",
                Enum.GetValues<ExplorationOutcomeKind>(),
                new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 3, 300, 5_000),
                ["capture-unavailable", "budget-exhausted"]),
            Risk("affordance-1"),
            true,
            true,
            1_000,
            100);
    }

    private static ExplorationContext Context(FixtureState fixture, ObservedScene scene) => new(
        ContractSchemaVersions.Revision03,
        "context-1",
        fixture.Policy,
        scene,
        fixture.Coordinator.CurrentStructureRevisionId,
        ["affordance-1"],
        ["edge-back"],
        new ExplorationBudget(
            ContractSchemaVersions.Revision03,
            fixture.Coordinator.RemainingProbes,
            5_000,
            60_000));

    private static ExplorationRiskAssessment Risk(string candidateId) => new(
        ContractSchemaVersions.Revision03,
        candidateId,
        ExplorationRiskLevel.Low,
        [],
        true,
        true,
        ["edge-back"],
        "risk-policy-1");

    private static ExplorationApproval Approval(
        ExplorationProposal proposal,
        ExplorationPolicy policy,
        string approvalId) => new(
        ContractSchemaVersions.Revision03,
        approvalId,
        proposal.ProposalId,
        proposal.SourceObservationId,
        policy.PolicyRevisionId,
        proposal.SourceStructureRevisionId,
        "user-1",
        Time(3));

    private static ExplorationOutcomeReport Outcome(
        string proposalId,
        ObservedScene after,
        ExplorationOutcomeKind outcome) => new(
        ContractSchemaVersions.Revision03,
        proposalId,
        after,
        outcome,
        3,
        300,
        $"transition-{proposalId}",
        1_000,
        1_500,
        Time(5));

    private static ObservedScene Scene(string observationId, long sequence, string hypothesis)
    {
        var frame = new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            "window:game",
            CaptureBackend.WindowsGraphicsCapture,
            sequence,
            sequence * 100,
            Time((int)sequence),
            1,
            10,
            300);
        return new ObservedScene(
            ContractSchemaVersions.Revision03,
            $"scene-{sequence}",
            observationId,
            frame,
            CaptureAvailability.Available,
            StateIdentityStatus.Novel,
            hypothesis,
            [],
            [new AffordanceCandidate(
                ContractSchemaVersions.Revision03,
                "affordance-1",
                observationId,
                sequence,
                1,
                "window:game",
                new AffordanceLocator(ContractSchemaVersions.Revision03, "ocr", [0.1, 0.1, 0.2, 0.1], "locator-1"),
                [new EvidenceRegion(ContractSchemaVersions.Revision03, "rect", [0.1, 0.1, 0.2, 0.1], "ocr-1")],
                0.9,
                ["click"])],
            "perception-1");
    }

    private static StructureScreenNode Node(
        string stateId,
        string environmentScope,
        StructureVerificationState verification) => new(
        ContractSchemaVersions.Revision03,
        stateId,
        environmentScope,
        ["signature-1"],
        [],
        ["transition-1"],
        null,
        verification);

    private static StructureDeltaCommitRequest DeltaRequest(
        string revisionId,
        StructureDeltaOperation operation,
        StructureMutation mutation,
        string stableId,
        IReadOnlyList<string> evidenceIds) => new(
        ContractSchemaVersions.Revision03,
        new StructureDeltaProposal(
            ContractSchemaVersions.Revision03,
            "delta-1",
            revisionId,
            evidenceIds,
            [operation]),
        new Dictionary<string, string>(StringComparer.Ordinal) { ["candidate-a"] = stableId },
        [new MaterializedStructureDeltaOperation(operation, mutation)],
        "correlation-1",
        "transition-1",
        Time(2),
        Time(2));

    private static StructureEventDraft Draft(
        string eventId,
        StructureEventKind kind,
        IReadOnlyList<string> evidenceIds) => new(
        ContractSchemaVersions.Revision03,
        eventId,
        "game-1",
        "env-1",
        kind,
        StructureEventActor.Controller,
        "correlation-1",
        "root",
        "observation-1",
        null,
        null,
        evidenceIds,
        StructureEventPayloadTypes.Observation,
        "{}",
        null,
        Time(1));

    private static AttemptDispatchGate Recover(FixtureState fixture)
    {
        var journal = RunJournal.Restore(fixture.RunStore, new NoopLog());
        return AttemptDispatchGate.Recover(fixture.RunStore, journal);
    }

    private static DateTimeOffset Time(long seconds) => DateTimeOffset.UnixEpoch.AddSeconds(seconds);

    private sealed record FixtureState(
        ExplorationCoordinator Coordinator,
        MemoryStructureStore StructureStore,
        MemoryRunStore RunStore,
        AttemptDispatchGate AttemptGate,
        ExplorationPolicy Policy,
        RunJournal Journal);

    private sealed class SequenceIds : IExplorationIdSource
    {
        private int value;
        public string Next(string prefix) => $"{prefix}:{++value}";
    }

    private sealed class MemoryStructureStore : IGameStructureStore
    {
        public List<StructureEvent> Events { get; } = [];

        public StructureEvent Append(StructureEventDraft draft, string? expectedParentRevisionId, DateTimeOffset persistedUtc)
        {
            var scoped = Events.Where(item => item.GameId == draft.GameId && item.EnvironmentScope == draft.EnvironmentScope).ToArray();
            var parent = scoped.LastOrDefault()?.ResultingStructureRevisionId;
            if (parent != expectedParentRevisionId)
            {
                throw new InvalidOperationException("revision conflict");
            }
            var sequence = scoped.LongLength + 1;
            var item = new StructureEvent(
                draft.SchemaVersion,
                draft.EventId,
                draft.GameId,
                draft.EnvironmentScope,
                sequence,
                parent,
                StructureRevisionIds.Next(parent, draft.EventId, sequence),
                draft.Kind,
                draft.Actor,
                draft.CorrelationId,
                draft.CausationId,
                draft.ObservationId,
                draft.ProposalId,
                draft.AttemptId,
                draft.EvidenceIds,
                draft.PayloadType,
                draft.PayloadJson,
                draft.Outcome,
                draft.OccurredUtc,
                persistedUtc);
            Events.Add(item);
            return item;
        }

        public IReadOnlyList<StructureEvent> ReadEvents(string gameId, string environmentScope) =>
            Events.Where(item => item.GameId == gameId && item.EnvironmentScope == environmentScope).ToArray();

        public IReadOnlyList<string> ListGameIds() => Events.Select(item => item.GameId).Distinct().ToArray();

        public GameStructureRevision LoadRevision(string gameId, string environmentScope) =>
            GameStructureProjector.Replay(gameId, environmentScope, ReadEvents(gameId, environmentScope));

        public StructureKnowledgePackExport Export(string gameId, string environmentScope, DateTimeOffset createdUtc) =>
            new(
                ContractSchemaVersions.Revision03,
                "knowledge:test",
                gameId,
                environmentScope,
                LoadRevision(gameId, environmentScope),
                ReadEvents(gameId, environmentScope),
                createdUtc);
    }

    private sealed class MemoryRunStore : IRunJournalStore
    {
        public List<RunEvent> Events { get; } = [];
        public void Append(RunEvent runEvent) => Events.Add(runEvent);
        public IReadOnlyList<RunEvent> ReadRun(string runId) => Events.Where(item => item.RunId == runId).OrderBy(item => item.RunSequence).ToArray();
        public IReadOnlyList<string> ListRunIds() => Events.Select(item => item.RunId).Distinct().ToArray();
        public IReadOnlyList<ExpiredRunPreview> PreviewExpiredRuns(DateTimeOffset asOfUtc, int retentionDays) => [];
        public void DeleteRun(string runId) => Events.RemoveAll(item => item.RunId == runId);
    }

    private sealed class NoopLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry) { }
    }
}
