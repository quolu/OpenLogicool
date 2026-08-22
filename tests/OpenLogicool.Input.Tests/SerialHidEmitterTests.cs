using OpenLogicool.Contracts.Devices.Shared;
using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class SerialHidEmitterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(80);

    [Fact]
    public void Connect_sends_hello_and_accepts_matching_ready_contract()
    {
        var exchange = new ScriptedExchange(Ready);

        var session = SerialHidProtocolSession.Connect(
            exchange,
            new SerialHidSemanticVersion(2, 3, 4),
            Timeout);

        var hello = SerialHidProtocolV1.Decode(Assert.Single(exchange.Requests));
        Assert.Equal(SerialHidMessageKind.Hello, hello.Kind);
        Assert.Equal((ushort)1, hello.Sequence);
        Assert.Equal([2, 3, 4, 7, 0], hello.Payload);
        Assert.Equal(new SerialHidSemanticVersion(1, 0, 2), session.ReadyInfo.FirmwareVersion);
        Assert.Equal(SerialHidProtocolV1.AllCapabilities, session.ReadyInfo.Capabilities);
        Assert.Equal((ushort)150, session.ReadyInfo.LeaseMilliseconds);
    }

    [Fact]
    public void Chord_and_mouse_send_one_snapshot_and_commit_after_matching_ack()
    {
        var exchange = new ScriptedExchange(Ready, Ack);
        var emitter = new SerialHidEmitter(Connect(exchange));

        emitter.Emit([Down("Key:LCtrl"), Down("Key:C"), Down("Mouse:Middle")]);

        var request = SerialHidProtocolV1.Decode(exchange.Requests[1]);
        Assert.Equal(SerialHidMessageKind.SetState, request.Kind);
        Assert.Equal((ushort)2, request.Sequence);
        Assert.Equal([0x01, 0x06, 0, 0, 0, 0, 0, 0x04], request.Payload);
        Assert.Equal(1, emitter.Revision);
        Assert.Equal([0x06], emitter.CommittedSnapshot.NormalKeys);
    }

    [Fact]
    public void Finite_sequence_preserves_checkpoint_order_and_waits_for_each_ack()
    {
        var exchange = new ScriptedExchange(Ready, Ack, Ack, Ack, Ack);
        var emitter = new SerialHidEmitter(Connect(exchange));

        emitter.Emit([
            Down("Key:LCtrl"), Down("Key:C"), Up("Key:C"), Up("Key:LCtrl"),
            Down("Key:Enter"), Up("Key:Enter"),
        ]);

        var payloads = exchange.Requests.Skip(1)
            .Select(request => SerialHidProtocolV1.Decode(request).Payload)
            .ToArray();
        Assert.Equal(4, payloads.Length);
        Assert.Equal([0x01, 0x06, 0, 0, 0, 0, 0, 0], payloads[0]);
        Assert.All(payloads[1], value => Assert.Equal((byte)0, value));
        Assert.Equal((byte)0x28, payloads[2][1]);
        Assert.All(payloads[3], value => Assert.Equal((byte)0, value));
        Assert.Equal(4, emitter.Revision);
    }

    [Fact]
    public void Duplicate_ownership_releases_only_after_both_ups()
    {
        var exchange = new ScriptedExchange(Ready, Ack, Ack, Ack, Ack);
        var emitter = new SerialHidEmitter(Connect(exchange));

        emitter.Emit([Down("Key:A")]);
        emitter.Emit([Down("Key:A")]);
        emitter.Emit([Up("Key:A")]);
        Assert.Equal([0x04], emitter.CommittedSnapshot.NormalKeys);

        emitter.Emit([Up("Key:A")]);
        Assert.Empty(emitter.CommittedSnapshot.NormalKeys);
    }

    [Fact]
    public void Seventh_key_faults_before_wire_and_does_not_send_partial_snapshot()
    {
        var exchange = new ScriptedExchange(Ready, Ack);
        var emitter = new SerialHidEmitter(Connect(exchange));
        emitter.Emit([Down("Key:A"), Down("Key:B"), Down("Key:C"), Down("Key:D"), Down("Key:E"), Down("Key:F")]);

        Assert.Throws<SerialHidStateFaultException>(() => emitter.Emit([Down("Key:G")]));

        Assert.Equal(2, exchange.Requests.Count);
        Assert.Equal(1, emitter.Revision);
        Assert.Equal(6, emitter.CommittedSnapshot.NormalKeys.Count);
    }

    [Fact]
    public void Wrong_ack_sequence_does_not_commit_and_makes_session_unavailable_without_retry()
    {
        var exchange = new ScriptedExchange(
            Ready,
            request =>
            {
                var frame = SerialHidProtocolV1.Decode(request);
                return SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, (ushort)(frame.Sequence + 1), []);
            });
        var session = Connect(exchange);
        var emitter = new SerialHidEmitter(session);

        var fault = Assert.Throws<SerialHidSessionFaultException>(() => emitter.Emit([Down("Key:A")]));
        Assert.Equal(SerialHidSessionFaultKind.UnexpectedResponse, fault.Kind);
        Assert.Equal(0, emitter.Revision);

        var unavailable = Assert.Throws<SerialHidSessionFaultException>(() => emitter.Emit([Down("Key:B")]));
        Assert.Equal(SerialHidSessionFaultKind.Unavailable, unavailable.Kind);
        Assert.Equal(2, exchange.Requests.Count);
    }

    [Fact]
    public void Timeout_is_terminal_and_request_is_not_resent()
    {
        var exchange = new ScriptedExchange(
            Ready,
            _ => throw new TimeoutException("fake timeout"));
        var session = Connect(exchange);
        var emitter = new SerialHidEmitter(session);

        var fault = Assert.Throws<SerialHidSessionFaultException>(() => emitter.Emit([Down("Key:A")]));

        Assert.Equal(SerialHidSessionFaultKind.Timeout, fault.Kind);
        Assert.Equal(2, exchange.Requests.Count);
        Assert.Equal(0, emitter.Revision);
        Assert.NotNull(session.TerminalFault);
    }

    [Fact]
    public void Firmware_fault_is_explicit_and_does_not_commit_or_fallback()
    {
        var exchange = new ScriptedExchange(
            Ready,
            request =>
            {
                var frame = SerialHidProtocolV1.Decode(request);
                return SerialHidProtocolV1.Encode(
                    SerialHidMessageKind.Fault,
                    frame.Sequence,
                    [(byte)SerialHidFaultCode.InternalFault, (byte)frame.Kind]);
            });
        var emitter = new SerialHidEmitter(Connect(exchange));

        var fault = Assert.Throws<SerialHidSessionFaultException>(() => emitter.Emit([Down("Mouse:Left")]));

        Assert.Equal(SerialHidSessionFaultKind.FirmwareFault, fault.Kind);
        Assert.Equal(SerialHidFaultCode.InternalFault, fault.FirmwareFaultCode);
        Assert.Equal((byte)SerialHidMessageKind.SetState, fault.OffendingKind);
        Assert.Equal(0, emitter.Revision);
        Assert.Equal(2, exchange.Requests.Count);
    }

    [Fact]
    public void Corrupt_response_is_protocol_fault_without_commit_or_retry()
    {
        var exchange = new ScriptedExchange(
            Ready,
            request =>
            {
                var response = Ack(request);
                response[^1] ^= 0xFF;
                return response;
            });
        var emitter = new SerialHidEmitter(Connect(exchange));

        var fault = Assert.Throws<SerialHidSessionFaultException>(() => emitter.Emit([Down("Key:A")]));

        Assert.Equal(SerialHidSessionFaultKind.Protocol, fault.Kind);
        Assert.Equal(0, emitter.Revision);
        Assert.Equal(2, exchange.Requests.Count);
    }

    [Fact]
    public void Missing_ready_capability_rejects_handshake()
    {
        var exchange = new ScriptedExchange(request => ReadyWithCapabilities(
            request,
            SerialHidCapability.Keyboard6Kro | SerialHidCapability.LeaseRelease));

        var fault = Assert.Throws<SerialHidSessionFaultException>(() => Connect(exchange));

        Assert.Equal(SerialHidSessionFaultKind.UnexpectedResponse, fault.Kind);
        Assert.Single(exchange.Requests);
    }

    [Fact]
    public void Transport_fault_is_terminal_without_automatic_retry()
    {
        var exchange = new ScriptedExchange(
            Ready,
            _ => throw new SerialHidTransportException("device removed"));
        var emitter = new SerialHidEmitter(Connect(exchange));

        var fault = Assert.Throws<SerialHidSessionFaultException>(() => emitter.Emit([Down("Key:A")]));

        Assert.Equal(SerialHidSessionFaultKind.Transport, fault.Kind);
        Assert.Equal(2, exchange.Requests.Count);
        Assert.Equal(0, emitter.Revision);
    }

    [Fact]
    public void All_up_and_heartbeat_use_next_sequence_and_require_ack()
    {
        var exchange = new ScriptedExchange(Ready, Ack, Ack);
        var session = Connect(exchange);

        Assert.Equal((ushort)2, session.SendHeartbeat());
        Assert.Equal((ushort)3, session.SendAllUp());
        Assert.Equal(
            [SerialHidMessageKind.Hello, SerialHidMessageKind.Heartbeat, SerialHidMessageKind.AllUp],
            exchange.Requests.Select(request => SerialHidProtocolV1.Decode(request).Kind).ToArray());
    }

    private static SerialHidProtocolSession Connect(ScriptedExchange exchange) =>
        SerialHidProtocolSession.Connect(exchange, new SerialHidSemanticVersion(1, 0, 0), Timeout);

    private static MappedOutputEdge Down(string output) => new(output, PhysicalInputEdge.Down);
    private static MappedOutputEdge Up(string output) => new(output, PhysicalInputEdge.Up);

    private static byte[] Ready(byte[] request) =>
        ReadyWithCapabilities(request, SerialHidProtocolV1.AllCapabilities);

    private static byte[] ReadyWithCapabilities(byte[] request, SerialHidCapability capabilities)
    {
        var frame = SerialHidProtocolV1.Decode(request);
        return SerialHidProtocolV1.Encode(
            SerialHidMessageKind.Ready,
            frame.Sequence,
            [1, 0, 2, SerialHidProtocolV1.Version, (byte)capabilities, (byte)((ushort)capabilities >> 8), 6, 150, 0]);
    }

    private static byte[] Ack(byte[] request)
    {
        var frame = SerialHidProtocolV1.Decode(request);
        return SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, frame.Sequence, []);
    }

    private sealed class ScriptedExchange(params Func<byte[], byte[]>[] responses) : ISerialHidFrameExchange
    {
        private readonly Queue<Func<byte[], byte[]>> _responses = new(responses);

        public List<byte[]> Requests { get; } = [];

        public byte[] Exchange(ReadOnlyMemory<byte> requestFrame, TimeSpan timeout)
        {
            Assert.Equal(Timeout, timeout);
            var request = requestFrame.ToArray();
            Requests.Add(request);
            Assert.NotEmpty(_responses);
            return _responses.Dequeue()(request);
        }
    }
}
