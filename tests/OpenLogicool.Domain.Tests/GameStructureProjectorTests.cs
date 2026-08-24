using System.Text.Json;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Domain.Tests;

public sealed class GameStructureProjectorTests
{
    [Fact]
    public void Replay_builds_graph_fact_and_marks_unresolved_dispatch_as_outcome_unknown()
    {
        var mutation = Event(
            1,
            null,
            StructureEventKind.MutationApplied,
            payload: Batch(
                UpsertNode(Node("state-a")),
                UpsertNode(Node("state-b")),
                UpsertEdge(Edge("edge-a-b", "state-a", "state-b")),
                UpsertFact(Fact("fact-daily"))));
        var armed = Event(
            2,
            mutation.ResultingStructureRevisionId,
            StructureEventKind.DispatchArmed,
            attemptId: "attempt-1");

        var revision = GameStructureProjector.Replay("game-1", "env-1", [mutation, armed]);

        Assert.Equal(2, revision.ThroughEvidenceSequence);
        Assert.Equal(["state-a", "state-b"], revision.ScreenGraph.Nodes.Select(node => node.StateId));
        Assert.Equal("state-b", Assert.Single(revision.ScreenGraph.Edges).DestinationStateId);
        Assert.Equal("fact-daily", Assert.Single(revision.StateFacts).FactId);
        Assert.Equal(ExplorationOutcomeKind.OutcomeUnknown, Assert.Single(revision.Dispatches).Outcome);
    }

    [Fact]
    public void Outcome_event_resolves_the_dispatch_without_rewriting_the_armed_event()
    {
        var armed = Event(1, null, StructureEventKind.DispatchArmed, attemptId: "attempt-1");
        var outcome = Event(
            2,
            armed.ResultingStructureRevisionId,
            StructureEventKind.OutcomeRecorded,
            attemptId: "attempt-1",
            outcome: ExplorationOutcomeKind.Destination,
            evidenceIds: ["transition-1"]);

        var revision = GameStructureProjector.Replay("game-1", "env-1", [armed, outcome]);

        var dispatch = Assert.Single(revision.Dispatches);
        Assert.Equal(ExplorationOutcomeKind.Destination, dispatch.Outcome);
        Assert.Equal("transition-1", dispatch.EvidenceId);
        Assert.Equal(StructureEventKind.DispatchArmed, armed.Kind);
    }

    [Fact]
    public void User_correction_cannot_promote_a_new_node_to_verified()
    {
        var correction = Event(
            1,
            null,
            StructureEventKind.CorrectionApplied,
            actor: StructureEventActor.User,
            payload: Batch(UpsertNode(Node("state-a") with
            {
                VerificationState = StructureVerificationState.Verified,
            })));

        var revision = GameStructureProjector.Replay("game-1", "env-1", [correction]);

        Assert.Equal(StructureVerificationState.Candidate, Assert.Single(revision.ScreenGraph.Nodes).VerificationState);
    }

    [Fact]
    public void Merge_retires_old_identity_reattributes_edges_and_preserves_evidence()
    {
        var created = Event(
            1,
            null,
            StructureEventKind.MutationApplied,
            payload: Batch(
                UpsertNode(Node("state-target", ["e-target"])),
                UpsertNode(Node("state-old", ["e-old"])),
                UpsertNode(Node("state-destination")),
                UpsertEdge(Edge("edge-old", "state-old", "state-destination"))));
        var merged = Event(
            2,
            created.ResultingStructureRevisionId,
            StructureEventKind.CorrectionApplied,
            actor: StructureEventActor.User,
            payload: Batch(new StructureMutation(
                ContractSchemaVersions.Revision03,
                StructureMutationKind.MergeNodes,
                StructureEntityKind.Node,
                "state-target",
                ["state-old"],
                null,
                null,
                null,
                null,
                null,
                null,
                ["e-merge"],
                "同一画面だったため統合")));

        var revision = GameStructureProjector.Replay("game-1", "env-1", [created, merged]);

        var target = Assert.Single(revision.ScreenGraph.Nodes, node => node.StateId == "state-target");
        var old = Assert.Single(revision.ScreenGraph.Nodes, node => node.StateId == "state-old");
        Assert.Equal(["e-merge", "e-old", "e-target"], target.EvidenceIds);
        Assert.True(old.Retired);
        Assert.Equal(StructureVerificationState.Retired, old.VerificationState);
        Assert.Equal("state-target", Assert.Single(revision.ScreenGraph.Edges).SourceStateId);
    }

    [Fact]
    public void Contradiction_demotes_verified_subject_without_deleting_it()
    {
        var created = Event(
            1,
            null,
            StructureEventKind.MutationApplied,
            payload: Batch(UpsertNode(Node("state-a") with
            {
                VerificationState = StructureVerificationState.Verified,
            })));
        var contradiction = new StructureContradiction(
            ContractSchemaVersions.Revision03,
            "contradiction-1",
            ["state-a"],
            ["observation-2"],
            "独立再観測と一致しない");
        var contradicted = Event(
            2,
            created.ResultingStructureRevisionId,
            StructureEventKind.MutationApplied,
            payload: Batch(new StructureMutation(
                ContractSchemaVersions.Revision03,
                StructureMutationKind.RecordContradiction,
                StructureEntityKind.Node,
                contradiction.ContradictionId,
                [],
                null,
                null,
                null,
                null,
                null,
                contradiction,
                ["observation-2"],
                contradiction.Reason)));

        var revision = GameStructureProjector.Replay("game-1", "env-1", [created, contradicted]);

        Assert.Equal(StructureVerificationState.Candidate, Assert.Single(revision.ScreenGraph.Nodes).VerificationState);
        Assert.Equal("contradiction-1", Assert.Single(revision.ScreenGraph.Contradictions).ContradictionId);
    }

    [Fact]
    public void Split_retires_the_old_identity_and_keeps_it_on_replacement_variants()
    {
        var created = Event(
            1,
            null,
            StructureEventKind.MutationApplied,
            payload: Batch(
                UpsertNode(Node("state-old")),
                UpsertNode(Node("state-left")),
                UpsertNode(Node("state-right"))));
        var split = Event(
            2,
            created.ResultingStructureRevisionId,
            StructureEventKind.CorrectionApplied,
            actor: StructureEventActor.User,
            payload: Batch(new StructureMutation(
                ContractSchemaVersions.Revision03,
                StructureMutationKind.SplitNode,
                StructureEntityKind.Node,
                "state-old",
                ["state-left", "state-right"],
                null,
                null,
                null,
                null,
                null,
                null,
                ["observation-split"],
                "別variantだったため分割")));

        var revision = GameStructureProjector.Replay("game-1", "env-1", [created, split]);

        Assert.True(Assert.Single(revision.ScreenGraph.Nodes, node => node.StateId == "state-old").Retired);
        Assert.All(
            revision.ScreenGraph.Nodes.Where(node => node.StateId is "state-left" or "state-right"),
            node => Assert.Contains("state-old", node.VariantStateIds));
    }

    [Fact]
    public void Replay_rejects_a_broken_revision_chain()
    {
        var structureEvent = Event(1, null, StructureEventKind.ObservationRecorded) with
        {
            ResultingStructureRevisionId = "structure:tampered",
        };

        Assert.Throws<InvalidOperationException>(() =>
            GameStructureProjector.Replay("game-1", "env-1", [structureEvent]));
    }

    [Fact]
    public void Replay_rejects_an_outcome_without_a_matching_armed_attempt()
    {
        var outcome = Event(
            1,
            null,
            StructureEventKind.OutcomeRecorded,
            attemptId: "attempt-missing",
            outcome: ExplorationOutcomeKind.Destination);

        Assert.Throws<InvalidOperationException>(() =>
            GameStructureProjector.Replay("game-1", "env-1", [outcome]));
    }

    private static StructureEvent Event(
        long sequence,
        string? parentRevision,
        StructureEventKind kind,
        StructureEventActor actor = StructureEventActor.Controller,
        string? payload = null,
        string? attemptId = null,
        ExplorationOutcomeKind? outcome = null,
        IReadOnlyList<string>? evidenceIds = null)
    {
        var eventId = $"event-{sequence}";
        return new StructureEvent(
            ContractSchemaVersions.Revision03,
            eventId,
            "game-1",
            "env-1",
            sequence,
            parentRevision,
            StructureRevisionIds.Next(parentRevision, eventId, sequence),
            kind,
            actor,
            "correlation-1",
            sequence == 1 ? "root" : $"event-{sequence - 1}",
            null,
            null,
            attemptId,
            evidenceIds ?? [],
            payload is null ? StructureEventPayloadTypes.None : StructureEventPayloadTypes.MutationBatch,
            payload ?? "{}",
            outcome,
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence));
    }

    private static string Batch(params StructureMutation[] mutations) =>
        JsonSerializer.Serialize(new StructureMutationBatch(ContractSchemaVersions.Revision03, mutations));

    private static StructureMutation UpsertNode(StructureScreenNode node) => new(
        ContractSchemaVersions.Revision03,
        StructureMutationKind.UpsertNode,
        StructureEntityKind.Node,
        node.StateId,
        [],
        node,
        null,
        null,
        null,
        null,
        null,
        node.EvidenceIds,
        "nodeを追加");

    private static StructureMutation UpsertEdge(StructureScreenEdge edge) => new(
        ContractSchemaVersions.Revision03,
        StructureMutationKind.UpsertEdge,
        StructureEntityKind.Edge,
        edge.EdgeId,
        [],
        null,
        edge,
        null,
        null,
        null,
        null,
        edge.EvidenceIds,
        "edgeを追加");

    private static StructureMutation UpsertFact(GameStateFact fact) => new(
        ContractSchemaVersions.Revision03,
        StructureMutationKind.UpsertFact,
        StructureEntityKind.Fact,
        fact.FactId,
        [],
        null,
        null,
        fact,
        null,
        null,
        null,
        fact.EvidenceIds,
        "factを追加");

    private static StructureScreenNode Node(string id, IReadOnlyList<string>? evidenceIds = null) => new(
        ContractSchemaVersions.Revision03,
        id,
        "env-1",
        [$"signature-{id}"],
        [],
        evidenceIds ?? [$"observation-{id}"],
        null,
        StructureVerificationState.Candidate);

    private static StructureScreenEdge Edge(string id, string sourceId, string destinationId) => new(
        ContractSchemaVersions.Revision03,
        id,
        sourceId,
        destinationId,
        null,
        "affordance-1",
        "locator-1",
        "click",
        "target-visible",
        [],
        true,
        "observation-before",
        "observation-after",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 3, 300, 5_000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 1)],
        ["transition-1"],
        StructureVerificationState.Candidate);

    private static GameStateFact Fact(string id) => new(
        ContractSchemaVersions.Revision03,
        id,
        "daily-count",
        "1",
        "ocr-1",
        ["observation-fact"],
        0.8,
        "daily-screen",
        "daily-reset",
        StructureVerificationState.Candidate);
}
