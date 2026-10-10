using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using OpenLogicool.Contracts.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class RemoteViewRuntimeTests
{
    private const string Authorization = "publisher:secretpw";

    private static readonly RemoteViewSettings Settings = new(
        RemoteViewSettings.CurrentSchemaVersion,
        "https://relay.example/game/whip",
        "https://relay.example/game",
        "publisher",
        RemoteViewQuality.Standard);

    // --- 音を運ぶ 1 周期（時計は偽物。実時間の待ちに頼らない） ---

    [Fact]
    public void 届いた音はそのままステレオのまま書かれ_鳴っている間は無音が0()
    {
        var clock = new FakeClock();
        var audio = new FakeAudio();
        var input = new CaptureStream();
        var pump = new RemoteViewAudioPump(audio, input, clock.Now);
        short[] chunk = [1, -1, 2, -2, 3, -3];

        // 1 周期ごとに 10ms 進むが、送るのは 3 frame（0.06ms）だけ。送った合計と時計を比べて埋めてはいけない。
        for (var step = 0; step < 50; step++)
        {
            audio.Enqueue(chunk);
            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.True(pump.Step());
        }

        Assert.Equal(0, pump.SilenceFramesSent);
        Assert.Equal(150, pump.FramesSent);
        var expected = Enumerable.Repeat(chunk, 50).SelectMany(values => values).ToArray();
        Assert.Equal(MemoryMarshal.AsBytes<short>(expected).ToArray(), input.ToArray());
    }

    [Fact]
    public void 無音は届かない時間が40ms以上続いた時だけ_その時間のぶん書かれる()
    {
        var clock = new FakeClock();
        var pump = new RemoteViewAudioPump(new FakeAudio(), new CaptureStream(), clock.Now);

        clock.Advance(TimeSpan.FromMilliseconds(39));
        Assert.True(pump.Step());
        Assert.Equal(0, pump.SilenceFramesSent);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(pump.Step());
        Assert.Equal(1920, pump.SilenceFramesSent);

        // 書いた時間のぶんは進めてある。すぐ次の周期では重ねて書かない。
        Assert.True(pump.Step());
        Assert.Equal(1920, pump.SilenceFramesSent);
    }

    [Fact]
    public void 無音の書き込み量は1回あたり100msまでで_無音は0の値だけ()
    {
        var clock = new FakeClock();
        var input = new CaptureStream();
        var pump = new RemoteViewAudioPump(new FakeAudio(), input, clock.Now);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(pump.Step());

        Assert.Equal(4800, pump.SilenceFramesSent);
        Assert.Equal(4800 * 4, input.ToArray().Length);
        Assert.All(input.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void stdinへ書けなくなったら周期はfalseを返す()
    {
        var audio = new FakeAudio();
        audio.Enqueue([1, 2]);
        var pump = new RemoteViewAudioPump(audio, new CaptureStream { FailWrites = true }, new FakeClock().Now);

        Assert.False(pump.Step());
    }

    // --- thread を含む起動・監視・停止 ---

    [Fact]
    public void 起動すると引数が組み立てられ_音が実際にstdinへ流れる()
    {
        var fixture = new Fixture();
        short[] chunk = [5, 6, 7, 8];
        fixture.Audio.Enqueue(chunk);

        fixture.Runtime.Start(0x2020, 42, "Game", Settings, Authorization);

        Assert.Equal(42, fixture.RequestedProcessId);
        Assert.Contains($"gfxcapture=hwnd={0x2020}:max_framerate=60:width=1280:height=720" +
                        ":resize_mode=scale_aspect:capture_cursor=1", fixture.Encoder.Arguments);
        WaitFor(() => fixture.Encoder.Input.ToArray().Length == 8);
        Assert.Equal(MemoryMarshal.AsBytes<short>(chunk).ToArray(), fixture.Encoder.Input.ToArray());
        Assert.Equal(RemoteViewPhase.Starting, fixture.Runtime.Status.Phase);
        Assert.Equal("Game", fixture.Runtime.Status.TargetProcessName);
        fixture.Runtime.Stop();
    }

    [Fact]
    public void 進捗行でコマ数が進み_最初のコマで配信中になる()
    {
        var fixture = new Fixture();
        fixture.Runtime.Start(1, 42, "Game", Settings, Authorization);

        fixture.Encoder.Emit("frame=   30 fps= 30 q=24.0 size=  120kB time=00:00:01.00 bitrate= 980.0kbits/s speed=1x");
        WaitFor(() => fixture.Runtime.Status.VideoFrames == 30);
        var status = fixture.Runtime.Status;
        Assert.Equal(RemoteViewPhase.Streaming, status.Phase);
        Assert.Equal(1.0, status.StreamedSeconds);

        fixture.Encoder.Emit("frame=   90 fps= 30 q=24.0 size=  360kB time=00:00:03.00 bitrate= 980.0kbits/s speed=1x");
        WaitFor(() => fixture.Runtime.Status.VideoFrames == 90);
        Assert.Equal(3.0, fixture.Runtime.Status.StreamedSeconds);
        fixture.Runtime.Stop();
    }

    [Fact]
    public void timeが2秒以上進まない間は映像が止まったと示す()
    {
        var fixture = new Fixture();
        fixture.Runtime.Start(1, 42, "Game", Settings, Authorization);

        fixture.Encoder.Emit("frame=   30 fps= 30 q=24.0 size=  120kB time=00:00:01.00 bitrate= 980.0kbits/s");
        WaitFor(() => fixture.Runtime.Status.StreamedSeconds == 1.0);

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.False(fixture.Runtime.Status.VideoStalled);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(fixture.Runtime.Status.VideoStalled);
        Assert.Contains("コマが届いていません", fixture.Runtime.Status.Detail);

        // 同じ time の進捗行が来ても止まったまま。進んだ時だけ戻る。
        fixture.Encoder.Emit("frame=   30 fps= 30 q=24.0 size=  120kB time=00:00:01.00 bitrate= 980.0kbits/s");
        fixture.Encoder.Emit("frame=   60 fps= 30 q=24.0 size=  240kB time=00:00:02.00 bitrate= 980.0kbits/s");
        WaitFor(() => fixture.Runtime.Status.StreamedSeconds == 2.0);
        Assert.False(fixture.Runtime.Status.VideoStalled);
        fixture.Runtime.Stop();
    }

    [Fact]
    public void ffmpegが自分で終了するとFaultedになり_認証の文字列は出さず_再起動もしない()
    {
        var fixture = new Fixture();
        fixture.Runtime.Start(1, 42, "Game", Settings, Authorization);

        fixture.Encoder.Emit("frame=   30 fps= 30 q=24.0 size=  120kB time=00:00:01.00 bitrate= 980.0kbits/s");
        fixture.Encoder.Emit("[whip @ 000001] request with publisher:secretpw to https://relay.example/game/whip");
        fixture.Encoder.Emit("password was secretpw");
        fixture.Encoder.Emit("[whip @ 000001] HTTP error 401 Unauthorized");
        fixture.Encoder.Emit("Error opening output file https://relay.example/game/whip.");
        fixture.Encoder.Exit(1);

        WaitFor(() => fixture.Runtime.Status.Phase == RemoteViewPhase.Faulted);
        var detail = fixture.Runtime.Status.Detail;
        Assert.Contains("終了コード 1", detail);
        Assert.Contains("HTTP error 401", detail);
        Assert.Contains("Error opening output file", detail);
        Assert.DoesNotContain("secretpw", detail);
        Assert.DoesNotContain("frame=", detail);
        Assert.False(fixture.Runtime.Status.VideoStalled);
        Assert.Equal(1, fixture.ProcessesStarted);
        fixture.Runtime.Stop();
    }

    [Fact]
    public void 音源を作れないと配信ごと止めて理由を示す()
    {
        var fixture = new Fixture { AudioFactoryFailure = new InvalidOperationException("音の取り込みを開始できませんでした") };

        fixture.Runtime.Start(1, 42, "Game", Settings, Authorization);

        WaitFor(() => fixture.Runtime.Status.Phase == RemoteViewPhase.Faulted);
        Assert.Contains("音の取り込みを開始できませんでした", fixture.Runtime.Status.Detail);
        WaitFor(() => fixture.Encoder.Killed);
        fixture.Runtime.Stop();
    }

    [Fact]
    public void 停止するとstdinが閉じ_音源は作った同じthreadで破棄され_停止になる()
    {
        var fixture = new Fixture();
        fixture.Runtime.Start(1, 42, "Game", Settings, Authorization);
        WaitFor(() => fixture.Audio.CreatedOnThread != 0);

        fixture.Runtime.Stop();

        Assert.True(fixture.Encoder.Input.IsClosed);
        Assert.True(fixture.Encoder.Disposed);
        Assert.False(fixture.Encoder.Killed);
        Assert.True(fixture.Audio.Disposed);
        Assert.Equal(fixture.Audio.CreatedOnThread, fixture.Audio.DisposedOnThread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, fixture.Audio.DisposedOnThread);
        Assert.Equal(RemoteViewPhase.Stopped, fixture.Runtime.Status.Phase);
        // 停止による終了は失敗として扱わない。
        Assert.DoesNotContain("終了しました", fixture.Runtime.Status.Detail);
    }

    [Fact]
    public void 停止後に再開でき_動いている間の二重起動は拒否する()
    {
        var fixture = new Fixture();
        fixture.Runtime.Start(1, 42, "Game", Settings, Authorization);
        Assert.Throws<InvalidOperationException>(
            () => fixture.Runtime.Start(1, 42, "Game", Settings, Authorization));
        fixture.Runtime.Stop();

        fixture.Runtime.Start(1, 42, "Game", Settings, Authorization);

        Assert.Equal(2, fixture.ProcessesStarted);
        Assert.Equal(RemoteViewPhase.Starting, fixture.Runtime.Status.Phase);
        fixture.Runtime.Stop();
        Assert.Equal(RemoteViewPhase.Stopped, fixture.Runtime.Status.Phase);
    }

    [Fact]
    public void Faulted_の後も開始し直せる()
    {
        var fixture = new Fixture();
        fixture.Runtime.Start(1, 42, "Game", Settings, Authorization);
        fixture.Encoder.Exit(2);
        WaitFor(() => fixture.Runtime.Status.Phase == RemoteViewPhase.Faulted);

        fixture.Runtime.Start(1, 42, "Game", Settings, Authorization);

        Assert.Equal(2, fixture.ProcessesStarted);
        Assert.Equal(RemoteViewPhase.Starting, fixture.Runtime.Status.Phase);
        fixture.Runtime.Stop();
    }

    [Theory]
    [InlineData("frame=  120 fps= 30 q=24.0 size=  480kB time=00:01:02.50 bitrate= 980.0kbits/s", 120L, 62.5)]
    [InlineData("frame=    0 fps=0.0 q=0.0 size=       0kB time=N/A bitrate=N/A speed=N/A", 0L, null)]
    public void 進捗行を解析する(string line, long frame, double? seconds)
    {
        Assert.True(RemoteViewProgressLine.TryParse(line, out var parsedFrame, out var parsedTime));
        Assert.Equal(frame, parsedFrame);
        Assert.Equal(seconds, parsedTime);
    }

    [Fact]
    public void 進捗行でない行は解析しない()
    {
        Assert.False(RemoteViewProgressLine.TryParse("Input #0, lavfi, from 'gfxcapture=hwnd=1':", out _, out _));
    }

    private static void WaitFor(Func<bool> condition)
    {
        Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10)), "条件が10秒以内に成立しませんでした。");
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Runtime = new RemoteViewRuntime(
                arguments =>
                {
                    Interlocked.Increment(ref processesStarted);
                    Encoder = new FakeEncoder(arguments);
                    return Encoder;
                },
                processId =>
                {
                    RequestedProcessId = processId;
                    if (AudioFactoryFailure is { } failure)
                    {
                        throw failure;
                    }

                    Audio.CreatedOnThread = Environment.CurrentManagedThreadId;
                    return Audio;
                },
                Clock.Now);
        }

        private int processesStarted;

        public FakeClock Clock { get; } = new();

        public FakeAudio Audio { get; } = new();

        public FakeEncoder Encoder { get; private set; } = null!;

        public RemoteViewRuntime Runtime { get; }

        public int RequestedProcessId { get; private set; }

        public Exception? AudioFactoryFailure { get; init; }

        public int ProcessesStarted => Volatile.Read(ref processesStarted);
    }

    private sealed class FakeClock
    {
        private long ticks;

        public TimeSpan Now() => TimeSpan.FromTicks(Interlocked.Read(ref ticks));

        public void Advance(TimeSpan value) => Interlocked.Add(ref ticks, value.Ticks);
    }

    private sealed class FakeAudio : IRemoteViewAudioSource
    {
        private readonly ConcurrentQueue<short[]> chunks = new();
        private int createdOnThread;
        private int disposedOnThread;

        public int CreatedOnThread
        {
            get => Volatile.Read(ref createdOnThread);
            set => Volatile.Write(ref createdOnThread, value);
        }

        public int DisposedOnThread => Volatile.Read(ref disposedOnThread);

        public bool Disposed => DisposedOnThread != 0;

        public void Enqueue(short[] interleaved) => chunks.Enqueue(interleaved);

        public int ReadStereo(Span<short> interleaved)
        {
            if (!chunks.TryDequeue(out var chunk))
            {
                return 0;
            }

            chunk.CopyTo(interleaved);
            return chunk.Length / 2;
        }

        public void Dispose() => Volatile.Write(ref disposedOnThread, Environment.CurrentManagedThreadId);
    }

    private sealed class FakeEncoder(IReadOnlyList<string> arguments) : IRemoteViewEncoderProcess
    {
        private readonly BlockingCollection<string> lines = [];
        private volatile bool exited;
        private int exitCode = -1;

        public IReadOnlyList<string> Arguments { get; } = arguments;

        public CaptureStream Input { get; } = new();

        public bool Killed { get; private set; }

        public bool Disposed { get; private set; }

        public Stream StandardInput => Input;

        public bool HasExited => exited;

        public int? ExitCode => exited ? exitCode : null;

        public void Emit(string line) => lines.Add(line);

        public void Exit(int code)
        {
            exitCode = code;
            exited = true;
            lines.CompleteAdding();
        }

        public IEnumerable<string> ReadErrorLines() => lines.GetConsumingEnumerable();

        /// <summary>ffmpeg は stdin が閉じると出力を終えて自分で終了する。</summary>
        public void CloseStandardInput()
        {
            Input.Close();
            if (!exited)
            {
                Exit(0);
            }
        }

        public bool WaitForExit(TimeSpan timeout) => SpinWait.SpinUntil(() => exited, timeout);

        public void Kill()
        {
            Killed = true;
            if (!exited)
            {
                Exit(137);
            }
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>別 thread から書かれる stdin の代わり。</summary>
    private sealed class CaptureStream : Stream
    {
        private readonly object gate = new();
        private readonly List<byte> written = [];
        private volatile bool closed;

        public bool FailWrites { get; init; }

        public bool IsClosed => closed;

        public byte[] ToArray()
        {
            lock (gate)
            {
                return [.. written];
            }
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (FailWrites || closed)
            {
                throw new IOException("パイプが閉じています。");
            }

            lock (gate)
            {
                written.AddRange(buffer.ToArray());
            }
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Close() => closed = true;

        public override void Flush()
        {
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
