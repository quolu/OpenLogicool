using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>
/// t07: 記録器を実windowへ繋ぐWindows合成のうち、実機を要さない部分。
/// 実windowとWGC captureが要る部分は self-window の live 確認で見る。
/// </summary>
public sealed class WindowsDemonstrationLiveSessionTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"openlogicool-demo-env-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(path);

    [Fact]
    public void The_scope_whose_resolution_matches_the_window_is_reused()
    {
        using var connection = Open();
        SeedScope(connection, "game", "game:1280x720", sequenceHint: 1);
        SeedScope(connection, "game", "game:1920x1080", sequenceHint: 2);

        Assert.Equal(
            "game:1280x720",
            DemonstrationEnvironmentScope.Resolve(connection, "game", 1280, 720));
    }

    [Fact]
    public void An_existing_scope_is_reused_even_when_no_resolution_matches()
    {
        using var connection = Open();
        SeedScope(connection, "game", "game:1280x720", sequenceHint: 1);
        SeedScope(connection, "game", "game:1920x1080", sequenceHint: 2);

        // 一致するscopeが無くても新しいscopeを作らない（既存structureから切り離さない）。
        // どちらの既存scopeを選ぶかは順序が同点になり得るので、そこは規定しない。
        var resolved = DemonstrationEnvironmentScope.Resolve(connection, "game", 800, 600);

        Assert.Contains(resolved, new[] { "game:1280x720", "game:1920x1080" });
        Assert.DoesNotContain("800x600", resolved);
    }

    [Fact]
    public void A_scope_is_created_only_when_the_game_has_none()
    {
        using var connection = Open();
        SeedScope(connection, "other", "other:1920x1080", sequenceHint: 1);

        Assert.Equal(
            "game:1920x1080",
            DemonstrationEnvironmentScope.Resolve(connection, "game", 1920, 1080));
    }

    [Fact]
    public async Task The_observation_adapter_delegates_to_the_same_parts_the_explorer_uses()
    {
        var before = Scene("before");
        var after = Scene("after");
        var observation = new RecordingObservationRuntime(before);
        var stability = new StubStabilityWaiter(after);
        var runtime = new WindowsDemonstrationObservationRuntime(observation, stability, new GameTransitionJudge());

        var observed = await runtime.ObserveAsync();
        Assert.Equal("before", observed.ObservationId);
        Assert.Equal(before, await runtime.DiscoverTargetsAsync(observed));

        var stable = await runtime.WaitStableAsync(
            before, new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000));
        Assert.Same(after, stable.StableScene);

        // 判定は探索と同じ GameTransitionJudge が出す（記録器が独自判定を持たない）。
        Assert.Equal(GameTransitionJudgement.Moved, runtime.Compare(before, stable).Judgement);
        Assert.Equal(1, observation.ObserveCalls);
        Assert.Equal(1, stability.WaitCalls);
    }

    private static ObservedScene Scene(string observationId)
    {
        var frame = new CapturedFrameReference(
            ContractSchemaVersions.Revision03, "window:demo", CaptureBackend.WindowsGraphicsCapture,
            1, 100, DateTimeOffset.UnixEpoch, 1, 10, 300);
        return new ObservedScene(
            ContractSchemaVersions.Revision03, $"scene-{observationId}", observationId, frame,
            CaptureAvailability.Available, StateIdentityStatus.Novel, null, [],
            [new AffordanceCandidate(
                ContractSchemaVersions.Revision03, $"affordance-{observationId}", observationId, 1, 1,
                "window:demo",
                new AffordanceLocator(ContractSchemaVersions.Revision03, "ocr", [0.4, 0.4, 0.2, 0.2], "locator-1"),
                [], 0.9, [GameInteractionOperations.Click], SemanticLabel: observationId)],
            "perception-1");
    }

    /// <summary>実storeで scope を作る（手書きSQLだとschemaとずれる）。</summary>
    private static void SeedScope(SqliteConnection connection, string gameId, string scope, int sequenceHint)
    {
        var node = new StructureScreenNode(
            ContractSchemaVersions.Revision03, $"state:{scope}", scope,
            [], [], ["evidence"], scope, StructureVerificationState.Candidate);
        var batch = new StructureMutationBatch(ContractSchemaVersions.Revision03,
        [
            new StructureMutation(
                ContractSchemaVersions.Revision03, StructureMutationKind.UpsertNode, StructureEntityKind.Node,
                node.StateId, [], node, null, null, null, null, null, node.EvidenceIds, "seed"),
        ]);
        _ = new SqliteGameStructureStore(connection).Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03, $"event:{scope}:{sequenceHint}", gameId, scope,
                StructureEventKind.MutationApplied, StructureEventActor.Controller,
                $"correlation:{scope}", $"causation:{scope}", $"observation:{scope}", null, null, ["evidence"],
                StructureEventPayloadTypes.MutationBatch, JsonSerializer.Serialize(batch), null,
                DateTimeOffset.UnixEpoch),
            null,
            DateTimeOffset.UnixEpoch.AddSeconds(sequenceHint));
    }

    private SqliteConnection Open()
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

    private sealed class RecordingObservationRuntime(ObservedScene scene) : IGameObservationRuntime
    {
        public int ObserveCalls { get; private set; }

        public ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            ObserveCalls++;
            return ValueTask.FromResult(new ObservationResult(
                ContractSchemaVersions.Revision03, scene.ObservationId, scene.Frame,
                CaptureAvailability.Available, StateIdentityStatus.Novel, [], "recognizer-1", 0, null));
        }

        public ValueTask<ObservedScene> DiscoverTargetsAsync(
            ObservationResult observation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(scene);
    }

    private sealed class StubStabilityWaiter(ObservedScene stable) : IGameInteractionStabilityWaiter
    {
        public int WaitCalls { get; private set; }

        public ValueTask<GameInteractionStabilityResult> WaitStableAsync(
            ObservedScene before, ExplorationWaitCondition condition, CancellationToken cancellationToken = default)
        {
            WaitCalls++;
            return ValueTask.FromResult(new GameInteractionStabilityResult(
                ContractSchemaVersions.Revision03, GameInteractionStabilityStatus.Stable,
                [stable], stable, 2, 1_000, 1_200, null));
        }
    }
}
