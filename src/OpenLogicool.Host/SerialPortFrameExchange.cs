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
        var assembler = new SerialHidResponseFrameAssembler();
        while (true)
        {
            var value = ReadByte(clock, timeout);
            if (assembler.Accept(value) is { } frame)
            {
                return frame;
            }
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
