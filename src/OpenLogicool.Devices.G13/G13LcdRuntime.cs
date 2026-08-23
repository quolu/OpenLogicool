namespace OpenLogicool.Devices.G13;

public interface IG13LcdTransport
{
    string? TryGetConnectionKey();

    int Write(ReadOnlyMemory<byte> report);
}

public sealed class G13LcdHidTransport : IG13LcdTransport
{
    private readonly G13LcdHidAccess access = new();

    public string? TryGetConnectionKey()
    {
        var candidates = access.EnumerateCollections()
            .Where(collection => collection.OutputReportByteLength == G13LcdFrame.ReportLength)
            .ToArray();
        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0].DevicePath,
            _ => throw new InvalidOperationException(
                $"G13に{G13LcdFrame.ReportLength}-byte output collectionが{candidates.Length}件あり、一意に選べません。"),
        };
    }

    public int Write(ReadOnlyMemory<byte> report)
    {
        using var handle = access.Open();
        return handle.Write(report.Span);
    }
}

public sealed record G13LcdRuntimeStatus(
    bool IsRunning,
    bool IsConnected,
    long RequestedRevision,
    long AppliedRevision,
    DateTime? LastUpdatedUtc,
    string? Failure);

/// <summary>
/// G13 LCDの低優先度resident worker。最新frameだけを保持し、fast pathを待たせずにWriteFileへ送る。
/// 接続keyを周期観測し、抜差し後はstale handleを使わず最新frameを再表示する。
/// </summary>
public sealed class G13LcdRuntime : IDisposable
{
    private readonly IG13LcdTransport transport;
    private readonly TimeSpan connectionPollInterval;
    private readonly AutoResetEvent wake = new(false);
    private readonly object gate = new();
    private readonly object cycleGate = new();
    private Thread? worker;
    private byte[]? latestReport;
    private long requestedRevision;
    private long appliedRevision;
    private string? connectionKey;
    private DateTime? lastUpdatedUtc;
    private string? failure;
    private volatile bool stopRequested;
    private bool started;
    private bool stopped;
    private bool connected;

    public G13LcdRuntime(IG13LcdTransport transport, TimeSpan? connectionPollInterval = null)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.connectionPollInterval = connectionPollInterval ?? TimeSpan.FromSeconds(1);
        if (this.connectionPollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(connectionPollInterval));
        }
    }

    public G13LcdRuntimeStatus Status
    {
        get
        {
            lock (gate)
            {
                return new G13LcdRuntimeStatus(
                    started && !stopped,
                    connected,
                    requestedRevision,
                    appliedRevision,
                    lastUpdatedUtc,
                    failure);
            }
        }
    }

    public void Start()
    {
        lock (gate)
        {
            if (started || stopped)
            {
                throw new InvalidOperationException("G13 LCD runtimeは一度しか起動できません。");
            }

            started = true;
            worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "OpenLogicoolG13Lcd",
                Priority = ThreadPriority.BelowNormal,
            };
            worker.Start();
        }
    }

    public long RequestFrame(ReadOnlySpan<byte> framebuffer)
    {
        var report = G13LcdFrame.BuildWireReport(framebuffer);
        long revision;
        lock (gate)
        {
            if (stopped)
            {
                throw new InvalidOperationException("停止済みのG13 LCD runtimeへframeを要求できません。");
            }

            latestReport = report;
            revision = ++requestedRevision;
        }

        wake.Set();
        return revision;
    }

    /// <summary>workerと同じ一周期を同期実行するfocused test seam。</summary>
    public void RunOnce()
    {
        lock (cycleGate)
        {
            RunOnceCore();
        }
    }

    private void RunOnceCore()
    {
        byte[]? report;
        long revision;
        string? previousConnectionKey;
        lock (gate)
        {
            report = latestReport;
            revision = requestedRevision;
            previousConnectionKey = connectionKey;
        }

        try
        {
            var currentConnectionKey = transport.TryGetConnectionKey();
            if (currentConnectionKey is null)
            {
                lock (gate)
                {
                    connected = false;
                    connectionKey = null;
                    failure = null;
                }

                return;
            }

            if (report is null)
            {
                lock (gate)
                {
                    connected = true;
                    connectionKey = currentConnectionKey;
                    failure = null;
                }

                return;
            }

            var connectionChanged = !string.Equals(
                previousConnectionKey,
                currentConnectionKey,
                StringComparison.OrdinalIgnoreCase);
            lock (gate)
            {
                if (!connectionChanged && appliedRevision == revision)
                {
                    connected = true;
                    failure = null;
                    return;
                }
            }

            var written = transport.Write(report);
            if (written != G13LcdFrame.ReportLength)
            {
                throw new IOException(
                    $"G13 LCD frameが途中までしか書かれませんでした。{written}/{G13LcdFrame.ReportLength} bytes");
            }

            lock (gate)
            {
                connected = true;
                connectionKey = currentConnectionKey;
                appliedRevision = Math.Max(appliedRevision, revision);
                lastUpdatedUtc = DateTime.UtcNow;
                failure = null;
            }
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                connected = false;
                connectionKey = null;
                failure = exception.Message;
            }
        }
    }

    public void Stop(bool clearDisplay = true)
    {
        lock (gate)
        {
            if (!started || stopped)
            {
                return;
            }
        }

        if (clearDisplay)
        {
            var clearRevision = RequestFrame(new byte[G13LcdFrame.FramebufferLength]);
            var deadline = DateTime.UtcNow.AddSeconds(1);
            while (DateTime.UtcNow < deadline && Status.AppliedRevision < clearRevision)
            {
                Thread.Sleep(10);
            }
        }

        stopRequested = true;
        wake.Set();
        worker?.Join(TimeSpan.FromSeconds(2));
        lock (gate)
        {
            stopped = true;
        }
    }

    public void Dispose()
    {
        Stop();
        wake.Dispose();
    }

    private void WorkerLoop()
    {
        while (!stopRequested)
        {
            RunOnce();
            wake.WaitOne(connectionPollInterval);
        }
    }
}
