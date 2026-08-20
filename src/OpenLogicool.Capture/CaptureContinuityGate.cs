using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Capture;

/// <summary>Capture の不連続後は、同じ backend と transform revision を再校正するまで自動入力を許可しない。</summary>
public sealed class CaptureContinuityGate
{
    private CaptureBackend? backend;
    private long? transformRevision;
    private CapturedFrame? latestFrame;
    private CaptureFault? blockingFault;
    private bool calibrated;

    public bool AllowsAutomaticInput => calibrated && blockingFault is null && latestFrame is not null;

    public CaptureFault? BlockingFault => blockingFault;

    public void Observe(CaptureRead read, long staleAfterMs)
    {
        if (staleAfterMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAfterMs));
        }

        if (read.Fault is not null)
        {
            Block(read.Fault);
            return;
        }

        if (read.Result is not FrameAvailable { Frame: var frame })
        {
            // WGC は変化駆動である。fault を伴わない無 frame は静止であり連続性を切らない。
            return;
        }

        latestFrame = frame;
        if (frame.FreshnessMs > staleAfterMs)
        {
            Block(new CaptureFault(CaptureFaultKind.Stale, "frame の鮮度が許容値を超えました。"));
            return;
        }

        if (backend is not null && backend != frame.Backend)
        {
            Block(new CaptureFault(CaptureFaultKind.BackendChanged, "capture backend が変わりました。"));
            return;
        }

        if (transformRevision is not null && transformRevision != frame.TransformRevision)
        {
            Block(new CaptureFault(CaptureFaultKind.Resize, "frame transform revision が変わりました。"));
            return;
        }

        backend = frame.Backend;
        transformRevision = frame.TransformRevision;
    }

    public void Recalibrate(CapturedFrame frame)
    {
        if (latestFrame is null
            || latestFrame.SourceId != frame.SourceId
            || latestFrame.Backend != frame.Backend
            || latestFrame.TransformRevision != frame.TransformRevision)
        {
            throw new InvalidOperationException("最後に観測した frame と同じ backend／transform revision で再校正してください。");
        }

        backend = frame.Backend;
        transformRevision = frame.TransformRevision;
        blockingFault = null;
        calibrated = true;
    }

    private void Block(CaptureFault fault)
    {
        blockingFault = fault;
        calibrated = false;
    }
}
