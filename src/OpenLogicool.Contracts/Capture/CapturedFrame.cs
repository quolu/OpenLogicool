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
