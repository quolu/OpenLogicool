namespace OpenLogicool.Contracts.Capture;

public enum CaptureBackend
{
    GdiBitBlt,
    DesktopDuplication,
    WindowsGraphicsCapture,
}

public sealed record CapturedFrame(
    string SchemaVersion,
    string SourceId,
    CaptureBackend Backend,
    long Sequence,
    double MonotonicMs,
    DateTimeOffset WallClockUtc,
    int Width,
    int Height,
    string PixelFormat,
    double DpiX,
    double DpiY,
    long TransformRevision,
    long FreshnessMs,
    long LastChangeMs);

public abstract record FrameReadResult;

public sealed record FrameAvailable(CapturedFrame Frame) : FrameReadResult;

public sealed record FrameUnavailable(string Reason) : FrameReadResult;

public interface IFrameSource
{
    FrameReadResult Pull();
}
