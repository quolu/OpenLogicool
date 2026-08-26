using System.Text.Json;
using System.IO;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class GameAgentWorkspaceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"openlogicool-agent-{Guid.NewGuid():N}");

    [Fact]
    public void Reference_is_stable_relative_and_game_specific()
    {
        var first = GameAgentWorkspaceLayout.CreateReference("NIKKE");
        var same = GameAgentWorkspaceLayout.CreateReference("nikke");
        var other = GameAgentWorkspaceLayout.CreateReference("other-game");

        Assert.Equal(first, same);
        Assert.NotEqual(first.ProfileId, other.ProfileId);
        Assert.False(Path.IsPathRooted(first.RelativePath));
        Assert.DoesNotContain('\\', first.RelativePath);
        Assert.DoesNotContain("..", first.RelativePath, StringComparison.Ordinal);
    }

    [Fact]
    public void App_creates_workspace_without_persisting_resolved_absolute_root()
    {
        var userRoot = Path.Combine(root, "user");
        var installRoot = Path.Combine(root, "install");
        var manager = new WindowsGameAgentWorkspaceManager(userRoot, installRoot);

        var workspace = manager.Ensure("nikke");
        manager.SaveSession(workspace, "thread-1");

        Assert.StartsWith(Path.GetFullPath(userRoot), workspace.ResolvedPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(workspace.AgentsPath));
        Assert.True(File.Exists(workspace.ManifestPath));
        Assert.True(File.Exists(workspace.SessionPath));
        Assert.Contains("fixed game profile `nikke`", File.ReadAllText(workspace.AgentsPath), StringComparison.Ordinal);
        var manifest = File.ReadAllText(workspace.ManifestPath);
        Assert.DoesNotContain(Path.GetFullPath(userRoot), manifest, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("thread-1", manager.LoadSession(workspace).ThreadId);
        using var document = JsonDocument.Parse(manifest);
        Assert.Equal("game-agents/", document.RootElement.GetProperty("RelativePath").GetString()![..12]);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
