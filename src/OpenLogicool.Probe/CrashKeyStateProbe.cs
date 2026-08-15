using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenLogicool.Probe;

// EXP-IN-03: hard crash 時の合成 output 残留の実測（計画 §14 EXP-IN-03・§6.2 watchdog decision）。
// 子 process が SendInput で key-down を送って保持 → 親が Process.Kill（TerminateProcess = hard crash 相当）→
// 親が GetAsyncKeyState を採取して残留を観測 → 別 process（親）からの SendInput key-up で release 可能かを実測する。
// 既定 VK は F13（0x7C）: ほぼ全ての app で無反応の無害 key。
internal static class CrashKeyStateProbe
{
    private const int DefaultVirtualKey = 0x7C; // VK_F13
    private const int SampleIntervalMs = 250;

    public static int Run(string[] arguments, string outputDirectory)
    {
        var virtualKey = DefaultVirtualKey;
        var observeMs = 5000;
        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "--vk" when i + 1 < arguments.Length:
                    virtualKey = Convert.ToInt32(arguments[++i].Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
                    break;
                case "--observe-ms" when i + 1 < arguments.Length:
                    observeMs = int.Parse(arguments[++i]);
                    break;
            }
        }

        var samples = new List<KeyStateSample>();
        var stopwatch = Stopwatch.StartNew();
        void Sample(string phase) => samples.Add(new KeyStateSample
        {
            Phase = phase,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            IsDown = (GetAsyncKeyState(virtualKey) & 0x8000) != 0,
        });

        Sample("baseline");
        if (samples[0].IsDown)
        {
            Console.Error.WriteLine($"VK 0x{virtualKey:X2} is already down before the experiment; aborting.");
            return 1;
        }

        // 子 process: 自 exe を hold-key mode で起動し、down 送信完了（stdout "HELD"）を待つ
        var child = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = $"hold-key --vk 0x{virtualKey:X2}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        child.Start();
        var line = child.StandardOutput.ReadLine();
        if (line != "HELD")
        {
            try { child.Kill(); } catch { }
            Console.Error.WriteLine($"child did not confirm key-down (got: {line ?? "<eof>"}).");
            return 1;
        }

        Sample("child-holding");
        var downConfirmed = samples[^1].IsDown;

        // hard crash: TerminateProcess（unwind なし・cleanup なし）
        child.Kill();
        child.WaitForExit();
        Sample("immediately-after-kill");

        var observeUntil = stopwatch.ElapsedMilliseconds + observeMs;
        while (stopwatch.ElapsedMilliseconds < observeUntil)
        {
            Thread.Sleep(SampleIntervalMs);
            Sample("post-crash-observation");
        }

        var residual = samples.Where(s => s.Phase is "immediately-after-kill" or "post-crash-observation").Any(s => s.IsDown);
        var residualAtEnd = samples[^1].IsDown;

        // watchdog 相当: 別 process（この親）からの key-up で release できるか
        var releasedByExternalKeyUp = false;
        if (residualAtEnd)
        {
            SendKey(virtualKey, keyUp: true);
            Thread.Sleep(SampleIntervalMs);
            Sample("after-external-key-up");
            releasedByExternalKeyUp = !samples[^1].IsDown;
        }

        var result = new CrashKeyStateResult
        {
            Probe = "crash-keystate",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            VirtualKey = $"0x{virtualKey:X2}",
            DownConfirmedWhileChildAlive = downConfirmed,
            ResidualAfterHardKill = residual,
            ResidualAtObservationEnd = residualAtEnd,
            ReleasedByExternalKeyUp = residualAtEnd ? releasedByExternalKeyUp : null,
            FinalStateIsDown = samples[^1].IsDown,
            Samples = samples,
        };

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"crash-keystate-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        var json = JsonSerializer.Serialize(result, JsonOptions.Value);
        File.WriteAllText(path, json);
        Console.WriteLine(json);
        Console.WriteLine($"output: {path}");

        // 残留が release できず残った場合だけ異常終了（実験自体は残留の有無どちらでも成立）
        return samples[^1].IsDown ? 2 : 0;
    }

    // 子 process mode: key-down を送って保持し続ける。親の TerminateProcess で殺される前提で key-up は決して送らない。
    public static int RunHold(string[] arguments)
    {
        var virtualKey = DefaultVirtualKey;
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == "--vk" && i + 1 < arguments.Length)
                virtualKey = Convert.ToInt32(arguments[++i].Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
        }

        SendKey(virtualKey, keyUp: false);
        Console.WriteLine("HELD");
        Console.Out.Flush();
        Thread.Sleep(TimeSpan.FromMinutes(2));

        // ここへ到達するのは親が kill しなかった異常系だけ。key を残さない。
        SendKey(virtualKey, keyUp: true);
        return 1;
    }

    private static void SendKey(int virtualKey, bool keyUp)
    {
        var input = new INPUT
        {
            type = 1, // INPUT_KEYBOARD
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKey,
                    dwFlags = keyUp ? 0x0002u : 0u, // KEYEVENTF_KEYUP
                },
            },
        };
        var sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent != 1)
            throw new InvalidOperationException($"SendInput failed (sent={sent}, error={Marshal.GetLastWin32Error()}).");
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}

internal sealed class CrashKeyStateResult
{
    public required string Probe { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string OsVersion { get; init; }
    public required string VirtualKey { get; init; }
    public required bool DownConfirmedWhileChildAlive { get; init; }
    public required bool ResidualAfterHardKill { get; init; }
    public required bool ResidualAtObservationEnd { get; init; }
    public bool? ReleasedByExternalKeyUp { get; init; }
    public required bool FinalStateIsDown { get; init; }
    public required List<KeyStateSample> Samples { get; init; }
}

internal sealed class KeyStateSample
{
    public required string Phase { get; init; }
    public required long ElapsedMs { get; init; }
    public required bool IsDown { get; init; }
}
