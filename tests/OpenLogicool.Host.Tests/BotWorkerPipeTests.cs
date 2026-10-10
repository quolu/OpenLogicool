using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Host;
using OpenLogicool.Input;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class BotWorkerPipeTests
{
    private static readonly SerialHidCandidate Nano = new("nano-id", "COM-test", "interface-test", 0x1B4F, 0x9205);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task 六操作と識別と物理観測とモードと片道イベントが往復する()
    {
        var device = new FakeDevice();
        var observed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new BotWorkerPipeServer(device, Nano, "dpi", () => new(42, 2), () => "mode-test", observed.SetResult);
        using var client = await BotWorkerPipeClient.ConnectAsync(server.InputName, server.QueryName, "dpi", CancellationToken.None);
        Assert.Equal(Nano, client.Nano);
        var first = new SerialHidCursorPoint(10, 20);
        var second = new SerialHidCursorPoint(30, 40);
        Assert.Equal("keyTap", client.KeyTap(["Key:Space", "Key:LCtrl"]));
        Assert.Equal("hover", client.Hover(first));
        Assert.Equal("click", client.Click(second));
        Assert.Equal("scroll", client.Scroll(first, 2, -3));
        Assert.Equal("drag", client.Drag(first, second));
        Assert.Equal("flick", client.Flick(second, first));
        Assert.Equal(new ResidentPhysicalInput(42, 2), client.Physical());
        Assert.Equal("mode-test", client.Mode());
        client.Report(JsonSerializer.SerializeToElement(new { Event = "run-started", Detail = "テスト" }));
        Assert.Equal("テスト", (await observed.Task.WaitAsync(Deadline)).GetProperty("Detail").GetString());
        Assert.Equal(new[] { "keyTap:Key:Space,Key:LCtrl", "hover:10,20", "click:30,40", "scroll:10,20:2,-3",
            "drag:10,20:30,40", "flick:30,40:10,20" }, device.Calls);
    }

    [Fact]
    public async Task 常駐がない物理観測と解除したモードはnullで戻る()
    {
        await using var server = new BotWorkerPipeServer(new FakeDevice(), Nano, "dpi", null, () => null, _ => { });
        using var client = await BotWorkerPipeClient.ConnectAsync(server.InputName, server.QueryName, "dpi", CancellationToken.None);
        Assert.Null(client.Physical());
        Assert.Null(client.Mode());
    }

    [Fact]
    public async Task 矢印不動は元の例外へ戻して次の入力を受け付ける()
    {
        var device = new FakeDevice { PointerUnmoved = true };
        await using var server = new BotWorkerPipeServer(device, Nano, "dpi", null, () => null, _ => { });
        using var client = await BotWorkerPipeClient.ConnectAsync(server.InputName, server.QueryName, "dpi", CancellationToken.None);
        var error = Assert.Throws<SerialHidPointerMoveException>(() => client.Click(new(10, 20)));
        Assert.Equal("利用者が矢印を握っています。", error.Message);
        Assert.Equal("keyTap", client.KeyTap(["Key:Space"]));
        Assert.Null(client.Fault);
    }

    [Theory]
    [InlineData(2, "dpi")]
    [InlineData(1, "other-dpi")]
    public async Task protocolとDPIの不一致を拒否する(int protocol, string dpi)
    {
        var device = new FakeDevice();
        await using var server = new BotWorkerPipeServer(device, Nano, "dpi", null, () => null, _ => { });
        var error = await Assert.ThrowsAsync<IOException>(() => BotWorkerPipeClient.ConnectAsync(
            server.InputName, server.QueryName, dpi, CancellationToken.None, protocol));
        Assert.Contains("一致しません", error.Message);
        await server.Completion.WaitAsync(Deadline);
        Assert.Empty(device.Calls);
    }

    [Fact]
    public async Task 入力が終わる前でも別パイプで物理ボタンを観測できる()
    {
        var device = new FakeDevice { BlockDrag = true };
        await using var server = new BotWorkerPipeServer(device, Nano, "dpi", () => new(7, 1), () => "test", _ => { });
        using var client = await BotWorkerPipeClient.ConnectAsync(server.InputName, server.QueryName, "dpi", CancellationToken.None);
        var drag = Task.Run(() => client.Drag(new(1, 2), new(3, 4)));
        await device.Entered.Task.WaitAsync(Deadline);
        try
        {
            Assert.Equal(new ResidentPhysicalInput(7, 1), await Task.Run(client.Physical).WaitAsync(Deadline));
            Assert.Equal("test", await Task.Run(client.Mode).WaitAsync(Deadline));
        }
        finally { device.Release.TrySetResult(); }
        Assert.Equal("drag", await drag.WaitAsync(Deadline));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 片方のパイプだけの切断でも受付を閉じる(bool input)
    {
        await using var server = new BotWorkerPipeServer(new FakeDevice(), Nano, "dpi", null, () => null, _ => { });
        using var pair = await RawPair.ConnectAsync(server);
        (input ? pair.Input : pair.Query).Dispose();
        await server.Completion.WaitAsync(Deadline);
        Assert.True(server.Stopped.IsCompleted);
    }

    [Fact]
    public async Task 入力中に入力用パイプだけが切れても問い合わせの受付を閉じて入力を終える()
    {
        var device = new FakeDevice { BlockDrag = true };
        await using var server = new BotWorkerPipeServer(device, Nano, "dpi", null, () => null, _ => { });
        using var pair = await RawPair.ConnectAsync(server);
        await BotWorkerPipe.WriteAsync(pair.Input, new BotWorkerRequest("drag"), CancellationToken.None);
        await device.Entered.Task.WaitAsync(Deadline);
        pair.Input.Dispose();
        try
        {
            await server.Stopped.WaitAsync(Deadline);
            Assert.False(server.Completion.IsCompleted);
        }
        finally { device.Release.TrySetResult(); }
        await server.Completion.WaitAsync(Deadline);
        Assert.True(device.Released);
        Assert.Single(device.Calls);
    }

    [Fact]
    public async Task 子の異常終了でもドラッグの解放まで番を保ち直後に次を始められる()
    {
        var device = new FakeDevice { BlockDrag = true };
        var available = new TaskCompletionSource<BotWorkerPipeServer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var faulted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new DemonstrationRecordingGate();
        using var intents = new HostBotScriptIntents([new("test", "テスト", "説明")], TestDirectory(), gate,
            async (_, _, _, token) =>
            {
                await using var server = new BotWorkerPipeServer(device, Nano, "dpi", null, () => null, _ => { });
                using var registration = token.Register(server.Stop);
                available.SetResult(server);
                await server.Completion;
                token.ThrowIfCancellationRequested();
                throw new IOException("Botが異常終了しました。");
            }, (_, detail) => { faulted.SetResult(detail); return Task.CompletedTask; });
        intents.Start("test");
        var firstServer = await available.Task.WaitAsync(Deadline);
        using (var pair = await RawPair.ConnectAsync(firstServer))
        {
            await BotWorkerPipe.WriteAsync(pair.Input, new BotWorkerRequest("drag", Target: new(1, 2), Destination: new(3, 4)), CancellationToken.None);
            await device.Entered.Task.WaitAsync(Deadline);
            pair.Input.Dispose(); pair.Query.Dispose();
            await firstServer.Stopped.WaitAsync(Deadline);
            try
            {
                Assert.False(firstServer.Completion.IsCompleted);
                Assert.False(device.Released);
                Assert.Equal(DemonstrationGateState.Playing, gate.State);
                Assert.Throws<InvalidOperationException>(() => intents.Start("test"));
            }
            finally { device.Release.TrySetResult(); }
            await firstServer.Completion.WaitAsync(Deadline);
        }
        Assert.Equal("Botが異常終了しました。", await faulted.Task.WaitAsync(Deadline));
        await intents.StopAsync().WaitAsync(Deadline);
        Assert.True(device.Released);
        Assert.Equal(DemonstrationGateState.Free, gate.State);
        Assert.Equal(BotScriptPhase.Faulted, intents.Current().Phase);
        available = new(TaskCreationOptions.RunContinuationsAsynchronously);
        intents.Start("test");
        var secondServer = await available.Task.WaitAsync(Deadline);
        using var next = await BotWorkerPipeClient.ConnectAsync(secondServer.InputName, secondServer.QueryName, "dpi", CancellationToken.None);
        Assert.Equal("keyTap", next.KeyTap(["Key:Space"]));
        await intents.StopAsync().WaitAsync(Deadline);
        Assert.Equal(DemonstrationGateState.Free, gate.State);
        Assert.Equal(BotScriptPhase.Stopped, intents.Current().Phase);
        Assert.Equal(2, device.Calls.Count);
    }

    [Fact]
    public async Task 標準入力の終わりで両方のパイプの接続待ちを止める()
    {
        using var stop = new CancellationTokenSource();
        var connecting = BotWorkerPipeClient.ConnectAsync("missing-input-" + Guid.NewGuid(),
            "missing-query-" + Guid.NewGuid(), "dpi", stop.Token);
        await BotWorkerCommand.WatchStandardInputAsync(new StringReader(""), stop, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting.WaitAsync(Deadline));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 標準入力の終わりで応答待ちを止め受付済み入力は本体で終える(bool query)
    {
        var device = new FakeDevice { BlockDrag = true };
        await using var server = new BotWorkerPipeServer(device, Nano, "dpi", () =>
        {
            device.Entered.TrySetResult();
            device.Release.Task.GetAwaiter().GetResult();
            return null;
        }, () => null, _ => { });
        using var stop = new CancellationTokenSource();
        using var client = await BotWorkerPipeClient.ConnectAsync(server.InputName, server.QueryName, "dpi", stop.Token);
        var drag = Task.Run(() => query ? client.Physical()?.ToString() : client.Drag(new(1, 2), new(3, 4)));
        await device.Entered.Task.WaitAsync(Deadline);
        try
        {
            await BotWorkerCommand.WatchStandardInputAsync(new StringReader(""), stop, CancellationToken.None);
            server.Stop();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drag.WaitAsync(Deadline));
            Assert.False(server.Completion.IsCompleted);
        }
        finally { device.Release.TrySetResult(); }
        await server.Completion.WaitAsync(Deadline);
        Assert.Equal(!query, device.Released);
        Assert.Equal(query ? 0 : 1, device.Calls.Count);
    }

    [Fact]
    public async Task 停止前に届いていても未受付の次の入力は送らない()
    {
        var device = new FakeDevice { BlockDrag = true };
        await using var server = new BotWorkerPipeServer(device, Nano, "dpi", null, () => null, _ => { });
        using var pair = await RawPair.ConnectAsync(server);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var queued = JsonSerializer.Serialize(new BotWorkerRequest("drag"), json) + "\n"
            + JsonSerializer.Serialize(new BotWorkerRequest("keyTap", Keys: ["Key:Space"]), json) + "\n";
        await pair.Input.WriteAsync(System.Text.Encoding.UTF8.GetBytes(queued));
        await device.Entered.Task.WaitAsync(Deadline);
        try
        {
            server.Stop();
            Assert.False(server.Completion.IsCompleted);
        }
        finally { device.Release.TrySetResult(); }
        await server.Completion.WaitAsync(Deadline);
        Assert.True(device.Released);
        Assert.Single(device.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("start\n")]
    public async Task Job登録の起動合図を待っている間もその後もEOFで停止する(string input)
    {
        using var stop = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await BotWorkerCommand.WatchStandardInputAsync(new StringReader(input), stop, CancellationToken.None, ready);
        Assert.Equal(input.Length != 0, ready.Task.IsCompleted);
        Assert.True(stop.IsCancellationRequested);
    }

    [Fact]
    public async Task 応答が消えた入力は一度だけ送り失敗後も送り直さない()
    {
        var inputName = "test-input-" + Guid.NewGuid();
        var queryName = "test-query-" + Guid.NewGuid();
        using var input = CreateServer(inputName);
        using var query = CreateServer(queryName);
        var received = 0;
        var serving = Task.Run(async () =>
        {
            await Task.WhenAll(input.WaitForConnectionAsync(), query.WaitForConnectionAsync());
            using var ir = BotWorkerPipe.Reader(input);
            using var qr = BotWorkerPipe.Reader(query);
            await BotWorkerPipe.ReadAsync<BotWorkerRequest>(ir, CancellationToken.None);
            await BotWorkerPipe.WriteAsync(input, new BotWorkerResponse(true, Nano: Nano), CancellationToken.None);
            await BotWorkerPipe.ReadAsync<BotWorkerRequest>(qr, CancellationToken.None);
            await BotWorkerPipe.WriteAsync(query, new BotWorkerResponse(true, Nano: Nano), CancellationToken.None);
            Assert.Equal("keyTap", (await BotWorkerPipe.ReadAsync<BotWorkerRequest>(ir, CancellationToken.None)).Operation);
            received++;
            input.Disconnect();
        });
        using var client = await BotWorkerPipeClient.ConnectAsync(inputName, queryName, "dpi", CancellationToken.None);
        await Assert.ThrowsAsync<EndOfStreamException>(() => Task.Run(() => client.KeyTap(["Key:Space"])).WaitAsync(Deadline));
        Assert.ThrowsAny<OperationCanceledException>(() => client.KeyTap(["Key:Space"]));
        await serving.WaitAsync(Deadline);
        Assert.Equal(1, received);
    }

    [Fact]
    public void currentが無い時は明示エラーで版の印がある間は削除できない()
    {
        var root = TestDirectory();
        var host = Path.Combine(root, "OpenLogicool");
        var bot = Path.Combine(root, "OpenLogicool.Bot");
        Directory.CreateDirectory(host);
        Assert.Throws<FileNotFoundException>(() => BotWorkerProcess.CurrentVersion(host));
        var version = Path.Combine(bot, "version-test");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "OpenLogicool.Host.exe"), "試験用の印");
        File.WriteAllText(Path.Combine(bot, "current.txt"), "version-test");
        var active = BotWorkerProcess.AcquireCurrentVersion(host);
        var mark = active.Marker.Name;
        try
        {
            Assert.Equal(version, active.Directory);
            Assert.Throws<IOException>(() => new FileStream(mark, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            Assert.Throws<IOException>(() => File.Delete(mark));
        }
        finally { active.Marker.Dispose(); }
        Assert.False(File.Exists(mark));
        File.WriteAllText(Path.Combine(bot, "current.txt"), "../other");
        Assert.Throws<InvalidDataException>(() => BotWorkerProcess.CurrentVersion(host));
    }

    [Fact]
    public void 自己試験で配布済み設定を全部読み込める() => BotWorkerCommand.ValidatePackages(AppContext.BaseDirectory);

    private static string TestDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "bot-worker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static NamedPipeServerStream CreateServer(string name) => new(name, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private sealed class RawPair : IDisposable
    {
        public NamedPipeClientStream Input { get; private init; } = null!;
        public NamedPipeClientStream Query { get; private init; } = null!;
        public static async Task<RawPair> ConnectAsync(BotWorkerPipeServer server)
        {
            var input = new NamedPipeClientStream(".", server.InputName, PipeDirection.InOut, PipeOptions.Asynchronous);
            var query = new NamedPipeClientStream(".", server.QueryName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(input.ConnectAsync(), query.ConnectAsync());
            using var ir = BotWorkerPipe.Reader(input);
            using var qr = BotWorkerPipe.Reader(query);
            var hello = new BotWorkerRequest("hello", Dpi: "dpi");
            await BotWorkerPipe.WriteAsync(input, hello, CancellationToken.None);
            Assert.True((await BotWorkerPipe.ReadAsync<BotWorkerResponse>(ir, CancellationToken.None)).Ok);
            await BotWorkerPipe.WriteAsync(query, hello, CancellationToken.None);
            Assert.True((await BotWorkerPipe.ReadAsync<BotWorkerResponse>(qr, CancellationToken.None)).Ok);
            return new() { Input = input, Query = query };
        }
        public void Dispose() { Input.Dispose(); Query.Dispose(); }
    }

    private sealed class FakeDevice : INanoGameInputDevice
    {
        public List<string> Calls { get; } = [];
        public bool PointerUnmoved { get; init; }
        public bool BlockDrag { get; init; }
        public bool Released { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static string Point(SerialHidCursorPoint point) => $"{point.X},{point.Y}";
        public string Hover(SerialHidCursorPoint target) { Calls.Add("hover:" + Point(target)); return "hover"; }
        public string Click(SerialHidCursorPoint target)
        {
            Calls.Add("click:" + Point(target));
            if (PointerUnmoved) throw new SerialHidPointerMoveException("利用者が矢印を握っています。");
            return "click";
        }
        public string KeyTap(IReadOnlyList<string> keys) { Calls.Add("keyTap:" + string.Join(",", keys)); return "keyTap"; }
        public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps)
        { Calls.Add($"scroll:{Point(target)}:{verticalSteps},{horizontalSteps}"); return "scroll"; }
        public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination)
        {
            Calls.Add($"drag:{Point(start)}:{Point(destination)}");
            Entered.TrySetResult();
            if (BlockDrag) Release.Task.GetAwaiter().GetResult();
            Released = true;
            return "drag";
        }
        public string Flick(SerialHidCursorPoint start, SerialHidCursorPoint destination)
        { Calls.Add($"flick:{Point(start)}:{Point(destination)}"); return "flick"; }
    }
}
