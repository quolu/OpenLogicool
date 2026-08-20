namespace OpenLogicool.Contracts.Capture;

public enum CaptureFaultKind
{
    Black,
    Stale,
    Drop,
    Resize,
    DeviceLost,
    BackendChanged,
    Occluded,
    Minimized,
}

public sealed record CaptureFault(CaptureFaultKind Kind, string Detail);

/// <summary>Frame の取得結果と、取得不能を静止か明示 fault かへ分けた詳細結果。</summary>
public sealed record CaptureRead(FrameReadResult Result, CaptureFault? Fault = null)
{
    public static CaptureRead Available(CapturedFrame frame) => new(new FrameAvailable(frame));

    public static CaptureRead Unavailable(string reason) => new(new FrameUnavailable(reason));

    public static CaptureRead Unavailable(CaptureFault fault) => new(new FrameUnavailable(fault.Detail), fault);
}

public interface IDetailedFrameSource : IFrameSource
{
    CaptureRead PullDetailed();
}
