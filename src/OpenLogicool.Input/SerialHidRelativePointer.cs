namespace OpenLogicool.Input;

public readonly record struct SerialHidCursorPoint(int X, int Y);

public interface ISerialHidCursorOracle
{
    SerialHidCursorPoint ReadCurrent();
    SerialHidCursorPoint ReadAfterDelta(SerialHidCursorPoint previous);
}

public sealed class SerialHidPointerMoveException(string message) : Exception(message);

public sealed record SerialHidPointerMoveReceipt(
    SerialHidCursorPoint Start,
    SerialHidCursorPoint Target,
    SerialHidCursorPoint End,
    int DeltaCount);

/// <summary>
/// Nanoのrelative mouseとOS cursor readbackを閉ループにし、mouse acceleration下でもscreen座標へ収束させる。
/// </summary>
public sealed class SerialHidRelativePointer(
    SerialHidProtocolSession session,
    ISerialHidCursorOracle cursorOracle)
{
    public SerialHidPointerMoveReceipt MoveTo(
        SerialHidCursorPoint target,
        int tolerance = 2,
        int maximumDelta = 64,
        int maximumSteps = 128,
        int maximumConsecutiveNoProgress = 2)
    {
        if (tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }
        if (maximumDelta is < 1 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDelta));
        }
        if (maximumSteps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSteps));
        }
        if (maximumConsecutiveNoProgress <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConsecutiveNoProgress));
        }

        var start = cursorOracle.ReadCurrent();
        var current = start;
        var noProgress = 0;
        for (var step = 0; step < maximumSteps; step++)
        {
            if (WithinTolerance(current, target, tolerance))
            {
                return new SerialHidPointerMoveReceipt(start, target, current, step);
            }

            var deltaX = DampedDelta((long)target.X - current.X, maximumDelta);
            var deltaY = DampedDelta((long)target.Y - current.Y, maximumDelta);
            session.SendMouseDelta(deltaX, deltaY, 0);
            var observed = cursorOracle.ReadAfterDelta(current);
            noProgress = observed == current ? noProgress + 1 : 0;
            if (noProgress >= maximumConsecutiveNoProgress)
            {
                throw new SerialHidPointerMoveException(
                    $"Nano relative pointerを{noProgress}回送ってもcursorが変化しませんでした"
                    + $"（cursor=({current.X},{current.Y}) target=({target.X},{target.Y})）。fallbackせず停止します。");
            }
            current = observed;
        }

        throw new SerialHidPointerMoveException(
            $"Nano relative pointerが{maximumSteps} stepでtarget ({target.X},{target.Y})へ収束しませんでした。"
            + $" final=({current.X},{current.Y})。fallbackせず停止します。");
    }

    /// <summary>
    /// 減速せず一定の刻みでtargetへ進む。進んだ割合がpassRatioを越えた直後に一度だけonPassedを呼び、
    /// 止まらずにtargetまで進み続ける。動いている途中でボタンを離す操作（払う操作）のために使う。
    /// </summary>
    public SerialHidPointerMoveReceipt GlideTo(
        SerialHidCursorPoint target,
        double passRatio,
        Action onPassed,
        int stepDelta = 12,
        int maximumSteps = 256,
        int maximumConsecutiveNoProgress = 2)
    {
        ArgumentNullException.ThrowIfNull(onPassed);
        if (!double.IsFinite(passRatio) || passRatio is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(passRatio));
        }
        if (stepDelta is < 1 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(stepDelta));
        }
        if (maximumSteps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSteps));
        }
        if (maximumConsecutiveNoProgress <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConsecutiveNoProgress));
        }

        var start = cursorOracle.ReadCurrent();
        double spanX = (long)target.X - start.X;
        double spanY = (long)target.Y - start.Y;
        var length = Math.Sqrt((spanX * spanX) + (spanY * spanY));
        if (length < stepDelta)
        {
            throw new ArgumentException("GlideToは一刻みより長い移動を必要とします。", nameof(target));
        }

        var deltaX = checked((sbyte)Math.Round(spanX / length * stepDelta));
        var deltaY = checked((sbyte)Math.Round(spanY / length * stepDelta));
        var current = start;
        var passed = false;
        var noProgress = 0;
        for (var step = 0; step < maximumSteps; step++)
        {
            var travelled = ((((long)current.X - start.X) * spanX) + (((long)current.Y - start.Y) * spanY)) / (length * length);
            if (!passed && travelled >= passRatio)
            {
                passed = true;
                onPassed();
            }
            if (travelled >= 1)
            {
                return new SerialHidPointerMoveReceipt(start, target, current, step);
            }

            session.SendMouseDelta(deltaX, deltaY, 0);
            var observed = cursorOracle.ReadAfterDelta(current);
            noProgress = observed == current ? noProgress + 1 : 0;
            if (noProgress >= maximumConsecutiveNoProgress)
            {
                throw new SerialHidPointerMoveException(
                    $"Nano relative pointerを{noProgress}回送ってもcursorが変化しませんでした"
                    + $"（cursor=({current.X},{current.Y}) target=({target.X},{target.Y})）。fallbackせず停止します。");
            }
            current = observed;
        }

        throw new SerialHidPointerMoveException(
            $"Nano relative pointerが{maximumSteps} stepでtarget ({target.X},{target.Y})を通過しませんでした。"
            + $" final=({current.X},{current.Y})。fallbackせず停止します。");
    }

    private static bool WithinTolerance(
        SerialHidCursorPoint current,
        SerialHidCursorPoint target,
        int tolerance) =>
        Math.Abs((long)target.X - current.X) <= tolerance
        && Math.Abs((long)target.Y - current.Y) <= tolerance;

    private static sbyte DampedDelta(long error, int maximumDelta)
    {
        if (error == 0)
        {
            return 0;
        }

        var damped = error / 4;
        if (damped == 0)
        {
            damped = Math.Sign(error);
        }
        return checked((sbyte)Math.Clamp(damped, -maximumDelta, maximumDelta));
    }
}
