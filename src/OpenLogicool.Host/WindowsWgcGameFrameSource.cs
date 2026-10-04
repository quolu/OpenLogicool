using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Host;

/// <summary>Windows Graphics Captureだけを所有するgame observation adapter。</summary>
public sealed class WindowsWgcGameFrameSource : IProductGameFrameSource, IDisposable
{
    private const int MaximumDrainFrames = 2;
    private readonly IDetailedFrameSource source;
    private readonly TimeSpan timeout;
    private readonly long maximumFrameFreshnessMilliseconds;
    private readonly TimeProvider time;
    private CapturedFrame? lastFrame;
    private long lastFrameReceivedAt;

    public WindowsWgcGameFrameSource(
        nint window,
        string sourceId,
        TimeSpan timeout,
        long maximumFrameFreshnessMilliseconds = 1_000)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(maximumFrameFreshnessMilliseconds);
        source = WgcFrameSource.CreateForWindow(window, sourceId, includeCursor: false);
        this.timeout = timeout;
        this.maximumFrameFreshnessMilliseconds = maximumFrameFreshnessMilliseconds;
        time = TimeProvider.System;
    }

    internal WindowsWgcGameFrameSource(
        IDetailedFrameSource source,
        TimeSpan timeout,
        long maximumFrameFreshnessMilliseconds = 1_000,
        TimeProvider? timeProvider = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(maximumFrameFreshnessMilliseconds);
        this.timeout = timeout;
        this.maximumFrameFreshnessMilliseconds = maximumFrameFreshnessMilliseconds;
        time = timeProvider ?? TimeProvider.System;
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
                if (!ReferenceEquals(latest, lastFrame))
                {
                    lastFrame = latest;
                    lastFrameReceivedAt = time.GetTimestamp();
                }
                // 確認待ちのqueueと静止画cacheも、受信後の経過時間を含めた同じ期限で扱う。
                var freshness = latest.FreshnessMs
                    + (long)time.GetElapsedTime(lastFrameReceivedAt).TotalMilliseconds;
                if (freshness <= maximumFrameFreshnessMilliseconds)
                    return latest with { FreshnessMs = freshness };
                lastUnavailable = $"画像が{freshness}ms前のため、{maximumFrameFreshnessMilliseconds}ms以内の新しい画像を待っています。";
            }
            else lastUnavailable = unavailable;
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
