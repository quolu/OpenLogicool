using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Host;

/// <summary>Windows Graphics Captureだけを所有するgame observation adapter。</summary>
public sealed class WindowsWgcGameFrameSource : IProductGameFrameSource, IDisposable
{
    private const int MaximumDrainFrames = 2;
    private readonly WgcFrameSource source;
    private readonly TimeSpan timeout;

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

    public async ValueTask<CapturedFrame> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        string? lastUnavailable = null;
        while (started.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = DrainNewest(source, out var unavailable);
            if (latest is not null)
            {
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

    public void Dispose() => source.Dispose();
}
