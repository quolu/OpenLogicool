using System.Diagnostics;
using System.Text.Json;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G13;
using OpenLogicool.Devices.G600;
using OpenLogicool.Domain;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

// sleep 実機 smoke（Phase 2 Exit 条件2の sleep 実測）: PC スリープ→復帰をまたいで
//   ①復帰後も fast path が fault なしで動作し続ける（または fault が明示される）
//   ②スリープ前後で output の stuck・wrong release がない
//   ③スリープ中〜復帰時の device change（切断/到着）を証跡化する
// を観測する。スリープ検出は wall clock（DateTime）の跳び（poll 間隔 ≫ 通常値）で行う。
internal static class SleepSmoke
{
    public static int Run(string[] arguments, string outputDirectory)
    {
        var deviceKind = "g600";
        var phaseTimeoutMs = 300_000;
        string? watchdogOverride = null;
        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "--device" when i + 1 < arguments.Length:
                    deviceKind = arguments[++i].ToLowerInvariant();
                    break;
                case "--phase-timeout-ms" when i + 1 < arguments.Length:
                    phaseTimeoutMs = int.Parse(arguments[++i]);
                    break;
                case "--watchdog" when i + 1 < arguments.Length:
                    watchdogOverride = arguments[++i];
                    break;
            }
        }

        var watchdogPath = watchdogOverride ?? Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "OpenLogicool.Watchdog", "bin", "Debug", "net10.0-windows", "OpenLogicool.Watchdog.exe"));
        if (!File.Exists(watchdogPath))
        {
            Console.Error.WriteLine($"watchdog exe not found: {watchdogPath} (build src/OpenLogicool.Watchdog first)");
            return 1;
        }

        IDeviceInputSource source;
        Func<long> droppedProbe;
        IDisposable disposable;
        if (deviceKind == "g13")
        {
            var g13 = new G13RawInputSource();
            source = g13;
            droppedProbe = () => g13.DroppedInputCount;
            disposable = g13;
        }
        else if (deviceKind == "g600")
        {
            var g600 = new G600RawInputSource();
            source = g600;
            droppedProbe = () => g600.DroppedInputCount;
            disposable = g600;
        }
        else
        {
            Console.Error.WriteLine($"unknown --device: {deviceKind} (g13 | g600)");
            return 1;
        }

        using var _ = disposable;
        var devices = source.EnumerateDevices();
        if (devices.Count == 0)
        {
            Console.Error.WriteLine($"{deviceKind} が見つかりません。接続してから実行してください。");
            return 1;
        }

        var runtimes = devices.ToDictionary(
            device => device.DeviceInstanceId,
            device => new DeviceMappingRuntime(
                device.DeviceInstanceId,
                deviceKind == "g13" ? BuildG13Profile() : BuildG600Profile()),
            StringComparer.Ordinal);

        using var watchdog = WatchdogChannel.Start(watchdogPath);
        var recorder = new SleepBalanceEmitter(new GuardedOutputEmitter(new SendInputEmitter(), watchdog));
        var changeObserver = new ChangeObservingSource(source);
        var pump = new FastPathPump([new FastPathSource(changeObserver, droppedProbe)], runtimes, recorder);
        pump.Start();

        bool WaitFor(Func<bool> condition)
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < phaseTimeoutMs && pump.Failure is null)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(50);
            }

            return false;
        }

        // Phase 1: baseline（side ボタン down/up 対）
        Console.WriteLine("Phase 1: 側面ボタンを1回押して離してください（baseline）。");
        var baselineWorks = WaitFor(() => recorder.TotalDownCount > 0 && recorder.LiveDownCount == 0);
        Console.WriteLine($"  baseline: {baselineWorks}");

        // Phase 2: スリープ→復帰。wall clock の跳び（poll 間隔 >5s）で復帰を検出する
        Console.WriteLine("Phase 2: PC をスリープし、10 秒ほど待ってから復帰させてください。");
        var lastPoll = DateTime.UtcNow;
        double maxGapSeconds = 0;
        var sleepDetected = baselineWorks && WaitFor(() =>
        {
            var now = DateTime.UtcNow;
            var gap = (now - lastPoll).TotalSeconds;
            lastPoll = now;
            if (gap > maxGapSeconds)
            {
                maxGapSeconds = gap;
            }

            return gap > 5;
        });
        Console.WriteLine($"  スリープ復帰検出: {sleepDetected}（最大 poll 間隔 {maxGapSeconds:F1}s）");

        // Phase 3: 復帰後の動作（down/up 対が新たに成立し、保持中 output なし）
        var downsBeforePhase3 = recorder.TotalDownCount;
        Console.WriteLine("Phase 3: もう一度側面ボタンを押して離してください。");
        var postResumeWorks = sleepDetected
            && WaitFor(() => recorder.TotalDownCount > downsBeforePhase3 && recorder.LiveDownCount == 0);
        Console.WriteLine($"  復帰後の down/up: {postResumeWorks}");

        string? stopError = null;
        try
        {
            pump.Stop();
        }
        catch (Exception ex)
        {
            stopError = $"{ex.GetType().Name}: {ex.Message}";
        }

        string? watchdogError = null;
        try
        {
            watchdog.Shutdown();
        }
        catch (Exception ex)
        {
            watchdogError = $"{ex.GetType().Name}: {ex.Message}";
        }

        var result = new SleepSmokeResult
        {
            Probe = "sleep-smoke",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            DeviceKind = deviceKind,
            Devices = devices.Select(device => device.DeviceInstanceId).ToList(),
            BaselineWorks = baselineWorks,
            SleepDetected = sleepDetected,
            MaxPollGapSeconds = Math.Round(maxGapSeconds, 1),
            PostResumeWorks = postResumeWorks,
            DeviceChangesObserved = changeObserver.ChangesSnapshot(),
            ProcessedInputCount = pump.ProcessedCount,
            DroppedInputCount = droppedProbe(),
            WrongReleaseCount = recorder.WrongReleaseCount,
            LiveDownCountAtEnd = recorder.LiveDownCount,
            Failure = pump.Failure?.ToString(),
            StopError = stopError,
            WatchdogError = watchdogError,
            EmittedEdges = recorder.Records,
        };

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"sleep-smoke-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        var json = JsonSerializer.Serialize(result, JsonOptions.Value);
        File.WriteAllText(path, json);
        Console.WriteLine(json);
        Console.WriteLine($"output: {path}");

        var ok = baselineWorks && sleepDetected && postResumeWorks
            && recorder.WrongReleaseCount == 0 && recorder.LiveDownCount == 0
            && pump.Failure is null && stopError is null && watchdogError is null;
        return ok ? 0 : 2;
    }

    private static MappingProfile BuildG600Profile()
    {
        var bindings = new List<MappingBinding>();
        for (var i = 0; i < 12; i++)
        {
            bindings.Add(new MappingBinding($"G{9 + i}", "normal", [$"Key:F{13 + i}"]));
        }

        return new MappingProfile(
            "sleep-smoke-g600",
            "map-r1",
            defaultLayerId: "normal",
            layerIds: ["normal"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string>(),
            bindings);
    }

    private static MappingProfile BuildG13Profile()
    {
        var bindings = new List<MappingBinding>();
        for (var i = 0; i < 12; i++)
        {
            bindings.Add(new MappingBinding($"G{1 + i}", "base", [$"Key:F{13 + i}"]));
        }

        return new MappingProfile(
            "sleep-smoke-g13",
            "map-r1",
            defaultLayerId: "base",
            layerIds: ["base"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string>(),
            bindings);
    }

    // pump が pull した device change を証跡用に記録して素通しする
    private sealed class ChangeObservingSource(IDeviceInputSource inner) : IDeviceInputSource, IDeviceChangeSource
    {
        private readonly List<string> _changes = [];

        public IReadOnlyList<DeviceInstance> EnumerateDevices() => inner.EnumerateDevices();

        public bool TryPull(out PhysicalInput input) => inner.TryPull(out input);

        public bool TryPullDeviceChange(out DeviceChange change)
        {
            if (inner is IDeviceChangeSource changeSource && changeSource.TryPullDeviceChange(out change))
            {
                lock (_changes)
                {
                    _changes.Add($"{change.Kind}@{change.MonotonicMs:F0}ms");
                }

                return true;
            }

            change = null!;
            return false;
        }

        public List<string> ChangesSnapshot()
        {
            lock (_changes)
            {
                return [.. _changes];
            }
        }
    }

    // 実 emit しつつ down/up 均衡を追跡する（hotplug-smoke と同型）
    private sealed class SleepBalanceEmitter(IOutputEmitter inner) : IOutputEmitter
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly HashSet<string> _liveDowns = new(StringComparer.Ordinal);
        private long _totalDownCount;
        private long _wrongReleaseCount;

        public List<EmittedEdgeRecord> Records { get; } = [];

        public long TotalDownCount => Interlocked.Read(ref _totalDownCount);

        public long WrongReleaseCount => Interlocked.Read(ref _wrongReleaseCount);

        public int LiveDownCount
        {
            get
            {
                lock (_liveDowns)
                {
                    return _liveDowns.Count;
                }
            }
        }

        public void Emit(IReadOnlyList<MappedOutputEdge> edges)
        {
            inner.Emit(edges);
            var at = _clock.Elapsed.TotalMilliseconds;
            lock (_liveDowns)
            {
                foreach (var edge in edges)
                {
                    if (edge.Edge == PhysicalInputEdge.Down)
                    {
                        _liveDowns.Add(edge.Output);
                        Interlocked.Increment(ref _totalDownCount);
                    }
                    else if (!_liveDowns.Remove(edge.Output))
                    {
                        Interlocked.Increment(ref _wrongReleaseCount);
                    }
                }
            }

            lock (Records)
            {
                foreach (var edge in edges)
                {
                    Records.Add(new EmittedEdgeRecord
                    {
                        AtMs = Math.Round(at, 3),
                        Output = edge.Output,
                        Edge = edge.Edge.ToString(),
                    });
                }
            }
        }
    }
}

internal sealed class SleepSmokeResult
{
    public required string Probe { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string OsVersion { get; init; }
    public required string DeviceKind { get; init; }
    public required List<string> Devices { get; init; }
    public required bool BaselineWorks { get; init; }
    public required bool SleepDetected { get; init; }
    public required double MaxPollGapSeconds { get; init; }
    public required bool PostResumeWorks { get; init; }
    public required List<string> DeviceChangesObserved { get; init; }
    public required long ProcessedInputCount { get; init; }
    public required long DroppedInputCount { get; init; }
    public required long WrongReleaseCount { get; init; }
    public required int LiveDownCountAtEnd { get; init; }
    public string? Failure { get; init; }
    public string? StopError { get; init; }
    public string? WatchdogError { get; init; }
    public required List<EmittedEdgeRecord> EmittedEdges { get; init; }
}
