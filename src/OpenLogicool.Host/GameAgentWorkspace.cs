using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace OpenLogicool.Host;

public enum GameAgentWorkspaceRootKind
{
    UserData,
    Install,
}

public sealed record GameAgentWorkspaceReference(
    string SchemaVersion,
    string ProfileId,
    GameAgentWorkspaceRootKind RootKind,
    string RelativePath)
{
    public const string CurrentSchemaVersion = "1.0.0";
}

public sealed record GameAgentSessionDocument(
    string SchemaVersion,
    string? ThreadId)
{
    public const string CurrentSchemaVersion = "1.0.0";
}

public sealed record GameAgentWorkspace(
    GameAgentWorkspaceReference Reference,
    string ResolvedPath,
    string AgentsPath,
    string ManifestPath,
    string SessionPath);

public interface IGameAgentWorkspaceManager
{
    GameAgentWorkspace Ensure(string processName);
    GameAgentSessionDocument LoadSession(GameAgentWorkspace workspace);
    void SaveSession(GameAgentWorkspace workspace, string threadId);
}

public static class GameAgentWorkspaceLayout
{
    public static GameAgentWorkspaceReference CreateReference(
        string processName,
        GameAgentWorkspaceRootKind rootKind = GameAgentWorkspaceRootKind.UserData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        var normalized = processName.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var readable = new string(normalized
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(40)
            .ToArray());
        if (readable.Length == 0) readable = "game";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant()[..12];
        var profileId = $"{readable}-{digest}";
        return new GameAgentWorkspaceReference(
            GameAgentWorkspaceReference.CurrentSchemaVersion,
            profileId,
            rootKind,
            $"game-agents/{profileId}");
    }

    public static void Validate(GameAgentWorkspaceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.SchemaVersion != GameAgentWorkspaceReference.CurrentSchemaVersion
            || string.IsNullOrWhiteSpace(reference.ProfileId)
            || string.IsNullOrWhiteSpace(reference.RelativePath)
            || Path.IsPathRooted(reference.RelativePath)
            || reference.RelativePath.Contains('\\')
            || reference.RelativePath.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException("game agent workspace referenceが相対path契約を満たしません。");
        }
    }
}

/// <summary>Windows user/install rootの解決とgame別Codex workspace生成だけを所有するadapter。</summary>
public sealed class WindowsGameAgentWorkspaceManager(
    string userDataRoot,
    string installRoot,
    GameAgentWorkspaceRootKind rootKind = GameAgentWorkspaceRootKind.UserData) : IGameAgentWorkspaceManager
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string userDataRoot = Path.GetFullPath(userDataRoot);
    private readonly string installRoot = Path.GetFullPath(installRoot);

    public GameAgentWorkspace Ensure(string processName)
    {
        var reference = GameAgentWorkspaceLayout.CreateReference(processName, rootKind);
        GameAgentWorkspaceLayout.Validate(reference);
        var root = reference.RootKind == GameAgentWorkspaceRootKind.UserData ? userDataRoot : installRoot;
        var resolved = Path.GetFullPath(Path.Combine(root, reference.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("game agent workspaceが選択rootの外です。");
        Directory.CreateDirectory(resolved);
        Directory.CreateDirectory(Path.Combine(resolved, "runs"));
        var workspace = new GameAgentWorkspace(
            reference,
            resolved,
            Path.Combine(resolved, "AGENTS.md"),
            Path.Combine(resolved, "workspace.json"),
            Path.Combine(resolved, "session.json"));
        File.WriteAllText(workspace.AgentsPath, CodexGameSystemPrompt.Build(processName));
        File.WriteAllText(workspace.ManifestPath, JsonSerializer.Serialize(reference, Json));
        if (!File.Exists(workspace.SessionPath))
        {
            File.WriteAllText(workspace.SessionPath, JsonSerializer.Serialize(
                new GameAgentSessionDocument(GameAgentSessionDocument.CurrentSchemaVersion, null), Json));
        }
        return workspace;
    }

    public GameAgentSessionDocument LoadSession(GameAgentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var session = JsonSerializer.Deserialize<GameAgentSessionDocument>(
            File.ReadAllText(workspace.SessionPath), Json)
            ?? throw new InvalidDataException("game agent sessionが空です。");
        if (session.SchemaVersion != GameAgentSessionDocument.CurrentSchemaVersion)
            throw new InvalidDataException("game agent session schemaが未対応です。");
        return session;
    }

    public void SaveSession(GameAgentWorkspace workspace, string threadId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        File.WriteAllText(workspace.SessionPath, JsonSerializer.Serialize(
            new GameAgentSessionDocument(GameAgentSessionDocument.CurrentSchemaVersion, threadId), Json));
    }
}

public static class CodexGameSystemPrompt
{
    public static string Build(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        return $"""
            # OpenLogicool Game Goal Operator

            You operate the fixed game profile `{processName}` for one user goal.
            Use only the OpenLogicool dynamic tools supplied by the host. Do not use shell, file editing, web, Computer Use, SendInput, direct database access, or any other input path.

            - Observe the current page before every action.
            - Use a usable saved action before proposing a new action.
            - If the user goal is already complete on the current page, call finish immediately. Goal completion takes precedence over any remaining saved action; do not execute a saved tail after all requested facts are collected.
            - Execute exactly one action, then observe the host's 10-second comparison result.
            - Treat Moved as progress even when a destination identifier differs.
            - Treat Stayed and Undetermined as learning results. Repair only the failed step and preserve every normal step and existing route revision.
            - Dismiss blocking in-game overlays, recover from unrelated pages with the in-game back action, and scroll information lists until no new relevant facts appear.
            - Never perform operations prohibited by the explicit Game Policy or the user goal.
            - Finish only after the requested result is collected. Return a concise result containing the collected facts.
            """;
    }
}
