using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenLogicool.Capture;

namespace OpenLogicool.Probe;

/// <summary>
/// ゲームの窓の映像（ffmpeg が自分で取り込む）と音（OpenLogicool が拾って ffmpeg の stdin へ渡す）を、
/// WHIP で中継ソフトへ送り、成立性を測る実験。
///   --pid N | --process-name NAME   窓と音の対象 process
///   --whip-url URL                  送り先（必須）
///   --authorization "user:pass"     認証（必須。記録とコンソールへ出さない）
///   --seconds N                     1 cycle で流す秒数（既定 60）
///   --width N / --height N          映像の大きさ（既定 1280 / 720）
///   --fps N                         映像のコマ数（既定 30）
///   --video-kbps N                  映像のビットレート（既定 3000）
///   --cycles N                      「開始 → 流す → 終了」を繰り返す回数（既定 1）
///   --kill                          終了を通常の終了でなく強制終了にする（フォーカスの確認用）
/// ffmpeg の進捗・映像の止まり・音の送出量・前面の窓の変化を記録する。製品の経路には触れない。
/// </summary>
internal static partial class RemoteViewSmoke
{
    public static int Run(string[] args, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var evidence = new Dictionary<string, object?>
        {
            ["Probe"] = "remote-view-smoke",
            ["CapturedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["Machine"] = Environment.MachineName,
            ["OsVersion"] = Environment.OSVersion.VersionString,
            ["Arguments"] = WithoutAuthorization(args),
        };

        int processId;
        if (Option(args, "--pid") is { } pidText)
        {
            processId = int.Parse(pidText);
        }
        else if (Option(args, "--process-name") is { } name)
        {
            var candidates = Process.GetProcessesByName(name);
            if (candidates.Length != 1)
            {
                return Finish(outputDirectory, evidence, $"process '{name}' が {candidates.Length} 件あり、一意に選べません。--pid で指定してください。");
            }

            processId = candidates[0].Id;
        }
        else
        {
            Console.Error.WriteLine("[remote-view] --pid N か --process-name NAME を指定してください。");
            return 1;
        }

        var whipUrl = Option(args, "--whip-url");
        var authorization = Option(args, "--authorization");
        if (string.IsNullOrEmpty(whipUrl) || string.IsNullOrEmpty(authorization))
        {
            Console.Error.WriteLine("[remote-view] --whip-url URL と --authorization \"user:pass\" を指定してください。");
            return 1;
        }

        var settings = new Settings(
            processId,
            whipUrl,
            authorization,
            int.Parse(Option(args, "--seconds") ?? "60"),
            int.Parse(Option(args, "--width") ?? "1280"),
            int.Parse(Option(args, "--height") ?? "720"),
            int.Parse(Option(args, "--fps") ?? "30"),
            int.Parse(Option(args, "--video-kbps") ?? "3000"),
            Array.IndexOf(args, "--kill") >= 0);
        var cycleCount = int.Parse(Option(args, "--cycles") ?? "1");

        using (var target = Process.GetProcessById(processId))
        {
            evidence["TargetProcessId"] = processId;
            evidence["TargetProcessName"] = target.ProcessName;
        }

        var results = new List<object>();
        evidence["Cycles"] = results;
        string? failure = null;
        using var job = new KillOnCloseJob();
        for (var index = 1; index <= cycleCount; index++)
        {
            var cycle = new Cycle(index, settings, job);
            Console.WriteLine($"[remote-view] cycle {index}/{cycleCount} 開始（{settings.Seconds} 秒{(settings.Kill ? "・強制終了" : "")}）");
            cycle.Run();
            results.Add(cycle.Result());
            Console.WriteLine($"[remote-view] cycle {index}/{cycleCount} 終了: {cycle.Summary()}");
            failure ??= cycle.Exception is { } exception
                ? $"cycle {index}: {exception}"
                : cycle.Frame == 0 ? $"cycle {index}: ffmpeg が 1 コマも出しませんでした。" : null;
        }

        return Finish(outputDirectory, evidence, failure);
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>記録とコンソールへ認証を出さないため、--authorization とその値を除く。</summary>
    private static string[] WithoutAuthorization(string[] args)
    {
        var kept = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--authorization")
            {
                index++;
                continue;
            }

            kept.Add(args[index]);
        }

        return [.. kept];
    }

    private static int Finish(string outputDirectory, Dictionary<string, object?> evidence, string? failure)
    {
        evidence["Failure"] = failure;
        var path = Path.Combine(outputDirectory, $"remote-view-smoke-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[remote-view] 記録: {path}");
        if (failure is not null)
        {
            Console.Error.WriteLine($"[remote-view] 失敗: {failure}");
            return 1;
        }

        return 0;
    }

    private sealed record Settings(
        int ProcessId,
        string WhipUrl,
        string Authorization,
        int Seconds,
        int Width,
        int Height,
        int Fps,
        int VideoKbps,
        bool Kill);

    /// <summary>「開始 → 流す → 終了」の 1 回分。途中で例外が出ても、そこまでの測定を残す。</summary>
    private sealed class Cycle(int index, Settings settings, KillOnCloseJob job)
    {
        private readonly List<object> foregroundChanges = [];
        private readonly FfmpegProgress progress = new();
        private AudioPump? pump;
        private long windowHandle;
        private double elapsedSeconds;
        private string stoppedBy = "未開始";
        private int? exitCode;
        private bool audioThreadStuck;

        public string? Exception { get; private set; }

        public long Frame => progress.Snapshot().Frame;

        public void Run()
        {
            var watch = Stopwatch.StartNew();
            try
            {
                Execute(watch);
            }
            catch (Exception ex)
            {
                Exception = $"{ex.GetType().Name}: {ex.Message}";
            }

            elapsedSeconds = watch.Elapsed.TotalSeconds;
        }

        public string Summary()
        {
            var snapshot = progress.Snapshot();
            return $"frame={snapshot.Frame} time={snapshot.TimeSeconds:0.0}s dup={snapshot.Dup} drop={snapshot.Drop} " +
                   $"止まり={snapshot.Stalls} 前面変化={foregroundChanges.Count} 終了={stoppedBy}";
        }

        public object Result()
        {
            var snapshot = progress.Snapshot();
            return new
            {
                Cycle = index,
                Kill = settings.Kill,
                TargetSeconds = settings.Seconds,
                ElapsedSeconds = elapsedSeconds,
                WindowHandle = windowHandle,
                StoppedBy = stoppedBy,
                FfmpegExitCode = exitCode,
                Video = new
                {
                    snapshot.Frame,
                    snapshot.TimeSeconds,
                    snapshot.Dup,
                    snapshot.Drop,
                    LastFps = snapshot.Fps,
                    snapshot.ProgressLines,
                    StallCount = snapshot.Stalls,
                    LongestStallSeconds = snapshot.LongestStall,
                },
                Audio = pump is null
                    ? null
                    : new
                    {
                        pump.SamplesSent,
                        pump.SilenceSamples,
                        pump.WriteExceptions,
                        pump.FirstWriteException,
                        ThreadStuck = audioThreadStuck,
                        pump.Failure,
                    },
                ForegroundChangeCount = foregroundChanges.Count,
                ForegroundChanges = foregroundChanges,
                FfmpegErrorLines = snapshot.ErrorLines,
                Exception,
            };
        }

        private void Execute(Stopwatch watch)
        {
            using var target = Process.GetProcessById(settings.ProcessId);
            target.Refresh();
            var window = target.MainWindowHandle;
            if (window == IntPtr.Zero)
            {
                throw new InvalidOperationException($"process {settings.ProcessId} に取り込める主窓がありません。");
            }

            windowHandle = window.ToInt64();
            var start = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in FfmpegArguments(windowHandle))
            {
                start.ArgumentList.Add(argument);
            }

            using var ffmpeg = Process.Start(start)
                ?? throw new InvalidOperationException("ffmpeg を起動できませんでした。");
            job.Add(ffmpeg);
            var errorReader = new Thread(() => progress.ReadAll(ffmpeg.StandardError))
            {
                IsBackground = true,
                Name = "remote-view-ffmpeg-stderr",
            };
            errorReader.Start();
            pump = new AudioPump(settings.ProcessId, ffmpeg.StandardInput.BaseStream);
            pump.Start();

            var completed = false;
            try
            {
                var lastForeground = ForegroundProcessName();
                var nextPrint = TimeSpan.FromSeconds(5);
                while (watch.Elapsed < TimeSpan.FromSeconds(settings.Seconds) && !ffmpeg.HasExited && pump.Failure is null)
                {
                    ffmpeg.WaitForExit(200);
                    var current = ForegroundProcessName();
                    if (current != lastForeground)
                    {
                        foregroundChanges.Add(new
                        {
                            AtSeconds = Math.Round(watch.Elapsed.TotalSeconds, 3),
                            From = lastForeground,
                            To = current,
                        });
                        lastForeground = current;
                    }

                    if (watch.Elapsed >= nextPrint)
                    {
                        nextPrint += TimeSpan.FromSeconds(5);
                        Console.WriteLine($"[remote-view] {watch.Elapsed.TotalSeconds,5:0.0}s {Summary()}");
                    }
                }

                if (!ffmpeg.HasExited)
                {
                    progress.CloseOpenStall();
                }

                completed = true;
            }
            finally
            {
                Stop(ffmpeg, errorReader, graceful: completed && !settings.Kill);
            }

            if (pump.Failure is { } audioFailure)
            {
                throw new InvalidOperationException($"音の送出が失敗しました: {audioFailure}");
            }
        }

        /// <summary>音の thread を止め、ffmpeg を終わらせる。通常は stdin を閉じて 5 秒待ち、終わらなければ Kill する。</summary>
        private void Stop(Process ffmpeg, Thread errorReader, bool graceful)
        {
            var exitedByItself = ffmpeg.HasExited;
            var killed = false;
            pump!.RequestStop();
            if (!graceful && !ffmpeg.HasExited)
            {
                ffmpeg.Kill();
                killed = true;
            }

            if (!pump.Join(TimeSpan.FromSeconds(2)))
            {
                // 書き込みが ffmpeg の側で詰まっている。ffmpeg を止めて書き込みを解く。
                if (!ffmpeg.HasExited)
                {
                    ffmpeg.Kill();
                    killed = true;
                }

                audioThreadStuck = !pump.Join(TimeSpan.FromSeconds(2));
            }

            if (graceful && !ffmpeg.HasExited)
            {
                ffmpeg.StandardInput.Close();
                if (!ffmpeg.WaitForExit(5000))
                {
                    ffmpeg.Kill();
                    stoppedBy = "KilledAfterTimeout";
                }
                else
                {
                    stoppedBy = "Graceful";
                }
            }
            else
            {
                stoppedBy = killed ? "Kill" : exitedByItself ? "ExitedByItself" : "Graceful";
            }

            ffmpeg.WaitForExit();
            exitCode = ffmpeg.ExitCode;
            errorReader.Join(TimeSpan.FromSeconds(5));
        }

        private IEnumerable<string> FfmpegArguments(long window)
        {
            yield return "-hide_banner";
            yield return "-loglevel";
            yield return "info";
            yield return "-f";
            yield return "lavfi";
            yield return "-i";
            yield return $"gfxcapture=hwnd={window}:max_framerate={settings.Fps}:width={settings.Width}:height={settings.Height}" +
                         ":resize_mode=scale_aspect:capture_cursor=1";
            yield return "-f";
            yield return "f32le";
            yield return "-ar";
            yield return AudioPump.SampleRate.ToString();
            yield return "-ch_layout";
            yield return "mono";
            yield return "-i";
            yield return "pipe:0";
            yield return "-c:v";
            yield return "h264_nvenc";
            yield return "-preset";
            yield return "p1";
            yield return "-tune";
            yield return "ull";
            yield return "-zerolatency";
            yield return "1";
            yield return "-rc";
            yield return "cbr";
            yield return "-b:v";
            yield return $"{settings.VideoKbps}k";
            yield return "-bf";
            yield return "0";
            yield return "-g";
            yield return (settings.Fps * 2).ToString();
            yield return "-c:a";
            yield return "libopus";
            yield return "-b:a";
            yield return "96k";
            yield return "-application";
            yield return "lowdelay";
            yield return "-ac";
            yield return "2";
            yield return "-t";
            yield return settings.Seconds.ToString();
            yield return "-f";
            yield return "whip";
            yield return "-authorization";
            yield return settings.Authorization;
            yield return settings.WhipUrl;
        }

        private static string ForegroundProcessName()
        {
            var window = Native.GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return "(なし)";
            }

            Native.GetWindowThreadProcessId(window, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                return process.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // 読む間に前面の process が終わった場合。
                return "(終了済み)";
            }
        }
    }

    /// <summary>ffmpeg の stderr を読み、進捗行から frame・time・dup・drop と映像の止まりを数える。</summary>
    private sealed partial class FfmpegProgress
    {
        private const int MaxErrorLines = 20;
        private static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(2);

        private readonly object gate = new();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<string> errorLines = [];
        private long frame;
        private double timeSeconds;
        private long dup;
        private long drop;
        private string fps = "";
        private int progressLines;
        private int stalls;
        private double longestStall;
        private bool hasTime;
        private double lastAdvanceAt;

        [GeneratedRegex(@"frame=\s*(\d+)", RegexOptions.CultureInvariant)]
        private static partial Regex FrameRegex();

        [GeneratedRegex(@"fps=\s*([\d.]+)", RegexOptions.CultureInvariant)]
        private static partial Regex FpsRegex();

        [GeneratedRegex(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
        private static partial Regex TimeRegex();

        [GeneratedRegex(@"dup=(\d+)", RegexOptions.CultureInvariant)]
        private static partial Regex DupRegex();

        [GeneratedRegex(@"drop=(\d+)", RegexOptions.CultureInvariant)]
        private static partial Regex DropRegex();

        /// <summary>進捗は \r、その他は \n で区切られる。</summary>
        public void ReadAll(StreamReader reader)
        {
            var line = new StringBuilder();
            int value;
            while ((value = reader.Read()) >= 0)
            {
                if (value is '\r' or '\n')
                {
                    if (line.Length > 0)
                    {
                        Feed(line.ToString());
                        line.Clear();
                    }
                }
                else
                {
                    line.Append((char)value);
                }
            }

            if (line.Length > 0)
            {
                Feed(line.ToString());
            }
        }

        public void CloseOpenStall()
        {
            lock (gate)
            {
                if (hasTime)
                {
                    CountStall(clock.Elapsed.TotalSeconds - lastAdvanceAt);
                }
            }
        }

        public ProgressSnapshot Snapshot()
        {
            lock (gate)
            {
                return new ProgressSnapshot(frame, timeSeconds, dup, drop, fps, progressLines, stalls, longestStall, [.. errorLines]);
            }
        }

        private void Feed(string line)
        {
            lock (gate)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("frame=", StringComparison.Ordinal) && trimmed.Contains("time=", StringComparison.Ordinal))
                {
                    progressLines++;
                    ParseProgress(trimmed);
                    return;
                }

                if (errorLines.Count < MaxErrorLines
                    && (line.Contains("error", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("failed", StringComparison.OrdinalIgnoreCase)))
                {
                    errorLines.Add(line);
                }
            }
        }

        private void ParseProgress(string line)
        {
            if (FrameRegex().Match(line) is { Success: true } frameMatch)
            {
                frame = long.Parse(frameMatch.Groups[1].Value);
            }

            if (FpsRegex().Match(line) is { Success: true } fpsMatch)
            {
                fps = fpsMatch.Groups[1].Value;
            }

            if (DupRegex().Match(line) is { Success: true } dupMatch)
            {
                dup = long.Parse(dupMatch.Groups[1].Value);
            }

            if (DropRegex().Match(line) is { Success: true } dropMatch)
            {
                drop = long.Parse(dropMatch.Groups[1].Value);
            }

            if (TimeRegex().Match(line) is not { Success: true } timeMatch)
            {
                return;
            }

            var time = int.Parse(timeMatch.Groups[1].Value) * 3600.0
                       + int.Parse(timeMatch.Groups[2].Value) * 60.0
                       + double.Parse(timeMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            var now = clock.Elapsed.TotalSeconds;
            if (!hasTime)
            {
                hasTime = true;
                lastAdvanceAt = now;
                timeSeconds = time;
                return;
            }

            if (time > timeSeconds)
            {
                CountStall(now - lastAdvanceAt);
                lastAdvanceAt = now;
                timeSeconds = time;
            }
        }

        /// <summary>time が進まなかった時間が閾値以上なら、止まり 1 回として数える。</summary>
        private void CountStall(double seconds)
        {
            if (seconds >= StallThreshold.TotalSeconds)
            {
                stalls++;
                longestStall = Math.Max(longestStall, seconds);
            }
        }

        public sealed record ProgressSnapshot(
            long Frame,
            double TimeSeconds,
            long Dup,
            long Drop,
            string Fps,
            int ProgressLines,
            int Stalls,
            double LongestStall,
            string[] ErrorLines);
    }

    /// <summary>
    /// 専用 thread で対象 process の音を読み、ffmpeg の stdin へ f32le で渡す。
    /// 鳴っていない間は届かないので、時計に合わせて無音を補う（音が先へ進むのを防ぐ）。
    /// </summary>
    private sealed class AudioPump(int processId, Stream destination)
    {
        public const int SampleRate = 48000;
        private const int ReadChunk = 16384;
        private const int SilenceTolerance = SampleRate / 50;

        private Thread? worker;
        private volatile bool stopRequested;
        private long samplesSent;
        private long silenceSamples;
        private int writeExceptions;
        private volatile string? failure;
        private string? firstWriteException;

        public long SamplesSent => Interlocked.Read(ref samplesSent);

        public long SilenceSamples => Interlocked.Read(ref silenceSamples);

        public int WriteExceptions => writeExceptions;

        public string? FirstWriteException => firstWriteException;

        public string? Failure => failure;

        public void Start()
        {
            worker = new Thread(Pump)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "remote-view-audio",
            };
            worker.Start();
        }

        public void RequestStop() => stopRequested = true;

        public bool Join(TimeSpan timeout) => worker!.Join(timeout);

        private void Pump()
        {
            var clock = Stopwatch.StartNew();
            ProcessLoopbackAudioSource? source = null;
            try
            {
                source = new ProcessLoopbackAudioSource(processId);
                var buffer = new float[ReadChunk];
                var silence = new float[SampleRate / 10];
                while (!stopRequested)
                {
                    var count = source.Read(buffer);
                    if (count > 0 && !Write(buffer.AsSpan(0, count)))
                    {
                        return;
                    }

                    var deficit = (long)(clock.Elapsed.TotalSeconds * SampleRate) - Interlocked.Read(ref samplesSent);
                    if (deficit >= SilenceTolerance)
                    {
                        Interlocked.Add(ref silenceSamples, deficit);
                        while (deficit > 0)
                        {
                            var chunk = (int)Math.Min(deficit, silence.Length);
                            if (!Write(silence.AsSpan(0, chunk)))
                            {
                                return;
                            }

                            deficit -= chunk;
                        }
                    }

                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                source?.Dispose();
            }
        }

        /// <summary>書けなければ例外を数えて false（再試行しない）。</summary>
        private bool Write(ReadOnlySpan<float> samples)
        {
            try
            {
                destination.Write(MemoryMarshal.AsBytes(samples));
                destination.Flush();
                Interlocked.Add(ref samplesSent, samples.Length);
                return true;
            }
            catch (IOException ex)
            {
                writeExceptions++;
                firstWriteException ??= $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }
    }

    /// <summary>この道具が落ちても ffmpeg が残らないよう、Job Object（閉じたら kill）へ入れる。</summary>
    private sealed class KillOnCloseJob : IDisposable
    {
        private const int ExtendedLimitInformationClass = 9;
        private const uint KillOnJobClose = 0x2000;

        private IntPtr handle;

        public KillOnCloseJob()
        {
            handle = Native.CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Job Object を作れませんでした。");
            }

            var info = new Native.ExtendedLimitInformation();
            info.Basic.LimitFlags = KillOnJobClose;
            var size = Marshal.SizeOf<Native.ExtendedLimitInformation>();
            var pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, pointer, false);
                if (!Native.SetInformationJobObject(handle, ExtendedLimitInformationClass, pointer, (uint)size))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Job Object に終了時 kill を設定できませんでした。");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        public void Add(Process process)
        {
            if (!Native.AssignProcessToJobObject(handle, process.Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ffmpeg を Job Object へ入れられませんでした。");
            }
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                Native.CloseHandle(handle);
                handle = IntPtr.Zero;
            }
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ExtendedLimitInformation
        {
            public BasicLimitInformation Basic;
            public IoCounters Io;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
