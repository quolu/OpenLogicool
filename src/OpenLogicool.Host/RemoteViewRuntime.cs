using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Host;

/// <summary>ゲームの音を左右のまま 16bit で読む音源。作った thread で読み、同じ thread で破棄する。</summary>
public interface IRemoteViewAudioSource : IDisposable
{
    /// <summary>L,R の順に interleaved で読む。戻り値は frame 数。何も届いていなければ 0。</summary>
    int ReadStereo(Span<short> interleaved);
}

/// <summary>起動済みの ffmpeg。runtime が音の書き込み・進捗の読み取り・停止に使う口だけを持つ。</summary>
public interface IRemoteViewEncoderProcess : IDisposable
{
    Stream StandardInput { get; }

    bool HasExited { get; }

    /// <summary>終了していなければ null。</summary>
    int? ExitCode { get; }

    /// <summary>stderr を1行ずつ返す。ffmpeg が終わって出力が尽きると列も終わる。</summary>
    IEnumerable<string> ReadErrorLines();

    void CloseStandardInput();

    bool WaitForExit(TimeSpan timeout);

    /// <summary>強制終了する。すでに終了していれば何もしない。</summary>
    void Kill();
}

public sealed record RemoteViewRuntimeStatus(
    RemoteViewPhase Phase,
    string Detail,
    string? TargetProcessName,
    double StreamedSeconds,
    long VideoFrames,
    bool VideoStalled);

/// <summary>ffmpeg の進捗行（frame=… time=…）の解析。</summary>
internal static partial class RemoteViewProgressLine
{
    [GeneratedRegex(@"frame=\s*(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex FrameRegex();

    [GeneratedRegex(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex TimeRegex();

    /// <summary>進捗行なら true。time が N/A の間は timeSeconds が null になる。</summary>
    public static bool TryParse(string line, out long frame, out double? timeSeconds)
    {
        frame = 0;
        timeSeconds = null;
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("frame=", StringComparison.Ordinal)
            || !trimmed.Contains("time=", StringComparison.Ordinal))
        {
            return false;
        }

        if (FrameRegex().Match(trimmed) is { Success: true } frameMatch)
        {
            frame = long.Parse(frameMatch.Groups[1].Value);
        }

        if (TimeRegex().Match(trimmed) is { Success: true } timeMatch)
        {
            timeSeconds = int.Parse(timeMatch.Groups[1].Value) * 3600.0
                          + int.Parse(timeMatch.Groups[2].Value) * 60.0
                          + double.Parse(timeMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return true;
    }
}

/// <summary>
/// 音源の音を ffmpeg の stdin へ s16le のステレオで書く 1 周期ぶんの処理。
/// 音源から何も届かない時間が 40ms 以上続いた時だけ、その時間のぶんの無音を書く。
/// 送った合計を時計と比べて埋めると、ffmpeg が読み始めるまでの遅れを不足と数え、鳴っている音へ無音が挟まる。
/// </summary>
internal sealed class RemoteViewAudioPump
{
    public const int ReadFrames = 16384;
    private const int SilenceChunkFrames = RemoteViewFfmpegArguments.AudioSampleRate / 10;
    private static readonly TimeSpan SilenceGap = TimeSpan.FromMilliseconds(40);

    private readonly IRemoteViewAudioSource source;
    private readonly Stream destination;
    private readonly Func<TimeSpan> clock;
    private readonly short[] buffer = new short[ReadFrames * 2];
    private readonly short[] silence = new short[SilenceChunkFrames * 2];
    private TimeSpan lastData;

    public RemoteViewAudioPump(IRemoteViewAudioSource source, Stream destination, Func<TimeSpan> clock)
    {
        this.source = source;
        this.destination = destination;
        this.clock = clock;
        lastData = clock();
    }

    public long FramesSent { get; private set; }

    public long SilenceFramesSent { get; private set; }

    /// <summary>1 回読んで必要なら書く。stdin へ書けなくなった（ffmpeg が閉じた）時は false。</summary>
    public bool Step()
    {
        var frames = source.ReadStereo(buffer);
        if (frames > 0)
        {
            if (!Write(buffer.AsSpan(0, frames * 2)))
            {
                return false;
            }

            FramesSent += frames;
            lastData = clock();
            return true;
        }

        var gap = clock() - lastData;
        if (gap < SilenceGap)
        {
            return true;
        }

        var missing = (int)Math.Min(SilenceChunkFrames, gap.TotalSeconds * RemoteViewFfmpegArguments.AudioSampleRate);
        if (!Write(silence.AsSpan(0, missing * 2)))
        {
            return false;
        }

        SilenceFramesSent += missing;
        lastData += TimeSpan.FromSeconds((double)missing / RemoteViewFfmpegArguments.AudioSampleRate);
        return true;
    }

    private bool Write(ReadOnlySpan<short> samples)
    {
        try
        {
            destination.Write(MemoryMarshal.AsBytes(samples));
            destination.Flush();
            return true;
        }
        catch (IOException)
        {
            // ffmpeg が stdin を閉じた（終了した）。終了の理由は stderr 側が Faulted として報告する。
            return false;
        }
    }
}

/// <summary>
/// ゲームの窓の映像（ffmpeg が自分で取り込む）と音（ここで拾って ffmpeg の stdin へ渡す）を中継サーバーへ送る常駐 worker。
/// 専用の低優先度 thread が音を運び、別 thread が ffmpeg の stderr から進捗を読む。fast path には触れない。
/// ffmpeg が自分で終了したら Faulted にして止まり、自動では再起動しない。
/// </summary>
public sealed class RemoteViewRuntime : IDisposable
{
    private static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(2);
    private const int TailLines = 5;

    private readonly Func<IReadOnlyList<string>, IRemoteViewEncoderProcess> processFactory;
    private readonly Func<int, IRemoteViewAudioSource> audioSourceFactory;
    private readonly Func<TimeSpan> clock;
    private readonly Func<DateTimeOffset> wallClock;
    private readonly object lifecycle = new();
    private volatile Session? session;

    public RemoteViewRuntime(
        Func<IReadOnlyList<string>, IRemoteViewEncoderProcess> processFactory,
        Func<int, IRemoteViewAudioSource> audioSourceFactory,
        Func<TimeSpan>? clock = null,
        Func<DateTimeOffset>? wallClock = null)
    {
        this.processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
        this.audioSourceFactory = audioSourceFactory ?? throw new ArgumentNullException(nameof(audioSourceFactory));
        var watch = Stopwatch.StartNew();
        this.clock = clock ?? (() => watch.Elapsed);
        this.wallClock = wallClock ?? (() => DateTimeOffset.UtcNow);
    }

    public RemoteViewRuntimeStatus Status
    {
        get
        {
            if (session is not { } current)
            {
                return new RemoteViewRuntimeStatus(RemoteViewPhase.Stopped, "停止中です。", null, 0, 0, false);
            }

            lock (current.Gate)
            {
                var running = current.Phase is RemoteViewPhase.Starting or RemoteViewPhase.Streaming;
                var stalled = running && clock() - current.LastAdvance >= StallThreshold;
                var detail = current.Phase switch
                {
                    RemoteViewPhase.Starting => "配信を始めています。",
                    RemoteViewPhase.Streaming when stalled =>
                        "映像のコマが届いていません。ゲームの窓が描き直されていない可能性があります。",
                    RemoteViewPhase.Streaming => "配信中です。",
                    _ => current.Detail,
                };
                return new RemoteViewRuntimeStatus(
                    current.Phase, detail, current.ProcessName, current.TimeSeconds, current.Frames, stalled);
            }
        }
    }

    public void Start(nint window, int processId, string processName, RemoteViewSettings settings, string authorization)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        ArgumentException.ThrowIfNullOrEmpty(authorization);
        lock (lifecycle)
        {
            if (session is { } previous)
            {
                lock (previous.Gate)
                {
                    if (previous.Phase is RemoteViewPhase.Starting or RemoteViewPhase.Streaming)
                    {
                        throw new InvalidOperationException("遠隔表示はすでに動いています。");
                    }
                }

                Teardown(previous);
            }

            var arguments = RemoteViewFfmpegArguments.Build(settings, window, authorization, wallClock());
            var process = processFactory(arguments)
                ?? throw new InvalidOperationException("ffmpeg の起動がnullを返しました。");
            var current = new Session(process, processName, authorization, clock());
            current.ErrorThread = new Thread(() => ReadErrors(current))
            {
                IsBackground = true,
                Name = "OpenLogicoolRemoteViewStderr",
            };
            current.AudioThread = new Thread(() => PumpAudio(current, processId))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "OpenLogicoolRemoteViewAudio",
            };
            session = current;
            current.ErrorThread.Start();
            current.AudioThread.Start();
        }
    }

    /// <summary>止める。stdin を閉じて ffmpeg を自分で終わらせ、終わらなければ Kill する。止まっていれば何もしない。</summary>
    public void Stop()
    {
        lock (lifecycle)
        {
            if (session is not { } current)
            {
                return;
            }

            lock (current.Gate)
            {
                current.Phase = RemoteViewPhase.Stopped;
                current.Detail = "停止しています。";
            }

            Teardown(current);
        }
    }

    public void Dispose() => Stop();

    /// <summary>lifecycle の中で呼ぶ。音の thread を止め、ffmpeg を終わらせて破棄し、session を捨てる。</summary>
    private void Teardown(Session target)
    {
        target.StopRequested = true;
        target.AudioHalt = true;
        var process = target.Process;
        if (!target.AudioThread!.Join(TimeSpan.FromSeconds(2)))
        {
            // 書き込みが ffmpeg の側で詰まっている。ffmpeg を止めて書き込みを解く。
            process.Kill();
            target.AudioThread.Join(TimeSpan.FromSeconds(2));
        }

        if (!process.HasExited)
        {
            try
            {
                process.CloseStandardInput();
            }
            catch (IOException)
            {
                // すでに ffmpeg 側が閉じている。終了を待つだけでよい。
            }

            if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
            {
                process.Kill();
            }
        }

        target.ErrorThread!.Join(TimeSpan.FromSeconds(5));
        process.Dispose();
        session = null;
    }

    private void ReadErrors(Session target)
    {
        try
        {
            foreach (var line in target.Process.ReadErrorLines())
            {
                Feed(target, line);
            }

            target.Process.WaitForExit(TimeSpan.FromSeconds(2));
            if (!target.StopRequested)
            {
                Fault(target, DescribeExit(target));
            }
        }
        catch (Exception exception)
        {
            // 専用 thread の最上位。停止の最中に起きた失敗は握りつぶさず、Fault が停止済みとして無視する。
            Fault(target, $"ffmpeg の出力を読めませんでした: {exception.Message}");
        }
    }

    private void Feed(Session target, string line)
    {
        lock (target.Gate)
        {
            if (RemoteViewProgressLine.TryParse(line, out var frame, out var time))
            {
                target.Frames = frame;
                if (time is { } seconds)
                {
                    if (!target.HasTime || seconds > target.TimeSeconds)
                    {
                        target.LastAdvance = clock();
                        target.TimeSeconds = seconds;
                    }

                    target.HasTime = true;
                }

                if (frame > 0 && target.Phase == RemoteViewPhase.Starting)
                {
                    target.Phase = RemoteViewPhase.Streaming;
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(line) && !target.ContainsSecret(line))
            {
                if (target.Tail.Count == TailLines)
                {
                    target.Tail.Dequeue();
                }

                target.Tail.Enqueue(line.Trim());
            }
        }
    }

    private static string DescribeExit(Session target)
    {
        lock (target.Gate)
        {
            var code = target.Process.ExitCode is { } value ? $"終了コード {value}" : "終了コード不明";
            return target.Tail.Count == 0
                ? $"ffmpeg が終了しました（{code}）。"
                : $"ffmpeg が終了しました（{code}）: {string.Join(" / ", target.Tail)}";
        }
    }

    private void PumpAudio(Session target, int processId)
    {
        IRemoteViewAudioSource? source = null;
        try
        {
            source = audioSourceFactory(processId)
                ?? throw new InvalidOperationException("audio source factoryがnullを返しました。");
            var pump = new RemoteViewAudioPump(source, target.Process.StandardInput, clock);
            while (!target.AudioHalt)
            {
                if (!pump.Step())
                {
                    break;
                }

                Thread.Sleep(10);
            }
        }
        catch (Exception exception)
        {
            // 音だけが欠けた映像を送り続けない。配信ごと止めて理由を表示する（停止の最中なら Fault が無視する）。
            Fault(target, $"ゲームの音を取り込めませんでした: {exception.Message}");
            target.Process.Kill();
        }
        finally
        {
            source?.Dispose();
        }
    }

    /// <summary>最初の失敗だけを残す。</summary>
    private static void Fault(Session target, string detail)
    {
        lock (target.Gate)
        {
            if (target.Phase is not (RemoteViewPhase.Starting or RemoteViewPhase.Streaming))
            {
                return;
            }

            target.Phase = RemoteViewPhase.Faulted;
            target.Detail = detail;
        }

        target.AudioHalt = true;
    }

    private sealed class Session(
        IRemoteViewEncoderProcess process, string processName, string authorization, TimeSpan startedAt)
    {
        private readonly string[] secrets = SecretsOf(authorization);

        public IRemoteViewEncoderProcess Process { get; } = process;

        public string ProcessName { get; } = processName;

        public object Gate { get; } = new();

        public Thread? AudioThread { get; set; }

        public Thread? ErrorThread { get; set; }

        public volatile bool StopRequested;

        public volatile bool AudioHalt;

        // 以下は Gate の中だけで触る。
        public RemoteViewPhase Phase = RemoteViewPhase.Starting;
        public string Detail = "";
        public long Frames;
        public double TimeSeconds;
        public bool HasTime;
        public TimeSpan LastAdvance = startedAt;
        public Queue<string> Tail { get; } = new();

        public bool ContainsSecret(string line) =>
            secrets.Any(secret => line.Contains(secret, StringComparison.Ordinal));

        /// <summary>認証の文字列そのものと、その中のパスワード部分（user:password の後ろ）。</summary>
        private static string[] SecretsOf(string authorization)
        {
            var separator = authorization.IndexOf(':');
            return separator >= 0 && separator + 1 < authorization.Length
                ? [authorization, authorization[(separator + 1)..]]
                : [authorization];
        }
    }
}
