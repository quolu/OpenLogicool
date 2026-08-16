using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

// Input Emitter＋watchdog の実機 smoke（DEV-006/DEV-009・NFR-008）。無害 key F13/F14 だけを使う。
// phase 1 roundtrip: PhysicalInput → DeviceMappingRuntime → GuardedOutputEmitter(SendInput) の全経路を
//   流し、GetAsyncKeyState で down/up の実反映を確認。watchdog は graceful EXIT で release なし終了。
// phase 2 watchdog-crash: 子 process（emitter-hold）が watchdog 起動→F13 down 保持→親が hard kill →
//   watchdog（孫 process・kill されない）が stdin EOF で F13 を release するまでの実測 latency を記録。
internal static class EmitterSmoke
{
    private const int VkF13 = 0x7C;
    private const int VkF14 = 0x7D;
    private const int VkF15 = 0x7E;

    public static int Run(string[] arguments, string outputDirectory)
    {
        var watchdogPath = ResolveWatchdogPath(arguments);
        if (!File.Exists(watchdogPath))
        {
            Console.Error.WriteLine($"watchdog exe not found: {watchdogPath} (build src/OpenLogicool.Watchdog first)");
            return 1;
        }

        if (IsDown(VkF13) || IsDown(VkF14))
        {
            Console.Error.WriteLine("F13/F14 is already down before the smoke; aborting.");
            return 1;
        }

        var roundtrip = RunRoundtrip(watchdogPath);
        var crash = RunWatchdogCrash(watchdogPath);

        var result = new EmitterSmokeResult
        {
            Probe = "emitter-smoke",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            Roundtrip = roundtrip,
            WatchdogCrash = crash,
        };

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"emitter-smoke-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        var json = JsonSerializer.Serialize(result, JsonOptions.Value);
        File.WriteAllText(path, json);
        Console.WriteLine(json);
        Console.WriteLine($"output: {path}");

        var ok = roundtrip.AllPassed && crash.ReleasedByWatchdog;
        return ok ? 0 : 2;
    }

    private static EmitterRoundtripResult RunRoundtrip(string watchdogPath)
    {
        var profile = new MappingProfile(
            "smoke-profile",
            "smoke-map",
            defaultLayerId: "base",
            layerIds: ["base"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string>(),
            bindings:
            [
                new MappingBinding("G9", "base", ["Key:F13"]),
                new MappingBinding("G10", "base", ["Key:F13", "Key:F14"]),
                new MappingBinding("G11", "base", ["Tap:Key:F13+Key:F14", "Tap:Key:F15"]),
            ]);
        var runtime = new DeviceMappingRuntime("smoke-device", profile);

        using var watchdog = WatchdogChannel.Start(watchdogPath);
        var emitter = new GuardedOutputEmitter(new SendInputEmitter(), watchdog);

        var checks = new List<EmitterCheck>();
        void Check(string name, bool passed) => checks.Add(new EmitterCheck { Name = name, Passed = passed });

        // 単一 key: G9 down → F13 down、G9 up → F13 up
        emitter.Emit(runtime.Process(Input("G9", PhysicalInputEdge.Down)));
        Thread.Sleep(50);
        Check("single-down-F13", IsDown(VkF13));
        emitter.Emit(runtime.Process(Input("G9", PhysicalInputEdge.Up)));
        Thread.Sleep(50);
        Check("single-up-F13", !IsDown(VkF13));

        // chord: G10 down → F13+F14 down（単一 SendInput call）→ up で両方解放
        emitter.Emit(runtime.Process(Input("G10", PhysicalInputEdge.Down)));
        Thread.Sleep(50);
        Check("chord-down-F13", IsDown(VkF13));
        Check("chord-down-F14", IsDown(VkF14));
        emitter.Emit(runtime.Process(Input("G10", PhysicalInputEdge.Up)));
        Thread.Sleep(50);
        Check("chord-up-F13", !IsDown(VkF13));
        Check("chord-up-F14", !IsDown(VkF14));

        // finite sequence（DEV-006）: G11 down → F13+F14 chord tap → F15 tap が単一 SendInput call で完結。
        // 低レベル keyboard hook で注入 event を順序ごと観測し、期待列との完全一致と残留なしを確認。
        using (var recorder = KeyEventRecorder.Start([VkF13, VkF14, VkF15]))
        {
            emitter.Emit(runtime.Process(Input("G11", PhysicalInputEdge.Down)));
            Thread.Sleep(200);
            Check("sequence-exact-order", recorder.Snapshot().SequenceEqual(
            [
                (VkF13, true), (VkF14, true), (VkF14, false), (VkF13, false),
                (VkF15, true), (VkF15, false),
            ]));
        }

        Check("sequence-no-residue-F13", !IsDown(VkF13));
        Check("sequence-no-residue-F14", !IsDown(VkF14));
        Check("sequence-no-residue-F15", !IsDown(VkF15));
        Check("sequence-up-emits-nothing", runtime.Process(Input("G11", PhysicalInputEdge.Up)).Count == 0);

        // handled stop: down 保持中に StopAndReleaseAll → release が実送出される（NFR-008 の release 経路）
        emitter.Emit(runtime.Process(Input("G9", PhysicalInputEdge.Down)));
        Thread.Sleep(50);
        var stopWatch = Stopwatch.StartNew();
        emitter.Emit(runtime.StopAndReleaseAll());
        var stopReleaseMs = stopWatch.ElapsedMilliseconds;
        Thread.Sleep(50);
        Check("stop-releases-F13", !IsDown(VkF13));

        // 通常終了: watchdog は EXIT で release なしに exit 0
        watchdog.Shutdown();
        Check("watchdog-graceful-exit", true);

        return new EmitterRoundtripResult
        {
            Checks = checks,
            StopReleaseEmitMs = stopReleaseMs,
            AllPassed = checks.All(c => c.Passed),
        };
    }

    private static WatchdogCrashResult RunWatchdogCrash(string watchdogPath)
    {
        var child = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = $"emitter-hold --watchdog \"{watchdogPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        child.Start();
        var line = child.StandardOutput.ReadLine();
        if (line != "HELD")
        {
            try { child.Kill(); } catch { }
            return new WatchdogCrashResult
            {
                DownConfirmedWhileChildAlive = false,
                ReleasedByWatchdog = false,
                Error = $"child did not confirm key-down (got: {line ?? "<eof>"}).",
            };
        }

        var downConfirmed = IsDown(VkF13);

        // hard crash 相当（TerminateProcess）。watchdog は孫 process なので巻き込まれない。
        child.Kill();
        child.WaitForExit();

        var stopwatch = Stopwatch.StartNew();
        long? releasedAtMs = null;
        while (stopwatch.ElapsedMilliseconds < 3000)
        {
            if (!IsDown(VkF13))
            {
                releasedAtMs = stopwatch.ElapsedMilliseconds;
                break;
            }

            Thread.Sleep(10);
        }

        if (releasedAtMs is null)
        {
            // 3秒待って残留 = watchdog release 失敗。残置しないよう自前で解消してから失敗として報告する。
            new SendInputEmitter().Emit([new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up)]);
        }

        return new WatchdogCrashResult
        {
            DownConfirmedWhileChildAlive = downConfirmed,
            ReleasedByWatchdog = releasedAtMs is not null,
            ReleaseLatencyMsAfterKill = releasedAtMs,
            MeetsNfr008Budget = releasedAtMs is <= 250,
        };
    }

    // 子 process mode: watchdog を起動し、GuardedOutputEmitter で F13 down を送って保持する。
    // 親の TerminateProcess で殺される前提で key-up は決して送らない。
    public static int RunHold(string[] arguments)
    {
        var watchdogPath = ResolveWatchdogPath(arguments);
        using var watchdog = WatchdogChannel.Start(watchdogPath);
        var emitter = new GuardedOutputEmitter(new SendInputEmitter(), watchdog);

        emitter.Emit([new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down)]);
        Console.WriteLine("HELD");
        Console.Out.Flush();
        Thread.Sleep(TimeSpan.FromMinutes(2));

        // ここへ到達するのは親が kill しなかった異常系だけ。key を残さない。
        emitter.Emit([new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up)]);
        watchdog.Shutdown();
        return 1;
    }

    private static string ResolveWatchdogPath(string[] arguments)
    {
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == "--watchdog" && i + 1 < arguments.Length)
            {
                return Path.GetFullPath(arguments[i + 1]);
            }
        }

        // 既定: repo 内の build 出力（probe と同じ configuration/tfm）
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "OpenLogicool.Watchdog", "bin", "Debug", "net10.0-windows", "OpenLogicool.Watchdog.exe"));
    }

    private static PhysicalInput Input(string controlId, PhysicalInputEdge edge) =>
        new(ContractSchemaVersions.Revision01, "smoke-device", controlId, edge, MonotonicMs: 0, ReportSequence: 0);

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// WH_KEYBOARD_LL による key event の順序付き観測。focus に依存せず注入 event を受けるため、
    /// 一瞬で完結する sequence tap の実発生と順序を検証できる。
    /// </summary>
    private sealed class KeyEventRecorder : IDisposable
    {
        private readonly List<(int Vk, bool IsDown)> _events = [];
        private readonly HashSet<int> _watchedVks;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private LowLevelKeyboardProc? _proc;
        private uint _threadId;

        private KeyEventRecorder(IEnumerable<int> watchedVks)
        {
            _watchedVks = [.. watchedVks];
            _thread = new Thread(HookLoop) { IsBackground = true, Name = "EmitterSmokeKeyRecorder" };
        }

        public static KeyEventRecorder Start(IEnumerable<int> watchedVks)
        {
            var recorder = new KeyEventRecorder(watchedVks);
            recorder._thread.Start();
            if (!recorder._ready.Wait(2000))
            {
                throw new InvalidOperationException("keyboard hook の設置が 2 秒以内に完了しませんでした。");
            }

            return recorder;
        }

        public IReadOnlyList<(int Vk, bool IsDown)> Snapshot()
        {
            lock (_events)
            {
                return [.. _events];
            }
        }

        private void HookLoop()
        {
            _threadId = GetCurrentThreadId();
            _proc = HookCallback;
            var hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _proc, IntPtr.Zero, 0);
            if (hook == IntPtr.Zero)
            {
                throw new InvalidOperationException($"SetWindowsHookEx failed (error={Marshal.GetLastWin32Error()}).");
            }

            _ready.Set();
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }

            UnhookWindowsHookEx(hook);
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
                if (_watchedVks.Contains(vk))
                {
                    var isDown = wParam is 0x0100 or 0x0104; // WM_KEYDOWN / WM_SYSKEYDOWN
                    lock (_events)
                    {
                        _events.Add((vk, isDown));
                    }
                }
            }

            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        public void Dispose()
        {
            PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(2000);
            _ready.Dispose();
        }

        private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref NativeMessage lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref NativeMessage lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public IntPtr pt_x;
            public IntPtr pt_y;
        }
    }
}

internal sealed class EmitterSmokeResult
{
    public required string Probe { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string OsVersion { get; init; }
    public required EmitterRoundtripResult Roundtrip { get; init; }
    public required WatchdogCrashResult WatchdogCrash { get; init; }
}

internal sealed class EmitterRoundtripResult
{
    public required List<EmitterCheck> Checks { get; init; }
    public required long StopReleaseEmitMs { get; init; }
    public required bool AllPassed { get; init; }
}

internal sealed class EmitterCheck
{
    public required string Name { get; init; }
    public required bool Passed { get; init; }
}

internal sealed class WatchdogCrashResult
{
    public required bool DownConfirmedWhileChildAlive { get; init; }
    public required bool ReleasedByWatchdog { get; init; }
    public long? ReleaseLatencyMsAfterKill { get; init; }
    public bool? MeetsNfr008Budget { get; init; }
    public string? Error { get; init; }
}
