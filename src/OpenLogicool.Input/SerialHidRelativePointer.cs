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
                    $"Nano relative pointerを{noProgress}回送ってもcursorが変化しませんでした。fallbackせず停止します。");
            }
            current = observed;
        }

        throw new SerialHidPointerMoveException(
            $"Nano relative pointerが{maximumSteps} stepでtarget ({target.X},{target.Y})へ収束しませんでした。"
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
