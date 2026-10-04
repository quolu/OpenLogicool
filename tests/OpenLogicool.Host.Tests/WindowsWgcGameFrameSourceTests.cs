using OpenLogicool.Contracts.Capture;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class WindowsWgcGameFrameSourceTests
{
    [Fact]
    public async Task Capture_waits_for_a_current_frame_after_a_confirmation_pause()
    {
        var source = new QueueDetailedFrameSource([
            CaptureRead.Available(Frame(sequence: 1, freshnessMs: 4_913)),
            CaptureRead.Unavailable("confirmation backlog drained"),
            CaptureRead.Available(Frame(sequence: 2, freshnessMs: 7)),
            CaptureRead.Unavailable("current queue drained"),
        ]);
        using var runtime = new WindowsWgcGameFrameSource(source, TimeSpan.FromSeconds(1), timeProvider: new ManualTimestampProvider());

        var current = await runtime.CaptureAsync();

        Assert.Equal(2, current.Sequence);
        Assert.Equal(7, current.FreshnessMs);
    }

    [Fact]
    public async Task Confirmation_wait_also_expires_a_cached_frame_with_no_new_wgc_arrival()
    {
        var clock = new ManualTimestampProvider();
        var source = new QueueDetailedFrameSource([
            CaptureRead.Available(Frame(sequence: 1, freshnessMs: 4)),
            CaptureRead.Unavailable("initial queue drained"),
            CaptureRead.Unavailable("no new frame yet"),
            CaptureRead.Available(Frame(sequence: 2, freshnessMs: 6)),
            CaptureRead.Unavailable("current queue drained"),
        ]);
        using var runtime = new WindowsWgcGameFrameSource(source, TimeSpan.FromSeconds(1), timeProvider: clock);

        Assert.Equal(1, (await runtime.CaptureAsync()).Sequence);
        clock.Advance(5_000);
        var current = await runtime.CaptureAsync();

        Assert.Equal(2, current.Sequence);
        Assert.Equal(6, current.FreshnessMs);
    }

    [Fact]
    public async Task Expired_cache_without_a_current_frame_times_out_instead_of_becoming_fresh()
    {
        var clock = new ManualTimestampProvider();
        var source = new StaticDetailedFrameSource(Frame(sequence: 1, freshnessMs: 4));
        using var runtime = new WindowsWgcGameFrameSource(source, TimeSpan.FromMilliseconds(40), timeProvider: clock);
        Assert.Equal(1, (await runtime.CaptureAsync()).Sequence);
        clock.Advance(5_000);

        var error = await Assert.ThrowsAsync<TimeoutException>(async () => await runtime.CaptureAsync());

        Assert.Contains("5004ms", error.Message);
        Assert.Contains("1000ms", error.Message);
    }

    [Fact]
    public async Task Capture_uses_the_same_freshness_limit_as_the_exploration_policy()
    {
        var source = new QueueDetailedFrameSource([
            CaptureRead.Available(Frame(sequence: 1, freshnessMs: 700)),
            CaptureRead.Unavailable("old queue drained"),
            CaptureRead.Available(Frame(sequence: 2, freshnessMs: 200)),
            CaptureRead.Unavailable("current queue drained"),
        ]);
        using var runtime = new WindowsWgcGameFrameSource(source, TimeSpan.FromSeconds(1),
            maximumFrameFreshnessMilliseconds: 500, timeProvider: new ManualTimestampProvider());

        Assert.Equal(2, (await runtime.CaptureAsync()).Sequence);
    }

    [Fact]
    public void Drain_returns_the_newest_frame_instead_of_the_oldest_backlog_entry()
    {
        var source = new QueueFrameSource([
            new FrameAvailable(Frame(sequence: 1, freshnessMs: 5_296)),
            new FrameAvailable(Frame(sequence: 2, freshnessMs: 12)),
            new FrameUnavailable("queue drained"),
        ]);

        var latest = WindowsWgcGameFrameSource.DrainNewest(source, out var unavailable);

        Assert.NotNull(latest);
        Assert.Equal(2, latest.Sequence);
        Assert.Equal(12, latest.FreshnessMs);
        Assert.Null(unavailable);
    }

    [Fact]
    public void Drain_returns_unavailable_when_no_frame_exists()
    {
        var source = new QueueFrameSource([new FrameUnavailable("empty")]);

        var latest = WindowsWgcGameFrameSource.DrainNewest(source, out var unavailable);

        Assert.Null(latest);
        Assert.Equal("empty", unavailable);
    }

    [Fact]
    public void Drain_is_bounded_when_animated_content_keeps_producing_frames()
    {
        var source = new QueueFrameSource([
            new FrameAvailable(Frame(sequence: 1, freshnessMs: 100)),
            new FrameAvailable(Frame(sequence: 2, freshnessMs: 10)),
            new FrameAvailable(Frame(sequence: 3, freshnessMs: 0)),
        ]);

        var latest = WindowsWgcGameFrameSource.DrainNewest(source, out var unavailable);

        Assert.NotNull(latest);
        Assert.Equal(2, latest.Sequence);
        Assert.Null(unavailable);
        Assert.Equal(1, source.Remaining);
    }

    [Fact]
    public void Detailed_drain_reuses_the_last_valid_frame_when_wgc_is_normally_static()
    {
        var cached = Frame(sequence: 7, freshnessMs: 4);
        var source = new QueueDetailedFrameSource([
            CaptureRead.Unavailable("wgc frame はまだ到着していません。"),
        ]);

        var latest = WindowsWgcGameFrameSource.DrainNewestDetailed(
            source,
            cached,
            out var unavailable,
            out var fault);

        Assert.Same(cached, latest);
        Assert.Equal("wgc frame はまだ到着していません。", unavailable);
        Assert.Null(fault);
    }

    [Fact]
    public void Detailed_drain_never_reuses_a_cached_frame_across_a_capture_fault()
    {
        var cached = Frame(sequence: 7, freshnessMs: 4);
        var minimized = new CaptureFault(CaptureFaultKind.Minimized, "window minimized");
        var source = new QueueDetailedFrameSource([
            CaptureRead.Unavailable(minimized),
        ]);

        var latest = WindowsWgcGameFrameSource.DrainNewestDetailed(
            source,
            cached,
            out var unavailable,
            out var fault);

        Assert.Null(latest);
        Assert.Equal("window minimized", unavailable);
        Assert.Same(minimized, fault);
    }

    [Fact]
    public async Task Capture_fault_clears_cache_and_later_static_unavailability_cannot_reuse_it()
    {
        var source = new FaultThenStaticDetailedFrameSource(Frame(sequence: 7, freshnessMs: 4));
        using var runtime = new WindowsWgcGameFrameSource(source, TimeSpan.FromMilliseconds(5));

        var first = await runtime.CaptureAsync();
        var fault = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runtime.CaptureAsync());
        var afterFault = await Assert.ThrowsAsync<TimeoutException>(
            async () => await runtime.CaptureAsync());

        Assert.Equal(7, first.Sequence);
        Assert.Contains("Minimized", fault.Message);
        Assert.Contains("static", afterFault.Message);
    }

    private static CapturedFrame Frame(long sequence, long freshnessMs) =>
        new(
            "0.3.0",
            "window:game",
            CaptureBackend.WindowsGraphicsCapture,
            sequence,
            sequence * 100,
            DateTimeOffset.UnixEpoch,
            100,
            100,
            "B8G8R8A8UIntNormalized",
            96,
            96,
            1,
            freshnessMs,
            0);

    private sealed class QueueFrameSource(IEnumerable<FrameReadResult> results) : IFrameSource
    {
        private readonly Queue<FrameReadResult> queue = new(results);

        public int Remaining => queue.Count;

        public FrameReadResult Pull() => queue.Dequeue();
    }

    private sealed class ManualTimestampProvider : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1_000;
        public override long GetTimestamp() => timestamp;
        public void Advance(long milliseconds) => timestamp += milliseconds;
    }

    private sealed class StaticDetailedFrameSource(CapturedFrame first) : IDetailedFrameSource
    {
        private bool supplied;
        public FrameReadResult Pull() => PullDetailed().Result;
        public CaptureRead PullDetailed()
        {
            if (supplied) return CaptureRead.Unavailable("static");
            supplied = true;
            return CaptureRead.Available(first);
        }
    }

    private sealed class QueueDetailedFrameSource(IEnumerable<CaptureRead> results) : IDetailedFrameSource
    {
        private readonly Queue<CaptureRead> queue = new(results);

        public FrameReadResult Pull() => PullDetailed().Result;

        public CaptureRead PullDetailed() => queue.Dequeue();
    }

    private sealed class FaultThenStaticDetailedFrameSource(CapturedFrame first) : IDetailedFrameSource
    {
        private int calls;

        public FrameReadResult Pull() => PullDetailed().Result;

        public CaptureRead PullDetailed() => calls++ switch
        {
            0 => CaptureRead.Available(first),
            1 => CaptureRead.Unavailable("initial queue drained"),
            2 => CaptureRead.Unavailable(new CaptureFault(CaptureFaultKind.Minimized, "window minimized")),
            _ => CaptureRead.Unavailable("static"),
        };
    }
}
