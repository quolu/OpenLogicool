using System.Text.Json;
using System.IO;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostSupervisedMacroIntentsTests
{
    [Fact]
    public void Real_sqlite_run_observes_before_journals_then_dispatches_nano_once_and_confirms_after()
    {
        using var connection = OpenMigrated();
        var saved = SeedRoute(connection);
        var runtime = new FakeRuntime();
        var intents = new HostSupervisedMacroIntents(connection, runtime, new NullLog());

        var started = intents.Start("game-1", "env-1", saved.RouteId!, saved.VersionId!);
        var completed = intents.Next();

        Assert.Equal(SupervisedMacroRunState.ReadyToDispatch, started.State);
        Assert.Equal(SupervisedMacroRunState.Completed, completed.State);
        Assert.Equal(
            [$"Pin:{saved.VersionId}", "Before:edge:daily", "Before:edge:daily", "Dispatch:edge:daily", "After:edge:daily"],
            runtime.Calls);
        Assert.Equal(1, runtime.DispatchCount);
        var events = intents.JournalStore.ReadRun(completed.RunId);
        Assert.Equal(
            [RunEventPayloadTypes.Observation, RunEventPayloadTypes.Observation,
                RunEventPayloadTypes.Proposal, RunEventPayloadTypes.Authorization,
                RunEventPayloadTypes.Dispatch, RunEventPayloadTypes.DispatchResult, RunEventPayloadTypes.Observation,
                RunEventPayloadTypes.Confirmation],
            events.Select(item => item.PayloadType));
        Assert.Contains(events, item => item.PayloadType == RunEventPayloadTypes.Confirmation);
        Assert.Equal(
            RunEventActorType.User,
            Assert.Single(events, item => item.PayloadType == RunEventPayloadTypes.Authorization).ActorType);
    }

    [Fact]
    public void Owner_delegated_run_records_automation_approval_instead_of_faking_a_user_click()
    {
        using var connection = OpenMigrated();
        var saved = SeedRoute(connection);
        var runtime = new FakeRuntime();
        var intents = new HostSupervisedMacroIntents(
            connection,
            runtime,
            new NullLog(),
            authorizationSource: SupervisedMacroAuthorizationSource.OwnerDelegatedAutomation);

        _ = intents.Start("game-1", "env-1", saved.RouteId!, saved.VersionId!);
        var completed = intents.Next();

        var approval = Assert.Single(
            intents.JournalStore.ReadRun(completed.RunId),
            item => item.PayloadType == RunEventPayloadTypes.Authorization);
        Assert.Equal(RunEventActorType.Automation, approval.ActorType);
        Assert.Contains("owner-delegated-automation", approval.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Before_mismatch_stops_without_nano_dispatch()
    {
        using var connection = OpenMigrated();
        var saved = SeedRoute(connection);
        var runtime = new FakeRuntime(beforeState: "state:other");
        var intents = new HostSupervisedMacroIntents(connection, runtime, new NullLog());

        var stopped = intents.Start("game-1", "env-1", saved.RouteId!, saved.VersionId!);

        Assert.Equal(SupervisedMacroRunState.Stopped, stopped.State);
        Assert.Equal(SupervisedMacroStopReason.BeforeAuditFailed, stopped.StopReason);
        Assert.Equal(0, runtime.DispatchCount);
        Assert.Throws<InvalidOperationException>(() => intents.Next());
    }

    [Fact]
    public void Next_reobserves_before_and_stops_when_the_screen_changed_during_user_wait()
    {
        using var connection = OpenMigrated();
        var saved = SeedRoute(connection);
        var runtime = new FakeRuntime(beforeStates: ["state:lobby", "state:other"]);
        var intents = new HostSupervisedMacroIntents(connection, runtime, new NullLog());

        var started = intents.Start("game-1", "env-1", saved.RouteId!, saved.VersionId!);
        var stopped = intents.Next();

        Assert.Equal(SupervisedMacroRunState.ReadyToDispatch, started.State);
        Assert.Equal(SupervisedMacroRunState.Stopped, stopped.State);
        Assert.Equal(SupervisedMacroStopReason.BeforeAuditFailed, stopped.StopReason);
        Assert.Equal(0, runtime.DispatchCount);
        Assert.Equal(
            [$"Pin:{saved.VersionId}", "Before:edge:daily", "Before:edge:daily"],
            runtime.Calls);
    }

    [Fact]
    public void Dispatch_fault_is_not_retried_and_leaves_outcome_unknown()
    {
        using var connection = OpenMigrated();
        var saved = SeedRoute(connection);
        var runtime = new FakeRuntime(dispatchFault: true);
        var intents = new HostSupervisedMacroIntents(connection, runtime, new NullLog());
        _ = intents.Start("game-1", "env-1", saved.RouteId!, saved.VersionId!);

        var stopped = intents.Next();
        Assert.Equal(1, runtime.DispatchCount);
        Assert.Equal(SupervisedMacroRunState.OutcomeUnknown, stopped.State);
        Assert.Throws<InvalidOperationException>(() => intents.Next());
    }

    [Fact]
    public void Durable_outcome_unknown_blocks_a_new_run_until_explicit_abandonment()
    {
        using var connection = OpenMigrated();
        var saved = SeedRoute(connection);
        var first = new HostSupervisedMacroIntents(
            connection, new FakeRuntime(dispatchFault: true), new NullLog());
        _ = first.Start("game-1", "env-1", saved.RouteId!, saved.VersionId!);
        var unknown = first.Next();
        Assert.Equal(SupervisedMacroRunState.OutcomeUnknown, unknown.State);

        Assert.Throws<InvalidOperationException>(() =>
            first.Start("game-1", "env-1", saved.RouteId!, saved.VersionId!));

        var afterRestartRuntime = new FakeRuntime();
        var afterRestart = new HostSupervisedMacroIntents(connection, afterRestartRuntime, new NullLog());
        Assert.Throws<InvalidOperationException>(() =>
            afterRestart.Start("game-1", "env-1", saved.RouteId!, saved.VersionId!));
        Assert.Empty(afterRestartRuntime.Calls);
    }

    [Fact]
    public void After_observation_fault_is_outcome_unknown_and_does_not_dispatch_again()
    {
        using var connection = OpenMigrated();
        var saved = SeedRoute(connection);
        var runtime = new FakeRuntime(afterObservationFault: true);
        var intents = new HostSupervisedMacroIntents(connection, runtime, new NullLog());
        _ = intents.Start("game-1", "env-1", saved.RouteId!, saved.VersionId!);

        var stopped = intents.Next();

        Assert.Equal(SupervisedMacroRunState.OutcomeUnknown, stopped.State);
        Assert.Equal(SupervisedMacroStopReason.ObservationFault, stopped.StopReason);
        Assert.Equal(1, runtime.DispatchCount);
        Assert.Throws<InvalidOperationException>(() => intents.Next());
    }

    private static LearningRouteScreenSnapshot SeedRoute(SqliteConnection connection)
    {
        var nodes = new[] { Node("state:lobby", "ロビー"), Node("state:daily", "日課一覧") };
        var edge = Edge("edge:daily", "state:lobby", "state:daily");
        var mutations = nodes.Select(node => new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertNode, StructureEntityKind.Node,
                node.StateId, [], node, null, null, null, null, null, node.EvidenceIds, "test seed"))
            .Append(new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertEdge, StructureEntityKind.Edge,
                edge.EdgeId, [edge.SourceStateId, edge.DestinationStateId!], null, edge, null, null, null, null,
                edge.EvidenceIds, "test seed"))
            .ToArray();
        _ = new SqliteGameStructureStore(connection).Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03, "event-route-seed", "game-1", "env-1",
                StructureEventKind.MutationApplied, StructureEventActor.Controller, "correlation-1",
                "observation-1", "observation-1", null, null, ["evidence-1"],
                StructureEventPayloadTypes.MutationBatch,
                JsonSerializer.Serialize(new StructureMutationBatch(ContractSchemaVersions.Revision03, mutations)),
                null, DateTimeOffset.UnixEpoch),
            null,
            DateTimeOffset.UnixEpoch);
        var editor = new HostLearningRouteIntents(connection);
        var initial = editor.Load("game-1", "env-1");
        return editor.Save(new LearningRouteSaveRequest(
            initial.GameId, initial.EnvironmentScope, initial.StructureRevisionId, null, null,
            "日課画面へ移動", ["edge:daily"], "教師付きで確認"));
    }

    private static SqliteConnection OpenMigrated()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static StructureScreenNode Node(string id, string label) => new(
        ContractSchemaVersions.Revision03, id, "env-1", [$"signature:{id}"], [], ["evidence-1"], label,
        StructureVerificationState.Replayed);

    private static StructureScreenEdge Edge(string id, string source, string destination) => new(
        ContractSchemaVersions.Revision03, id, source, destination, null, $"affordance:{id}", "locator:v1", "click",
        "supervised", [], true, $"before:{id}", $"after:{id}",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 300, 10000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 2)], ["evidence-1"],
        StructureVerificationState.Replayed);

    private static ObservedScene Scene(string stateId, string observationId) => new(
        ContractSchemaVersions.Revision03, $"scene:{observationId}", observationId,
        new CapturedFrameReference(
            ContractSchemaVersions.Revision03, "window:game", CaptureBackend.WindowsGraphicsCapture, 1, 1000,
            DateTimeOffset.UnixEpoch, 1, 0, 400),
        CaptureAvailability.Available, StateIdentityStatus.Known, stateId,
        [new StateCandidate(ContractSchemaVersions.Revision03, stateId, 1,
            [new EvidenceRegion(ContractSchemaVersions.Revision03, "rect", [0d, 0d, 1d, 1d], "test")])],
        [new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            "affordance:edge:daily",
            observationId,
            1,
            1,
            "window:game",
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "ocr-normalized-rect",
                [0.1, 0.1, 0.1, 0.1],
                "locator:v1"),
            [],
            1,
            ["click"])], "test");

    private sealed class FakeRuntime(
        string? beforeState = null,
        bool dispatchFault = false,
        bool afterObservationFault = false,
        IReadOnlyList<string>? beforeStates = null)
        : ISupervisedMacroRuntimePort
    {
        private readonly Queue<string>? beforeSequence = beforeStates is null ? null : new(beforeStates);
        public List<string> Calls { get; } = [];
        public int DispatchCount { get; private set; }

        public void Pin(VisualMacroProgram program) => Calls.Add($"Pin:{program.RouteVersionId}");

        public ObservedScene ObserveBefore(VisualMacroStep step)
        {
            Calls.Add($"Before:{step.StructureEdgeId}");
            return Scene(
                beforeSequence is { Count: > 0 } ? beforeSequence.Dequeue() : beforeState ?? step.SourceStateId,
                $"observation:Before:{step.Sequence}");
        }

        public void DispatchNano(VisualMacroStep step, ObservedScene beforeScene)
        {
            Calls.Add($"Dispatch:{step.StructureEdgeId}");
            DispatchCount++;
            if (dispatchFault)
            {
                throw new IOException("nano fault");
            }
        }

        public SupervisedMacroTransitionObservation ObserveAfter(
            VisualMacroStep step,
            ObservedScene beforeScene)
        {
            Calls.Add($"After:{step.StructureEdgeId}");
            if (afterObservationFault)
            {
                throw new IOException("capture fault");
            }
            var after = Scene(step.DestinationStateId, $"observation:After:{step.Sequence}");
            return new SupervisedMacroTransitionObservation(
                new GameInteractionStabilityResult(
                    ContractSchemaVersions.Revision03,
                    GameInteractionStabilityStatus.Stable,
                    [after],
                    after,
                    2,
                    300,
                    300,
                    null),
                new GameTransitionComparison(
                    ContractSchemaVersions.Revision03,
                    beforeScene.ObservationId,
                    after.ObservationId,
                    GameTransitionJudgement.Moved,
                    [],
                    ["fake transition"]),
                after,
                DestinationMatched: true);
        }
    }

    private sealed class NullLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry) { }
    }
}
