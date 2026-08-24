using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Fakes;
using Xunit;

namespace OpenLogicool.Conformance.Tests;

public sealed class DiscoveryContractTests
{
    [Fact]
    public void Available_novel_is_representable_without_pretending_capture_failed()
    {
        var observation = FakeObservations.Novel("observation-novel");

        ContractConformanceSuite.Verify(observation);
        Assert.Equal(CaptureAvailability.Available, observation.CaptureAvailability);
        Assert.Equal(StateIdentityStatus.Novel, observation.StateIdentity);
        Assert.Null(observation.CaptureFailureReason);
        Assert.Empty(observation.StateCandidates);
    }

    [Fact]
    public void Affordance_is_bound_to_observation_frame_transform_window_and_primitive()
    {
        var affordance = Affordance();

        Assert.Equal("observation-novel", affordance.ObservationId);
        Assert.Equal(7, affordance.FrameSequence);
        Assert.Equal(3, affordance.TransformRevision);
        Assert.Equal("window:game", affordance.TargetWindowSourceId);
        Assert.Equal(["click", "back"], affordance.AllowedPrimitives);
        Assert.InRange(affordance.Confidence, 0, 1);
    }

    [Fact]
    public void Exploration_contract_is_separate_from_task_planner_contract()
    {
        var proposal = new ExplorationProposal(
            ContractSchemaVersions.Revision03,
            "proposal-1",
            "observation-novel",
            "structure-0",
            "affordance-1",
            "click",
            "このtargetで遷移候補を観測する",
            [ExplorationOutcomeKind.Destination, ExplorationOutcomeKind.Novel, ExplorationOutcomeKind.NoChange],
            new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 3, 300, 5_000),
            ["capture-unavailable", "budget-exhausted"]);

        Assert.Equal("structure-0", proposal.SourceStructureRevisionId);
        Assert.DoesNotContain(
            typeof(ExplorationProposal).GetProperties(),
            property => property.Name.Contains("SemanticAction", StringComparison.Ordinal)
                || property.Name.Contains("DestinationState", StringComparison.Ordinal));
    }

    [Fact]
    public void Observed_scene_policy_context_revision_and_fact_form_one_immutable_contract_graph()
    {
        var frame = new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            "window:game",
            CaptureBackend.WindowsGraphicsCapture,
            7,
            700,
            DateTimeOffset.UnixEpoch,
            3,
            10,
            250);
        var scene = new ObservedScene(
            ContractSchemaVersions.Revision03,
            "scene-1",
            "observation-novel",
            frame,
            CaptureAvailability.Available,
            StateIdentityStatus.Novel,
            "hypothesis-1",
            [],
            [Affordance()],
            "perception-1");
        var budget = new ExplorationBudget(ContractSchemaVersions.Revision03, 4, 2_000, 30_000);
        var policy = new ExplorationPolicy(
            ContractSchemaVersions.Revision03,
            "policy-1",
            "app:game",
            "window:game",
            "env-1",
            "lobby-safe-slice",
            ["click", "back"],
            ["purchase", "resource-consumption"],
            budget,
            true,
            "consent-1",
            "back",
            new ExplorationStopPolicy(ContractSchemaVersions.Revision03, 500, 2, 2, 2),
            ["budget-exhausted", "recovery-lost"]);
        var context = new ExplorationContext(
            ContractSchemaVersions.Revision03,
            "context-1",
            policy,
            scene,
            "structure-0",
            ["affordance-1"],
            [],
            budget);
        var fact = new GameStateFact(
            ContractSchemaVersions.Revision03,
            "fact-1",
            "resource-count",
            "unknown",
            "ocr-1",
            ["observation-novel"],
            0.6,
            "env-1",
            "daily",
            StructureVerificationState.Candidate);
        var revision = new GameStructureRevision(
            ContractSchemaVersions.Revision03,
            "structure-1",
            "structure-0",
            1,
            new StructureScreenGraph(
                ContractSchemaVersions.Revision03,
                "graph-1",
                [],
                [],
                [],
                "env-1"),
            [fact],
            [],
            "env-1",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(StateIdentityStatus.Novel, context.CurrentScene.StateIdentity);
        Assert.True(context.Policy.OneStepApprovalRequired);
        Assert.Equal("structure-0", revision.ParentRevisionId);
        Assert.Equal(StructureVerificationState.Candidate, Assert.Single(revision.StateFacts).VerificationState);
    }

    [Fact]
    public void Structure_delta_and_transition_evidence_keep_proposal_and_observation_layers_separate()
    {
        var delta = new StructureDeltaProposal(
            ContractSchemaVersions.Revision03,
            "delta-1",
            "structure-0",
            ["transition-1"],
            [new StructureDeltaOperation(
                ContractSchemaVersions.Revision03,
                StructureDeltaKind.CreateNode,
                "node-local-1",
                null,
                "仮ラベル",
                null,
                null)]);
        var evidence = new TransitionEvidence(
            ContractSchemaVersions.Revision03,
            "transition-1",
            "observation-before",
            "observation-after",
            "attempt-1",
            "affordance-1",
            "click",
            ExplorationOutcomeKind.Novel,
            "env-1",
            1_000,
            1_450,
            DateTimeOffset.UnixEpoch);

        Assert.Equal("structure-0", delta.SourceStructureRevisionId);
        Assert.Equal("observation-before", evidence.BeforeObservationId);
        Assert.Equal("observation-after", evidence.AfterObservationId);
        Assert.Equal(ExplorationOutcomeKind.Novel, evidence.Outcome);
    }

    private static AffordanceCandidate Affordance() => new(
        ContractSchemaVersions.Revision03,
        "affordance-1",
        "observation-novel",
        7,
        3,
        "window:game",
        new AffordanceLocator(
            ContractSchemaVersions.Revision03,
            "ocr-rect",
            [0.2, 0.3, 0.1, 0.05],
            "locator-1"),
        [new EvidenceRegion(
            ContractSchemaVersions.Revision03,
            "rect",
            [0.2, 0.3, 0.1, 0.05],
            "windows-ocr")],
        0.91,
        ["click", "back"]);
}
