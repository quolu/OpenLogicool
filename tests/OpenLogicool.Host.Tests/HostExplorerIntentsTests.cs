using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostExplorerIntentsTests
{
    [Fact]
    public void RealStoreProjectionShowsFrontierEvidenceLevelsAndRuntimeControls()
    {
        using var connection = OpenMigrated();
        SeedNodes(connection);
        var runtime = new FakeRuntime();
        var intents = new HostExplorerIntents(connection, runtime);

        var scope = Assert.Single(intents.ListScopes());
        var snapshot = intents.Load(scope.GameId, scope.EnvironmentScope);

        Assert.Equal("game-1", snapshot.GameId);
        Assert.Equal(1, snapshot.KnownStateCount);
        Assert.Equal(1, snapshot.NovelStateCount);
        Assert.Contains("state-candidate", snapshot.FrontierIds);
        Assert.Equal(1, snapshot.VerificationCounts.Candidate);
        Assert.Equal(1, snapshot.VerificationCounts.Replayed);
        Assert.Contains(snapshot.Nodes, node => node.EvidenceLevelLabel == "未確認");
        Assert.Contains(snapshot.Nodes, node => node.EvidenceLevelLabel == "強い推定");
        Assert.Equal("候補 button-1 を click", snapshot.ActiveProbeLabel);
        Assert.Equal("中", snapshot.RiskLabel);
        Assert.Equal(4, snapshot.RemainingProbeCount);
        Assert.Equal(["edge-return"], snapshot.RecoveryPathEdgeIds);
        Assert.True(snapshot.CanPause);
        Assert.False(snapshot.CanStep);
        Assert.True(snapshot.CanAbandon);
    }

    [Fact]
    public void PauseStepAndAbandonAreRoutedOnlyToTheMatchingLiveRun()
    {
        using var connection = OpenMigrated();
        SeedNodes(connection);
        var runtime = new FakeRuntime();
        var intents = new HostExplorerIntents(connection, runtime);

        var paused = intents.Pause("game-1", "env-1");
        var stepped = intents.Step("game-1", "env-1");
        var abandoned = intents.Abandon("game-1", "env-1");

        Assert.Equal(["pause", "step", "abandon"], runtime.Calls);
        Assert.True(paused.CanStep);
        Assert.Equal("一手実行を予約", stepped.ActiveProbeLabel);
        Assert.Equal("利用者が探索を終了", abandoned.StopReasonLabel);
        Assert.False(abandoned.CanAbandon);
        Assert.Throws<InvalidOperationException>(() => intents.Pause("other", "env-1"));
    }

    [Fact]
    public void CorrectionAppendsUserEventAndRelabelsWithoutChangingIdentityOrEvidenceLevel()
    {
        using var connection = OpenMigrated();
        SeedNodes(connection);
        var intents = new HostExplorerIntents(connection);

        var before = intents.Load("game-1", "env-1");
        var corrected = intents.Correct(
            "game-1",
            "env-1",
            new ExplorerLabelCorrection("state-candidate", "ロビー", "画面を見て訂正"));

        Assert.NotEqual(before.StructureRevisionId, corrected.StructureRevisionId);
        var node = Assert.Single(corrected.Nodes, item => item.StateId == "state-candidate");
        Assert.Equal("ロビー", node.Label);
        Assert.Equal("未確認", node.EvidenceLevelLabel);
        var events = new SqliteGameStructureStore(connection).ReadEvents("game-1", "env-1");
        var correction = Assert.Single(events, item => item.Kind == StructureEventKind.CorrectionApplied);
        Assert.Equal(StructureEventActor.User, correction.Actor);
        var batch = JsonSerializer.Deserialize<StructureMutationBatch>(correction.PayloadJson);
        Assert.Equal("画面を見て訂正", Assert.Single(batch!.Mutations).Reason);
    }

    private static SqliteConnection OpenMigrated()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static void SeedNodes(SqliteConnection connection)
    {
        var nodes = new[]
        {
            Node("state-candidate", "見つけた画面", StructureVerificationState.Candidate),
            Node("state-known", "再現した画面", StructureVerificationState.Replayed),
        };
        var mutations = nodes.Select(node => new StructureMutation(
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
            "test seed")).ToArray();
        var batch = new StructureMutationBatch(ContractSchemaVersions.Revision03, mutations);
        _ = new SqliteGameStructureStore(connection).Append(
            new StructureEventDraft(
                ContractSchemaVersions.Revision03,
                "event-seed",
                "game-1",
                "env-1",
                StructureEventKind.MutationApplied,
                StructureEventActor.Controller,
                "correlation-1",
                "observation-1",
                "observation-1",
                null,
                null,
                ["evidence-1"],
                StructureEventPayloadTypes.MutationBatch,
                JsonSerializer.Serialize(batch),
                null,
                DateTimeOffset.UnixEpoch),
            null,
            DateTimeOffset.UnixEpoch);
    }

    private static StructureScreenNode Node(string id, string label, StructureVerificationState state) => new(
        ContractSchemaVersions.Revision03,
        id,
        "env-1",
        [$"scene:{id}"],
        [],
        ["evidence-1"],
        label,
        state);

    private sealed class FakeRuntime : IHostExplorerRuntimeControl
    {
        public List<string> Calls { get; } = [];

        public HostExplorerRuntimeSnapshot Snapshot { get; private set; } = new(
            "game-1",
            "env-1",
            "候補 button-1 を click",
            "中",
            "初回の一手承認が必要",
            4,
            10_000,
            2_000,
            ["edge-return"],
            "停止していません",
            CanPause: true,
            CanStep: false,
            CanAbandon: true);

        public void Pause()
        {
            Calls.Add("pause");
            Snapshot = Snapshot with { CanPause = false, CanStep = true };
        }

        public void Step()
        {
            Calls.Add("step");
            Snapshot = Snapshot with { ActiveProbeLabel = "一手実行を予約", CanStep = false };
        }

        public void Abandon()
        {
            Calls.Add("abandon");
            Snapshot = Snapshot with
            {
                StopReasonLabel = "利用者が探索を終了",
                CanPause = false,
                CanStep = false,
                CanAbandon = false,
            };
        }
    }
}
