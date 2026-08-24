using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using OpenLogicool.Capture;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

internal static class LiveDiscoveryNanoPrimitiveSmoke
{
    private const int F13VirtualKey = 0x7C;

    public static int Run(string[] arguments, string outputDirectory)
    {
        try
        {
            return RunAsync(arguments, Path.GetFullPath(outputDirectory)).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] arguments, string outputDirectory)
    {
        var port = RequiredArgument(arguments, "--port");
        var processName = RequiredArgument(arguments, "--process");
        Directory.CreateDirectory(outputDirectory);

        var target = LiveDiscoveryObserveSmoke.FindWindow(processName);
        var before = await CaptureAsync(target, outputDirectory, "before");
        using var observer = HidObservationWindow.Start();
        var exchange = new ProbeSerialPortFrameExchange(port);
        using var residentSession = new SerialHidResidentOutputSession(
            exchange,
            new SerialHidSemanticVersion(1, 1, 0),
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromMilliseconds(50),
            SerialHidProtocolV1.AllCapabilities);
        residentSession.Start();
        var emitter = residentSession.Emitter as SerialHidEmitter
            ?? throw new InvalidOperationException("Nano resident sessionがSerialHidEmitterを返しませんでした。");

        residentSession.Protocol.SendAllUp();
        var altTabCount = LiveDiscoveryNanoActionSmoke.FocusTargetWithNano(target.Window, emitter);
        observer.Clear();
        var marker = observer.Count;

        emitter.Emit([Down("Key:F13")]);
        emitter.Emit([Up("Key:F13")]);
        var wheelUpAck = residentSession.Protocol.SendMouseDelta(0, 0, 1);
        var wheelDownAck = residentSession.Protocol.SendMouseDelta(0, 0, -1);

        var expected = new[]
        {
            new ObservedHidEvent("key", "down", F13VirtualKey, false, 0),
            new ObservedHidEvent("key", "up", F13VirtualKey, false, 0),
            new ObservedHidEvent("mouse", "wheel", 120, false, 0),
            new ObservedHidEvent("mouse", "wheel", -120, false, 0),
        };
        var exactSequenceObserved = true;
        string? receiverFailure = null;
        try
        {
            observer.WaitFor(expected, marker, TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException exception)
        {
            exactSequenceObserved = false;
            receiverFailure = exception.Message;
        }
        var observed = observer.Events.Skip(marker).ToArray();
        var injected = observer.InjectedEvents.ToArray();
        var targetForeground = GetForegroundWindow() == target.Window;
        var after = await CaptureAsync(target, outputDirectory, "after");
        var passed = exactSequenceObserved && injected.Length == 0 && targetForeground;

        var evidence = new
        {
            SchemaVersion = "1.0.0",
            Probe = "live-discovery-nano-primitives",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Target = new
            {
                target.ProcessId,
                target.ProcessName,
                target.WindowTitle,
                target.CaptureRect,
                target.Dpi,
                ForegroundAfterDispatch = targetForeground,
            },
            Nano = new
            {
                Port = port,
                FirmwareVersion = residentSession.Protocol.ReadyInfo.FirmwareVersion.ToString(),
                Capabilities = $"0x{(ushort)residentSession.Protocol.ReadyInfo.Capabilities:X4}",
                Route = "NanoSerialHid",
                Fallback = "None",
                AltTabCount = altTabCount,
                F13 = "down/up; matching ACK per state",
                WheelUpAck = wheelUpAck,
                WheelDownAck = wheelDownAck,
                AllUp = true,
            },
            Receiver = new
            {
                Surface = "Windows low-level keyboard/mouse hooks",
                Expected = expected,
                Observed = observed,
                Injected = injected,
                ExactSequenceObserved = exactSequenceObserved,
                Failure = receiverFailure,
                AllObservedEventsArePhysical = observed.All(item => !item.IsInjected),
            },
            Screen = new { Before = before, After = after },
            Classification = new
            {
                GenericKey = exactSequenceObserved
                    ? "Windows receiver accepted physical F13 while NIKKE was foreground"
                    : "Unverified at NIKKE foreground: standard-integrity hook observed no events",
                Scroll = exactSequenceObserved
                    ? "Windows receiver accepted physical wheel +120/-120 while NIKKE was foreground"
                    : "Unverified at NIKKE foreground: standard-integrity hook observed no events",
                GameEffect = "Unverified; this probe makes no in-game outcome claim",
            },
            Input = new
            {
                DispatchRoute = "NanoSerialHid only",
                SendInputDispatchCount = 0,
                ComputerUseDispatchCount = 0,
                ExternalAiTransmissionCount = 0,
                ExternalAiApiCostUsd = 0,
            },
            Passed = passed,
        };
        var outputPath = Path.Combine(
            outputDirectory,
            $"live-discovery-nano-primitives-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(evidence, JsonOptions));
        Console.WriteLine(outputPath);
        Console.WriteLine(passed ? "PASS" : "FAIL: physical receiver classification did not hold");
        return passed ? 0 : 3;
    }

    private static async Task<object> CaptureAsync(
        LiveDiscoveryObserveSmoke.WindowTarget target,
        string outputDirectory,
        string suffix)
    {
        using var source = WgcFrameSource.CreateForWindow(
            target.Window,
            $"window:live-discovery-nano-primitives:{target.ProcessId}");
        var frame = await LiveDiscoveryObserveSmoke.WaitForFrameAsync(source, TimeSpan.FromSeconds(10));
        var png = LiveDiscoveryObserveSmoke.EncodePng(frame);
        var capturedAt = DateTimeOffset.UtcNow;
        var path = Path.Combine(
            outputDirectory,
            $"live-discovery-nano-primitives-{capturedAt:yyyyMMdd-HHmmss-fff}-{suffix}.png");
        await File.WriteAllBytesAsync(path, png.Bytes);
        return new
        {
            CapturedAtUtc = capturedAt,
            Width = png.Width,
            Height = png.Height,
            Sha256 = Convert.ToHexString(SHA256.HashData(png.Bytes)).ToLowerInvariant(),
            LocalPath = path,
        };
    }

    private static MappedOutputEdge Down(string token) => new(token, PhysicalInputEdge.Down);
    private static MappedOutputEdge Up(string token) => new(token, PhysicalInputEdge.Up);

    private static string RequiredArgument(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        if (index < 0 || index == arguments.Length - 1 || string.IsNullOrWhiteSpace(arguments[index + 1]))
        {
            throw new ArgumentException($"{name} が必要です。");
        }
        return arguments[index + 1];
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
