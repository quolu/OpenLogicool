using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenLogicool.Devices.G13;

namespace OpenLogicool.Probe;

/// <summary>
/// G13 のバックライト色を Windows 標準 HID の feature report で変えられるかを確かめる実験。
/// 根拠は rag/openlogicool/g13-backlight-color-2026-10-10.md（公開実装 libg13 の SET_REPORT 0x307）。
/// 段階を分けて実行する:
///   --inspect-only            列挙と feature report 5／7 の読み出しだけ（書かない）
///   --set R,G,B               色を1回だけ書く
///   --sweep --seconds N --rate HZ [--restore R,G,B] [--wait-for-press]
///                             虹色を連続で書き、書き込みの所要時間・失敗・入力の取りこぼしを測る
///                             （--wait-for-press は G13 のボタンが押されてから始める）
/// 送るのは feature report 7（色）だけで、driver・firmware・profile には触れない。
/// </summary>
internal static class G13BacklightSmoke
{
    private const byte ModeLedReportId = 5;
    private const byte ColorReportId = 7;

    public static int Run(string[] args, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var inspectOnly = args.Contains("--inspect-only", StringComparer.Ordinal);
        var setColor = ParseColor(Option(args, "--set"));
        var restoreColor = ParseColor(Option(args, "--restore"));
        var sweep = args.Contains("--sweep", StringComparer.Ordinal);
        var seconds = int.Parse(Option(args, "--seconds") ?? "20");
        var rate = int.Parse(Option(args, "--rate") ?? "10");
        if (!inspectOnly && setColor is null && !sweep)
        {
            Console.Error.WriteLine("[g13-backlight] --inspect-only、--set R,G,B、--sweep のどれかを指定してください。");
            return 1;
        }

        var collections = new G13LcdHidAccess().EnumerateCollections();
        var withFeature = collections.Where(candidate => candidate.FeatureReportByteLength > 0).ToArray();
        var collection = withFeature.Length == 1 ? withFeature[0] : null;
        var evidence = new Dictionary<string, object?>
        {
            ["Probe"] = "g13-backlight-smoke",
            ["CapturedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["Machine"] = Environment.MachineName,
            ["OsVersion"] = Environment.OSVersion.VersionString,
            ["Arguments"] = args,
            ["Collections"] = collections,
        };
        if (collection is null)
        {
            return Finish(outputDirectory, evidence, "feature report を持つ G13 の HID collection が一意に見つかりません。");
        }

        Console.WriteLine($"[g13-backlight] feature report 長 {collection.FeatureReportByteLength} bytes: {collection.DevicePath}");
        var handle = CreateFile(collection.DevicePath, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == new IntPtr(-1))
        {
            return Finish(outputDirectory, evidence, $"G13 を開けません: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        try
        {
            var length = collection.FeatureReportByteLength;
            var before = new Dictionary<string, object?>();
            foreach (var reportId in new[] { ModeLedReportId, ColorReportId })
            {
                before[$"Report{reportId}"] = ReadFeature(handle, reportId, length);
            }

            evidence["Before"] = before;
            Console.WriteLine($"[g13-backlight] 読み出し: {JsonSerializer.Serialize(before)}");
            if (inspectOnly)
            {
                return Finish(outputDirectory, evidence, null);
            }

            if (setColor is { } single)
            {
                var result = WriteColor(handle, length, single);
                evidence["Set"] = new { Color = single, result.Succeeded, result.Win32Error, result.ElapsedMs };
                evidence["AfterSet"] = ReadFeature(handle, ColorReportId, length);
                Console.WriteLine($"[g13-backlight] 色 {single} を書きました: 成功={result.Succeeded} error={result.Win32Error} {result.ElapsedMs:0.00}ms");
                if (!result.Succeeded)
                {
                    return Finish(outputDirectory, evidence, "色の書き込みが失敗しました。");
                }
            }

            if (sweep)
            {
                evidence["Sweep"] = Sweep(handle, length, seconds, rate, args.Contains("--wait-for-press", StringComparer.Ordinal));
                if (restoreColor is { } restore)
                {
                    var result = WriteColor(handle, length, restore);
                    evidence["Restore"] = new { Color = restore, result.Succeeded, result.Win32Error };
                }
            }

            return Finish(outputDirectory, evidence, null);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>虹色を一定の間隔で書き続け、1回ごとの所要時間と失敗、G13 入力の取りこぼしを数える。</summary>
    private static object Sweep(IntPtr handle, int length, int seconds, int rate, bool waitForPress)
    {
        using var input = new G13RawInputSource();
        var devices = input.EnumerateDevices().Count;
        var downs = 0;
        var ups = 0;
        if (waitForPress)
        {
            Console.WriteLine("[g13-backlight] G13 のボタンが押されるのを待っています（120 秒まで）。");
            var waited = Stopwatch.StartNew();
            while (downs == 0 && waited.Elapsed < TimeSpan.FromSeconds(120))
            {
                while (input.TryPull(out var first))
                {
                    if (first.Edge == OpenLogicool.Contracts.Devices.Shared.PhysicalInputEdge.Down) downs++; else ups++;
                }

                Thread.Sleep(2);
            }

            if (downs == 0)
            {
                return new { Started = false, Reason = "120 秒以内に G13 のボタンが押されませんでした。", G13RawInputDevices = devices };
            }
        }

        Console.WriteLine($"[g13-backlight] {seconds} 秒間、毎秒 {rate} 回の色を書きます。その間、G1 を何度か押してください。");
        var elapsed = new List<double>();
        var failures = 0;
        var interval = TimeSpan.FromSeconds(1.0 / rate);
        var clock = Stopwatch.StartNew();
        var next = TimeSpan.Zero;
        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (clock.Elapsed >= next)
            {
                var hue = clock.Elapsed.TotalSeconds * 60 % 360;
                var result = WriteColor(handle, length, FromHue(hue));
                elapsed.Add(result.ElapsedMs);
                if (!result.Succeeded)
                {
                    failures++;
                }

                next += interval;
            }

            while (input.TryPull(out var edge))
            {
                if (edge.Edge == OpenLogicool.Contracts.Devices.Shared.PhysicalInputEdge.Down) downs++; else ups++;
            }

            Thread.Sleep(1);
        }

        elapsed.Sort();
        return new
        {
            Seconds = seconds,
            RatePerSecond = rate,
            Writes = elapsed.Count,
            Failures = failures,
            WriteMsMedian = elapsed.Count == 0 ? 0 : elapsed[elapsed.Count / 2],
            WriteMsP99 = elapsed.Count == 0 ? 0 : elapsed[(int)(elapsed.Count * 0.99)],
            WriteMsMax = elapsed.Count == 0 ? 0 : elapsed[^1],
            Started = true,
            G13RawInputDevices = devices,
            G13Downs = downs,
            G13Ups = ups,
            G13DroppedInputs = input.DroppedInputCount,
        };
    }

    private static (bool Succeeded, int Win32Error, double ElapsedMs) WriteColor(IntPtr handle, int length, (byte R, byte G, byte B) color)
    {
        // feature report 7: [report ID, 赤, 緑, 青, 0]。残りは 0 で埋める（Windows は collection の feature report 長を要求する）。
        var buffer = new byte[length];
        buffer[0] = ColorReportId;
        buffer[1] = color.R;
        buffer[2] = color.G;
        buffer[3] = color.B;
        var started = Stopwatch.GetTimestamp();
        var succeeded = HidD_SetFeature(handle, buffer, buffer.Length);
        var error = succeeded ? 0 : Marshal.GetLastWin32Error();
        return (succeeded, error, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static object ReadFeature(IntPtr handle, byte reportId, int length)
    {
        var buffer = new byte[length];
        buffer[0] = reportId;
        var succeeded = HidD_GetFeature(handle, buffer, buffer.Length);
        return new
        {
            Succeeded = succeeded,
            Win32Error = succeeded ? 0 : Marshal.GetLastWin32Error(),
            FirstBytes = succeeded ? Convert.ToHexString(buffer.AsSpan(0, Math.Min(8, buffer.Length))) : null,
        };
    }

    private static (byte R, byte G, byte B) FromHue(double hue)
    {
        var sector = hue / 60;
        var rising = (byte)(255 * (sector % 1));
        var falling = (byte)(255 - rising);
        return (int)sector switch
        {
            0 => (255, rising, 0),
            1 => (falling, 255, 0),
            2 => (0, 255, rising),
            3 => (0, falling, 255),
            4 => (rising, 0, 255),
            _ => (255, 0, falling),
        };
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static (byte R, byte G, byte B)? ParseColor(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var parts = text.Split(',');
        if (parts.Length != 3)
        {
            throw new ArgumentException($"色は R,G,B（各 0〜255）で指定してください: {text}");
        }

        return (byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]));
    }

    private static int Finish(string outputDirectory, Dictionary<string, object?> evidence, string? failure)
    {
        evidence["Failure"] = failure;
        var path = Path.Combine(outputDirectory, $"g13-backlight-smoke-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine($"[g13-backlight] 記録: {path}");
        if (failure is not null)
        {
            Console.Error.WriteLine($"[g13-backlight] 失敗: {failure}");
            return 1;
        }

        return 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string fileName, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetFeature(IntPtr handle, byte[] buffer, int length);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(IntPtr handle, byte[] buffer, int length);
}
