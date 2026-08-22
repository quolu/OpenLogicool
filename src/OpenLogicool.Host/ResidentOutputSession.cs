using OpenLogicool.Input;

namespace OpenLogicool.Host;

public enum ResidentOutputRoute
{
    SendInput,
    SerialHid,
}

/// <summary>resident fast pathが使うoutput emitterとrelease ownerのlifecycle境界。</summary>
public interface IResidentOutputSession : IDisposable
{
    ResidentOutputRoute Route { get; }
    IOutputEmitter Emitter { get; }
    Exception? BackgroundFailure { get; }
    void Start();
    void Stop();
}

public static class ResidentOutputPolicy
{
    public static void EnsureStartAllowed(ResidentOutputRoute route, bool g600OnboardActive)
    {
        if (route == ResidentOutputRoute.SerialHid && g600OnboardActive)
        {
            throw new InvalidOperationException(
                "G600本体書き込み中はSerial HID出力を同時に使えません。G600本体設定を戻してからSerial HIDを開始してください。");
        }
    }
}

/// <summary>SendInput＋Windows watchdogを一つのresident output sessionとして所有する。</summary>
public sealed class SendInputResidentOutputSession(string watchdogExePath) : IResidentOutputSession
{
    private WatchdogChannel? _watchdog;
    private IOutputEmitter? _emitter;
    private Exception? _backgroundFailure;
    private bool _stopped;

    public ResidentOutputRoute Route => ResidentOutputRoute.SendInput;

    public IOutputEmitter Emitter =>
        _emitter ?? throw new InvalidOperationException("SendInput output sessionは未起動です。");

    public Exception? BackgroundFailure
    {
        get
        {
            if (_watchdog is { HasExited: true } && !_stopped)
            {
                Interlocked.CompareExchange(
                    ref _backgroundFailure,
                    new InvalidOperationException("Windows watchdogがresident実行中に終了しました。"),
                    null);
            }

            return _backgroundFailure;
        }
    }

    public void Start()
    {
        if (_watchdog is not null || _stopped)
        {
            throw new InvalidOperationException("SendInput output sessionは一度しか起動できません。");
        }

        _watchdog = WatchdogChannel.Start(watchdogExePath);
        _emitter = new GuardedOutputEmitter(new SendInputEmitter(), _watchdog);
    }

    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _watchdog?.Shutdown();
    }

    public void Dispose()
    {
        Stop();
        _watchdog?.Dispose();
    }
}

/// <summary>
/// Serial HID emitter、50ms heartbeat、ALL_UP ACK、transport closeを所有するresident output session。
/// hard crashとI/O fault後のrelease保証はfirmware leaseだけが所有する。
/// </summary>
public sealed class SerialHidResidentOutputSession : IResidentOutputSession
{
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromMilliseconds(50);

    private readonly ISerialHidFrameExchange _exchange;
    private readonly SerialHidSemanticVersion _hostVersion;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _heartbeatInterval;
    private readonly ManualResetEventSlim _heartbeatStop = new(false);
    private Thread? _heartbeatThread;
    private SerialHidProtocolSession? _protocol;
    private SerialHidEmitter? _emitter;
    private Exception? _backgroundFailure;
    private bool _started;
    private bool _stopped;
    private bool _disposed;
    private bool _exchangeDisposed;

    public SerialHidResidentOutputSession(
        ISerialHidFrameExchange exchange,
        SerialHidSemanticVersion hostVersion,
        TimeSpan requestTimeout,
        TimeSpan? heartbeatInterval = null)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        var interval = heartbeatInterval ?? DefaultHeartbeatInterval;
        if (interval <= TimeSpan.Zero || interval >= TimeSpan.FromMilliseconds(SerialHidProtocolV1.LeaseMilliseconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(heartbeatInterval),
                $"heartbeat間隔は0より大きくlease {SerialHidProtocolV1.LeaseMilliseconds}ms未満でなければなりません。");
        }

        _exchange = exchange;
        _hostVersion = hostVersion;
        _requestTimeout = requestTimeout;
        _heartbeatInterval = interval;
    }

    internal SerialHidResidentOutputSession(
        ISerialHidFrameExchange exchange,
        SerialHidProtocolSession connectedProtocol,
        TimeSpan heartbeatInterval)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(connectedProtocol);
        if (heartbeatInterval <= TimeSpan.Zero
            || heartbeatInterval >= TimeSpan.FromMilliseconds(SerialHidProtocolV1.LeaseMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval));
        }

        _exchange = exchange;
        _protocol = connectedProtocol;
        _hostVersion = default;
        _requestTimeout = default;
        _heartbeatInterval = heartbeatInterval;
    }

    public ResidentOutputRoute Route => ResidentOutputRoute.SerialHid;

    public IOutputEmitter Emitter =>
        _emitter ?? throw new InvalidOperationException("Serial HID output sessionは未起動です。");

    public Exception? BackgroundFailure => Volatile.Read(ref _backgroundFailure);

    public void Start()
    {
        if (_started || _stopped || _disposed)
        {
            throw new InvalidOperationException("Serial HID output sessionは一度しか起動できません。");
        }

        _protocol ??= SerialHidProtocolSession.Connect(_exchange, _hostVersion, _requestTimeout);
        _emitter = new SerialHidEmitter(_protocol);
        _started = true;
        _heartbeatThread = new Thread(HeartbeatWorker)
        {
            IsBackground = true,
            Name = "OpenLogicoolSerialHidHeartbeat",
        };
        _heartbeatThread.Start();
    }

    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _heartbeatStop.Set();
        if (_heartbeatThread is { IsAlive: true }
            && !_heartbeatThread.Join(TimeSpan.FromSeconds(2)))
        {
            throw new InvalidOperationException("Serial HID heartbeatが2秒以内に停止しませんでした。");
        }

        // background fault後はprotocol sessionがterminalである。再送せずcloseし、firmware leaseへreleaseを委ねる。
        try
        {
            if (_protocol is not null && BackgroundFailure is null)
            {
                _protocol.SendAllUp();
            }
        }
        finally
        {
            DisposeExchange();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Stop();
        }
        finally
        {
            _disposed = true;
            DisposeExchange();
            _heartbeatStop.Dispose();
        }
    }

    private void HeartbeatWorker()
    {
        while (!_heartbeatStop.Wait(_heartbeatInterval))
        {
            try
            {
                _protocol!.SendHeartbeat();
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref _backgroundFailure, exception, null);
                return;
            }
        }
    }

    private void DisposeExchange()
    {
        if (_exchangeDisposed)
        {
            return;
        }

        _exchangeDisposed = true;
        _exchange.Dispose();
    }
}
