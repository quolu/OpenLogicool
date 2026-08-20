namespace OpenLogicool.Contracts.Capture;

public enum CaptureBackend
{
    GdiBitBlt,
    DesktopDuplication,
    WindowsGraphicsCapture,
}

public enum FrameColorSpace
{
    Unknown,
}

public enum FrameRotation
{
    None,
    Clockwise90,
    Clockwise180,
    Clockwise270,
}

public sealed record FrameCrop(int X, int Y, int Width, int Height);

public sealed record FramePixels(ReadOnlyMemory<byte> Bgra8, int Stride);

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
    long LastChangeMs,
    FrameColorSpace ColorSpace = FrameColorSpace.Unknown,
    FrameRotation Rotation = FrameRotation.None,
    FrameCrop? Crop = null,
    FramePixels? Pixels = null);

public abstract record FrameReadResult;

public sealed record FrameAvailable(CapturedFrame Frame) : FrameReadResult;

public sealed record FrameUnavailable(string Reason) : FrameReadResult;

public interface IFrameSource
{
    FrameReadResult Pull();
}
