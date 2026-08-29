using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Exploration.Tests;

public sealed class DemonstrationRouteCompilerTests
{
    private const string GameId = "nikke";
    private const string EnvironmentScope = "env-1";
    private const string Goal = "アークを開く";

    [Fact]
    public void Moved_operations_become_route_edges_while_stayed_and_undetermined_are_excluded_without_committing()
    {
        var sceneA = Scene("scene-a", label: "btn-to-b");
        var sceneB = Scene("scene-b", label: "btn-to-c");
        var committer = new FakeStructureCommitter();
        var routes = new FakeLearningRouteStore();
        var compiler = new DemonstrationRouteCompiler(committer, routes, FixedTime(10));

        var session = Session(
            Operation("op-1", GameInteractionOperations.Click, sceneA, sceneB, GameTransitionJudgement.Moved),
            Operation("op-2", GameInteractionOperations.Click, sceneB, sceneB, GameTransitionJudgement.Stayed),
            Operation("op-3", GameInteractionOperations.Click, sceneB, null, GameTransitionJudgement.Undetermined));

        var result = compiler.Compile(session);

        Assert.Equal(3, result.Decisions.Count);
        Assert.Equal(DemonstrationRouteDecisionKind.Accepted, result.Decisions[0].Kind);
        Assert.Equal(DemonstrationRouteDecisionKind.ExcludedStayed, result.Decisions[1].Kind);
        Assert.Equal(DemonstrationRouteDecisionKind.ExcludedUndetermined, result.Decisions[2].Kind);
        Assert.Single(result.Route.EdgeIds);
        Assert.Equal(1, committer.CommitCallCount);
        Assert.Equal(GameId, result.Route.GameId);
        Assert.Equal(Goal, result.Route.Goal);
    }

    [Fact]
    public void Returning_to_an_already_visited_state_is_excluded_from_the_route_as_a_detour()
    {
        var sceneA = Scene("scene-a", label: "btn-to-b");
        var sceneB = Scene("scene-b", label: "btn-to-a");
        var committer = new FakeStructureCommitter();
        var routes = new FakeLearningRouteStore();
        var compiler = new DemonstrationRouteCompiler(committer, routes, FixedTime(10));

        var session = Session(
            Operation("op-1", GameInteractionOperations.Click, sceneA, sceneB, GameTransitionJudgement.Moved),
            Operation("op-2", GameInteractionOperations.Click, sceneB, sceneA, GameTransitionJudgement.Moved));

        var result = compiler.Compile(session);

        Assert.Equal(DemonstrationRouteDecisionKind.Accepted, result.Decisions[0].Kind);
        Assert.Equal(DemonstrationRouteDecisionKind.ExcludedDetour, result.Decisions[1].Kind);
        Assert.NotNull(result.Decisions[1].EdgeId);
        Assert.Single(result.Route.EdgeIds);
        Assert.Equal(2, committer.CommitCallCount);
    }

    [Fact]
    public void Repeating_the_same_transition_is_excluded_from_the_route_as_a_duplicate()
    {
        var sceneA = Scene("scene-a", label: "btn-to-b");
        var sceneB = Scene("scene-b", label: "btn-to-c");
        var committer = new FakeStructureCommitter();
        var routes = new FakeLearningRouteStore();
        var compiler = new DemonstrationRouteCompiler(committer, routes, FixedTime(10));

        var session = Session(
            Operation("op-1", GameInteractionOperations.Click, sceneA, sceneB, GameTransitionJudgement.Moved),
            Operation("op-2", GameInteractionOperations.Click, sceneA, sceneB, GameTransitionJudgement.Moved));

        var result = compiler.Compile(session);

        Assert.Equal(DemonstrationRouteDecisionKind.Accepted, result.Decisions[0].Kind);
        Assert.Equal(DemonstrationRouteDecisionKind.ExcludedDuplicate, result.Decisions[1].Kind);
        Assert.Single(result.Route.EdgeIds);
    }

    [Fact]
    public void First_compilation_for_a_goal_creates_a_root_revision_with_the_deterministic_goal_route_id()
    {
        var sceneA = Scene("scene-a", label: "btn-to-b");
        var sceneB = Scene("scene-b", label: "btn-to-c");
        var committer = new FakeStructureCommitter();
        var routes = new FakeLearningRouteStore();
        var compiler = new DemonstrationRouteCompiler(committer, routes, FixedTime(10));
        var session = Session(Operation("op-1", GameInteractionOperations.Click, sceneA, sceneB, GameTransitionJudgement.Moved));

        var result = compiler.Compile(session);

        var expectedRouteId = DemonstrationGoalRouteIds.Create(GameId, EnvironmentScope, Goal);
        Assert.Equal(expectedRouteId, result.Route.RouteId);
        Assert.Null(result.Route.ParentVersionId);
        Assert.Equal(1, result.Route.RevisionNumber);
        Assert.Equal(LearningRouteAuthor.User, result.Route.Author);
        Assert.Equal(LearningRouteStatus.Compiled, result.Route.Status);
    }

    [Fact]
    public void Compilation_appends_onto_an_existing_route_for_the_same_goal_instead_of_replacing_it()
    {
        var sceneA = Scene("scene-a", label: "btn-to-b");
        var sceneB = Scene("scene-b", label: "btn-to-c");
        var committer = new FakeStructureCommitter();
        var routes = new FakeLearningRouteStore();
        var routeId = DemonstrationGoalRouteIds.Create(GameId, EnvironmentScope, Goal);
        var existing = routes.Append(new LearningRouteDraft(
            ContractSchemaVersions.Revision03, routeId, null, GameId, EnvironmentScope, "structure:seed",
            Goal, ["edge:seed"], LearningRouteAuthor.Ai, null, "seed", LearningRouteStatus.Compiled, FixedTime(1).GetUtcNow()));
        var compiler = new DemonstrationRouteCompiler(committer, routes, FixedTime(10));
        var session = Session(Operation("op-1", GameInteractionOperations.Click, sceneA, sceneB, GameTransitionJudgement.Moved));

        var result = compiler.Compile(session);

        Assert.Equal(existing.VersionId, result.Route.ParentVersionId);
        Assert.Equal(2, result.Route.RevisionNumber);
        Assert.Equal(routeId, result.Route.RouteId);
    }

    [Fact]
    public void Compilation_refuses_when_the_existing_route_scope_or_goal_does_not_match_the_session()
    {
        var sceneA = Scene("scene-a", label: "btn-to-b");
        var sceneB = Scene("scene-b", label: "btn-to-c");
        var committer = new FakeStructureCommitter();
        var routes = new FakeLearningRouteStore();
        var routeId = DemonstrationGoalRouteIds.Create(GameId, EnvironmentScope, Goal);
        routes.Append(new LearningRouteDraft(
            ContractSchemaVersions.Revision03, routeId, null, GameId, "other-env", "structure:seed",
            Goal, ["edge:seed"], LearningRouteAuthor.Ai, null, "seed", LearningRouteStatus.Compiled, FixedTime(1).GetUtcNow()));
        var compiler = new DemonstrationRouteCompiler(committer, routes, FixedTime(10));
        var session = Session(Operation("op-1", GameInteractionOperations.Click, sceneA, sceneB, GameTransitionJudgement.Moved));

        Assert.Throws<InvalidOperationException>(() => compiler.Compile(session));
    }

    [Fact]
    public void Compilation_refuses_a_session_that_is_still_recording()
    {
        var sceneA = Scene("scene-a", label: "btn-to-b");
        var sceneB = Scene("scene-b", label: "btn-to-c");
        var compiler = new DemonstrationRouteCompiler(new FakeStructureCommitter(), new FakeLearningRouteStore());
        var session = Session(Operation("op-1", GameInteractionOperations.Click, sceneA, sceneB, GameTransitionJudgement.Moved))
            with
        { State = DemonstrationSessionState.Recording };

        Assert.Throws<InvalidOperationException>(() => compiler.Compile(session));
    }

    [Fact]
    public void Compilation_refuses_a_session_with_no_operation_events()
    {
        var draft = SessionDraft();
        var stop = new DemonstrationEvent(
            ContractSchemaVersions.Revision03, draft.SessionId, 1, "event-1", null, "revision-1",
            DemonstrationEventKind.Stopped, FixedTime(1).GetUtcNow(), FixedTime(1).GetUtcNow(), null, null,
            new DemonstrationStop(ContractSchemaVersions.Revision03, "利用者停止", FixedTime(1).GetUtcNow()));
        var session = new DemonstrationSessionRecord(draft, DemonstrationSessionState.Stopped, "revision-1", [stop]);
        var compiler = new DemonstrationRouteCompiler(new FakeStructureCommitter(), new FakeLearningRouteStore());

        Assert.Throws<InvalidOperationException>(() => compiler.Compile(session));
    }

    [Fact]
    public void Compilation_refuses_when_every_operation_is_excluded()
    {
        var sceneA = Scene("scene-a", label: "btn-to-b");
        var compiler = new DemonstrationRouteCompiler(new FakeStructureCommitter(), new FakeLearningRouteStore());
        var session = Session(Operation("op-1", GameInteractionOperations.Click, sceneA, sceneA, GameTransitionJudgement.Stayed));

        Assert.Throws<InvalidOperationException>(() => compiler.Compile(session));
    }

    [Fact]
    public void Key_tap_operations_are_committed_with_their_key_tokens_and_no_pointer_bounds()
    {
        var sceneA = Scene("scene-a", label: "btn-to-b");
        var sceneB = Scene("scene-b", label: "btn-to-c");
        var committer = new FakeStructureCommitter();
        var routes = new FakeLearningRouteStore();
        var compiler = new DemonstrationRouteCompiler(committer, routes, FixedTime(10));
        var session = Session(Operation(
            "op-1", GameInteractionOperations.KeyTap, sceneA, sceneB, GameTransitionJudgement.Moved,
            keyTokens: ["Key:Enter"]));

        compiler.Compile(session);

        var candidate = Assert.Single(committer.LastBefore!.Affordances, item => item.SemanticKind == "demonstration");
        Assert.Equal(["Key:Enter"], candidate.KeyTokens);
        Assert.Equal([0d, 0d, 0d, 0d], candidate.Locator.NormalizedBounds);
        Assert.Equal("demonstration-key", candidate.Locator.LocatorType);
    }

    private static DemonstrationSessionDraft SessionDraft() => new(
        ContractSchemaVersions.Revision03,
        "session-1",
        GameId,
        EnvironmentScope,
        Goal,
        @"C:\game\nikke.exe",
        "window:game",
        "recorder-1.0.0",
        FixedTime(0).GetUtcNow());

    private static DemonstrationSessionRecord Session(params DemonstrationOperation[] operations)
    {
        var draft = SessionDraft();
        var events = new List<DemonstrationEvent>();
        string? parentRevisionId = null;
        long sequence = 1;
        foreach (var operation in operations)
        {
            var resultingRevisionId = $"revision-{sequence}";
            events.Add(new DemonstrationEvent(
                ContractSchemaVersions.Revision03,
                draft.SessionId,
                sequence,
                $"event-{sequence}",
                parentRevisionId,
                resultingRevisionId,
                DemonstrationEventKind.Operation,
                operation.OccurredUtc,
                operation.OccurredUtc,
                operation,
                null,
                null));
            parentRevisionId = resultingRevisionId;
            sequence++;
        }

        var stopRevisionId = $"revision-{sequence}";
        events.Add(new DemonstrationEvent(
            ContractSchemaVersions.Revision03,
            draft.SessionId,
            sequence,
            $"event-{sequence}",
            parentRevisionId,
            stopRevisionId,
            DemonstrationEventKind.Stopped,
            FixedTime(20).GetUtcNow(),
            FixedTime(20).GetUtcNow(),
            null,
            null,
            new DemonstrationStop(ContractSchemaVersions.Revision03, "利用者停止", FixedTime(20).GetUtcNow())));

        return new DemonstrationSessionRecord(draft, DemonstrationSessionState.Stopped, stopRevisionId, events);
    }

    private static DemonstrationOperation Operation(
        string operationId,
        string primitive,
        ObservedScene before,
        ObservedScene? afterStable,
        GameTransitionJudgement judgement,
        IReadOnlyList<string>? keyTokens = null)
    {
        var isKeyTap = string.Equals(primitive, GameInteractionOperations.KeyTap, StringComparison.Ordinal);
        var target = new DemonstrationFrameBinding(
            ContractSchemaVersions.Revision03,
            before.ObservationId,
            before.Frame.Sequence,
            before.Frame.TransformRevision,
            before.Frame.SourceId,
            isKeyTap ? null : [0.5, 0.5]);
        var stability = new GameInteractionStabilityResult(
            ContractSchemaVersions.Revision03,
            afterStable is null ? GameInteractionStabilityStatus.TimedOut : GameInteractionStabilityStatus.Stable,
            afterStable is null ? [] : [afterStable],
            afterStable,
            afterStable is null ? 0 : 3,
            afterStable is null ? 0 : 300,
            500,
            null);
        var comparison = new GameTransitionComparison(
            ContractSchemaVersions.Revision03,
            before.ObservationId,
            afterStable?.ObservationId,
            judgement,
            [],
            [$"test:{judgement}"]);
        return new DemonstrationOperation(
            ContractSchemaVersions.Revision03,
            operationId,
            primitive,
            isKeyTap ? DemonstrationInputSource.Keyboard : DemonstrationInputSource.Mouse,
            target,
            before,
            stability,
            comparison,
            $"evidence-{operationId}",
            100,
            600,
            FixedTime(1).GetUtcNow(),
            KeyTokens: isKeyTap ? keyTokens ?? ["Key:Enter"] : null);
    }

    private static ObservedScene Scene(string observationId, string label)
    {
        var frame = new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            "window:game",
            CaptureBackend.WindowsGraphicsCapture,
            1,
            100,
            FixedTime(0).GetUtcNow(),
            1,
            10,
            300);
        return new ObservedScene(
            ContractSchemaVersions.Revision03,
            $"scene-{observationId}",
            observationId,
            frame,
            CaptureAvailability.Available,
            StateIdentityStatus.Novel,
            null,
            [],
            [new AffordanceCandidate(
                ContractSchemaVersions.Revision03,
                $"affordance-{observationId}",
                observationId,
                1,
                1,
                "window:game",
                new AffordanceLocator(ContractSchemaVersions.Revision03, "ocr", [0.4, 0.4, 0.2, 0.2], "locator-1"),
                [],
                0.9,
                [GameInteractionOperations.Click],
                SemanticLabel: label)],
            "perception-1");
    }

    private static TimeProvider FixedTime(int seconds) => new FixedTimeProvider(
        DateTimeOffset.UnixEpoch.AddSeconds(seconds));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStructureCommitter : IGameInteractionStructureCommitter
    {
        private int revisionCounter;

        public int CommitCallCount { get; private set; }

        public ObservedScene? LastBefore { get; private set; }

        public GameInteractionStructureCommitResult Commit(
            ObservedScene before,
            ObservedScene after,
            TransitionEvidence evidence,
            ExplorationWaitCondition waitCondition,
            IReadOnlyList<string> riskTags,
            bool reversible,
            DateTimeOffset recordedUtc)
        {
            CommitCallCount++;
            LastBefore = before;
            revisionCounter++;
            var revision = new GameStructureRevision(
                ContractSchemaVersions.Revision03,
                $"structure:{revisionCounter}",
                revisionCounter == 1 ? null : $"structure:{revisionCounter - 1}",
                revisionCounter,
                new StructureScreenGraph(ContractSchemaVersions.Revision03, "graph-1", [], [], [], EnvironmentScope),
                [],
                [],
                EnvironmentScope,
                recordedUtc);
            return new GameInteractionStructureCommitResult(revision, $"edge:{revisionCounter}");
        }
    }

    private sealed class FakeLearningRouteStore : ILearningRouteStore
    {
        private readonly Dictionary<string, List<LearningRouteRevision>> revisionsByRoute = new(StringComparer.Ordinal);

        public LearningRouteRevision Append(LearningRouteDraft draft)
        {
            if (!revisionsByRoute.TryGetValue(draft.RouteId, out var revisions))
            {
                revisions = [];
                revisionsByRoute[draft.RouteId] = revisions;
            }

            var revision = new LearningRouteRevision(
                draft.SchemaVersion,
                draft.RouteId,
                revisions.Count + 1,
                $"version:{draft.RouteId}:{revisions.Count + 1}",
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
            revisions.Add(revision);
            return revision;
        }

        public IReadOnlyList<LearningRouteRevision> ReadRevisions(string routeId) =>
            revisionsByRoute.TryGetValue(routeId, out var revisions) ? revisions : [];

        public LearningRouteRevision? LoadLatest(string routeId) =>
            revisionsByRoute.TryGetValue(routeId, out var revisions) ? revisions[^1] : null;

        public IReadOnlyList<string> ListRouteIds(string gameId, string environmentScope) =>
            revisionsByRoute.Keys.ToArray();
    }
}
