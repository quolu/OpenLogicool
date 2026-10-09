using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

public interface ISerialHidExchangeFactory
{
    ISerialHidFrameExchange Open(SerialHidCandidate candidate);
}

public sealed class SerialPortExchangeFactory(int responseReadPauseMilliseconds = 0) : ISerialHidExchangeFactory
{
    public ISerialHidFrameExchange Open(SerialHidCandidate candidate) => new SerialPortFrameExchange(candidate.PortName, responseReadPauseMilliseconds);
}

/// <summary>CDC serialをbinary frameとして同期一往復するtransport。任意のpartial readを完成frameへ組み立てる。</summary>
public sealed class SerialPortFrameExchange : ISerialHidFrameExchange
{
    private readonly SerialPort _port;
    private readonly int _responseReadPauseMilliseconds;
    private bool _disposed;
    private int _receivedBytes;
    private int _lastReadAvailableBytes;

    public SerialPortFrameExchange(string portName, int responseReadPauseMilliseconds = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        _responseReadPauseMilliseconds = responseReadPauseMilliseconds;
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
        var clock = Stopwatch.StartNew();
        var stage = "送信";
        _receivedBytes = 0;
        _lastReadAvailableBytes = 0;
        try
        {
            _port.WriteTimeout = timeoutMilliseconds;
            var request = requestFrame.ToArray();
            _port.Write(request, 0, request.Length);
            stage = "受信";
            return ReadFrame(timeout);
        }
        catch (TimeoutException error)
        {
            throw new TimeoutException($"serial {_port.PortName}の{stage}で時間切れ（経過={clock.Elapsed.TotalMilliseconds:F1}ms, 読取済み={_receivedBytes} bytes, 直前buffer={_lastReadAvailableBytes} bytes）。元のエラー: {error.Message}", error);
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
        if (_responseReadPauseMilliseconds > 0) Thread.Sleep(_responseReadPauseMilliseconds);
        var assembler = new SerialHidResponseFrameAssembler();
        var deadline = new SerialHidReadDeadline(timeout);
        while (true)
        {
            var value = ReadByte(clock, deadline);
            if (assembler.Accept(value) is { } frame)
            {
                return frame;
            }
        }
    }

    private byte ReadByte(Stopwatch clock, SerialHidReadDeadline deadline)
    {
        _lastReadAvailableBytes = _port.BytesToRead;
        _port.ReadTimeout = deadline.ReadTimeoutMilliseconds(clock.Elapsed, _lastReadAvailableBytes);
        var value = _port.ReadByte();
        if (value < 0)
        {
            throw new SerialHidTransportException("serial portがresponse frameの途中で閉じました。");
        }

        _receivedBytes++;
        return (byte)value;
    }

}

/// <summary>期限に到達していたら、既に受信済みのバイトだけを一度取り出し、不足分を追加で待たない。</summary>
internal sealed class SerialHidReadDeadline(TimeSpan timeout)
{
    private int? bufferedAtDeadline;

    public int ReadTimeoutMilliseconds(TimeSpan elapsed, int availableBytes)
    {
        var remaining = timeout - elapsed;
        if (bufferedAtDeadline is null && remaining > TimeSpan.Zero)
            return Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds));
        bufferedAtDeadline ??= availableBytes;
        if (bufferedAtDeadline <= 0)
            throw new TimeoutException("serial response frameの期限を超え、受信済みのバイトでもframeが完成しませんでした。");
        bufferedAtDeadline--;
        return 1;
    }
}
