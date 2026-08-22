using System.Buffers.Binary;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

/// <summary>CDC serialの任意分割byte列を、magicで再同期しながらprotocol v1 frameへ組み立てる。</summary>
public sealed class SerialHidResponseFrameAssembler
{
    private readonly byte[] _buffer = new byte[
        SerialHidProtocolV1.HeaderLength
        + SerialHidProtocolV1.MaximumPayloadLength
        + SerialHidProtocolV1.CrcLength];
    private int _length;
    private int _expectedLength;

    public byte[]? Accept(byte value)
    {
        if (_length == 0)
        {
            if (value == SerialHidProtocolV1.Magic0)
            {
                _buffer[_length++] = value;
            }

            return null;
        }

        if (_length == 1 && value != SerialHidProtocolV1.Magic1)
        {
            _length = value == SerialHidProtocolV1.Magic0 ? 1 : 0;
            return null;
        }

        _buffer[_length++] = value;
        if (_length == SerialHidProtocolV1.HeaderLength)
        {
            var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(6, 2));
            if (payloadLength > SerialHidProtocolV1.MaximumPayloadLength)
            {
                var sequence = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(4, 2));
                var offendingKind = _buffer[3];
                Reset();
                throw new SerialHidProtocolException(
                    SerialHidFaultCode.LengthMismatch,
                    sequence,
                    offendingKind,
                    $"response payload length {payloadLength} は上限 {SerialHidProtocolV1.MaximumPayloadLength} を超えます。");
            }

            _expectedLength = SerialHidProtocolV1.HeaderLength
                + payloadLength
                + SerialHidProtocolV1.CrcLength;
        }

        if (_expectedLength == 0 || _length != _expectedLength)
        {
            return null;
        }

        var frame = _buffer.AsSpan(0, _length).ToArray();
        Reset();
        return frame;
    }

    private void Reset()
    {
        _length = 0;
        _expectedLength = 0;
    }
}
