using System.IO;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class UiSnapshotTests
{
    [Fact]
    public void Every_screen_is_drawn_to_a_png_without_opening_a_window()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ui-snapshot-{Guid.NewGuid():N}");
        try
        {
            Assert.Equal(0, UiSnapshot.Run(["--out", directory]));

            string[] expected =
            [
                "input-studio-g13.png", "input-studio-g600.png", "input-studio-no-selection.png", "input-studio-lcd-and-light.png", "key-capture.png",
                "game-operator-bot.png", "game-operator-macro.png", "game-operator-recording.png", "game-operator-research.png",
                "game-operator-remoteview.png", "game-operator-remoteview-streaming.png",
            ];
            Assert.Equal(expected.Order(), Directory.GetFiles(directory).Select(Path.GetFileName).Order());
            Assert.All(Directory.GetFiles(directory), path => Assert.True(new FileInfo(path).Length > 10_000, path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Output_folder_is_required()
    {
        Assert.Equal(1, UiSnapshot.Run([]));
    }
}
