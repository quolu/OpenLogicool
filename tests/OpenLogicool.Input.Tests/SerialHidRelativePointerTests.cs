using System.Buffers.Binary;
using OpenLogicool.Contracts.Devices.Shared;
using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class SerialHidRelativePointerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(80);

    [Fact]
    public void Closed_loop_converges_and_never_sends_minus_128()
    {
        var cursor = new FakeCursor();
        var exchange = new PointerExchange(cursor);
        var session = Connect(exchange);
        var pointer = new SerialHidRelativePointer(session, cursor);

        var receipt = pointer.MoveTo(new SerialHidCursorPoint(320, -200));

        Assert.Equal(new SerialHidCursorPoint(0, 0), receipt.Start);
        Assert.InRange(Math.Abs(receipt.End.X - 320), 0, 2);
        Assert.InRange(Math.Abs(receipt.End.Y + 200), 0, 2);
        Assert.InRange(receipt.DeltaCount, 1, 128);
        var deltas = exchange.Requests
            .Select(frame => SerialHidProtocolV1.Decode(frame))
            .Where(frame => frame.Kind == SerialHidMessageKind.MouseDelta)
            .ToArray();
        Assert.Equal(receipt.DeltaCount, deltas.Length);
        Assert.All(deltas, frame => Assert.DoesNotContain((byte)0x80, frame.Payload));
    }

    [Fact]
    public void Recentered_cursor_aborts_without_fallback()
    {
        var cursor = new FakeCursor { IgnoreDelta = true };
        var exchange = new PointerExchange(cursor);
        var pointer = new SerialHidRelativePointer(Connect(exchange), cursor);

        var fault = Assert.Throws<SerialHidPointerMoveException>(() =>
            pointer.MoveTo(new SerialHidCursorPoint(100, 100)));

        Assert.Contains("cursorが変化しません", fault.Message, StringComparison.Ordinal);
        Assert.Equal(2, exchange.Requests.Count(frame =>
            SerialHidProtocolV1.Decode(frame).Kind == SerialHidMessageKind.MouseDelta));
    }

    [Fact]
    public void Damped_closed_loop_converges_under_accelerated_cursor_gain()
    {
        var cursor = new FakeCursor { Gain = 3 };
        var exchange = new PointerExchange(cursor);
        var pointer = new SerialHidRelativePointer(Connect(exchange), cursor);

        var receipt = pointer.MoveTo(new SerialHidCursorPoint(1362, 995));

        Assert.InRange(Math.Abs(receipt.End.X - 1362), 0, 2);
        Assert.InRange(Math.Abs(receipt.End.Y - 995), 0, 2);
        Assert.InRange(receipt.DeltaCount, 1, 128);
    }

    [Fact]
    public void Button_down_delta_and_up_keep_single_ordered_session()
    {
        var cursor = new FakeCursor();
        var exchange = new PointerExchange(cursor);
        var session = Connect(exchange);
        var emitter = new SerialHidEmitter(session);

        emitter.Emit([new("Mouse:Left", PhysicalInputEdge.Down)]);
        session.SendMouseDelta(5, 0, 0);
        emitter.Emit([new("Mouse:Left", PhysicalInputEdge.Up)]);

        var frames = exchange.Requests.Skip(1)
            .Select(frame => SerialHidProtocolV1.Decode(frame))
            .ToArray();
        Assert.Equal(
            [SerialHidMessageKind.SetState, SerialHidMessageKind.MouseDelta, SerialHidMessageKind.SetState],
            frames.Select(frame => frame.Kind).ToArray());
        Assert.Equal((byte)0x01, frames[0].Payload[7]);
        Assert.Equal((byte)0x00, frames[2].Payload[7]);
    }

    private static SerialHidProtocolSession Connect(PointerExchange exchange) =>
        SerialHidProtocolSession.Connect(
            exchange,
            new SerialHidSemanticVersion(1, 1, 0),
            Timeout,
            SerialHidProtocolV1.AllCapabilities);

    private sealed class FakeCursor : ISerialHidCursorOracle
    {
        public SerialHidCursorPoint Position { get; private set; }
        public bool IgnoreDelta { get; init; }
        public int Gain { get; init; } = 1;

        public SerialHidCursorPoint ReadCurrent() => Position;
        public SerialHidCursorPoint ReadAfterDelta(SerialHidCursorPoint previous) => Position;

        public void Apply(sbyte deltaX, sbyte deltaY)
        {
            if (!IgnoreDelta)
            {
                Position = new SerialHidCursorPoint(
                    Position.X + deltaX * Gain,
                    Position.Y + deltaY * Gain);
            }
        }
    }

    private sealed class PointerExchange(FakeCursor cursor) : ISerialHidFrameExchange
    {
        public List<byte[]> Requests { get; } = [];

        public byte[] Exchange(ReadOnlyMemory<byte> requestFrame, TimeSpan timeout)
        {
            Assert.Equal(Timeout, timeout);
            var bytes = requestFrame.ToArray();
            Requests.Add(bytes);
            var request = SerialHidProtocolV1.Decode(bytes);
            if (request.Kind == SerialHidMessageKind.Hello)
            {
                var capabilities = BinaryPrimitives.ReadUInt16LittleEndian(request.Payload.AsSpan(3));
                return SerialHidProtocolV1.Encode(
                    SerialHidMessageKind.Ready,
                    request.Sequence,
                    [1, 1, 0, 1, (byte)capabilities, (byte)(capabilities >> 8), 6, 150, 0]);
            }
            if (request.Kind == SerialHidMessageKind.MouseDelta)
            {
                cursor.Apply(unchecked((sbyte)request.Payload[0]), unchecked((sbyte)request.Payload[1]));
            }
            return SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, request.Sequence, []);
        }

        public void Dispose()
        {
        }
    }
}
