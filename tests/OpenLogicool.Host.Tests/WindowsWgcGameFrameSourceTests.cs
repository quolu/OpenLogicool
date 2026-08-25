using OpenLogicool.Contracts.Capture;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class WindowsWgcGameFrameSourceTests
{
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
}
