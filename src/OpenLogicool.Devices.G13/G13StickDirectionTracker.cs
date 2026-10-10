using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.Shared;

namespace OpenLogicool.Devices.G13;

/// <summary>
/// スティックの生値（X/Y 各 0〜255）を上下左右の押下・解放へ変換する pure 状態機械。
/// 実測（docs/probes/g13-input-map-2026-08-15.md・全 control 記録）で可動範囲は両軸とも 0〜255、
/// 中立は X≈143／Y≈120。押下は端から 1/4（63 以下・192 以上）、解放は中立寄りの 80〜175 へ戻った時とし、
/// 境界付近の揺れで押下と解放を繰り返さないよう幅 16 の戻り代を持たせる。
/// 軸ごとに独立に判定するので、斜めは2方向が同時に押下になる。
/// </summary>
public sealed class G13StickDirectionTracker
{
    public const byte LowPressAtOrBelow = 63;
    public const byte LowReleaseAtOrAbove = 80;
    public const byte HighReleaseAtOrBelow = 175;
    public const byte HighPressAtOrAbove = 192;

    private AxisSide xSide;
    private AxisSide ySide;

    private enum AxisSide
    {
        Neutral,
        Low,
        High,
    }

    /// <summary>1 sample を消化し、発生した方向の edge を追加する（解放を押下より先に並べる）。</summary>
    public void Feed(byte x, byte y, List<(string ControlId, PhysicalInputEdge Edge)> edges)
    {
        FeedAxis(x, ref xSide, G13Controls.StickLeft, G13Controls.StickRight, edges);
        FeedAxis(y, ref ySide, G13Controls.StickUp, G13Controls.StickDown, edges);
    }

    private static void FeedAxis(
        byte value,
        ref AxisSide side,
        string lowControlId,
        string highControlId,
        List<(string ControlId, PhysicalInputEdge Edge)> edges)
    {
        var next = side switch
        {
            AxisSide.Low when value < LowReleaseAtOrAbove => AxisSide.Low,
            AxisSide.High when value > HighReleaseAtOrBelow => AxisSide.High,
            _ when value <= LowPressAtOrBelow => AxisSide.Low,
            _ when value >= HighPressAtOrAbove => AxisSide.High,
            _ => AxisSide.Neutral,
        };
        if (next == side)
        {
            return;
        }

        if (side != AxisSide.Neutral)
        {
            edges.Add((side == AxisSide.Low ? lowControlId : highControlId, PhysicalInputEdge.Up));
        }

        if (next != AxisSide.Neutral)
        {
            edges.Add((next == AxisSide.Low ? lowControlId : highControlId, PhysicalInputEdge.Down));
        }

        side = next;
    }
}
