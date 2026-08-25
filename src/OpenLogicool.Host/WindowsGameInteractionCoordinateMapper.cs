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
}
