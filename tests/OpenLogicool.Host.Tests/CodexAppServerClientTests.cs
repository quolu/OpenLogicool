using System.IO;
using System.Text.Json;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class CodexAppServerClientTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"openlogicool-codex-{Guid.NewGuid():N}");

    [Fact]
    public async Task Starts_subscription_thread_with_workspace_instructions_and_handles_dynamic_tool_call()
    {
        var workspace = new WindowsGameAgentWorkspaceManager(
            Path.Combine(root, "user"),
            Path.Combine(root, "install")).Ensure("nikke");
        var transport = new Transport([
            "{\"id\":1,\"result\":{}}",
            "{\"id\":2,\"result\":{\"thread\":{\"id\":\"thread-1\"}}}",
            "{\"id\":3,\"result\":{\"turn\":{\"id\":\"turn-1\"}}}",
            "{\"id\":\"request-1\",\"method\":\"item/tool/call\",\"params\":{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\",\"callId\":\"call-1\",\"tool\":\"observe\",\"arguments\":{}}}",
            "{\"method\":\"turn/completed\",\"params\":{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":\"completed\",\"items\":[{\"id\":\"message-1\",\"type\":\"agentMessage\",\"text\":\"{\\\"status\\\":\\\"completed\\\"}\"}]}}}",
        ]);
        var tools = new Tools();
        var client = new CodexAppServerClient(_ => transport, tools);

        var result = await client.RunAsync(
            workspace,
            new GameAgentSessionDocument(GameAgentSessionDocument.CurrentSchemaVersion, null),
            "日課情報を取得する",
            "禁止操作を実行しない。");

        Assert.Equal("thread-1", result.ThreadId);
        Assert.Equal("turn-1", result.TurnId);
        Assert.Equal("completed", result.Status);
        Assert.Equal(1, result.ToolCallCount);
        Assert.Equal("observe", Assert.Single(tools.Calls));
        var messages = transport.Writes.Select(Parse).ToArray();
        var threadStart = messages.Single(message => Method(message) == "thread/start");
        Assert.Equal(workspace.ResolvedPath, threadStart.GetProperty("params").GetProperty("cwd").GetString());
        Assert.Equal("read-only", threadStart.GetProperty("params").GetProperty("sandbox").GetString());
        Assert.Contains("fixed game profile `nikke`", threadStart.GetProperty("params").GetProperty("baseInstructions").GetString(), StringComparison.Ordinal);
        Assert.Equal("observe", threadStart.GetProperty("params").GetProperty("dynamicTools")[0].GetProperty("name").GetString());
        var toolResponse = messages.Single(message => message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String);
        Assert.True(toolResponse.GetProperty("result").GetProperty("success").GetBoolean());
        Assert.Equal(2, toolResponse.GetProperty("result").GetProperty("contentItems").GetArrayLength());
    }

    [Fact]
    public async Task Existing_game_session_resumes_the_same_thread()
    {
        var workspace = new WindowsGameAgentWorkspaceManager(
            Path.Combine(root, "user"),
            Path.Combine(root, "install")).Ensure("nikke");
        var transport = new Transport([
            "{\"id\":1,\"result\":{}}",
            "{\"id\":2,\"result\":{\"thread\":{\"id\":\"thread-existing\"}}}",
            "{\"id\":3,\"result\":{\"turn\":{\"id\":\"turn-2\"}}}",
            "{\"method\":\"turn/completed\",\"params\":{\"threadId\":\"thread-existing\",\"turn\":{\"id\":\"turn-2\",\"status\":\"completed\",\"items\":[{\"id\":\"message-2\",\"type\":\"agentMessage\",\"text\":\"done\"}]}}}",
        ]);
        var client = new CodexAppServerClient(_ => transport, new Tools());

        var result = await client.RunAsync(
            workspace,
            new GameAgentSessionDocument(GameAgentSessionDocument.CurrentSchemaVersion, "thread-existing"),
            "goal",
            "policy");

        Assert.Equal("thread-existing", result.ThreadId);
        Assert.Contains(transport.Writes, value => Method(Parse(value)) == "thread/resume");
        Assert.DoesNotContain(transport.Writes, value => Method(Parse(value)) == "thread/start");
    }

    [Fact]
    public void Windows_transport_removes_api_key_from_subscription_child()
    {
        var start = WindowsCodexAppServerTransport.CreateStartInfo("codex.ps1", root);

        Assert.False(start.Environment.ContainsKey("OPENAI_API_KEY"));
        Assert.Equal("pwsh.exe", start.FileName);
        Assert.Contains("app-server", start.ArgumentList);
        Assert.Contains("--stdio", start.ArgumentList);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string? Method(JsonElement value) =>
        value.TryGetProperty("method", out var method) ? method.GetString() : null;

    private sealed class Transport(IEnumerable<string> reads) : ICodexAppServerTransport
    {
        private readonly Queue<string> reads = new(reads);
        public List<string> Writes { get; } = [];
        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            Writes.Add(line);
            return ValueTask.CompletedTask;
        }
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(reads.Count == 0 ? null : reads.Dequeue());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Tools : ICodexDynamicToolHandler
    {
        public List<string> Calls { get; } = [];
        public IReadOnlyList<CodexDynamicToolDefinition> Definitions { get; } =
        [
            new CodexDynamicToolDefinition(
                "observe",
                "Observe current game page.",
                Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}")),
        ];
        public ValueTask<CodexDynamicToolOutput> ExecuteAsync(
            string tool,
            JsonElement arguments,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(tool);
            return ValueTask.FromResult(new CodexDynamicToolOutput(
                true,
                "{\"page\":\"title\"}",
                "data:image/png;base64,AQ=="));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
