using System.IO;
using OpenLogicool.Launcher;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class GuiHostLaunchCommandTests
{
    [Fact]
    public void Default_launch_starts_the_resident_ui_without_a_console_window()
    {
        using var directory = new TemporaryDirectory();
        var hostPath = Path.Combine(directory.Path, "OpenLogicool.Host.exe");
        File.WriteAllText(hostPath, string.Empty);

        var command = GuiHostLaunchCommand.Create(directory.Path, []);

        Assert.Equal(hostPath, command.FileName);
        Assert.Equal(directory.Path, command.WorkingDirectory);
        Assert.False(command.UseShellExecute);
        Assert.True(command.CreateNoWindow);
        Assert.Equal(["ui", "--resident"], command.ArgumentList);
    }

    [Fact]
    public void Development_launch_accepts_an_explicit_host_path_without_using_a_shell()
    {
        using var directory = new TemporaryDirectory();
        var hostPath = Path.Combine(directory.Path, "host output", "OpenLogicool.Host.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(hostPath)!);
        File.WriteAllText(hostPath, string.Empty);

        var command = GuiHostLaunchCommand.Create(
            directory.Path,
            ["--host", hostPath, "ui", "--resident"]);

        Assert.Equal(hostPath, command.FileName);
        Assert.False(command.UseShellExecute);
        Assert.True(command.CreateNoWindow);
        Assert.Equal(["ui", "--resident"], command.ArgumentList);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"openlogicool-launcher-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
