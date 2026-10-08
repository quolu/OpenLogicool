using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace OpenLogicool.Host;

internal sealed record ControlRequest(string Action, string? Operation = null, JsonElement? Arguments = null,
    string? JobId = null, int Version = 1, string? ExpectedDatabasePath = null);
internal sealed record ControlResponse(bool Success, JsonElement? Value = null, ControlFault? Error = null, int Version = 1);

internal sealed class ApplicationControlPipe : IDisposable
{
    public static string DefaultName => "OpenLogicool.Control.v1." + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User!.Value)))[..20];
    private readonly ControlOperationRegistry registry;
    private readonly ControlJobs jobs;
    private readonly string name;
    private readonly string? databasePath;
    private readonly CancellationTokenSource stop = new();
    private readonly Task listener;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Task, byte> connections = new();
    private readonly SingleInstanceGuard? guard;

    public ApplicationControlPipe(ControlOperationRegistry registry, ControlJobs jobs, string? name = null, string? databasePath = null)
    {
        this.registry = registry; this.jobs = jobs; this.name = name ?? DefaultName;
        this.databasePath = databasePath;
        if (name is null)
        {
            guard = new SingleInstanceGuard(@"Local\OpenLogicool.ApplicationControl");
            if (!guard.IsOwner) { guard.Dispose(); throw new InvalidOperationException("アプリの操作APIは既に起動しています。"); }
        }
        var first = Create();
        listener = Listen(first);
    }

    private NamedPipeServerStream Create() => new(name, PipeDirection.InOut, 16,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task Listen(NamedPipeServerStream next)
    {
        try
        {
            while (true)
            {
                await next.WaitForConnectionAsync(stop.Token);
                var connected = next;
                var task = Handle(connected);
                connections.TryAdd(task, 0);
                _ = task.ContinueWith(completed =>
                {
                    connections.TryRemove(completed, out _);
                    if (completed.Exception is { } error) Console.Error.WriteLine("操作APIの接続処理に失敗しました: " + error.GetBaseException().Message);
                }, TaskContinuationOptions.ExecuteSynchronously);
                next = Create();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { next.Dispose(); }
    }

    private async Task Handle(NamedPipeServerStream stream)
    {
        using (stream)
        using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), leaveOpen: true))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
        {
            ControlJobSnapshot? deliveredJob = null;
            ControlResponse response;
            try
            {
                var line = await reader.ReadLineAsync(stop.Token) ?? throw new InvalidDataException("操作要求が空です。");
                var request = JsonSerializer.Deserialize<ControlRequest>(line, ControlOperationRegistry.Json)
                    ?? throw new InvalidDataException("操作要求が空です。");
                if (request.Version != 1) throw new NotSupportedException("操作APIのversionが一致しません。");
                if (request.ExpectedDatabasePath is not null && !string.Equals(request.ExpectedDatabasePath, databasePath, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("指定したDBと起動中アプリのDBが異なります。app.statusで確認してください。");
                object value = request.Action switch
                {
                    "operations" => registry.List(),
                    "invoke" => deliveredJob = jobs.Start(request.Operation ?? throw new ArgumentException("operationが必要です。"),
                        request.Arguments ?? JsonSerializer.SerializeToElement(new { })),
                    "job" => deliveredJob = jobs.Get(request.JobId ?? throw new ArgumentException("jobIdが必要です。")),
                    "jobs" => jobs.List(),
                    "cancel" => deliveredJob = jobs.Cancel(request.JobId ?? throw new ArgumentException("jobIdが必要です。")),
                    _ => throw new ArgumentException($"要求が未対応です: {request.Action}")
                };
                response = new(true, JsonSerializer.SerializeToElement(value, ControlOperationRegistry.Json));
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception error)
            { response = new(false, Error: new("request-failed", error.Message)); }
            try
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, ControlOperationRegistry.Json).AsMemory(), stop.Token);
                if (deliveredJob is not null) jobs.Replied(deliveredJob);
            }
            catch (IOException error) { Console.Error.WriteLine("操作APIの接続が閉じました: " + error.Message); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.GetAwaiter().GetResult();
        Task.WhenAll(connections.Keys).GetAwaiter().GetResult();
        stop.Dispose();
        guard?.Dispose();
    }

    public static async Task<ControlResponse> Send(ControlRequest request, string? name = null, CancellationToken token = default)
    {
        using var pipe = new NamedPipeClientStream(".", name ?? DefaultName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(1500, token);
        using var reader = new StreamReader(pipe, new UTF8Encoding(false, true), leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, ControlOperationRegistry.Json).AsMemory(), token);
        var line = await reader.ReadLineAsync(token) ?? throw new IOException("操作APIが応答前に終了しました。");
        return JsonSerializer.Deserialize<ControlResponse>(line, ControlOperationRegistry.Json)
            ?? throw new InvalidDataException("操作APIの応答が空です。");
    }
}
