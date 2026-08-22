using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

public interface ISerialHidExchangeFactory
{
    ISerialHidFrameExchange Open(SerialHidCandidate candidate);
}

public sealed class SerialPortExchangeFactory : ISerialHidExchangeFactory
{
    public ISerialHidFrameExchange Open(SerialHidCandidate candidate) => new SerialPortFrameExchange(candidate.PortName);
}

/// <summary>CDC serialをbinary frameとして同期一往復するtransport。任意のpartial readを完成frameへ組み立てる。</summary>
public sealed class SerialPortFrameExchange : ISerialHidFrameExchange
{
    private readonly SerialPort _port;
    private bool _disposed;

    public SerialPortFrameExchange(string portName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = false,
            ReadBufferSize = 256,
            WriteBufferSize = 256,
        };

        try
        {
            _port.Open();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _port.Dispose();
            throw new SerialHidTransportException($"serial port {portName} を開けませんでした。", exception);
        }
    }

    public byte[] Exchange(ReadOnlyMemory<byte> requestFrame, TimeSpan timeout)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SerialPortFrameExchange));
        }

        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var timeoutMilliseconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds));
        try
        {
            _port.WriteTimeout = timeoutMilliseconds;
            var request = requestFrame.ToArray();
            _port.Write(request, 0, request.Length);
            return ReadFrame(timeout);
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (SerialHidProtocolException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new SerialHidTransportException("serial frameのwrite/readに失敗しました。", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _port.Dispose();
    }

    private byte[] ReadFrame(TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        var magic0Seen = false;
        while (true)
        {
            var value = ReadByte(clock, timeout);
            if (!magic0Seen)
            {
                magic0Seen = value == SerialHidProtocolV1.Magic0;
                continue;
            }

            if (value != SerialHidProtocolV1.Magic1)
            {
                magic0Seen = value == SerialHidProtocolV1.Magic0;
                continue;
            }

            var header = new byte[SerialHidProtocolV1.HeaderLength];
            header[0] = SerialHidProtocolV1.Magic0;
            header[1] = SerialHidProtocolV1.Magic1;
            ReadExact(header, 2, header.Length - 2, clock, timeout);
            var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6, 2));
            if (payloadLength > SerialHidProtocolV1.MaximumPayloadLength)
            {
                var sequence = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4, 2));
                throw new SerialHidProtocolException(
                    SerialHidFaultCode.LengthMismatch,
                    sequence,
                    header[3],
                    $"response payload length {payloadLength} は上限 {SerialHidProtocolV1.MaximumPayloadLength} を超えます。");
            }

            var frame = new byte[SerialHidProtocolV1.HeaderLength + payloadLength + SerialHidProtocolV1.CrcLength];
            header.CopyTo(frame, 0);
            ReadExact(
                frame,
                SerialHidProtocolV1.HeaderLength,
                payloadLength + SerialHidProtocolV1.CrcLength,
                clock,
                timeout);
            return frame;
        }
    }

    private void ReadExact(byte[] buffer, int offset, int count, Stopwatch clock, TimeSpan timeout)
    {
        var read = 0;
        while (read < count)
        {
            SetRemainingReadTimeout(clock, timeout);
            var current = _port.Read(buffer, offset + read, count - read);
            if (current == 0)
            {
                continue;
            }

            read += current;
        }
    }

    private byte ReadByte(Stopwatch clock, TimeSpan timeout)
    {
        SetRemainingReadTimeout(clock, timeout);
        var value = _port.ReadByte();
        if (value < 0)
        {
            throw new SerialHidTransportException("serial portがresponse frameの途中で閉じました。");
        }

        return (byte)value;
    }

    private void SetRemainingReadTimeout(Stopwatch clock, TimeSpan timeout)
    {
        var remaining = timeout - clock.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException("serial response frameの期限を超えました。");
        }

        _port.ReadTimeout = Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds));
    }
}
