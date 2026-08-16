using System.Diagnostics;
using System.Text.Json;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G13;
using OpenLogicool.Devices.G600;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

// fast path 通し smoke: 実機 G13/G600 → RawInputSource → FastPathPump（Mapping Runtime）→
// GuardedOutputEmitter（SendInput＋watchdog）の全経路を resident で回す（計画 §6.1〜6.2）。
// 出力は無害 key F13〜F24 だけ。layer 動作も観測する:
//   G600: G9〜G20 → F13〜F24（通常層）、G6 hold で shift 層（G9→F24 逆順デモ）。
//   G13:  G1〜G12 → F13〜F24（base 層）、M2 latch で m2 層（G1→F24 逆順デモ）。
// 全 emit を timestamp 付きで記録し、drop・fault・処理件数を JSON 証跡に残す。
internal static class FastPathSmoke
{
    public static int Run(string[] arguments, string outputDirectory)
    {
        var durationMs = 45_000;
        string? watchdogOverride = null;
        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "--duration-ms" when i + 1 < arguments.Length:
                    durationMs = int.Parse(arguments[++i]);
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

        using var g13Source = new G13RawInputSource();
        using var g600Source = new G600RawInputSource();

        var g13Devices = g13Source.EnumerateDevices();
        var g600Devices = g600Source.EnumerateDevices();
        if (g13Devices.Count == 0 && g600Devices.Count == 0)
        {
            Console.Error.WriteLine("G13 も G600 も見つかりません。");
            return 1;
        }

        var runtimes = new Dictionary<string, DeviceMappingRuntime>(StringComparer.Ordinal);
        foreach (var device in g13Devices)
        {
            runtimes[device.DeviceInstanceId] = new DeviceMappingRuntime(device.DeviceInstanceId, BuildG13Profile());
        }

        foreach (var device in g600Devices)
        {
            runtimes[device.DeviceInstanceId] = new DeviceMappingRuntime(device.DeviceInstanceId, BuildG600Profile());
        }

        using var watchdog = WatchdogChannel.Start(watchdogPath);
        var recorder = new RecordingEmitter(new GuardedOutputEmitter(new SendInputEmitter(), watchdog));

        var pump = new FastPathPump(
            [
                new FastPathSource(g13Source, () => g13Source.DroppedInputCount),
                new FastPathSource(g600Source, () => g600Source.DroppedInputCount),
            ],
            runtimes,
            recorder);

        Console.WriteLine($"fast path 稼働中（{durationMs / 1000}s）。G13/G600 のボタンを押すと F13〜F24 が送出される。");
        pump.Start();
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < durationMs && pump.Failure is null)
        {
            Thread.Sleep(100);
        }

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

        var result = new FastPathSmokeResult
        {
            Probe = "fastpath-smoke",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            DurationMs = stopwatch.ElapsedMilliseconds,
            G13Devices = g13Devices.Select(d => d.DeviceInstanceId).ToList(),
            G600Devices = g600Devices.Select(d => d.DeviceInstanceId).ToList(),
            ProcessedInputCount = pump.ProcessedCount,
            DroppedG13 = g13Source.DroppedInputCount,
            DroppedG600 = g600Source.DroppedInputCount,
            Failure = pump.Failure?.ToString(),
            StopError = stopError,
            WatchdogError = watchdogError,
            EmittedEdges = recorder.Records,
        };

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"fastpath-smoke-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        var json = JsonSerializer.Serialize(result, JsonOptions.Value);
        File.WriteAllText(path, json);
        Console.WriteLine(json);
        Console.WriteLine($"output: {path}");

        var ok = pump.Failure is null && stopError is null && watchdogError is null && pump.ProcessedCount > 0;
        return ok ? 0 : 2;
    }

    // G600: 通常層 G9〜G20 → F13〜F24。G6 は hold selector（G-Shift 相当）で shift 層は逆順（G9→F24）。
    private static MappingProfile BuildG600Profile()
    {
        var bindings = new List<MappingBinding>();
        for (var i = 0; i < 12; i++)
        {
            bindings.Add(new MappingBinding($"G{9 + i}", "normal", [$"Key:F{13 + i}"]));
            bindings.Add(new MappingBinding($"G{9 + i}", "shift", [$"Key:F{24 - i}"]));
        }

        return new MappingProfile(
            "fastpath-smoke-g600",
            "map-r1",
            defaultLayerId: "normal",
            layerIds: ["normal", "shift"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string> { ["G6"] = "shift" },
            bindings);
    }

    // G13: base 層 G1〜G12 → F13〜F24。M1=base latch・M2=m2 latch で m2 層は逆順（G1→F24）。
    private static MappingProfile BuildG13Profile()
    {
        var bindings = new List<MappingBinding>();
        for (var i = 0; i < 12; i++)
        {
            bindings.Add(new MappingBinding($"G{1 + i}", "base", [$"Key:F{13 + i}"]));
            bindings.Add(new MappingBinding($"G{1 + i}", "m2", [$"Key:F{24 - i}"]));
        }

        return new MappingProfile(
            "fastpath-smoke-g13",
            "map-r1",
            defaultLayerId: "base",
            layerIds: ["base", "m2"],
            latchSelectors: new Dictionary<string, string> { ["M1"] = "base", ["M2"] = "m2" },
            holdSelectors: new Dictionary<string, string>(),
            bindings);
    }

    private sealed class RecordingEmitter(IOutputEmitter inner) : IOutputEmitter
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public List<EmittedEdgeRecord> Records { get; } = [];

        public void Emit(IReadOnlyList<MappedOutputEdge> edges)
        {
            inner.Emit(edges);
            var at = _clock.Elapsed.TotalMilliseconds;
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

internal sealed class FastPathSmokeResult
{
    public required string Probe { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string OsVersion { get; init; }
    public required long DurationMs { get; init; }
    public required List<string> G13Devices { get; init; }
    public required List<string> G600Devices { get; init; }
    public required long ProcessedInputCount { get; init; }
    public required long DroppedG13 { get; init; }
    public required long DroppedG600 { get; init; }
    public string? Failure { get; init; }
    public string? StopError { get; init; }
    public string? WatchdogError { get; init; }
    public required List<EmittedEdgeRecord> EmittedEdges { get; init; }
}

internal sealed class EmittedEdgeRecord
{
    public required double AtMs { get; init; }
    public required string Output { get; init; }
    public required string Edge { get; init; }
}
