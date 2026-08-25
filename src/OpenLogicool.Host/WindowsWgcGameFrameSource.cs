using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Host;

/// <summary>Windows Graphics Captureだけを所有するgame observation adapter。</summary>
public sealed class WindowsWgcGameFrameSource : IProductGameFrameSource, IDisposable
{
    private const int MaximumDrainFrames = 2;
    private readonly IDetailedFrameSource source;
    private readonly TimeSpan timeout;
    private CapturedFrame? lastFrame;

    public WindowsWgcGameFrameSource(
        nint window,
        string sourceId,
        TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        source = WgcFrameSource.CreateForWindow(window, sourceId, includeCursor: false);
        this.timeout = timeout;
    }

    internal WindowsWgcGameFrameSource(
        IDetailedFrameSource source,
        TimeSpan timeout)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        this.timeout = timeout;
    }

    public async ValueTask<CapturedFrame> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        string? lastUnavailable = null;
        while (started.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = DrainNewestDetailed(source, lastFrame, out var unavailable, out var fault);
            if (fault is not null)
            {
                lastFrame = null;
                throw new InvalidOperationException($"WGC capture fault: {fault.Kind}: {fault.Detail}");
            }
            if (latest is not null)
            {
                lastFrame = latest;
                return latest;
            }
            lastUnavailable = unavailable;
            await Task.Delay(16, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"WGC frameを{timeout.TotalMilliseconds:F0}ms以内に取得できませんでした: {lastUnavailable ?? "reason unavailable"}");
    }

    internal static CapturedFrame? DrainNewest(
        IFrameSource frameSource,
        out string? lastUnavailable)
    {
        ArgumentNullException.ThrowIfNull(frameSource);
        CapturedFrame? latest = null;
        lastUnavailable = null;
        for (var index = 0; index < MaximumDrainFrames; index++)
        {
            switch (frameSource.Pull())
            {
                case FrameAvailable available:
                    latest = available.Frame;
                    break;
                case FrameUnavailable unavailable:
                    lastUnavailable = unavailable.Reason;
                    return latest;
            }
        }
        return latest;
    }

    internal static CapturedFrame? DrainNewestDetailed(
        IDetailedFrameSource frameSource,
        CapturedFrame? cached,
        out string? lastUnavailable,
        out CaptureFault? fault)
    {
        ArgumentNullException.ThrowIfNull(frameSource);
        CapturedFrame? latest = null;
        lastUnavailable = null;
        fault = null;
        for (var index = 0; index < MaximumDrainFrames; index++)
        {
            var read = frameSource.PullDetailed();
            switch (read.Result)
            {
                case FrameAvailable available:
                    latest = available.Frame;
                    break;
                case FrameUnavailable unavailable:
                    lastUnavailable = unavailable.Reason;
                    if (read.Fault is not null)
                    {
                        fault = read.Fault;
                        return null;
                    }
                    return latest ?? cached;
            }
        }
        return latest ?? cached;
    }

    public void Dispose() => (source as IDisposable)?.Dispose();
}
