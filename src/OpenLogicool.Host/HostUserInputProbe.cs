using System.Diagnostics;
using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

/// <summary>手入力監視とNano除外を、一回の正規診断で観測する。</summary>
internal static class HostUserInputProbe
{
    public static int Run(string[] arguments)
    {
        try { return RunCore(arguments); }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 2; }
    }

    private static int RunCore(string[] arguments)
    {
        var durationIndex = Array.IndexOf(arguments, "--duration-ms");
        var duration = durationIndex < 0 ? 12000 : int.Parse(arguments[durationIndex + 1]);
        if (duration <= 0) throw new ArgumentException("観測時間は正のミリ秒です。");
        var discovery = new SerialHidDiscoveryService(new SetupApiSerialCandidateEnumerator(), new SerialPortExchangeFactory());
        var selection = discovery.Resolve(null, SerialHidProtocolV1.AllCapabilities);
        using var session = selection.Session;
        session.Start();
        var matcher = new NanoRawInputMatcher(selection.Candidate);
        using var monitor = new RawUserInputMonitor(matcher.Matches);
        var samples = new List<object>();
        var clock = Stopwatch.StartNew();
        var sent = false;
        UserInputPauseSnapshot? before = null;
        UserInputPauseSnapshot? after = null;
        long sentAt = 0;
        while (clock.ElapsedMilliseconds < duration)
        {
            var snapshot = monitor.Snapshot();
            samples.Add(new { AtMs = clock.ElapsedMilliseconds, State = snapshot });
            if (!sent && !snapshot.Paused)
            {
                before = snapshot;
                var emitter = (SerialHidEmitter)session.Emitter;
                // クリックや通常キーを使わず、物理HIDの入力元だけを検証する。
                emitter.Emit([new("Key:F24", PhysicalInputEdge.Down), new("Key:F24", PhysicalInputEdge.Up)]);
                session.Protocol.SendMouseDelta(1, 0, 0);
                session.Protocol.SendMouseDelta(-1, 0, 0);
                sent = true; sentAt = clock.ElapsedMilliseconds;
            }
            if (sent && after is null && clock.ElapsedMilliseconds - sentAt >= 500) after = snapshot;
            Thread.Sleep(50);
        }
        var result = new { Mode = "user-input-probe", NanoContainer = matcher.Container,
            StartupHeldCount = monitor.StartupHeldCount,
            SoftwareMoves = monitor.SoftwareMoves, SoftwarePositionChanges = monitor.SoftwarePositionChanges,
            TestSent = sent, BeforeNano = before, AfterNano = after, Samples = samples };
        var json = JsonSerializer.Serialize(result);
        var outputIndex = Array.IndexOf(arguments, "--out");
        if (outputIndex >= 0)
        {
            var path = Path.GetFullPath(arguments[outputIndex + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        Console.WriteLine(json);
        return 0;
    }
}
