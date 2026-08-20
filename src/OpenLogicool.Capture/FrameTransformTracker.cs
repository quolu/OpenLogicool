using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Capture;

/// <summary>座標系へ効く frame 属性が変わった時だけ source ごとの revision を進める。</summary>
public sealed class FrameTransformTracker
{
    private FrameTransformSignature? current;
    private long revision;

    public long Observe(CapturedFrame frame, FrameRect contentBounds, nint monitorHandle)
    {
        var next = new FrameTransformSignature(
            frame.Width,
            frame.Height,
            frame.PixelFormat,
            frame.DpiX,
            frame.DpiY,
            contentBounds,
            monitorHandle);
        if (next != current)
        {
            current = next;
            revision++;
        }

        return revision;
    }

    public CapturedFrame Apply(CapturedFrame frame, FrameRect contentBounds, nint monitorHandle) =>
        frame with { TransformRevision = Observe(frame, contentBounds, monitorHandle) };

    public bool IsCurrent(long locatorTransformRevision) =>
        revision > 0 && locatorTransformRevision == revision;
}
