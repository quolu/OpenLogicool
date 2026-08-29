using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

public sealed record GameCaptureScreenBounds(int Left, int Top, int Width, int Height);

/// <summary>normalized WGC座標をWindows screen座標へ変換するOS adapter。</summary>
public sealed class WindowsGameInteractionCoordinateMapper(
    Func<GameCaptureScreenBounds> bounds) : IGameInteractionCoordinateMapper
{
    public SerialHidCursorPoint MapTargetCenter(GameInteractionTargetBinding target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var normalized = target.NormalizedBounds;
        return MapNormalized([
            normalized[0] + normalized[2] / 2,
            normalized[1] + normalized[3] / 2,
        ]);
    }

    public SerialHidCursorPoint MapNormalized(IReadOnlyList<double> normalizedPoint)
    {
        if (normalizedPoint.Count != 2)
        {
            throw new ArgumentException("normalized pointはxとyを必要とします。", nameof(normalizedPoint));
        }
        var current = bounds();
        if (current.Width <= 0 || current.Height <= 0)
        {
            throw new InvalidOperationException("capture screen boundsが正ではありません。");
        }
        return new SerialHidCursorPoint(
            checked(current.Left + (int)Math.Round(normalizedPoint[0] * current.Width)),
            checked(current.Top + (int)Math.Round(normalizedPoint[1] * current.Height)));
    }

    /// <summary>
    /// Windows screen座標を、その時点のcapture client frameへ正規化する（操作デモ記録の逆変換）。
    /// frameの外側の点はnullを返す——原本には正規化できた座標だけを書き、
    /// desktop絶対座標を保存しない。
    /// </summary>
    public IReadOnlyList<double>? TryMapScreenToNormalized(int screenX, int screenY)
    {
        var current = bounds();
        if (current.Width <= 0 || current.Height <= 0)
        {
            throw new InvalidOperationException("capture screen boundsが正ではありません。");
        }

        var offsetX = screenX - current.Left;
        var offsetY = screenY - current.Top;
        if (offsetX < 0 || offsetY < 0 || offsetX > current.Width || offsetY > current.Height)
        {
            return null;
        }

        return [(double)offsetX / current.Width, (double)offsetY / current.Height];
    }
}
