using OpenLogicool.Contracts.Audio;
using OpenLogicool.Devices.G13;
using Xunit;

namespace OpenLogicool.Devices.G13.Tests;

public sealed class G13AudioBacklightRuntimeTests
{
    private const int SampleRate = 48000;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(30);
    private static readonly G13BacklightColor PowerOnColor = new(0x5A, 0xFF, 0x6E);

    [Fact]
    public void Without_a_target_the_device_is_never_opened()
    {
        var transport = new FakeTransport();
        var sources = new FakeSources();
        using var runtime = new G13AudioBacklightRuntime(transport, sources.Create);

        Run(runtime, 10);

        Assert.Equal(0, transport.OpenAttempts);
        Assert.Empty(transport.Writes);
        Assert.Empty(sources.Created);
    }

    [Fact]
    public void Following_a_process_remembers_the_color_before_and_writes_colors_from_its_sound()
    {
        var transport = new FakeTransport();
        var sources = new FakeSources();
        using var runtime = new G13AudioBacklightRuntime(transport, sources.Create);

        runtime.SetTarget(4242);
        Run(runtime, 20);

        var source = Assert.Single(sources.Created);
        Assert.Equal(4242, source.ProcessId);
        Assert.Equal(1, transport.Reads);
        Assert.NotEmpty(transport.Writes);
        Assert.All(transport.Writes, color => Assert.Equal(0, Math.Min(color.Red, Math.Min(color.Green, color.Blue))));
        var status = runtime.Status;
        Assert.True(status.IsFollowing);
        Assert.True(status.IsConnected);
        Assert.Equal(4242, status.TargetProcessId);
        Assert.Equal(transport.Writes.Count, status.ColorWrites);
        Assert.Null(status.Failure);
    }

    [Fact]
    public void Clearing_the_target_restores_the_color_before_and_releases_the_sound()
    {
        var transport = new FakeTransport();
        var sources = new FakeSources();
        using var runtime = new G13AudioBacklightRuntime(transport, sources.Create);
        runtime.SetTarget(4242);
        Run(runtime, 20);

        runtime.SetTarget(null);
        Run(runtime, 2);
        var writesAfterRestore = transport.Writes.Count;
        Run(runtime, 5);

        Assert.True(sources.Created[0].Disposed);
        Assert.Equal(PowerOnColor, transport.Writes[^1]);
        Assert.Equal(writesAfterRestore, transport.Writes.Count);
        Assert.False(runtime.Status.IsFollowing);
    }

    [Fact]
    public void Switching_to_another_process_keeps_the_color_remembered_first()
    {
        var transport = new FakeTransport();
        var sources = new FakeSources();
        using var runtime = new G13AudioBacklightRuntime(transport, sources.Create);
        runtime.SetTarget(1);
        Run(runtime, 10);

        runtime.SetTarget(2);
        Run(runtime, 10);
        runtime.SetTarget(null);
        Run(runtime, 2);

        Assert.Equal([1, 2], sources.Created.Select(source => source.ProcessId));
        Assert.True(sources.Created[0].Disposed);
        Assert.True(sources.Created[1].Disposed);
        Assert.Equal(1, transport.Reads);
        Assert.Equal(PowerOnColor, transport.Writes[^1]);
    }

    [Fact]
    public void Silence_advances_time_so_the_color_dims_to_the_floor()
    {
        var transport = new FakeTransport();
        var sources = new FakeSources();
        using var runtime = new G13AudioBacklightRuntime(transport, sources.Create);
        runtime.SetTarget(4242);
        Run(runtime, 30);
        var loud = transport.Writes[^1];

        sources.Created[0].Silent = true;
        Run(runtime, 100);

        var quiet = transport.Writes[^1];
        var floor = 255 * G13AudioBacklightEffect.BrightnessFloor;
        Assert.InRange(Math.Max(loud.Red, Math.Max(loud.Green, loud.Blue)), 240, 255);
        Assert.InRange(Math.Max(quiet.Red, Math.Max(quiet.Green, quiet.Blue)), floor - 1, floor + 3);
    }

    [Fact]
    public void A_missing_G13_is_not_a_failure_and_writing_resumes_when_it_arrives()
    {
        var transport = new FakeTransport { Present = false };
        var sources = new FakeSources();
        using var runtime = new G13AudioBacklightRuntime(transport, sources.Create);
        runtime.SetTarget(4242);

        Run(runtime, 40);
        Assert.Empty(transport.Writes);
        Assert.False(runtime.Status.IsConnected);
        Assert.Null(runtime.Status.Failure);
        // 開き直しは 1 秒おき（毎周期は列挙しない）。
        Assert.InRange(transport.OpenAttempts, 1, 2);

        transport.Present = true;
        Run(runtime, 40);

        Assert.NotEmpty(transport.Writes);
        Assert.True(runtime.Status.IsConnected);
    }

    [Fact]
    public void A_failed_write_is_reported_and_retried_after_reopening()
    {
        var transport = new FakeTransport();
        var sources = new FakeSources();
        using var runtime = new G13AudioBacklightRuntime(transport, sources.Create);
        runtime.SetTarget(4242);
        Run(runtime, 5);

        transport.FailWrites = true;
        Run(runtime, 5);
        Assert.False(runtime.Status.IsConnected);
        Assert.Contains("書けません", runtime.Status.Failure);
        Assert.True(transport.Closes >= 1);

        transport.FailWrites = false;
        var writesBefore = transport.Writes.Count;
        Run(runtime, 40);

        Assert.True(transport.Writes.Count > writesBefore);
        Assert.True(runtime.Status.IsConnected);
        Assert.Null(runtime.Status.Failure);
    }

    [Fact]
    public void A_process_whose_sound_cannot_be_captured_is_reported_and_leaves_the_color_alone()
    {
        var transport = new FakeTransport();
        var sources = new FakeSources { FailCreation = true };
        using var runtime = new G13AudioBacklightRuntime(transport, sources.Create);

        runtime.SetTarget(4242);
        Run(runtime, 10);

        Assert.Empty(transport.Writes);
        Assert.False(runtime.Status.IsFollowing);
        Assert.Contains("process 4242 の音を拾えません", runtime.Status.Failure);

        sources.FailCreation = false;
        runtime.SetTarget(4243);
        Run(runtime, 10);

        Assert.True(runtime.Status.IsFollowing);
        Assert.Null(runtime.Status.Failure);
    }

    [Fact]
    public void When_the_sound_can_no_longer_be_read_the_color_before_is_restored()
    {
        var transport = new FakeTransport();
        var sources = new FakeSources();
        using var runtime = new G13AudioBacklightRuntime(transport, sources.Create);
        runtime.SetTarget(4242);
        Run(runtime, 20);

        sources.Created[0].FailRead = true;
        Run(runtime, 3);

        Assert.True(sources.Created[0].Disposed);
        Assert.Equal(PowerOnColor, transport.Writes[^1]);
        Assert.False(runtime.Status.IsFollowing);
        Assert.Contains("音を読めなくなりました", runtime.Status.Failure);
        Assert.Single(sources.Created);
    }

    [Fact]
    public void Stopping_restores_the_color_before()
    {
        var transport = new FakeTransport();
        var sources = new FakeSources();
        var runtime = new G13AudioBacklightRuntime(transport, sources.Create);
        runtime.SetTarget(4242);
        Run(runtime, 20);

        runtime.Stop();

        Assert.True(sources.Created[0].Disposed);
        Assert.Equal(PowerOnColor, transport.Writes[^1]);
        Assert.False(transport.IsOpen);
        Assert.Throws<InvalidOperationException>(() => runtime.SetTarget(1));
    }

    [Fact]
    public void The_worker_thread_follows_and_restores_on_stop()
    {
        var transport = new FakeTransport();
        var sources = new FakeSources();
        var runtime = new G13AudioBacklightRuntime(transport, sources.Create);
        runtime.SetTarget(4242);
        runtime.Start();

        Assert.True(SpinWait.SpinUntil(() => runtime.Status.ColorWrites >= 3, TimeSpan.FromSeconds(5)));
        runtime.Stop();

        Assert.False(runtime.Status.IsRunning);
        Assert.Equal(PowerOnColor, transport.Writes[^1]);
    }

    private static void Run(G13AudioBacklightRuntime runtime, int cycles)
    {
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            runtime.RunOnce(Tick);
        }
    }

    private sealed class FakeTransport : IG13BacklightTransport
    {
        private readonly object gate = new();
        private readonly List<G13BacklightColor> writes = [];

        public bool Present { get; set; } = true;

        public bool FailWrites { get; set; }

        public bool IsOpen { get; private set; }

        public int OpenAttempts { get; private set; }

        public int Reads { get; private set; }

        public int Closes { get; private set; }

        public IReadOnlyList<G13BacklightColor> Writes
        {
            get
            {
                lock (gate)
                {
                    return writes.ToArray();
                }
            }
        }

        public bool TryOpen()
        {
            if (IsOpen)
            {
                return true;
            }

            OpenAttempts++;
            IsOpen = Present;
            return IsOpen;
        }

        public G13BacklightColor Read()
        {
            Reads++;
            lock (gate)
            {
                return writes.Count == 0 ? PowerOnColor : writes[^1];
            }
        }

        public void Write(G13BacklightColor color)
        {
            if (FailWrites)
            {
                throw new IOException("G13のバックライト色を書けませんでした。");
            }

            lock (gate)
            {
                writes.Add(color);
            }
        }

        public void Close()
        {
            if (IsOpen)
            {
                Closes++;
            }

            IsOpen = false;
        }

        public void Dispose() => Close();
    }

    private sealed class FakeSources
    {
        public List<FakeSource> Created { get; } = [];

        public bool FailCreation { get; set; }

        public IProcessAudioSource Create(int processId)
        {
            if (FailCreation)
            {
                throw new InvalidOperationException("取り込みを開始できませんでした。");
            }

            var source = new FakeSource(processId);
            Created.Add(source);
            return source;
        }
    }

    /// <summary>周期ごとに 30 ms ぶんの音を返す。帯を順に替えるので色が動く。</summary>
    private sealed class FakeSource(int processId) : IProcessAudioSource
    {
        private long position;

        public int ProcessId { get; } = processId;

        public bool Silent { get; set; }

        public bool FailRead { get; set; }

        public bool Disposed { get; private set; }

        public int SampleRate => G13AudioBacklightRuntimeTests.SampleRate;

        public int Read(Span<float> destination)
        {
            if (FailRead)
            {
                throw new InvalidOperationException("対象の process が終了しました。");
            }

            if (Silent)
            {
                return 0;
            }

            var count = SampleRate * 30 / 1000;
            for (var index = 0; index < count; index++, position++)
            {
                var band = (int)(position / (SampleRate / 5) % G13AudioBacklightEffect.BandCount);
                destination[index] = (float)(0.4 * Math.Sin(
                    2 * Math.PI * G13AudioBacklightEffect.BandCenterFrequencyHz(band) * position / SampleRate));
            }

            return count;
        }

        public void Dispose() => Disposed = true;
    }
}
