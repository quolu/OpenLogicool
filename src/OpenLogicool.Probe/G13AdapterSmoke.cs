using System.Text;
using System.Text.Json;
using OpenLogicool.Devices.G13;

namespace OpenLogicool.Probe;

// G13RawInputSource（live adapter）の実機 smoke。列挙 → N 秒間 pull → 証跡 JSON 封入。
// read-only（Raw Input 受信のみ、device への write なし）。
internal static class G13AdapterSmoke
{
    public static int Run(string[] args, string outputDirectory)
    {
        var untilG1 = args.Contains("--until-g1", StringComparer.Ordinal);
        var secondsArgument = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
        var seconds = secondsArgument is not null ? int.Parse(secondsArgument) : 30;

        using var source = new G13RawInputSource();
        var devices = source.EnumerateDevices();
        Console.WriteLine($"[g13-smoke] G13 devices: {devices.Count}");
        foreach (var device in devices)
        {
            Console.WriteLine($"[g13-smoke]   {device.DevicePath} container={device.ContainerId}");
        }

        if (devices.Count == 0)
        {
            Console.Error.WriteLine("[g13-smoke] G13 が列挙されませんでした。");
            return 1;
        }

        Console.WriteLine(untilG1
            ? "[g13-smoke] G1のdown/upを観測するまで待機します。G1を1回押して離してください。"
            : $"[g13-smoke] {seconds}s 間 pull します。G13 のキー・スティックを操作してください。");

        var inputs = new List<object>();
        var stickSampleCount = 0L;
        object? firstStick = null;
        object? lastStick = null;
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var sawG1Down = false;
        var sawG1Up = false;

        while (untilG1 ? !sawG1Up : DateTime.UtcNow < deadline)
        {
            var pulled = false;
            while (source.TryPull(out var input))
            {
                pulled = true;
                Console.WriteLine($"[g13-smoke] {input.MonotonicMs,10:F1}ms {input.ControlId,-11} {input.Edge} seq={input.ReportSequence}");
                inputs.Add(new { input.MonotonicMs, input.ControlId, Edge = input.Edge.ToString(), input.ReportSequence });
                if (input.ControlId == "G1" && input.Edge == OpenLogicool.Contracts.Devices.Shared.PhysicalInputEdge.Down)
                {
                    sawG1Down = true;
                }
                else if (input.ControlId == "G1" &&
                         input.Edge == OpenLogicool.Contracts.Devices.Shared.PhysicalInputEdge.Up &&
                         sawG1Down)
                {
                    sawG1Up = true;
                }
            }

            while (source.TryPullStick(out var stick))
            {
                pulled = true;
                stickSampleCount++;
                var record = new { stick.MonotonicMs, stick.X, stick.Y, stick.ReportSequence };
                firstStick ??= record;
                lastStick = record;
            }

            if (!pulled)
            {
                Thread.Sleep(5);
            }
        }

        var outputPath = Path.Combine(outputDirectory, $"g13-adapter-smoke-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var evidence = new
        {
            Probe = "g13-adapter-smoke",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            DurationSeconds = untilG1 ? (int?)null : seconds,
            UntilG1 = untilG1,
            SawG1DownUp = sawG1Down && sawG1Up,
            Devices = devices,
            InputCount = inputs.Count,
            Inputs = inputs,
            StickSampleCount = stickSampleCount,
            FirstStickSample = firstStick,
            LastStickSample = lastStick,
            source.DroppedInputCount,
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

        Console.WriteLine($"[g13-smoke] inputs={inputs.Count} stickSamples={stickSampleCount} dropped={source.DroppedInputCount}");
        Console.WriteLine($"[g13-smoke] evidence → {outputPath}");
        return untilG1 && (!sawG1Up || source.DroppedInputCount != 0) ? 2 : 0;
    }
}
