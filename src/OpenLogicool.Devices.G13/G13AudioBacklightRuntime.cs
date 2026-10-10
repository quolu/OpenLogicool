using System.Diagnostics;
using OpenLogicool.Contracts.Audio;

namespace OpenLogicool.Devices.G13;

public sealed record G13AudioBacklightStatus(
    bool IsRunning,
    int? TargetProcessId,
    bool IsFollowing,
    bool IsConnected,
    long ColorWrites,
    string? Failure);

/// <summary>
/// 対象 process の音に合わせて G13 のバックライト色を書き続ける低優先度 resident worker。
/// fast path を待たせない専用 thread で、音の読み出し・色の計算・色の書き込みを行う。
/// 追従を始める前の色を覚え、追従をやめる時と停止時にその色へ戻す。
/// G13 の抜き挿しと書き込みの失敗は 1 秒おきに開き直す。
/// </summary>
public sealed class G13AudioBacklightRuntime : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(1);

    private readonly IG13BacklightTransport transport;
    private readonly Func<int, IProcessAudioSource> audioSourceFactory;
    private readonly object gate = new();
    private readonly object cycleGate = new();
    private Thread? worker;
    private volatile bool stopRequested;
    private bool started;
    private bool stopped;
    private int? requestedTarget;

    // 以下は cycleGate の中だけで触る。
    private int? activeTarget;
    private IProcessAudioSource? audio;
    private G13AudioBacklightEffect? effect;
    private float[] samples = [];
    private G13BacklightColor? baseline;
    private G13BacklightColor? lastWritten;
    private TimeSpan clock;
    private TimeSpan nextOpenAttempt;

    // 以下は gate の中だけで触る。
    private bool following;
    private bool connected;
    private long colorWrites;
    private string? audioFailure;
    private string? deviceFailure;

    public G13AudioBacklightRuntime(
        IG13BacklightTransport transport,
        Func<int, IProcessAudioSource> audioSourceFactory)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.audioSourceFactory = audioSourceFactory ?? throw new ArgumentNullException(nameof(audioSourceFactory));
    }

    public G13AudioBacklightStatus Status
    {
        get
        {
            lock (gate)
            {
                return new G13AudioBacklightStatus(
                    started && !stopped,
                    requestedTarget,
                    following,
                    connected,
                    colorWrites,
                    audioFailure ?? deviceFailure);
            }
        }
    }

    public void Start()
    {
        lock (gate)
        {
            if (started || stopped)
            {
                throw new InvalidOperationException("G13 backlight runtimeは一度しか起動できません。");
            }

            started = true;
            worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "OpenLogicoolG13Backlight",
                Priority = ThreadPriority.BelowNormal,
            };
            worker.Start();
        }
    }

    /// <summary>音を追う process を指定する。null は追従しない（追従前の色へ戻す）。</summary>
    public void SetTarget(int? processId)
    {
        lock (gate)
        {
            if (stopped)
            {
                throw new InvalidOperationException("停止済みのG13 backlight runtimeへ対象を指定できません。");
            }

            requestedTarget = processId;
        }
    }

    /// <summary>workerと同じ一周期を同期実行するfocused test seam。elapsedは前の周期からの経過時間。</summary>
    public void RunOnce(TimeSpan elapsed)
    {
        lock (cycleGate)
        {
            RunOnceCore(elapsed);
        }
    }

    public void Stop()
    {
        Thread? running;
        lock (gate)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            running = worker;
        }

        if (running is null)
        {
            lock (cycleGate)
            {
                Shutdown();
            }

            return;
        }

        stopRequested = true;
        running.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose() => Stop();

    private void WorkerLoop()
    {
        var watch = Stopwatch.StartNew();
        var previous = TimeSpan.Zero;
        while (!stopRequested)
        {
            var now = watch.Elapsed;
            RunOnce(now - previous);
            previous = now;
            Thread.Sleep(Tick);
        }

        lock (cycleGate)
        {
            Shutdown();
        }
    }

    private void RunOnceCore(TimeSpan elapsed)
    {
        clock += elapsed;
        int? desired;
        lock (gate)
        {
            desired = requestedTarget;
        }

        if (desired != activeTarget)
        {
            activeTarget = desired;
            StopFollowing(null);
            if (desired is { } processId)
            {
                StartFollowing(processId);
            }
        }

        if (audio is null || effect is null)
        {
            RestoreBaseline();
            return;
        }

        int count;
        try
        {
            count = audio.Read(samples);
        }
        catch (Exception exception)
        {
            // 対象 process の終了などで読めなくなった。別の process が指定されるまで追従しない。
            StopFollowing($"音を読めなくなりました: {exception.Message}");
            return;
        }

        if (count == 0)
        {
            // 鳴っていない間は何も届かない。経過時間ぶんの無音として時刻を進める。
            count = (int)Math.Min(samples.Length, elapsed.TotalSeconds * audio.SampleRate);
            samples.AsSpan(0, count).Clear();
        }

        var color = effect.Process(samples.AsSpan(0, count));
        if (color != lastWritten)
        {
            WriteColor(color, rememberBaseline: true);
        }
    }

    private void StartFollowing(int processId)
    {
        try
        {
            audio = audioSourceFactory(processId)
                ?? throw new InvalidOperationException("audio source factoryがnullを返しました。");
            effect = new G13AudioBacklightEffect(audio.SampleRate);
            samples = new float[audio.SampleRate / 2];
            lock (gate)
            {
                following = true;
                audioFailure = null;
            }
        }
        catch (Exception exception)
        {
            StopFollowing($"process {processId} の音を拾えません: {exception.Message}");
        }
    }

    private void StopFollowing(string? reason)
    {
        audio?.Dispose();
        audio = null;
        effect = null;
        lock (gate)
        {
            following = false;
            audioFailure = reason;
        }
    }

    private void RestoreBaseline()
    {
        if (baseline is not { } color)
        {
            return;
        }

        if (WriteColor(color, rememberBaseline: false) is not false)
        {
            // 書けた時と、G13 が外れている時（挿し直すと電源投入時の色へ戻る）は覚えた色を捨てる。
            baseline = null;
        }
    }

    /// <summary>色を書く。書けたら true、G13 が接続されていなければ null、失敗と開き直し待ちは false。</summary>
    private bool? WriteColor(G13BacklightColor color, bool rememberBaseline)
    {
        if (clock < nextOpenAttempt)
        {
            return false;
        }

        try
        {
            if (!transport.TryOpen())
            {
                nextOpenAttempt = clock + ReconnectInterval;
                lastWritten = null;
                lock (gate)
                {
                    connected = false;
                }

                return null;
            }

            if (rememberBaseline)
            {
                baseline ??= transport.Read();
            }

            transport.Write(color);
            lastWritten = color;
            lock (gate)
            {
                connected = true;
                colorWrites++;
                deviceFailure = null;
            }

            return true;
        }
        catch (Exception exception)
        {
            transport.Close();
            nextOpenAttempt = clock + ReconnectInterval;
            lastWritten = null;
            lock (gate)
            {
                connected = false;
                deviceFailure = exception.Message;
            }

            return false;
        }
    }

    private void Shutdown()
    {
        StopFollowing(null);
        nextOpenAttempt = TimeSpan.Zero;
        RestoreBaseline();
        transport.Close();
    }
}
