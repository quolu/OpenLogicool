using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Capture;

/// <summary>座標系へ効く frame 属性が変わった時だけ source ごとの revision を進める。</summary>
public sealed class FrameTransformTracker
{
    private FrameTransformSignature? current;
    private long revision;

    public long Observe(CapturedFrame frame, FrameRect contentBounds)
    {
        var next = new FrameTransformSignature(
            frame.Width,
            frame.Height,
            frame.PixelFormat,
            frame.DpiX,
            frame.DpiY,
            contentBounds);
        if (next != current)
        {
            current = next;
            revision++;
        }

        return revision;
    }

    public CapturedFrame Apply(CapturedFrame frame, FrameRect contentBounds) =>
        frame with { TransformRevision = Observe(frame, contentBounds) };

    public bool IsCurrent(long locatorTransformRevision) =>
        revision > 0 && locatorTransformRevision == revision;
}
