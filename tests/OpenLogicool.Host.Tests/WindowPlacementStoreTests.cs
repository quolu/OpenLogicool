using System.IO;
using OpenLogicool.Desktop;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class WindowPlacementStoreTests
{
    [Fact]
    public void Placements_are_kept_per_window_next_to_the_database_and_survive_reopen()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"window-placement-{Guid.NewGuid():N}");
        try
        {
            var databasePath = Path.Combine(directory, "input-studio.db");
            var store = WindowPlacementStore.ForDatabase(databasePath);
            Assert.Null(store.Load("input-studio"));

            store.Save("input-studio", new WindowPlacement(120, 80, 1480, 920, false));
            store.Save("game-operator", new WindowPlacement(-1800, 40, -820, 800, true));
            store.Save("input-studio", new WindowPlacement(200, 100, 1560, 940, false));

            Assert.True(File.Exists(Path.Combine(directory, "input-studio.db.window-placement.json")));
            var reopened = WindowPlacementStore.ForDatabase(databasePath);
            Assert.Equal(new WindowPlacement(200, 100, 1560, 940, false), reopened.Load("input-studio"));
            Assert.Equal(new WindowPlacement(-1800, 40, -820, 800, true), reopened.Load("game-operator"));
            Assert.Null(reopened.Load("diagnostics"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Unknown_schema_version_is_rejected_instead_of_being_ignored()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"window-placement-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "input-studio.db.window-placement.json"),
                """{"SchemaVersion":"9.9","Windows":{}}""");
            var store = WindowPlacementStore.ForDatabase(Path.Combine(directory, "input-studio.db"));

            Assert.Throws<InvalidDataException>(() => store.Load("input-studio"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
