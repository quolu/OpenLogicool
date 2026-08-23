using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Host;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

/// <summary>
/// signal起床を含むFastPathPump→実Serial HID matching ACKの自動latency測定。
/// 物理deviceの機能受入はserial-hid-live-smokeが所有し、このprobeは原因修正後のNFR-002だけをfocusedに測る。
/// </summary>
internal static class SerialHidFastPathLatencySmoke
{
    private const int EdgeCount = 200;

    public static int Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var result = new SerialHidFastPathLatencyResult
        {
            Schema = "openlogicool.serial-hid.fastpath-latency.v1",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            RequestedEdgeCount = EdgeCount,
        };

        FastPathPump? pump = null;
        SerialHidResidentOutputSession? session = null;
        HidObservationWindow? observer = null;
        try
        {
            var selection = new SerialHidDiscoveryService(
                new SetupApiSerialCandidateEnumerator(),
                new SerialPortExchangeFactory()).Resolve(selectedDeviceInstanceId: null);
            result.SelectedDeviceInstanceId = selection.Candidate.DeviceInstanceId;
            result.TransientPort = selection.Candidate.PortName;
            result.FirmwareVersion = $"{selection.ReadyInfo.FirmwareVersion.Major}.{selection.ReadyInfo.FirmwareVersion.Minor}.{selection.ReadyInfo.FirmwareVersion.Patch}";

            session = selection.Session;
            session.Start();
            observer = HidObservationWindow.Start();
            observer.Clear();

            using var source = new SignaledProbeInputSource();
            var runtime = new DeviceMappingRuntime(SignaledProbeInputSource.DeviceId, Profile());
            pump = new FastPathPump(
                [new FastPathSource(source)],
                new Dictionary<string, DeviceMappingRuntime>(StringComparer.Ordinal)
                {
                    [SignaledProbeInputSource.DeviceId] = runtime,
                },
                session.Emitter,
                enableTrace: true,
                traceCapacity: EdgeCount);
            pump.Start();

            for (var index = 0; index < EdgeCount; index++)
            {
                var edge = index % 2 == 0 ? PhysicalInputEdge.Down : PhysicalInputEdge.Up;
                source.Enqueue(new PhysicalInput(
                    ContractSchemaVersions.Revision01,
                    SignaledProbeInputSource.DeviceId,
                    "Pulse",
                    edge,
                    MonotonicMilliseconds(),
                    index + 1));
                var expectedCount = index + 1L;
                if (!SpinWait.SpinUntil(
                        () => pump.ProcessedCount >= expectedCount || pump.Failure is not null,
                        TimeSpan.FromSeconds(2)))
                {
                    throw new TimeoutException($"fast pathがedge {expectedCount}を2秒以内に処理しませんでした。");
                }
                if (pump.Failure is not null)
                {
                    throw new InvalidOperationException("fast pathがlatency測定中にfault停止しました。", pump.Failure);
                }

                // 次edgeはworkerがsignal待機へ戻った後に投入し、空振りpollの偶然で速く見せない。
                Thread.Sleep(2);
            }

            pump.Stop();
            session.Stop();
            Thread.Sleep(150);

            var trace = pump.DrainTrace();
            var hardware = observer.Events
                .Where(entry => !entry.IsInjected && entry.Kind == "key" && entry.Code == 0x87)
                .ToArray();
            var injected = observer.InjectedEvents.ToArray();
            var balance = EventBalance.Analyze(hardware);
            result.ProcessedEdgeCount = pump.ProcessedCount;
            result.TraceEntries = trace;
            result.HardwareEvents = hardware;
            result.InjectedEvents = injected;
            result.WrongReleaseCount = balance.WrongReleaseCount;
            result.StuckOutputs = balance.StuckOutputs;
            result.Latency = LatencySummary.From(trace.Select(entry => entry.DispatchLatencyMs));
            result.Passed = result.ProcessedEdgeCount == EdgeCount
                && trace.Count == EdgeCount
                && hardware.Length >= EdgeCount
                && injected.Length == 0
                && result.WrongReleaseCount == 0
                && result.StuckOutputs.Count == 0
                && result.Latency.P99Milliseconds <= 10
                && pump.Failure is null
                && session.BackgroundFailure is null;
        }
        catch (Exception exception)
        {
            result.Error = exception.ToString();
        }
        finally
        {
            TryStop(pump, result.StopErrors);
            TryStop(session, result.StopErrors);
            pump?.Dispose();
            session?.Dispose();
            observer?.Dispose();
        }

        var path = Path.Combine(outputDirectory, $"serial-hid-fastpath-latency-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine);
        Console.WriteLine(path);
        Console.WriteLine(result.Passed
            ? $"PASS: p99={result.Latency.P99Milliseconds:F3}ms max={result.Latency.MaximumMilliseconds:F3}ms"
            : $"FAIL: {result.Error ?? $"p99={result.Latency.P99Milliseconds:F3}ms max={result.Latency.MaximumMilliseconds:F3}ms"}");
        return result.Passed ? 0 : 1;
    }

    private static MappingProfile Profile() => new(
        "serial-hid-fastpath-latency",
        "map-r1",
        defaultLayerId: "base",
        layerIds: ["base"],
        latchSelectors: new Dictionary<string, string>(),
        holdSelectors: new Dictionary<string, string>(),
        bindings: [new MappingBinding("Pulse", "base", ["Key:F24"])]);

    private static void TryStop(FastPathPump? pump, List<string> errors)
    {
        try
        {
            if (pump is { IsRunning: true })
            {
                pump.Stop();
            }
        }
        catch (Exception exception)
        {
            errors.Add($"pump: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void TryStop(SerialHidResidentOutputSession? session, List<string> errors)
    {
        try
        {
            session?.Stop();
        }
        catch (Exception exception)
        {
            errors.Add($"session: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static double MonotonicMilliseconds() =>
        Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class SignaledProbeInputSource : IDeviceInputSource, IDeviceInputSignalSource, IDisposable
    {
        public const string DeviceId = "serial-hid-latency-source";
        private readonly ConcurrentQueue<PhysicalInput> inputs = new();
        private readonly AutoResetEvent inputAvailable = new(false);

        public WaitHandle InputAvailable => inputAvailable;

        public IReadOnlyList<DeviceInstance> EnumerateDevices() => [];

        public void Enqueue(PhysicalInput input)
        {
            inputs.Enqueue(input);
            inputAvailable.Set();
        }

        public bool TryPull(out PhysicalInput input) => inputs.TryDequeue(out input!);

        public void Dispose() => inputAvailable.Dispose();
    }
}

internal sealed class SerialHidFastPathLatencyResult
{
    public required string Schema { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public string? SelectedDeviceInstanceId { get; set; }
    public string? TransientPort { get; set; }
    public string? FirmwareVersion { get; set; }
    public int RequestedEdgeCount { get; set; }
    public long ProcessedEdgeCount { get; set; }
    public IReadOnlyList<InputTraceEntry> TraceEntries { get; set; } = [];
    public IReadOnlyList<ObservedHidEvent> HardwareEvents { get; set; } = [];
    public IReadOnlyList<ObservedHidEvent> InjectedEvents { get; set; } = [];
    public int WrongReleaseCount { get; set; }
    public IReadOnlyList<string> StuckOutputs { get; set; } = [];
    public LatencySummary Latency { get; set; } = LatencySummary.Empty;
    public List<string> StopErrors { get; } = [];
    public bool Passed { get; set; }
    public string? Error { get; set; }
}
