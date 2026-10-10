using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>人の手の入力装置すべてを合わせて、Botが手を止めるかを決める部分。</summary>
public sealed class BotUserInputGateTests
{
    private static readonly SerialHidCandidate Nano = new("USB\\VID_1B4F&PID_9206\\TEST", "COM9", @"\\?\test", 0x1B4F, 0x9206);

    [Theory]
    [InlineData(0x3000, 0x2000, true)]  // 管理者権限のゲームと通常権限のBot。
    [InlineData(0x2000, 0x2000, false)]
    [InlineData(0x2000, 0x3000, false)]
    [InlineData(null, 0x2000, true)]    // 対象の権限を読めない時は、見えているとは言えない。
    public void 対象がBotより高い権限か読めない時だけ管理者権限の監視を使う(int? target, int own, bool expected) =>
        Assert.Equal(expected, ProcessIntegrity.NeedsElevatedWatch(target, own));

    [Fact]
    public void 自processの整合性の水準を読める() =>
        Assert.InRange(ProcessIntegrity.Current(), 0x1000, 0x4000);

    [Fact]
    public void 二つの観測はどちらかの押下で止まり無入力の時間は短い方を使う()
    {
        var merged = BotUserInputGate.Merge(new(false, 0, 9000, 10, 80, 1, []), new(true, 1, 200, 3, 0, 0, [0x20000]));
        Assert.True(merged.Paused);
        Assert.Equal(1, merged.HeldCount);
        Assert.Equal(200, merged.IdleMilliseconds);
        Assert.Equal(13, merged.UserEvents);
        Assert.Equal(80, merged.NanoEvents);
        Assert.Equal(1, merged.LostReleases);
        Assert.Equal([0x20000], merged.HeldCodes!);
        Assert.False(BotUserInputGate.Merge(new(false, 0, 9000, 0, 0), new(false, 0, 6000, 0, 0)).Paused);
    }

    [Fact]
    public void キーボードとマウスが静かでもG13の押下と矢印を動かせない合図で止まる()
    {
        long now = 0;
        ResidentPhysicalInput? physical = new(0, 0);
        using var gate = new BotUserInputGate(new QuietSource(), () => physical, () => now, "試験");
        now = 5000;
        Assert.False(gate.Snapshot().Paused);
        // G13／G600の物理ボタンは、押している間ずっと止める。
        physical = new(1, 1);
        now = 60_000;
        var held = gate.Snapshot();
        Assert.True(held.Paused);
        Assert.Equal([PhysicalInputPauseBridge.Code], held.HeldCodes!);
        physical = new(2, 0);
        Assert.True(gate.Snapshot().Paused);
        now = 64_999; Assert.True(gate.Snapshot().Paused);
        now = 65_000; Assert.False(gate.Snapshot().Paused);
        // Botが矢印を動かせなかった時は、手入力があった時と同じだけ待つ。
        gate.PointerHeldByOther();
        now = 69_999; Assert.True(gate.Snapshot().Paused);
        now = 70_000; Assert.False(gate.Snapshot().Paused);
    }

    [Fact]
    public void 監視processとの連絡は押下の数と無入力の時間だけを運ぶ()
    {
        var line = UserInputWatchProtocol.Format(new(true, 2, 1234, 55, 80, 1, [0x41, 0x10001]));
        Assert.Equal("2 1234 55 80 1", line);
        var parsed = UserInputWatchProtocol.Parse(line);
        Assert.True(parsed.Paused);
        Assert.Equal(2, parsed.HeldCount);
        Assert.Equal(1234, parsed.IdleMilliseconds);
        Assert.Equal(55, parsed.UserEvents);
        Assert.Equal(80, parsed.NanoEvents);
        Assert.Equal(1, parsed.LostReleases);
        Assert.Empty(parsed.HeldCodes!);
        Assert.False(UserInputWatchProtocol.Parse("0 5000 0 0 0").Paused);
        Assert.True(UserInputWatchProtocol.Parse("0 4999 0 0 0").Paused);
        Assert.Equal(0x3000, UserInputWatchProtocol.ParseReady(UserInputWatchProtocol.Ready(0x3000)));
        Assert.Throws<FormatException>(() => UserInputWatchProtocol.Parse("0 1 2 3"));
        Assert.Throws<FormatException>(() => UserInputWatchProtocol.Parse("0 -1 2 3 4"));
        Assert.Throws<FormatException>(() => UserInputWatchProtocol.ParseReady("0 1 2 3 4"));
        Assert.Throws<FormatException>(() => UserInputWatchProtocol.ParseReady(null));
    }

    [Fact]
    public async Task 監視processの報告を受け取り途絶えたら見えないまま続けない()
    {
        var pipeName = "OpenLogicool.UserInputWatch.Test." + Guid.NewGuid().ToString("N");
        var held = new TaskCompletionSource();
        var close = new TaskCompletionSource();
        Task? client = null;
        using var watch = new ElevatedUserInputWatch(Nano, 0x3000, () => client = Task.Run(async () =>
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(5000);
            var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 256, leaveOpen: true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 256, leaveOpen: true) { AutoFlush = true };
            // Botは最初に、入力元から外すNanoの識別を渡す。
            Assert.Equal(Nano, JsonSerializer.Deserialize<SerialHidCandidate>((await reader.ReadLineAsync())!));
            await writer.WriteLineAsync(UserInputWatchProtocol.Ready(0x3000));
            await writer.WriteLineAsync("0 9000 5 7 0");
            await held.Task;
            await writer.WriteLineAsync("1 0 6 7 0");
            await close.Task;
        }), pipeName);

        var first = watch.Snapshot();
        Assert.False(first.Paused);
        Assert.Equal(0, first.HeldCount);
        Assert.InRange(first.IdleMilliseconds, 9000, 11000);
        Assert.Equal(5, first.UserEvents);
        Assert.Equal(7, first.NanoEvents);

        held.SetResult();
        var deadline = Environment.TickCount64 + 3000;
        while (watch.Snapshot().HeldCount == 0 && Environment.TickCount64 < deadline) await Task.Delay(20);
        var pressed = watch.Snapshot();
        Assert.True(pressed.Paused);
        Assert.Equal(1, pressed.HeldCount);
        Assert.Equal(6, pressed.UserEvents);

        // 監視processが終わったら、手入力が見えないので失敗にする。
        close.SetResult();
        await client!;
        deadline = Environment.TickCount64 + 3000;
        Exception? failure = null;
        while (failure is null && Environment.TickCount64 < deadline)
        {
            failure = Record.Exception(() => watch.Snapshot());
            if (failure is null) await Task.Delay(20);
        }
        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    public void 監視processの権限が足りない時と起動できない時は始めない()
    {
        var pipeName = "OpenLogicool.UserInputWatch.Test." + Guid.NewGuid().ToString("N");
        var low = Assert.Throws<InvalidOperationException>(() => new ElevatedUserInputWatch(Nano, 0x3000, () => Task.Run(async () =>
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(5000);
            var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 256, leaveOpen: true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 256, leaveOpen: true) { AutoFlush = true };
            await reader.ReadLineAsync();
            await writer.WriteLineAsync(UserInputWatchProtocol.Ready(0x2000));
            await Task.Delay(500);
        }), pipeName));
        Assert.Contains("権限が足りません", low.Message, StringComparison.Ordinal);

        // 起動できなかった時は通信口を残さない（同じ名前でやり直せる）。
        Assert.Throws<InvalidOperationException>(() => new ElevatedUserInputWatch(Nano, 0x3000,
            () => throw new InvalidOperationException("起動できません"), pipeName));
        var silent = Assert.Throws<InvalidOperationException>(() => new ElevatedUserInputWatch(Nano, 0x3000, () => { }, pipeName,
            startTimeoutMs: 200));
        Assert.Contains("時間内に始まりません", silent.Message, StringComparison.Ordinal);
    }

    private sealed class QuietSource : IRawUserInputSource
    {
        public UserInputPauseSnapshot Snapshot() => new(false, 0, 600_000, 0, 0, 0, []);
        public void Dispose() { }
    }
}
