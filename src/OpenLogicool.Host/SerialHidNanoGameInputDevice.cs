using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

/// <summary>Nano Serial HIDの具体送信だけを所有するadapter。</summary>
public sealed class SerialHidNanoGameInputDevice(
    SerialHidProtocolSession session,
    SerialHidEmitter emitter,
    ISerialHidCursorOracle cursorOracle) : INanoGameInputDevice
{
    private readonly SerialHidRelativePointer pointer = new(session, cursorOracle);

    public string Hover(SerialHidCursorPoint target) => PointerReceipt(pointer.MoveTo(target));

    public string Click(SerialHidCursorPoint target)
    {
        var move = pointer.MoveTo(target);
        emitter.Emit([Down("Mouse:Left"), Up("Mouse:Left")]);
        return $"{PointerReceipt(move)};mouse-left-down-up";
    }

    public string KeyTap(IReadOnlyList<string> keys)
    {
        emitter.Emit([
            .. keys.Select(Down),
            .. keys.Reverse().Select(Up),
        ]);
        return $"keys:{string.Join("+", keys)}:down-up";
    }

    public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps)
    {
        if (horizontalSteps != 0)
        {
            throw new NotSupportedException("Nano Serial HID 1.1.0はhorizontal wheelをサポートしません。");
        }
        if (verticalSteps is < sbyte.MinValue or > sbyte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(verticalSteps));
        }
        var move = pointer.MoveTo(target);
        var ack = session.SendMouseDelta(0, 0, checked((sbyte)verticalSteps));
        return $"{PointerReceipt(move)};wheel:{verticalSteps};ack:{ack}";
    }

    public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination)
    {
        var startMove = pointer.MoveTo(start);
        emitter.Emit([Down("Mouse:Left")]);
        SerialHidPointerMoveReceipt? dragMove = null;
        try
        {
            dragMove = pointer.MoveTo(destination);
        }
        finally
        {
            emitter.Emit([Up("Mouse:Left")]);
        }
        return $"start:{PointerReceipt(startMove)};drag:{PointerReceipt(dragMove!)};mouse-left-up";
    }

    private static string PointerReceipt(SerialHidPointerMoveReceipt receipt) =>
        $"pointer:{receipt.Start.X},{receipt.Start.Y}->{receipt.End.X},{receipt.End.Y};deltas:{receipt.DeltaCount}";

    private static MappedOutputEdge Down(string token) => new(token, PhysicalInputEdge.Down);

    private static MappedOutputEdge Up(string token) => new(token, PhysicalInputEdge.Up);
}
