using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Perception;

namespace OpenLogicool.Host;

/// <summary>描画領域の画像を複数回比較し、演出中の変化と静止した未知画面を区別する。</summary>
internal sealed class VisualProgressSceneMonitor
{
    private VisualPatchSignature? reference;

    public (bool Changed, double Difference) Observe(CapturedFrame frame, FrameRect viewport)
    {
        var current = VisualPatchMatcher.Capture(frame,
            [viewport.X / frame.Width, viewport.Y / frame.Height, viewport.Width / frame.Width, viewport.Height / frame.Height]);
        var difference = reference is null ? 0 : VisualPatchSignatureComparer.MeanAbsoluteDifference(reference, current);
        // 既存の画面同一性判定と同じ輝度差を使う。小さい変化は基準画像へ累積して比較する。
        var changed = difference >= 6;
        if (reference is null || changed) reference = current;
        return (changed, difference);
    }
}
