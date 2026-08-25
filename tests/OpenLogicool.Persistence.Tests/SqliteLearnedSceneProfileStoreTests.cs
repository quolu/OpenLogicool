using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Persistence.Tests;

public sealed class SqliteLearnedSceneProfileStoreTests
{
    [Fact]
    public void Migrated_store_roundtrips_profile_and_replaces_same_scope()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        var store = new SqliteLearnedSceneProfileStore(connection);
        var document = Document("v1");

        store.Upsert(document);
        store.Upsert(document with { ProfileVersion = "v2" });

        var restored = Assert.IsType<LearnedSceneProfileDocument>(store.Load("game", "env"));
        Assert.Equal("v2", restored.ProfileVersion);
        var state = Assert.Single(restored.States);
        Assert.Equal(2, state.Anchors.Count);
        var action = Assert.Single(state.Affordances);
        Assert.Equal(-3, action.VerticalScrollSteps);
        Assert.Equal(0, action.HorizontalScrollSteps);
    }

    private static LearnedSceneProfileDocument Document(string version) => new(
        "0.3.0", "profile", version, "game", "env", "game", null, 500, 0.04,
        [new LearnedStateSceneSignature(
            "state", "signature:v1",
            [
                new LearnedSceneAnchor("A", [0.1, 0.1, 0.1, 0.1], "e1"),
                new LearnedSceneAnchor("B", [0.8, 0.8, 0.1, 0.1], "e2"),
            ],
            [new LearnedAffordanceSignature(
                "scroll-action", "locator:v1", "一覧", [0.2, 0.2, 0.5, 0.5], ["scroll"], ["e3"],
                VerticalScrollSteps: -3,
                HorizontalScrollSteps: 0)],
            ["e1", "e2", "e3"])], ["e1", "e2", "e3"]);
}
