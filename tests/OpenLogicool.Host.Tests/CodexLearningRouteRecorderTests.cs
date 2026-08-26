using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class CodexLearningRouteRecorderTests
{
    [Fact]
    public void Saved_step_advances_and_failed_step_only_is_replaced_by_codex_edge()
    {
        var initial = Route(["e1", "e2"], LearningRouteStatus.Compiled);
        var routes = new Routes(initial);
        var recorder = new CodexLearningRouteRecorder(
            "game", "env", "goal", new Structures(), routes, new FixedTimeProvider());

        Assert.Equal("e1", recorder.NextSavedEdge!.EdgeId);
        recorder.Record(Outcome(GameTransitionJudgement.Moved, "e1"), usedSavedEdge: true);
        Assert.Equal("e2", recorder.NextSavedEdge!.EdgeId);
        recorder.Record(Outcome(GameTransitionJudgement.Stayed, "e2"), usedSavedEdge: true);
        Assert.True(recorder.Repairing);
        recorder.Record(Outcome(GameTransitionJudgement.Moved, "e3"), usedSavedEdge: false);

        Assert.False(recorder.Repairing);
        Assert.Equal(["e1", "e3"], routes.History[^1].EdgeIds);
        Assert.Equal(LearningRouteStatus.Draft, routes.History[^1].Status);
        recorder.Complete(["fact"]);
        Assert.Equal(LearningRouteStatus.Compiled, routes.History[^1].Status);
        Assert.Equal(3, routes.History.Count);
    }

    private static CodexGameActionOutcome Outcome(GameTransitionJudgement judgement, string edgeId) =>
        new("Learned", judgement, edgeId, judgement.ToString());

    private static LearningRouteRevision Route(IReadOnlyList<string> edges, LearningRouteStatus status) => new(
        ContractSchemaVersions.Revision03,
        PurposeLearningRouteIds.Create("game", "env", "goal"),
        1,
        "route:v1",
        null,
        "game",
        "env",
        "structure:current",
        "goal",
        edges,
        LearningRouteAuthor.Ai,
        null,
        "seed",
        status,
        DateTimeOffset.UnixEpoch);

    private static StructureScreenEdge Edge(string id) => new(
        ContractSchemaVersions.Revision03,
        id,
        "source",
        "destination",
        null,
        "candidate",
        "locator",
        GameInteractionOperations.Click,
        "guard",
        [],
        false,
        "before",
        "after",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 1)],
        ["evidence"],
        StructureVerificationState.Candidate,
        TargetSemanticKey: $"icon|{id}|0|0",
        TargetNormalizedBounds: [0.1, 0.1, 0.1, 0.1]);

    private sealed class Structures : IGameStructureStore
    {
        public GameStructureRevision LoadRevision(string gameId, string environmentScope) => new(
            ContractSchemaVersions.Revision03,
            "structure:current",
            null,
            1,
            new StructureScreenGraph(
                ContractSchemaVersions.Revision03,
                "graph",
                [],
                [Edge("e1"), Edge("e2"), Edge("e3")],
                [],
                "env"),
            [],
            [],
            "env",
            DateTimeOffset.UnixEpoch);
        public StructureEvent Append(StructureEventDraft draft, string? expectedParentRevisionId, DateTimeOffset persistedUtc) => throw new NotSupportedException();
        public IReadOnlyList<StructureEvent> ReadEvents(string gameId, string environmentScope) => [];
        public IReadOnlyList<string> ListGameIds() => ["game"];
        public StructureKnowledgePackExport Export(string gameId, string environmentScope, DateTimeOffset createdUtc) => throw new NotSupportedException();
    }

    private sealed class Routes(LearningRouteRevision initial) : ILearningRouteStore
    {
        public List<LearningRouteRevision> History { get; } = [initial];
        public LearningRouteRevision Append(LearningRouteDraft draft)
        {
            var revision = new LearningRouteRevision(
                draft.SchemaVersion,
                draft.RouteId,
                History.Count + 1,
                $"route:v{History.Count + 1}",
                draft.ParentVersionId,
                draft.GameId,
                draft.EnvironmentScope,
                draft.StructureRevisionId,
                draft.Goal,
                draft.EdgeIds,
                draft.Author,
                draft.UserInstruction,
                draft.ChangeReason,
                draft.Status,
                draft.CreatedUtc);
            History.Add(revision);
            return revision;
        }
        public IReadOnlyList<LearningRouteRevision> ReadRevisions(string routeId) => History;
        public LearningRouteRevision? LoadLatest(string routeId) => History.LastOrDefault(item => item.RouteId == routeId);
        public IReadOnlyList<string> ListRouteIds(string gameId, string environmentScope) => History.Select(item => item.RouteId).Distinct().ToArray();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }
}
