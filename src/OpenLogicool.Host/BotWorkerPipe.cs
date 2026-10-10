using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

internal sealed record BotWorkerRequest(string Operation, int Protocol = BotWorkerPipe.Protocol,
    string? Dpi = null, string[]? Keys = null, SerialHidCursorPoint Target = default,
    SerialHidCursorPoint Destination = default, int VerticalSteps = 0, int HorizontalSteps = 0,
    JsonElement? Event = null);
internal sealed record BotWorkerResponse(bool Ok, string? Receipt = null, string? Kind = null,
    string? Message = null, SerialHidCandidate? Nano = null, BotWorkerPhysical? Physical = null, string? Mode = null);
internal sealed record BotWorkerPhysical(long Edges, int Held);

internal static class BotWorkerPipe
{
    public const int Protocol = 1;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static StreamReader Reader(Stream stream) => new(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token) =>
        await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json) + "\n"), token);
    public static async Task<T> ReadAsync<T>(StreamReader reader, CancellationToken token) =>
        JsonSerializer.Deserialize<T>(await reader.ReadLineAsync(token)
            ?? throw new EndOfStreamException("Botとのパイプが切断されました。"), Json)
            ?? throw new InvalidDataException("Botとの通信内容が空です。");
}

/// <summary>有限入力は本体で完結する。停止は受付だけを閉じ、装置の操作を中断しない。</summary>
internal sealed class BotWorkerPipeServer : IAsyncDisposable
{
    private readonly NamedPipeServerStream input;
    private readonly NamedPipeServerStream query;
    private readonly INanoGameInputDevice device;
    private readonly SerialHidCandidate nano;
    private readonly string dpi;
    private readonly Func<ResidentPhysicalInput?>? physical;
    private readonly Func<string?> mode;
    private readonly Action<JsonElement> report;
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stop = new();
    private bool accepting = true;
    public string InputName { get; } = "OpenLogicool.Bot.Input." + Guid.NewGuid().ToString("N");
    public string QueryName { get; } = "OpenLogicool.Bot.Query." + Guid.NewGuid().ToString("N");
    public Task Completion { get; }
    public Task Stopped => stopped.Task;
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Exception? Fault { get; private set; }

    public BotWorkerPipeServer(INanoGameInputDevice device, SerialHidCandidate nano, string dpi,
        Func<ResidentPhysicalInput?>? physical, Func<string?> mode, Action<JsonElement> report)
    {
        this.device = device; this.nano = nano; this.dpi = dpi;
        this.physical = physical; this.mode = mode; this.report = report;
        input = Create(InputName); query = Create(QueryName);
        Completion = Task.WhenAll(HandleAsync(input, true), HandleAsync(query, false));
    }

    private static NamedPipeServerStream Create(string name) => new(name, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    public void Stop()
    {
        lock (gate) accepting = false;
        stop.Cancel();
        stopped.TrySetResult();
    }

    private async Task HandleAsync(NamedPipeServerStream stream, bool isInput)
    {
        StreamReader? reader = null;
        Task<BotWorkerRequest?>? next = null;
        try
        {
            await stream.WaitForConnectionAsync(stop.Token);
            reader = BotWorkerPipe.Reader(stream);
            var hello = await BotWorkerPipe.ReadAsync<BotWorkerRequest>(reader, stop.Token);
            if (hello.Operation != "hello" || hello.Protocol != BotWorkerPipe.Protocol || hello.Dpi != dpi)
            {
                var message = $"BotのprotocolまたはDPIが一致しません（protocol={hello.Protocol}, DPI={hello.Dpi} / 本体={BotWorkerPipe.Protocol}, {dpi}）。";
                await BotWorkerPipe.WriteAsync(stream, new BotWorkerResponse(false, Kind: "fault", Message: message), stop.Token);
                throw new InvalidDataException(message);
            }
            await BotWorkerPipe.WriteAsync(stream, new BotWorkerResponse(true, Nano: nano), stop.Token);
            next = ReceiveAsync(reader);
            while (await next is { } request)
            {
                Task<BotWorkerResponse> pending;
                lock (gate)
                {
                    if (!accepting) return;
                    // 受付の確定だけを囲む。装置・JSON・待機はこのlockの外で動く。
                    pending = Task.Run(() => Dispatch(request, isInput));
                }
                // 入力中の片側切断も検知する。先に届いた次の依頼は、現在の入力と応答が終わるまで受け付けない。
                next = ReceiveAsync(reader);
                var response = await pending;
                if (request.Operation != "event") await BotWorkerPipe.WriteAsync(stream, response, stop.Token);
                if (!response.Ok && response.Kind == "fault") throw new IOException(response.Message);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (EndOfStreamException) { }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException)
        {
            lock (gate) Fault ??= error;
        }
        finally
        {
            Stop();
            if (next is not null) await next;
            reader?.Dispose();
        }
    }

    private async Task<BotWorkerRequest?> ReceiveAsync(StreamReader reader)
    {
        try { return await BotWorkerPipe.ReadAsync<BotWorkerRequest>(reader, stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return null; }
        catch (EndOfStreamException) { }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException)
        {
            lock (gate) Fault ??= error;
        }
        Stop();
        return null;
    }

    private BotWorkerResponse Dispatch(BotWorkerRequest request, bool isInput)
    {
        try
        {
            if (isInput)
            {
                var receipt = request.Operation switch
                {
                    "keyTap" => device.KeyTap(request.Keys ?? throw new InvalidDataException("キーの指定がありません。")),
                    "hover" => device.Hover(request.Target),
                    "click" => device.Click(request.Target),
                    "scroll" => device.Scroll(request.Target, request.VerticalSteps, request.HorizontalSteps),
                    "drag" => device.Drag(request.Target, request.Destination),
                    "flick" => device.Flick(request.Target, request.Destination),
                    _ => throw new InvalidDataException($"未知のBot入力です: {request.Operation}"),
                };
                return new(true, Receipt: receipt);
            }
            switch (request.Operation)
            {
                case "physical":
                    var reading = physical?.Invoke();
                    return new(true, Physical: reading is { } current ? new(current.EdgeCount, current.HeldCount) : null);
                case "mode": return new(true, Mode: mode());
                case "event": report(request.Event ?? throw new InvalidDataException("イベントが空です。")); return new(true);
                default: throw new InvalidDataException($"未知のBot問い合わせです: {request.Operation}");
            }
        }
        catch (SerialHidPointerMoveException error) { return new(false, Kind: "pointer-unmoved", Message: error.Message); }
        catch (Exception error)
        {
            // process境界で装置の失敗を明示する。入力を再試行しない。
            return new(false, Kind: "fault", Message: error.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        try { await Completion; }
        finally { input.Dispose(); query.Dispose(); stop.Dispose(); }
    }
}

/// <summary>同期の装置の口を保ち、入力1回につきパイプの往復を1回だけ行う。</summary>
internal sealed class BotWorkerPipeClient : INanoGameInputDevice, IDisposable
{
    private readonly NamedPipeClientStream input;
    private readonly NamedPipeClientStream query;
    private readonly StreamReader inputReader;
    private readonly StreamReader queryReader;
    private readonly SemaphoreSlim inputGate = new(1, 1);
    private readonly SemaphoreSlim queryGate = new(1, 1);
    private readonly CancellationTokenSource stop;
    public SerialHidCandidate Nano { get; private set; } = null!;
    public Exception? Fault { get; private set; }
    public CancellationToken Token => stop.Token;

    private BotWorkerPipeClient(string inputName, string queryName, CancellationToken token)
    {
        input = Create(inputName); query = Create(queryName);
        inputReader = BotWorkerPipe.Reader(input); queryReader = BotWorkerPipe.Reader(query);
        stop = CancellationTokenSource.CreateLinkedTokenSource(token);
    }

    private static NamedPipeClientStream Create(string name) => new(".", name, PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    public static async Task<BotWorkerPipeClient> ConnectAsync(string inputName, string queryName, string dpi,
        CancellationToken token, int protocol = BotWorkerPipe.Protocol)
    {
        var client = new BotWorkerPipeClient(inputName, queryName, token);
        try
        {
            await Task.WhenAll(client.input.ConnectAsync(client.Token), client.query.ConnectAsync(client.Token));
            var hello = new BotWorkerRequest("hello", protocol, dpi);
            client.Nano = (await client.ExchangeAsync(hello, true)).Nano
                ?? throw new InvalidDataException("本体からNanoの識別が届きませんでした。");
            await client.ExchangeAsync(hello, false);
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private async Task<BotWorkerResponse> ExchangeAsync(BotWorkerRequest request, bool isInput)
    {
        var gate = isInput ? inputGate : queryGate;
        await gate.WaitAsync(Token);
        try
        {
            await BotWorkerPipe.WriteAsync(isInput ? input : query, request, Token);
            if (request.Operation == "event") return new(true);
            var response = await BotWorkerPipe.ReadAsync<BotWorkerResponse>(isInput ? inputReader : queryReader, Token);
            if (!response.Ok)
            {
                if (response.Kind == "pointer-unmoved") throw new SerialHidPointerMoveException(response.Message!);
                throw new IOException(response.Message ?? "本体のBot入力が失敗しました。");
            }
            return response;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException)
        {
            Fault ??= error;
            stop.Cancel();
            throw;
        }
        finally { gate.Release(); }
    }

    private string Input(BotWorkerRequest request) => ExchangeAsync(request, true).GetAwaiter().GetResult().Receipt
        ?? throw new InvalidDataException("入力の受領結果がありません。");
    public string KeyTap(IReadOnlyList<string> keys) => Input(new("keyTap", Keys: keys.ToArray()));
    public string Hover(SerialHidCursorPoint target) => Input(new("hover", Target: target));
    public string Click(SerialHidCursorPoint target) => Input(new("click", Target: target));
    public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps) =>
        Input(new("scroll", Target: target, VerticalSteps: verticalSteps, HorizontalSteps: horizontalSteps));
    public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination) => Input(new("drag", Target: start, Destination: destination));
    public string Flick(SerialHidCursorPoint start, SerialHidCursorPoint destination) => Input(new("flick", Target: start, Destination: destination));
    public ResidentPhysicalInput? Physical()
    {
        var reading = ExchangeAsync(new("physical"), false).GetAwaiter().GetResult().Physical;
        return reading is { } current ? new(current.Edges, current.Held) : null;
    }
    public string? Mode() => ExchangeAsync(new("mode"), false).GetAwaiter().GetResult().Mode;
    public void Report(JsonElement entry) => ExchangeAsync(new("event", Event: entry), false).GetAwaiter().GetResult();

    public void Dispose()
    {
        stop.Cancel();
        inputReader.Dispose(); queryReader.Dispose();
        input.Dispose(); query.Dispose(); stop.Dispose(); inputGate.Dispose(); queryGate.Dispose();
    }
}
