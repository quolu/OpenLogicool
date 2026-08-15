using System.Text;
using System.Text.Json;
using OpenLogicool.Devices.G600;

namespace OpenLogicool.Probe;

// G600RawInputSource（live adapter）の実機 smoke。列挙 → N 秒間 pull → 証跡 JSON 封入。
// read-only（Raw Input 受信のみ、device への write なし）。
internal static class G600AdapterSmoke
{
    public static int Run(string[] args, string outputDirectory)
    {
        var seconds = args.Length > 0 ? int.Parse(args[0]) : 30;

        using var source = new G600RawInputSource();
        var devices = source.EnumerateDevices();
        Console.WriteLine($"[g600-smoke] G600 devices: {devices.Count}");
        foreach (var device in devices)
        {
            Console.WriteLine($"[g600-smoke]   {device.DevicePath} container={device.ContainerId}");
        }

        if (devices.Count == 0)
        {
            Console.Error.WriteLine("[g600-smoke] G600 が列挙されませんでした。");
            return 1;
        }

        Console.WriteLine($"[g600-smoke] {seconds}s 間 pull します。G600 のボタン・ホイールを操作してください。");

        var inputs = new List<object>();
        var wheelTicks = new List<object>();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < deadline)
        {
            var pulled = false;
            while (source.TryPull(out var input))
            {
                pulled = true;
                Console.WriteLine($"[g600-smoke] {input.MonotonicMs,10:F1}ms {input.ControlId,-4} {input.Edge} seq={input.ReportSequence}");
                inputs.Add(new { input.MonotonicMs, input.ControlId, Edge = input.Edge.ToString(), input.ReportSequence });
            }

            while (source.TryPullWheel(out var tick))
            {
                pulled = true;
                Console.WriteLine($"[g600-smoke] {tick.MonotonicMs,10:F1}ms WHEEL {tick.Delta:+0;-0} seq={tick.ReportSequence}");
                wheelTicks.Add(new { tick.MonotonicMs, tick.Delta, tick.ReportSequence });
            }

            if (!pulled)
            {
                Thread.Sleep(5);
            }
        }

        var outputPath = Path.Combine(outputDirectory, $"g600-adapter-smoke-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var evidence = new
        {
            Probe = "g600-adapter-smoke",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            DurationSeconds = seconds,
            Devices = devices,
            InputCount = inputs.Count,
            Inputs = inputs,
            WheelTickCount = wheelTicks.Count,
            WheelTicks = wheelTicks,
            source.DroppedInputCount,
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

        Console.WriteLine($"[g600-smoke] inputs={inputs.Count} wheelTicks={wheelTicks.Count} dropped={source.DroppedInputCount}");
        Console.WriteLine($"[g600-smoke] evidence → {outputPath}");
        return 0;
    }
}
