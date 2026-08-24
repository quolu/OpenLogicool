using System.Buffers.Binary;

namespace OpenLogicool.Input;

[Flags]
public enum SerialHidCapability : ushort
{
    Keyboard6Kro = 0x0001,
    MouseButtons = 0x0002,
    LeaseRelease = 0x0004,
    RelativeMouse = 0x0008,
}

public enum SerialHidMessageKind : byte
{
    Hello = 0x01,
    Ready = 0x02,
    SetState = 0x03,
    AllUp = 0x04,
    Heartbeat = 0x05,
    Ack = 0x06,
    Fault = 0x07,
    MouseDelta = 0x08,
}

public enum SerialHidFaultCode : byte
{
    BadMagic = 0x01,
    UnsupportedVersion = 0x02,
    ChecksumMismatch = 0x03,
    LengthMismatch = 0x04,
    UnknownMessage = 0x05,
    InvalidPayload = 0x06,
    UnsupportedCapability = 0x07,
    SequenceViolation = 0x08,
    InternalFault = 0x09,
}

public sealed class SerialHidProtocolException(
    SerialHidFaultCode faultCode,
    ushort sequence,
    byte offendingKind,
    string message) : Exception(message)
{
    public SerialHidFaultCode FaultCode { get; } = faultCode;
    public ushort Sequence { get; } = sequence;
    public byte OffendingKind { get; } = offendingKind;
}

public sealed record SerialHidFrame(
    byte Version,
    SerialHidMessageKind Kind,
    ushort Sequence,
    byte[] Payload);

/// <summary>OpenLogicool Serial HID protocol v1 のbyte-level契約。I/Oを持たない。</summary>
public static class SerialHidProtocolV1
{
    public const byte Magic0 = 0x4F; // O
    public const byte Magic1 = 0x4C; // L
    public const byte Version = 0x01;
    public const int HeaderLength = 8;
    public const int CrcLength = 2;
    public const int MaximumPayloadLength = 32;
    public const int SetStatePayloadLength = 8;
    public const int MouseDeltaPayloadLength = 3;
    public const ushort FirstRequestSequence = 1;
    public const ushort LeaseMilliseconds = 150;
    public const byte MaximumNormalKeys = 6;
    public const SerialHidCapability BaselineCapabilities =
        SerialHidCapability.Keyboard6Kro | SerialHidCapability.MouseButtons | SerialHidCapability.LeaseRelease;
    public const SerialHidCapability AllCapabilities =
        BaselineCapabilities | SerialHidCapability.RelativeMouse;

    public static ushort NextRequestSequence(ushort current) =>
        current is 0 or ushort.MaxValue ? FirstRequestSequence : (ushort)(current + 1);

    public static byte[] Encode(SerialHidMessageKind kind, ushort sequence, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), $"payload は {MaximumPayloadLength} byte 以下です。");
        }

        ValidatePayload(kind, payload, sequence, (byte)kind);
        ValidateSequence(kind, sequence, (byte)kind);

        var frame = new byte[HeaderLength + payload.Length + CrcLength];
        frame[0] = Magic0;
        frame[1] = Magic1;
        frame[2] = Version;
        frame[3] = (byte)kind;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), (ushort)payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderLength));
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(frame.Length - CrcLength),
            ComputeCrc(frame.AsSpan(0, frame.Length - CrcLength)));
        return frame;
    }

    public static SerialHidFrame Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderLength + CrcLength)
        {
            throw Fault(SerialHidFaultCode.LengthMismatch, 0, 0, "frame が最小長に達していません。");
        }

        var sequence = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(4, 2));
        var rawKind = frame[3];
        if (frame[0] != Magic0 || frame[1] != Magic1)
        {
            throw Fault(SerialHidFaultCode.BadMagic, 0, 0, "magic が一致しません。");
        }

        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(6, 2));
        if (payloadLength > MaximumPayloadLength
            || frame.Length != HeaderLength + payloadLength + CrcLength)
        {
            throw Fault(SerialHidFaultCode.LengthMismatch, sequence, rawKind, "payload length とframe長が一致しません。");
        }

        var expectedCrc = BinaryPrimitives.ReadUInt16LittleEndian(frame[^CrcLength..]);
        var actualCrc = ComputeCrc(frame[..^CrcLength]);
        if (actualCrc != expectedCrc)
        {
            throw Fault(SerialHidFaultCode.ChecksumMismatch, sequence, rawKind, "CRCが一致しません。");
        }

        if (frame[2] != Version)
        {
            throw Fault(SerialHidFaultCode.UnsupportedVersion, sequence, rawKind, $"protocol version {frame[2]} は未対応です。");
        }

        if (!Enum.IsDefined(typeof(SerialHidMessageKind), rawKind))
        {
            throw Fault(SerialHidFaultCode.UnknownMessage, sequence, rawKind, $"message kind 0x{rawKind:X2} は未対応です。");
        }

        var kind = (SerialHidMessageKind)rawKind;
        var payload = frame.Slice(HeaderLength, payloadLength).ToArray();
        ValidatePayload(kind, payload, sequence, rawKind);
        ValidateSequence(kind, sequence, rawKind);
        return new SerialHidFrame(frame[2], kind, sequence, payload);
    }

    /// <summary>CRC-16/CCITT-FALSE: poly=0x1021, init=0xFFFF, refin/refout=false, xorout=0。</summary>
    public static ushort ComputeCrc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
            }
        }

        return crc;
    }

    private static void ValidatePayload(
        SerialHidMessageKind kind,
        ReadOnlySpan<byte> payload,
        ushort sequence,
        byte rawKind)
    {
        var expectedLength = kind switch
        {
            SerialHidMessageKind.Hello => 5,
            SerialHidMessageKind.Ready => 9,
            SerialHidMessageKind.SetState => SetStatePayloadLength,
            SerialHidMessageKind.AllUp => 0,
            SerialHidMessageKind.Heartbeat => 0,
            SerialHidMessageKind.Ack => 0,
            SerialHidMessageKind.Fault => 2,
            SerialHidMessageKind.MouseDelta => MouseDeltaPayloadLength,
            _ => throw Fault(SerialHidFaultCode.UnknownMessage, sequence, rawKind, $"message kind 0x{rawKind:X2} は未対応です。"),
        };

        if (payload.Length != expectedLength)
        {
            throw Fault(
                SerialHidFaultCode.InvalidPayload,
                sequence,
                rawKind,
                $"{kind} payload は {expectedLength} byte 固定ですが {payload.Length} byteでした。");
        }

        if (kind == SerialHidMessageKind.SetState)
        {
            ValidateSetState(payload, sequence, rawKind);
        }
        else if (kind == SerialHidMessageKind.MouseDelta)
        {
            ValidateMouseDelta(payload, sequence, rawKind);
        }
        else if (kind == SerialHidMessageKind.Hello)
        {
            ValidateCapabilities(BinaryPrimitives.ReadUInt16LittleEndian(payload[3..]), sequence, rawKind);
        }
        else if (kind == SerialHidMessageKind.Ready)
        {
            if (payload[3] != Version
                || payload[6] != MaximumNormalKeys
                || BinaryPrimitives.ReadUInt16LittleEndian(payload[7..]) != LeaseMilliseconds)
            {
                throw Fault(SerialHidFaultCode.InvalidPayload, sequence, rawKind, "READYのprotocol、6KRO、lease契約がv1と一致しません。");
            }

            ValidateCapabilities(BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]), sequence, rawKind);
        }
        else if (kind == SerialHidMessageKind.Fault
                 && !Enum.IsDefined(typeof(SerialHidFaultCode), payload[0]))
        {
            throw Fault(SerialHidFaultCode.InvalidPayload, sequence, rawKind, "FAULT codeが未定義です。");
        }
    }

    private static void ValidateMouseDelta(ReadOnlySpan<byte> payload, ushort sequence, byte rawKind)
    {
        if (payload.IndexOf((byte)0x80) >= 0)
        {
            throw Fault(
                SerialHidFaultCode.InvalidPayload,
                sequence,
                rawKind,
                "MOUSE_DELTAのdx、dy、wheelはそれぞれ-127..127でなければなりません。");
        }
    }

    private static void ValidateSetState(ReadOnlySpan<byte> payload, ushort sequence, byte rawKind)
    {
        var keys = payload.Slice(1, 6);
        var zeroSeen = false;
        byte previousUsage = 0;
        foreach (var usage in keys)
        {
            if (usage == 0)
            {
                zeroSeen = true;
                continue;
            }

            if (zeroSeen || usage <= previousUsage || usage is < 0x04 or >= 0xE0)
            {
                throw Fault(
                    SerialHidFaultCode.InvalidPayload,
                    sequence,
                    rawKind,
                    "SET_STATEの通常keyは重複なし昇順・末尾zero paddingでなければなりません。");
            }

            previousUsage = usage;
        }

        if ((payload[7] & 0xE0) != 0)
        {
            throw Fault(SerialHidFaultCode.InvalidPayload, sequence, rawKind, "mouse button maskの予約bitが立っています。");
        }
    }

    private static void ValidateCapabilities(ushort rawCapabilities, ushort sequence, byte rawKind)
    {
        if ((rawCapabilities & ~(ushort)AllCapabilities) != 0)
        {
            throw Fault(
                SerialHidFaultCode.UnsupportedCapability,
                sequence,
                rawKind,
                $"未知のcapability bit 0x{rawCapabilities & ~(ushort)AllCapabilities:X4}があります。");
        }
    }

    private static void ValidateSequence(SerialHidMessageKind kind, ushort sequence, byte rawKind)
    {
        if (sequence == 0 && kind != SerialHidMessageKind.Fault)
        {
            throw Fault(
                SerialHidFaultCode.SequenceViolation,
                sequence,
                rawKind,
                $"{kind}は相関可能なsequence 1〜65535を必要とします。");
        }
    }

    private static SerialHidProtocolException Fault(
        SerialHidFaultCode code,
        ushort sequence,
        byte kind,
        string message) => new(code, sequence, kind, message);
}
