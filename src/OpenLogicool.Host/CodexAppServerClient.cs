using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OpenLogicool.Host;

public sealed record CodexDynamicToolDefinition(
    string Name,
    string Description,
    JsonElement InputSchema);

public sealed record CodexDynamicToolOutput(
    bool Success,
    string Text,
    string? ImageDataUrl = null);

public interface ICodexDynamicToolHandler
{
    IReadOnlyList<CodexDynamicToolDefinition> Definitions { get; }
    ValueTask<CodexDynamicToolOutput> ExecuteAsync(
        string tool,
        JsonElement arguments,
        CancellationToken cancellationToken = default);
}

public interface ICodexAppServerTransport : IAsyncDisposable
{
    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default);
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default);
}

public sealed record CodexAppServerRunResult(
    string ThreadId,
    string TurnId,
    string Status,
    string FinalText,
    int ToolCallCount);

public sealed class CodexAppServerClient(
    Func<string, ICodexAppServerTransport> transportFactory,
    ICodexDynamicToolHandler tools)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<CodexAppServerRunResult> RunAsync(
        GameAgentWorkspace workspace,
        GameAgentSessionDocument session,
        string goal,
        string developerInstructions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        await using var transport = transportFactory(workspace.ResolvedPath);
        await SendAsync(transport, new
        {
            id = 1,
            method = "initialize",
            @params = new
            {
                clientInfo = new { name = "openlogicool", version = "1.0" },
                capabilities = new { experimentalApi = true },
            },
        }, cancellationToken).ConfigureAwait(false);
        _ = await ReadResponseAsync(transport, 1, cancellationToken).ConfigureAwait(false);
        await SendAsync(transport, new { method = "initialized", @params = new { } }, cancellationToken).ConfigureAwait(false);

        var threadId = session.ThreadId;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await SendAsync(transport, new
            {
                id = 2,
                method = "thread/start",
                @params = new
                {
                    cwd = workspace.ResolvedPath,
                    ephemeral = false,
                    approvalPolicy = "never",
                    sandbox = "read-only",
                    baseInstructions = File.ReadAllText(workspace.AgentsPath),
                    developerInstructions,
                    dynamicTools = tools.Definitions.Select(definition => new
                    {
                        type = "function",
                        name = definition.Name,
                        description = definition.Description,
                        inputSchema = definition.InputSchema,
                    }).ToArray(),
                    threadSource = "openlogicool",
                },
            }, cancellationToken).ConfigureAwait(false);
            var started = await ReadResponseAsync(transport, 2, cancellationToken).ConfigureAwait(false);
            threadId = started.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()
                ?? throw new InvalidDataException("Codex thread/start responseにthread idがありません。");
        }
        else
        {
            await SendAsync(transport, new
            {
                id = 2,
                method = "thread/resume",
                @params = new
                {
                    threadId,
                    cwd = workspace.ResolvedPath,
                    approvalPolicy = "never",
                    sandbox = "read-only",
                    baseInstructions = File.ReadAllText(workspace.AgentsPath),
                    developerInstructions,
                },
            }, cancellationToken).ConfigureAwait(false);
            _ = await ReadResponseAsync(transport, 2, cancellationToken).ConfigureAwait(false);
        }

        await SendAsync(transport, new
        {
            id = 3,
            method = "turn/start",
            @params = new
            {
                threadId,
                input = new[] { new { type = "text", text = goal } },
                cwd = workspace.ResolvedPath,
            },
        }, cancellationToken).ConfigureAwait(false);
        var turnStarted = await ReadResponseAsync(transport, 3, cancellationToken).ConfigureAwait(false);
        var turnId = turnStarted.GetProperty("result").GetProperty("turn").GetProperty("id").GetString()
            ?? throw new InvalidDataException("Codex turn/start responseにturn idがありません。");
        var toolCalls = 0;
        while (true)
        {
            var line = await transport.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new EndOfStreamException("Codex app-serverがturn完了前に終了しました。");
            using var message = JsonDocument.Parse(line);
            var root = message.RootElement;
            if (root.TryGetProperty("method", out var methodElement)
                && methodElement.GetString() == "item/tool/call"
                && root.TryGetProperty("id", out var requestId)
                && root.TryGetProperty("params", out var call))
            {
                toolCalls++;
                var output = await tools.ExecuteAsync(
                    call.GetProperty("tool").GetString() ?? string.Empty,
                    call.GetProperty("arguments"),
                    cancellationToken).ConfigureAwait(false);
                var contentItems = new List<object> { new { type = "inputText", text = output.Text } };
                if (!string.IsNullOrWhiteSpace(output.ImageDataUrl))
                    contentItems.Add(new { type = "inputImage", imageUrl = output.ImageDataUrl });
                await SendRawAsync(transport, JsonSerializer.Serialize(new
                {
                    id = requestId.Clone(),
                    result = new { contentItems, success = output.Success },
                }, Json), cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (root.TryGetProperty("method", out methodElement)
                && methodElement.GetString() == "turn/completed")
            {
                var turn = root.GetProperty("params").GetProperty("turn");
                var status = turn.GetProperty("status").GetString() ?? "unknown";
                var finalText = turn.GetProperty("items").EnumerateArray()
                    .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "agentMessage")
                    .Select(item => item.GetProperty("text").GetString())
                    .LastOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty;
                return new CodexAppServerRunResult(threadId, turnId, status, finalText, toolCalls);
            }
        }
    }

    private static async ValueTask SendAsync(
        ICodexAppServerTransport transport,
        object value,
        CancellationToken cancellationToken) =>
        await SendRawAsync(transport, JsonSerializer.Serialize(value, Json), cancellationToken).ConfigureAwait(false);

    private static ValueTask SendRawAsync(
        ICodexAppServerTransport transport,
        string json,
        CancellationToken cancellationToken) => transport.WriteLineAsync(json, cancellationToken);

    private static async Task<JsonElement> ReadResponseAsync(
        ICodexAppServerTransport transport,
        int expectedId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await transport.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new EndOfStreamException("Codex app-server response前にstreamが終了しました。");
            using var message = JsonDocument.Parse(line);
            var root = message.RootElement;
            if (!root.TryGetProperty("id", out var id) || !id.TryGetInt32(out var value) || value != expectedId)
                continue;
            if (root.TryGetProperty("error", out var error))
                throw new InvalidOperationException($"Codex app-server error: {error.GetProperty("message").GetString()}");
            return root.Clone();
        }
    }
}

/// <summary>PowerShell 7と公式codex.ps1 wrapperだけを使うWindows App Server transport。</summary>
public sealed class WindowsCodexAppServerTransport : ICodexAppServerTransport
{
    private readonly Process process;
    private readonly Task<string> stderr;

    private WindowsCodexAppServerTransport(Process process)
    {
        this.process = process;
        stderr = process.StandardError.ReadToEndAsync();
    }

    public static WindowsCodexAppServerTransport Start(string workingDirectory)
    {
        var wrapper = ResolveCodexWrapper();
        var start = CreateStartInfo(wrapper, workingDirectory);
        var process = Process.Start(start)
            ?? throw new InvalidOperationException("Codex App Serverを開始できませんでした。");
        return new WindowsCodexAppServerTransport(process);
    }

    internal static ProcessStartInfo CreateStartInfo(string wrapper, string workingDirectory)
    {
        var start = new ProcessStartInfo
        {
            FileName = "pwsh.exe",
            WorkingDirectory = Path.GetFullPath(workingDirectory),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-File", wrapper, "app-server", "--stdio" })
            start.ArgumentList.Add(argument);
        start.Environment.Remove("OPENAI_API_KEY");
        return start;
    }

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
        await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        process.StandardInput.Close();
        if (!process.WaitForExit(3_000)) process.Kill(entireProcessTree: true);
        _ = await stderr.ConfigureAwait(false);
        process.Dispose();
    }

    internal static string ResolveCodexWrapper()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), "codex.ps1");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException("公式Codex PowerShell wrapperをPATHから解決できません。");
    }
}
