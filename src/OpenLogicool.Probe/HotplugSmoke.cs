using System.Diagnostics;
using System.Text.Json;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G13;
using OpenLogicool.Devices.G600;
using OpenLogicool.Domain;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

// hotplug 実機 smoke（Phase 2 Exit 条件2の抜線実測）: 実機の USB 抜挿で
//   ①ボタン押下中の切断 → 所有 output の自動 release（DEV-008）
//   ②再接続 → 新規 down の受理再開と down/up の正常動作
// を FastPathPump 経路（Mapping Runtime＋SendInput＋watchdog）で観測する。
// 出力は無害 key F13〜F24 だけ。3 段階の対話手順で、各段階の成否を JSON 証跡に残す。
internal static class HotplugSmoke
{
    public static int Run(string[] arguments, string outputDirectory)
    {
        var deviceKind = "g600";
        var phaseTimeoutMs = 60_000;
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
        var recorder = new BalanceTrackingEmitter(new GuardedOutputEmitter(new SendInputEmitter(), watchdog));
        var pump = new FastPathPump([new FastPathSource(source, droppedProbe)], runtimes, recorder);
        pump.Start();

        bool AnyStopped() => runtimes.Values.Any(runtime => !runtime.AcceptsNewDowns);
        bool AllAccepting() => runtimes.Values.All(runtime => runtime.AcceptsNewDowns);
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

        // Phase 1: 押下中に抜線 → 切断検出（新規 down 停止）と自動 release
        Console.WriteLine("Phase 1: 側面ボタン（G600: G9〜G20 / G13: G1〜G12）を押したまま、USB を抜いてください。");
        var downObserved = WaitFor(() => recorder.TotalDownCount > 0);
        var heldAtRemoval = recorder.LiveDownSnapshot();
        var removalDetected = downObserved && WaitFor(AnyStopped);
        var releasedOnRemoval = removalDetected && WaitFor(() => recorder.LiveDownCount == 0);
        Console.WriteLine($"  down 観測: {downObserved} / 切断検出: {removalDetected} / 自動 release: {releasedOnRemoval}");

        // Phase 2: 再接続 → 新規 down の受理再開
        Console.WriteLine("Phase 2: USB を挿し直してください。");
        var resumed = releasedOnRemoval && WaitFor(AllAccepting);
        Console.WriteLine($"  再開: {resumed}");

        // Phase 3: 再接続後の通常動作（down/up 対）
        var downsBeforePhase3 = recorder.TotalDownCount;
        Console.WriteLine("Phase 3: もう一度ボタンを押して離してください。");
        var postResumeWorks = resumed
            && WaitFor(() => recorder.TotalDownCount > downsBeforePhase3 && recorder.LiveDownCount == 0);
        Console.WriteLine($"  再接続後の down/up: {postResumeWorks}");

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

        var result = new HotplugSmokeResult
        {
            Probe = "hotplug-smoke",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            DeviceKind = deviceKind,
            Devices = devices.Select(device => device.DeviceInstanceId).ToList(),
            DownObserved = downObserved,
            RemovalDetected = removalDetected,
            HeldOutputsBeforeRemoval = heldAtRemoval,
            ReleasedOnRemoval = releasedOnRemoval,
            Resumed = resumed,
            PostResumeWorks = postResumeWorks,
            ProcessedInputCount = pump.ProcessedCount,
            DroppedInputCount = droppedProbe(),
            WrongReleaseCount = recorder.WrongReleaseCount,
            Failure = pump.Failure?.ToString(),
            StopError = stopError,
            WatchdogError = watchdogError,
            EmittedEdges = recorder.Records,
        };

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"hotplug-smoke-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        var json = JsonSerializer.Serialize(result, JsonOptions.Value);
        File.WriteAllText(path, json);
        Console.WriteLine(json);
        Console.WriteLine($"output: {path}");

        var ok = downObserved && removalDetected && releasedOnRemoval && resumed && postResumeWorks
            && recorder.WrongReleaseCount == 0 && pump.Failure is null && stopError is null && watchdogError is null;
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
            "hotplug-smoke-g600",
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
            "hotplug-smoke-g13",
            "map-r1",
            defaultLayerId: "base",
            layerIds: ["base"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string>(),
            bindings);
    }

    // 送出を実 emit しつつ down/up の均衡を追跡する（wrong release＝対応 down のない up の検出込み）
    private sealed class BalanceTrackingEmitter(IOutputEmitter inner) : IOutputEmitter
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

        public List<string> LiveDownSnapshot()
        {
            lock (_liveDowns)
            {
                return [.. _liveDowns.Order(StringComparer.Ordinal)];
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

internal sealed class HotplugSmokeResult
{
    public required string Probe { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string OsVersion { get; init; }
    public required string DeviceKind { get; init; }
    public required List<string> Devices { get; init; }
    public required bool DownObserved { get; init; }
    public required bool RemovalDetected { get; init; }
    public required List<string> HeldOutputsBeforeRemoval { get; init; }
    public required bool ReleasedOnRemoval { get; init; }
    public required bool Resumed { get; init; }
    public required bool PostResumeWorks { get; init; }
    public required long ProcessedInputCount { get; init; }
    public required long DroppedInputCount { get; init; }
    public required long WrongReleaseCount { get; init; }
    public string? Failure { get; init; }
    public string? StopError { get; init; }
    public string? WatchdogError { get; init; }
    public required List<EmittedEdgeRecord> EmittedEdges { get; init; }
}
