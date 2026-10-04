using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using Microsoft.Data.Sqlite;
using OpenLogicool.Persistence;
using System.IO;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class PurposeDirectedExplorationRuntimeTests
{
    [Fact]
    public async Task Recorded_route_stops_at_pending_step_without_skipping_the_suffix_or_dispatching_it()
    {
        var initial = Route(["e1", "e3"]) with
        {
            RecordedSteps = [Recorded("op1", "e1", GameTransitionJudgement.Moved),
                Recorded("op2", null, null), Recorded("op3", "e3", GameTransitionJudgement.Moved)],
        };
        var routes = new Routes(initial);
        var steps = new Steps([Moved("e1")]);
        var runtime = Runtime(steps, routes, new Completion(false));
        Assert.Equal(PurposeDirectedStepStatus.Advanced, (await runtime.ExecuteNextAsync()).Status);
        var stopped = await runtime.ExecuteNextAsync();
        Assert.Equal(PurposeDirectedStepStatus.Stopped, stopped.Status);
        Assert.Contains("手順 2", stopped.Detail);
        Assert.Single(steps.Hints);
        Assert.Equal(initial, Assert.Single(routes.History));
    }

    [Fact]
    public async Task User_confirmed_no_change_step_advances_only_when_live_compare_is_stayed()
    {
        var initial = Route([]) with { RecordedSteps = [Recorded("op1", null, GameTransitionJudgement.Stayed)] };
        var routes = new Routes(initial);
        var runtime = Runtime(new Steps([Stayed("unchanged")]), routes, new Completion(false));
        Assert.Equal(PurposeDirectedStepStatus.Completed, (await runtime.ExecuteNextAsync()).Status);
        Assert.Single(routes.History);
        var mismatch = Runtime(new Steps([Moved("e1")]), new Routes(initial), new Completion(false));
        Assert.Equal(PurposeDirectedStepStatus.Stopped, (await mismatch.ExecuteNextAsync()).Status);
    }

    private static DemonstrationRouteStep Recorded(string id, string? edge, GameTransitionJudgement? expected) =>
        new("demo", id, GameInteractionOperations.Click, [0.2, 0.3], null, null, null, null, edge, expected, "test", "demo");

    [Fact]
    public async Task Resume_from_repaired_step_does_not_dispatch_the_already_completed_prefix()
    {
        var initial = Route(["e1", "e3"]) with
        { RecordedSteps = [Recorded("op1", "e1", GameTransitionJudgement.Moved), Recorded("op2", "e3", GameTransitionJudgement.Moved)] };
        var steps = new Steps([Moved("e3")]);
        var routes = new Routes(initial);
        var runtime = new PurposeDirectedExplorationRuntime("game", "env", initial.Goal,
            steps, new Structures(), routes, new Completion(false), initialRoute: initial, startStepIndex: 1);
        Assert.Equal(PurposeDirectedStepStatus.Completed, (await runtime.ExecuteNextAsync()).Status);
        Assert.Equal("e3", Assert.Single(steps.Hints)!.EdgeId);
        Assert.Equal(initial, Assert.Single(routes.History));
    }

    [Fact]
    public async Task Known_recorded_step_can_be_ai_repaired_while_the_pending_suffix_and_source_references_stay_intact()
    {
        var initial = Route(["e1", "e3"]) with
        { RecordedSteps = [Recorded("op1", "e1", GameTransitionJudgement.Moved), Recorded("op2", null, null), Recorded("op3", "e3", GameTransitionJudgement.Moved)] };
        var routes = new Routes(initial);
        var runtime = Runtime(new Steps([Stayed("failed"), Moved("e2")]), routes, new Completion(false));
        Assert.Equal(PurposeDirectedStepStatus.LearningContinues, (await runtime.ExecuteNextAsync()).Status);
        var repaired = (await runtime.ExecuteNextAsync()).Route!;
        Assert.Equal("e2", repaired.RecordedSteps![0].EdgeId);
        Assert.Equal(initial.RecordedSteps[1], repaired.RecordedSteps[1]);
        Assert.Equal(initial.RecordedSteps[2], repaired.RecordedSteps[2]);
        Assert.Equal(3, repaired.StepCount);
        Assert.Equal(initial, routes.History[0]);
    }

    [Fact]
    public void Goal_completion_ignores_ocr_text_that_normalizes_to_empty()
    {
        var scene = Scene("after");
        scene = scene with
        {
            Affordances = [scene.Affordances[0] with { SemanticLabel = "、" }],
        };

        Assert.False(new SemanticTextGoalCompletionEvaluator().IsSatisfied(
            "部隊編成を開く", scene, scene.Affordances[0] with { SemanticLabel = "アーク" }));
    }

    [Fact]
    public void Goal_completion_accepts_a_similar_destination_label()
    {
        var scene = Scene("after");
        Assert.True(new SemanticTextGoalCompletionEvaluator().IsSatisfied(
            "ランキングを開く", scene, scene.Affordances[0] with { SemanticLabel = "別action" }));
    }

    [Fact]
    public void Goal_completion_accepts_the_moved_action_label_without_a_destination_id()
    {
        var scene = Scene("after");
        Assert.True(new SemanticTextGoalCompletionEvaluator().IsSatisfied(
            "アークを開く", scene, scene.Affordances[0] with { SemanticLabel = "0アーク" }));
    }

    [Fact]
    public void Goal_completion_does_not_use_local_ocr_substrings_as_goal_evidence()
    {
        var original = Scene("after");
        var scene = original with
        {
            Affordances = [original.Affordances[0] with { SemanticLabel = "お知らせ" }],
            DiscoveryEvidence = new SceneDiscoveryEvidence(
                "windows-ocr-local", "none", "local-target-tracking-v1", "none",
                "Completed", "None", null, "{}", 0, 0, 0m,
                LocalGroundingTexts: ["る", "す", "日"]),
        };

        Assert.False(new SemanticTextGoalCompletionEvaluator().IsSatisfied(
            "日課の情報を取得する", scene, scene.Affordances[0] with { SemanticLabel = "Continue" }));
    }

    [Fact]
    public async Task First_run_appends_each_moved_edge_and_completes_from_goal_evaluator()
    {
        var routes = new Routes();
        var steps = new Steps([Moved("e1")]);
        var runtime = Runtime(steps, routes, new Completion(true));

        var result = await runtime.ExecuteNextAsync();

        Assert.Equal(PurposeDirectedStepStatus.Completed, result.Status);
        Assert.Equal(["e1"], result.Route!.EdgeIds);
        Assert.Equal(LearningRouteStatus.Compiled, result.Route.Status);
        Assert.Single(routes.Appended);
        Assert.Null(steps.Hints.Single());
    }

    [Fact]
    public async Task Non_moving_saved_step_repairs_only_that_step_and_preserves_route_history()
    {
        var routes = new Routes(Route(["e1", "e3"]));
        var steps = new Steps([Stayed("e1-failed"), Moved("e2")]);
        var runtime = Runtime(steps, routes, new Completion(false));

        var learned = await runtime.ExecuteNextAsync();
        var repaired = await runtime.ExecuteNextAsync();

        Assert.Equal(PurposeDirectedStepStatus.LearningContinues, learned.Status);
        Assert.Equal(["e1", "e3"], learned.Route!.EdgeIds);
        Assert.Equal(["e2", "e3"], repaired.Route!.EdgeIds);
        Assert.Equal(2, routes.History.Count);
        Assert.All(steps.Hints, edge => Assert.Equal("e1", edge!.EdgeId));
        Assert.Equal([false, true], steps.RepairFlags);
    }

    [Fact]
    public async Task Ai_free_playback_stops_on_non_move_without_ai_repair_or_route_update()
    {
        var initial = Route(["e1", "e3"]);
        var routes = new Routes(initial);
        var steps = new Steps([Stayed("e1-failed")]);
        var runtime = Runtime(steps, routes, new Completion(false), MacroPlaybackMode.AiFree);

        var stopped = await runtime.ExecuteNextAsync();

        Assert.Equal(PurposeDirectedStepStatus.Stopped, stopped.Status);
        Assert.Equal(["e1", "e3"], stopped.Route!.EdgeIds);
        Assert.Empty(routes.Appended);
        Assert.Equal([false], steps.RepairFlags);
        Assert.Contains("AI監視なし", stopped.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restart_replays_all_saved_steps_without_writing_a_new_route_revision()
    {
        var routes = new Routes(Route(["e1", "e3"]));
        var steps = new Steps([Moved("replay1"), Moved("replay2")]);
        var runtime = Runtime(steps, routes, new Completion(false), MacroPlaybackMode.AiFree);

        var first = await runtime.ExecuteNextAsync();
        var second = await runtime.ExecuteNextAsync();

        Assert.Equal(PurposeDirectedStepStatus.Advanced, first.Status);
        Assert.Equal(PurposeDirectedStepStatus.Completed, second.Status);
        Assert.Empty(routes.Appended);
        Assert.Equal(["e1", "e3"], steps.Hints.Select(edge => edge!.EdgeId));
        Assert.Equal([false, false], steps.RepairFlags);
    }

    [Fact]
    public async Task Restarted_draft_prefix_replays_then_continues_discovery_until_goal_completion()
    {
        var routes = new Routes(Route(["e1"]) with { Status = LearningRouteStatus.Draft });
        var steps = new Steps([Moved("replay1"), Moved("e2")]);
        var runtime = Runtime(steps, routes, new Completion(true));

        var prefix = await runtime.ExecuteNextAsync();
        var completed = await runtime.ExecuteNextAsync();

        Assert.Equal(PurposeDirectedStepStatus.Advanced, prefix.Status);
        Assert.Equal(PurposeDirectedStepStatus.Completed, completed.Status);
        Assert.Equal(["e1", "e2"], completed.Route!.EdgeIds);
        Assert.Equal(LearningRouteStatus.Compiled, completed.Route.Status);
        Assert.Equal("e1", steps.Hints[0]!.EdgeId);
        Assert.Null(steps.Hints[1]);
    }

    [Fact]
    public async Task Explicit_composed_route_keeps_its_route_identity_when_repaired()
    {
        var initial = Route(["e1", "e3"]) with { RouteId = "macro:composed:daily", VersionId = "macro:composed:daily:v1" };
        var routes = new Routes(initial);
        var steps = new Steps([Stayed("failed"), Moved("e2")]);
        var runtime = new PurposeDirectedExplorationRuntime(
            "game", "env", "ランキングを開く", steps, new Structures(), routes,
            new Completion(false), new FixedTimeProvider(DateTimeOffset.UnixEpoch),
            MacroPlaybackMode.AiMonitored, initial);

        _ = await runtime.ExecuteNextAsync();
        var repaired = await runtime.ExecuteNextAsync();

        Assert.Equal("macro:composed:daily", repaired.Route!.RouteId);
        Assert.Equal(["e2", "e3"], repaired.Route.EdgeIds);
    }

    [Fact]
    public async Task Sqlite_reopen_resolves_the_same_goal_route_and_replays_without_a_new_revision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openlogicool-purpose-{Guid.NewGuid():N}.db");
        try
        {
            using (var first = Open(path))
            {
                _ = new SqliteLearningRouteStore(first).Append(new LearningRouteDraft(
                    ContractSchemaVersions.Revision03,
                    PurposeLearningRouteIds.Create("game", "env", "ランキングを開く"), null,
                    "game", "env", "structure:current", "ランキングを開く", ["e1", "e3"],
                    LearningRouteAuthor.Ai, null, "初回探索", LearningRouteStatus.Compiled, DateTimeOffset.UnixEpoch));
            }
            using (var reopened = Open(path))
            {
                var store = new SqliteLearningRouteStore(reopened);
                var steps = new Steps([Moved("replay1"), Moved("replay2")]);
                var runtime = new PurposeDirectedExplorationRuntime(
                    "game", "env", "ランキングを開く", steps, new Structures(), store,
                    new Completion(false), new FixedTimeProvider(DateTimeOffset.UnixEpoch));

                _ = await runtime.ExecuteNextAsync();
                var completed = await runtime.ExecuteNextAsync();

                Assert.Equal(PurposeDirectedStepStatus.Completed, completed.Status);
                Assert.Single(store.ReadRevisions(PurposeLearningRouteIds.Create("game", "env", "ランキングを開く")));
                Assert.Equal(["e1", "e3"], steps.Hints.Select(edge => edge!.EdgeId));
            }
        }
        finally { File.Delete(path); }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static PurposeDirectedExplorationRuntime Runtime(
        Steps steps,
        Routes routes,
        IPurposeGoalCompletionEvaluator completion,
        MacroPlaybackMode playbackMode = MacroPlaybackMode.AiMonitored) =>
        new("game", "env", "ランキングを開く", steps, new Structures(), routes, completion,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch), playbackMode);

    private static ProductGameExplorerStepResult Moved(string evidence) => Step(
        evidence, GameTransitionJudgement.Moved, ExplorationOutcomeKind.Destination);

    private static ProductGameExplorerStepResult Stayed(string evidence) => Step(
        evidence, GameTransitionJudgement.Stayed, ExplorationOutcomeKind.NoChange);

    private static ProductGameExplorerStepResult Step(
        string evidenceId, GameTransitionJudgement judgement, ExplorationOutcomeKind outcome)
    {
        var before = Scene("before");
        var after = Scene("after");
        var dispatch = new GameInteractionDispatchReceipt(
            ContractSchemaVersions.Revision03, GameInteractionOperations.Click,
            GameInteractionDispatchStatus.Dispatched, before.ObservationId, before.Frame.SourceId,
            "NanoSerialHid", 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            "candidate", "nano", null);
        var comparison = new GameTransitionComparison(
            ContractSchemaVersions.Revision03, before.ObservationId, after.ObservationId, judgement, [], ["test"]);
        var stability = new GameInteractionStabilityResult(
            ContractSchemaVersions.Revision03, GameInteractionStabilityStatus.Stable,
            [after], after, 2, 1_000, 10_000, null);
        var evidence = new TransitionEvidence(
            ContractSchemaVersions.Revision03, evidenceId, before.ObservationId, after.ObservationId,
            "attempt", "candidate", GameInteractionOperations.Click, outcome, "env", 1, 2,
            DateTimeOffset.UnixEpoch, "policy", dispatch, comparison, [after.ObservationId]);
        return new ProductGameExplorerStepResult(
            ProductGameExplorerStepStatus.Learned, before, before.Affordances[0], dispatch, stability,
            comparison, new GameTransitionLearningResult(GameTransitionLearningStatus.Learned, evidence, "learned"),
            "structure:current", "learned", evidenceId);
    }

    private static ObservedScene Scene(string id) => new(
        ContractSchemaVersions.Revision03, $"scene:{id}", id,
        new CapturedFrameReference(ContractSchemaVersions.Revision03, "window", CaptureBackend.WindowsGraphicsCapture,
            1, 1, DateTimeOffset.UnixEpoch, 1, 0, 0), CaptureAvailability.Available,
        StateIdentityStatus.Known, "state", [],
        [new AffordanceCandidate(ContractSchemaVersions.Revision03, "candidate", id, 1, 1, "window",
            new AffordanceLocator(ContractSchemaVersions.Revision03, "text", [0.1, 0.1, 0.1, 0.1], "locator"),
            [], 1, [GameInteractionOperations.Click], "text", "ランキング")], "test");

    private static LearningRouteRevision Route(IReadOnlyList<string> edges) => new(
        ContractSchemaVersions.Revision03, PurposeLearningRouteIds.Create("game", "env", "ランキングを開く"),
        1, "route:v1", null, "game", "env", "structure:current", "ランキングを開く", edges,
        LearningRouteAuthor.Ai, null, "seed", LearningRouteStatus.Compiled, DateTimeOffset.UnixEpoch);

    private static StructureScreenEdge Edge(string id) => new(
        ContractSchemaVersions.Revision03, id, "source", "destination", null, "candidate", "locator",
        GameInteractionOperations.Click, "goal", [], false, "before", "after",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 1)], [id], StructureVerificationState.Candidate,
        TargetSemanticKey: "text|ランキング|0|0", TargetNormalizedBounds: [0.1, 0.1, 0.1, 0.1]);

    private static StructureScreenEdge OtherEdgeWithEvidence(string evidenceId) =>
        Edge("other") with { BeforeObservationId = "other-before", EvidenceIds = [evidenceId] };

    private sealed class Steps(IEnumerable<ProductGameExplorerStepResult> values) : IProductGameStepRuntime
    {
        private readonly Queue<ProductGameExplorerStepResult> queue = new(values);
        public List<StructureScreenEdge?> Hints { get; } = [];
        public List<bool> RepairFlags { get; } = [];
        public void SetRouteTarget(StructureScreenEdge? edge, bool repairing)
        {
            Hints.Add(edge);
            RepairFlags.Add(repairing);
        }
        public ValueTask<ProductGameExplorerStepResult> ExecuteNextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(queue.Dequeue());
    }

    private sealed class Routes(LearningRouteRevision? initial = null) : ILearningRouteStore
    {
        public List<LearningRouteRevision> History { get; } = initial is null ? [] : [initial];
        public List<LearningRouteRevision> Appended { get; } = [];
        public LearningRouteRevision Append(LearningRouteDraft draft)
        {
            var revision = new LearningRouteRevision(draft.SchemaVersion, draft.RouteId, History.Count + 1,
                $"route:v{History.Count + 1}", draft.ParentVersionId, draft.GameId, draft.EnvironmentScope,
                draft.StructureRevisionId, draft.Goal, draft.EdgeIds, draft.Author, draft.UserInstruction,
                draft.ChangeReason, draft.Status, draft.CreatedUtc, draft.RecordedSteps);
            History.Add(revision); Appended.Add(revision); return revision;
        }
        public IReadOnlyList<LearningRouteRevision> ReadRevisions(string routeId) => History;
        public LearningRouteRevision? LoadLatest(string routeId) => History.LastOrDefault();
        public IReadOnlyList<string> ListRouteIds(string gameId, string environmentScope) => History.Select(x => x.RouteId).Distinct().ToArray();
    }

    private sealed class Structures : IGameStructureStore
    {
        public GameStructureRevision LoadRevision(string gameId, string environmentScope) => new(
            ContractSchemaVersions.Revision03, "structure:current", null, 1,
            new StructureScreenGraph(ContractSchemaVersions.Revision03, "graph", [],
                [Edge("e1"), Edge("e2"), Edge("e3"), Edge("replay1"), Edge("replay2"), OtherEdgeWithEvidence("e1")], [], "env"),
            [], [], "env", DateTimeOffset.UnixEpoch);
        public StructureEvent Append(StructureEventDraft draft, string? expectedParentRevisionId, DateTimeOffset persistedUtc) => throw new NotSupportedException();
        public IReadOnlyList<StructureEvent> ReadEvents(string gameId, string environmentScope) => [];
        public IReadOnlyList<string> ListGameIds() => ["game"];
        public StructureKnowledgePackExport Export(string gameId, string environmentScope, DateTimeOffset createdUtc) => throw new NotSupportedException();
    }

    private sealed class Completion(bool value) : IPurposeGoalCompletionEvaluator
    {
        public bool IsSatisfied(string goal, ObservedScene scene, AffordanceCandidate target) => value;
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
