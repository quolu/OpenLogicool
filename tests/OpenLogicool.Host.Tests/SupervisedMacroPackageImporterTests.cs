using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class SupervisedMacroPackageImporterTests
{
    [Fact]
    public void Compile_rejection_rolls_back_structure_scene_profile_and_route_as_one_import()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        var package = Package() with
        {
            Edges = [Package().Edges[0] with { RiskTags = ["spend-premium-currency"] }],
        };

        Assert.Throws<InvalidOperationException>(() => SupervisedMacroPackageImporter.Import(connection, package));

        Assert.Empty(new SqliteGameStructureStore(connection).ReadEvents("game", "env"));
        Assert.Null(new SqliteLearnedSceneProfileStore(connection).Load("game", "env"));
        Assert.Empty(new SqliteLearningRouteStore(connection).ReadRevisions("route:1"));
    }

    [Fact]
    public void Empty_prohibited_tag_set_does_not_block_import()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        var package = Package() with { ProhibitedRiskTags = [] };

        var result = SupervisedMacroPackageImporter.Import(connection, package);

        Assert.Equal(package.PackageId, result.PackageId);
        Assert.Single(new SqliteLearningRouteStore(connection).ReadRevisions(package.RouteId));
    }

    private static SupervisedMacroPackageDocument Package()
    {
        var node1 = new StructureScreenNode(
            ContractSchemaVersions.Revision03, "state:lobby", "env", ["signature:lobby"], [], ["e1"], "ロビー",
            StructureVerificationState.Replayed);
        var node2 = new StructureScreenNode(
            ContractSchemaVersions.Revision03, "state:squad", "env", ["signature:squad"], [], ["e2"], "部隊編成",
            StructureVerificationState.Replayed);
        var edge = new StructureScreenEdge(
            ContractSchemaVersions.Revision03, "edge:1", node1.StateId, node2.StateId, null,
            "affordance:squad", "locator:squad:v1", "click", "supervised", [], true,
            "before:1", "after:1", new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 1, 0, 1_000),
            [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 1)], ["e3"],
            StructureVerificationState.Replayed);
        var profile = new LearnedSceneProfileDocument(
            ContractSchemaVersions.Revision03, "profile:1", "profile:v1", "game", "env", "game", null, 500, 0.04,
            [
                new LearnedStateSceneSignature(node1.StateId, "signature:lobby",
                    [new LearnedSceneAnchor("ロビー", [0.1, 0.1, 0.1, 0.1], "e1"),
                     new LearnedSceneAnchor("隊員募集", [0.7, 0.1, 0.1, 0.1], "e2")],
                    [new LearnedAffordanceSignature("affordance:squad", "locator:squad:v1", "部隊",
                        [0.4, 0.8, 0.1, 0.1], ["click"], ["e3"])], ["e1", "e2"]),
                new LearnedStateSceneSignature(node2.StateId, "signature:squad",
                    [new LearnedSceneAnchor("部隊編成", [0.1, 0.1, 0.1, 0.1], "e4"),
                     new LearnedSceneAnchor("CAMPAIGN", [0.7, 0.1, 0.1, 0.1], "e5")], [], ["e4", "e5"]),
            ], ["profile-evidence"]);
        return new SupervisedMacroPackageDocument(
            ContractSchemaVersions.Revision03, "package:1", "game", "env", profile, [node1, node2], [edge],
            "route:1", "部隊を開く", [edge.EdgeId],
            ["spend-premium-currency", "spend-rare-resource", "spend-real-money"], ["package-evidence"],
            DateTimeOffset.UnixEpoch);
    }
}
