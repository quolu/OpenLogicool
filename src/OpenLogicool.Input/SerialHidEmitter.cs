using System.Buffers.Binary;

namespace OpenLogicool.Input;

/// <summary>Serial HID frameを1回だけ送信し、同じrequestへの応答frameを返すtransport境界。</summary>
public interface ISerialHidFrameExchange : IDisposable
{
    byte[] Exchange(ReadOnlyMemory<byte> requestFrame, TimeSpan timeout);
}

/// <summary>serial open、write、readの境界で起きたI/O fault。requestの自動再送は呼出側も行わない。</summary>
public sealed class SerialHidTransportException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public enum SerialHidSessionFaultKind
{
    Timeout,
    Transport,
    Protocol,
    UnexpectedResponse,
    FirmwareFault,
    Unavailable,
    UnsupportedCapability,
}

/// <summary>Serial HID sessionを継続できない明示fault。</summary>
public sealed class SerialHidSessionFaultException : Exception
{
    internal SerialHidSessionFaultException(
        SerialHidSessionFaultKind kind,
        string message,
        ushort? sequence = null,
        SerialHidFaultCode? firmwareFaultCode = null,
        byte? offendingKind = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Sequence = sequence;
        FirmwareFaultCode = firmwareFaultCode;
        OffendingKind = offendingKind;
    }

    public SerialHidSessionFaultKind Kind { get; }
    public ushort? Sequence { get; }
    public SerialHidFaultCode? FirmwareFaultCode { get; }
    public byte? OffendingKind { get; }
}

public readonly record struct SerialHidSemanticVersion(byte Major, byte Minor, byte Patch);

public sealed record SerialHidReadyInfo(
    SerialHidSemanticVersion FirmwareVersion,
    SerialHidCapability Capabilities,
    byte MaximumNormalKeys,
    ushort LeaseMilliseconds);

/// <summary>
/// HELLO後のSerial HID requestを1件ずつ送るhost protocol session。
/// 応答が曖昧になったsessionはterminal faultへ遷移し、同じ要求も次の要求も自動送信しない。
/// </summary>
public sealed class SerialHidProtocolSession
{
    private readonly object _gate = new();
    private readonly ISerialHidFrameExchange _exchange;
    private readonly TimeSpan _requestTimeout;
    private SerialHidSessionFaultException? _terminalFault;
    private ushort _lastIssuedSequence;

    private SerialHidProtocolSession(ISerialHidFrameExchange exchange, TimeSpan requestTimeout)
    {
        _exchange = exchange;
        _requestTimeout = requestTimeout;
    }

    public SerialHidReadyInfo ReadyInfo { get; private set; } = null!;
    public SerialHidSessionFaultException? TerminalFault => _terminalFault;
    public ushort LastIssuedSequence => _lastIssuedSequence;

    public static SerialHidProtocolSession Connect(
        ISerialHidFrameExchange exchange,
        SerialHidSemanticVersion hostVersion,
        TimeSpan requestTimeout,
        SerialHidCapability requestedCapabilities = SerialHidProtocolV1.BaselineCapabilities,
        TimeSpan? handshakeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "request timeoutは正でなければなりません。");
        }
        var helloTimeout = handshakeTimeout ?? requestTimeout;
        if (helloTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handshakeTimeout), "handshake timeoutは正でなければなりません。");
        }

        if (requestedCapabilities == 0
            || (requestedCapabilities & ~SerialHidProtocolV1.AllCapabilities) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedCapabilities), "要求capabilityがSerial HID v1の範囲外です。");
        }

        var session = new SerialHidProtocolSession(exchange, requestTimeout);
        var helloPayload = new byte[5];
        helloPayload[0] = hostVersion.Major;
        helloPayload[1] = hostVersion.Minor;
        helloPayload[2] = hostVersion.Patch;
        BinaryPrimitives.WriteUInt16LittleEndian(helloPayload.AsSpan(3), (ushort)requestedCapabilities);

        lock (session._gate)
        {
            var ready = session.ExchangeCore(
                SerialHidMessageKind.Hello,
                helloPayload,
                SerialHidMessageKind.Ready,
                helloTimeout);
            var capabilities = (SerialHidCapability)BinaryPrimitives.ReadUInt16LittleEndian(ready.Payload.AsSpan(4, 2));
            if ((capabilities & requestedCapabilities) != requestedCapabilities)
            {
                throw session.Latch(new SerialHidSessionFaultException(
                    SerialHidSessionFaultKind.UnexpectedResponse,
                    $"READY capability 0x{(ushort)capabilities:X4} は要求 0x{(ushort)requestedCapabilities:X4} を満たしません。",
                    ready.Sequence));
            }

            session.ReadyInfo = new SerialHidReadyInfo(
                new SerialHidSemanticVersion(ready.Payload[0], ready.Payload[1], ready.Payload[2]),
                capabilities,
                ready.Payload[6],
                BinaryPrimitives.ReadUInt16LittleEndian(ready.Payload.AsSpan(7, 2)));
        }

        return session;
    }

    /// <summary>
    /// sequenceを確保してからpayloadを構築し、matching ACKだけを成功として返す。
    /// payloadFactoryが投げた場合はwireへ送らずsequenceも消費しない。
    /// </summary>
    public ushort SendSetState(Func<ushort, byte[]> payloadFactory)
    {
        ArgumentNullException.ThrowIfNull(payloadFactory);
        lock (_gate)
        {
            ThrowIfUnavailable();
            var sequence = SerialHidProtocolV1.NextRequestSequence(_lastIssuedSequence);
            var payload = payloadFactory(sequence);
            ArgumentNullException.ThrowIfNull(payload);
            return ExchangeCore(SerialHidMessageKind.SetState, payload, SerialHidMessageKind.Ack).Sequence;
        }
    }

    public ushort SendAllUp()
    {
        lock (_gate)
        {
            ThrowIfUnavailable();
            return ExchangeCore(SerialHidMessageKind.AllUp, [], SerialHidMessageKind.Ack).Sequence;
        }
    }

    public ushort SendHeartbeat()
    {
        lock (_gate)
        {
            ThrowIfUnavailable();
            return ExchangeCore(SerialHidMessageKind.Heartbeat, [], SerialHidMessageKind.Ack).Sequence;
        }
    }

    /// <summary>
    /// 相対mouse reportを一度だけ要求する。ACKが失われた場合は適用済みか不明のままterminal faultとし、
    /// 同じdeltaを再送しない。
    /// </summary>
    public ushort SendMouseDelta(sbyte deltaX, sbyte deltaY, sbyte wheel)
    {
        lock (_gate)
        {
            ThrowIfUnavailable();
            if ((ReadyInfo.Capabilities & SerialHidCapability.RelativeMouse) == 0)
            {
                throw new SerialHidSessionFaultException(
                    SerialHidSessionFaultKind.UnsupportedCapability,
                    "Serial HID sessionはRelativeMouse capabilityを交渉していません。再flashまたは対応sessionが必要です。");
            }

            return ExchangeCore(
                SerialHidMessageKind.MouseDelta,
                [unchecked((byte)deltaX), unchecked((byte)deltaY), unchecked((byte)wheel)],
                SerialHidMessageKind.Ack).Sequence;
        }
    }

    private SerialHidFrame ExchangeCore(
        SerialHidMessageKind requestKind,
        ReadOnlySpan<byte> payload,
        SerialHidMessageKind expectedResponseKind,
        TimeSpan? timeout = null)
    {
        var sequence = SerialHidProtocolV1.NextRequestSequence(_lastIssuedSequence);
        var request = SerialHidProtocolV1.Encode(requestKind, sequence, payload);
        _lastIssuedSequence = sequence;

        byte[] responseBytes;
        try
        {
            responseBytes = _exchange.Exchange(request, timeout ?? _requestTimeout);
        }
        catch (TimeoutException exception)
        {
            throw Latch(new SerialHidSessionFaultException(
                SerialHidSessionFaultKind.Timeout,
                $"Serial HID {requestKind} sequence {sequence} の応答がtimeoutしました。自動再送しません。",
                sequence,
                innerException: exception));
        }
        catch (SerialHidTransportException exception)
        {
            throw Latch(new SerialHidSessionFaultException(
                SerialHidSessionFaultKind.Transport,
                $"Serial HID {requestKind} sequence {sequence} のI/Oに失敗しました。自動再送しません。",
                sequence,
                innerException: exception));
        }
        catch (SerialHidProtocolException exception)
        {
            throw Latch(new SerialHidSessionFaultException(
                SerialHidSessionFaultKind.Protocol,
                $"Serial HID {requestKind} sequence {sequence} の応答frameが破損または非互換です。自動再送しません。",
                sequence,
                innerException: exception));
        }

        SerialHidFrame response;
        try
        {
            response = SerialHidProtocolV1.Decode(responseBytes);
        }
        catch (SerialHidProtocolException exception)
        {
            throw Latch(new SerialHidSessionFaultException(
                SerialHidSessionFaultKind.Protocol,
                $"Serial HID {requestKind} sequence {sequence} の応答frameが破損または非互換です。自動再送しません。",
                sequence,
                innerException: exception));
        }

        if (response.Sequence != sequence
            && !(response.Kind == SerialHidMessageKind.Fault && response.Sequence == 0))
        {
            throw Latch(new SerialHidSessionFaultException(
                SerialHidSessionFaultKind.UnexpectedResponse,
                $"Serial HID応答sequence {response.Sequence} は要求 {sequence} と一致しません。自動再送しません。",
                sequence));
        }

        if (response.Kind == SerialHidMessageKind.Fault)
        {
            var code = (SerialHidFaultCode)response.Payload[0];
            throw Latch(new SerialHidSessionFaultException(
                SerialHidSessionFaultKind.FirmwareFault,
                $"firmwareが{code}を返しました（request={requestKind}, sequence={sequence}, offending=0x{response.Payload[1]:X2}）。自動再送しません。",
                sequence,
                code,
                response.Payload[1]));
        }

        if (response.Kind != expectedResponseKind)
        {
            throw Latch(new SerialHidSessionFaultException(
                SerialHidSessionFaultKind.UnexpectedResponse,
                $"Serial HID {requestKind} sequence {sequence} へ{response.Kind}が返りました。期待は{expectedResponseKind}です。",
                sequence));
        }

        return response;
    }

    private void ThrowIfUnavailable()
    {
        if (_terminalFault is null)
        {
            return;
        }

        throw new SerialHidSessionFaultException(
            SerialHidSessionFaultKind.Unavailable,
            $"Serial HID sessionは既発fault（{_terminalFault.Kind}）後のため再利用できません。再接続が必要です。",
            _terminalFault.Sequence,
            innerException: _terminalFault);
    }

    private SerialHidSessionFaultException Latch(SerialHidSessionFaultException fault)
    {
        _terminalFault ??= fault;
        return fault;
    }
}

/// <summary>Mapping Runtimeのedge列を完全HID snapshotへ変換し、ACK後だけ所有状態を確定するemitter。</summary>
public sealed class SerialHidEmitter(SerialHidProtocolSession session) : IOutputEmitter
{
    private readonly object _gate = new();
    private readonly SerialHidOwnershipState _ownership = new();

    public long Revision => _ownership.Revision;
    public SerialHidSnapshot CommittedSnapshot => _ownership.CommittedSnapshot;

    public void Emit(IReadOnlyList<MappedOutputEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (edges.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var checkpoint in SerialHidCheckpointBuilder.Build(edges))
            {
                PreparedSerialHidState? prepared = null;
                var acknowledgedSequence = session.SendSetState(sequence =>
                {
                    prepared = _ownership.Prepare(checkpoint, sequence);
                    return prepared.Snapshot.ToSetStatePayload();
                });
                _ownership.CommitAck(prepared!, acknowledgedSequence);
            }
        }
    }
}
