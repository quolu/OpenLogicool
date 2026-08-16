using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

// EXP-IN-01: SendInput acceptance の受信側観測（計画 §14）。
// 判定は API 戻り値でなく「受信側の観測」で行う——SendInput は UIPI 遮断を戻り値・GetLastError で
// 示さないため、test target window の WM_KEYDOWN/WM_KEYUP 受信 log と送信列を突合する。
//
// standard 側: 子 process（input-target）を stdout channel で起動して突合（実測済み・確認済み）。
// elevated 側: --elevated で target を runas（UAC 承認要＝オーナー手番）起動する。昇格 process は
//   stdout redirect できないため、受信 log は file channel（--log）で受け取る。WM_CLOSE も UIPI で
//   届かないため、target は --exit-after-ms の自前 timer で終了する。target が実際に昇格しているかは
//   仮定せず TokenElevation で自己観測して READY 行に載せる。
// mouse button の受信分類は cursor 位置に依存するため本 probe の対象外（未分類のまま）。
internal static class SendInputAcceptProbe
{
    private const int TargetExitAfterMs = 12_000;

    public static int Run(string[] arguments, string outputDirectory)
    {
        var elevated = false;
        string? label = null;
        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "--label" when i + 1 < arguments.Length:
                    label = arguments[++i];
                    break;
                case "--elevated":
                    elevated = true;
                    break;
            }
        }

        label ??= elevated ? "elevated" : "standard";
        Directory.CreateDirectory(outputDirectory);

        using var channel = elevated
            ? TargetChannel.StartElevated(outputDirectory)
            : TargetChannel.StartStandard();
        if (channel.StartError is not null)
        {
            Console.Error.WriteLine(channel.StartError);
            return 1;
        }

        // UAC 承認の待ち時間を含めて READY を待つ
        var readyTokens = channel.WaitForReady(TimeSpan.FromSeconds(elevated ? 120 : 10));
        if (readyTokens is null)
        {
            Console.Error.WriteLine("input-target が READY を報告しませんでした。");
            return 1;
        }

        var foregroundConfirmed = readyTokens.Contains("FOREGROUND");
        var targetElevated = readyTokens.Contains("ELEVATED");

        SentKeyRecord[] sent = [];
        string? sendError = null;
        if (foregroundConfirmed)
        {
            var emitter = new SendInputEmitter();
            try
            {
                // 単一 key: F13 down/up
                emitter.Emit([new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down)]);
                Thread.Sleep(80);
                emitter.Emit([new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up)]);
                Thread.Sleep(80);

                // chord: F13+F14 を単一 SendInput call で down → up
                emitter.Emit([
                    new MappedOutputEdge("Key:F13", PhysicalInputEdge.Down),
                    new MappedOutputEdge("Key:F14", PhysicalInputEdge.Down),
                ]);
                Thread.Sleep(80);
                emitter.Emit([
                    new MappedOutputEdge("Key:F13", PhysicalInputEdge.Up),
                    new MappedOutputEdge("Key:F14", PhysicalInputEdge.Up),
                ]);
                Thread.Sleep(200);

                sent =
                [
                    new SentKeyRecord { Edge = "DOWN", VirtualKey = "7C" },
                    new SentKeyRecord { Edge = "UP", VirtualKey = "7C" },
                    new SentKeyRecord { Edge = "DOWN", VirtualKey = "7C" },
                    new SentKeyRecord { Edge = "DOWN", VirtualKey = "7D" },
                    new SentKeyRecord { Edge = "UP", VirtualKey = "7C" },
                    new SentKeyRecord { Edge = "UP", VirtualKey = "7D" },
                ];
            }
            catch (OutputEmitFaultException ex)
            {
                sendError = ex.Message;
            }
        }

        var received = channel.CloseAndCollectReceived();

        var allReceivedInOrder = sent.Length > 0 &&
            sent.Length == received.Count &&
            sent.Zip(received).All(pair =>
                pair.First.Edge == pair.Second.Edge && pair.First.VirtualKey == pair.Second.VirtualKey);
        var classificationMade = foregroundConfirmed && sendError is null && sent.Length > 0;

        var verdict = (foregroundConfirmed, sendError, allReceivedInOrder, received.Count) switch
        {
            (false, _, _, _) => "Unverified: target window could not take foreground; no classification made.",
            (_, not null, _, _) => "Fault: SendInput partial failure; no classification made.",
            (_, _, true, _) => $"Delivered: keyboard SendInput reaches the {label} foreground window (single key and chord, exact order).",
            (_, _, false, 0) => $"Blocked: no key messages reached the {label} foreground window (UIPI-consistent).",
            _ => $"Partial: sent and received sequences differ for the {label} target.",
        };

        var result = new SendInputAcceptResult
        {
            Probe = "sendinput-accept",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            TargetLabel = label,
            TargetElevated = targetElevated,
            ForegroundConfirmed = foregroundConfirmed,
            Sent = sent.ToList(),
            Received = received,
            SendError = sendError,
            AllReceivedInOrder = allReceivedInOrder,
            Verdict = verdict,
        };

        var path = Path.Combine(outputDirectory, $"sendinput-accept-{label}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        var json = JsonSerializer.Serialize(result, JsonOptions.Value);
        File.WriteAllText(path, json);
        Console.WriteLine(json);
        Console.WriteLine($"output: {path}");

        return classificationMade ? 0 : 2;
    }

    /// <summary>target との受信 channel。standard は stdout、elevated は log file。</summary>
    private sealed class TargetChannel : IDisposable
    {
        private readonly Process? _process;
        private readonly string? _logPath;
        private readonly List<ReceivedKeyRecord> _received = [];
        private readonly Thread? _stdoutReader;
        private string? _readyLine;
        private readonly ManualResetEventSlim _readySeen = new(false);

        private TargetChannel(Process? process, string? logPath, string? startError)
        {
            _process = process;
            _logPath = logPath;
            StartError = startError;

            if (process is not null && logPath is null)
            {
                _stdoutReader = new Thread(() =>
                {
                    while (process.StandardOutput.ReadLine() is { } line)
                    {
                        if (line.StartsWith("READY", StringComparison.Ordinal))
                        {
                            _readyLine = line;
                            _readySeen.Set();
                        }
                        else if (TryParseReceived(line, out var record))
                        {
                            lock (_received)
                            {
                                _received.Add(record);
                            }
                        }
                    }
                })
                { IsBackground = true };
                _stdoutReader.Start();
            }
        }

        public string? StartError { get; }

        public static TargetChannel StartStandard()
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    Arguments = "input-target",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };
            process.Start();
            return new TargetChannel(process, logPath: null, startError: null);
        }

        public static TargetChannel StartElevated(string outputDirectory)
        {
            var logPath = Path.Combine(
                outputDirectory, $"sendinput-target-log-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    Arguments = $"input-target --log \"{logPath}\" --exit-after-ms {TargetExitAfterMs}",
                    UseShellExecute = true,
                    Verb = "runas",
                },
            };

            try
            {
                process.Start();
            }
            catch (Win32Exception ex)
            {
                return new TargetChannel(
                    null,
                    logPath,
                    $"elevated input-target を起動できませんでした（UAC 拒否または失敗）: {ex.Message}");
            }

            return new TargetChannel(process, logPath, startError: null);
        }

        /// <summary>READY 行の token 列（例: ["READY","FOREGROUND","ELEVATED"]）。timeout なら null。</summary>
        public string[]? WaitForReady(TimeSpan timeout)
        {
            if (_logPath is null)
            {
                return _readySeen.Wait(timeout) ? _readyLine!.Split(' ') : null;
            }

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var lines = ReadLogLines();
                var ready = lines.FirstOrDefault(line => line.StartsWith("READY", StringComparison.Ordinal));
                if (ready is not null)
                {
                    return ready.Split(' ');
                }

                Thread.Sleep(100);
            }

            return null;
        }

        /// <summary>target を終える（standard は WM_CLOSE、elevated は自前 timer 終了を待つ）→ 受信列を返す。</summary>
        public List<ReceivedKeyRecord> CloseAndCollectReceived()
        {
            if (_process is null)
            {
                return [];
            }

            if (_logPath is null)
            {
                _process.CloseMainWindow();
                if (!_process.WaitForExit(3000))
                {
                    _process.Kill();
                }

                _stdoutReader!.Join(1000);
                lock (_received)
                {
                    return [.. _received];
                }
            }

            // elevated: UIPI で WM_CLOSE が届かないため、target の --exit-after-ms 終了を待って log を読む
            if (!_process.WaitForExit(TargetExitAfterMs + 10_000))
            {
                throw new InvalidOperationException(
                    "elevated input-target が自前 timer で終了しませんでした（kill は昇格差で不可）。手動で閉じてください。");
            }

            return ReadLogLines()
                .Select(line => TryParseReceived(line, out var record) ? record : null)
                .Where(record => record is not null)
                .Select(record => record!)
                .ToList();
        }

        private IReadOnlyList<string> ReadLogLines()
        {
            if (!File.Exists(_logPath!))
            {
                return [];
            }

            using var stream = new FileStream(_logPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines;
        }

        private static bool TryParseReceived(string line, out ReceivedKeyRecord record)
        {
            var parts = line.Split(' ');
            if (parts.Length == 3 && parts[0] == "RECV")
            {
                record = new ReceivedKeyRecord { Edge = parts[1], VirtualKey = parts[2] };
                return true;
            }

            record = null!;
            return false;
        }

        public void Dispose()
        {
            _readySeen.Dispose();
            _process?.Dispose();
        }
    }

    // 子 process mode: 可視 window を作り foreground を取り、受信 key message を channel へ流す。
    // --log <path> 指定時は stdout でなく file へ書く（昇格起動用）。--exit-after-ms で自前終了。
    public static int RunTarget(string[] arguments)
    {
        string? logPath = null;
        int? exitAfterMs = null;
        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "--log" when i + 1 < arguments.Length:
                    logPath = arguments[++i];
                    break;
                case "--exit-after-ms" when i + 1 < arguments.Length:
                    exitAfterMs = int.Parse(arguments[++i]);
                    break;
            }
        }

        void Emit(string line)
        {
            if (logPath is null)
            {
                Console.WriteLine(line);
                Console.Out.Flush();
            }
            else
            {
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
        }

        var className = $"OpenLogicoolInputTarget-{Environment.ProcessId}";
        WndProcDelegate wndProc = (hWnd, msg, wParam, lParam) =>
        {
            switch (msg)
            {
                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                    Emit($"RECV DOWN {(int)wParam:X2}");
                    return IntPtr.Zero;
                case WM_KEYUP:
                case WM_SYSKEYUP:
                    Emit($"RECV UP {(int)wParam:X2}");
                    return IntPtr.Zero;
                case WM_TIMER:
                case WM_CLOSE:
                    PostQuitMessage(0);
                    return IntPtr.Zero;
                default:
                    return DefWindowProc(hWnd, msg, wParam, lParam);
            }
        };

        var wndClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = className,
        };
        if (RegisterClassEx(ref wndClass) == 0)
        {
            Console.Error.WriteLine($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
            return 1;
        }

        const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        const uint WS_VISIBLE = 0x10000000;
        var hwnd = CreateWindowEx(
            0, className, "OpenLogicool EXP-IN-01 target", WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            100, 100, 320, 120, IntPtr.Zero, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            Console.Error.WriteLine($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
            return 1;
        }

        SetForegroundWindow(hwnd);
        SetFocus(hwnd);
        Thread.Sleep(100);
        var isForeground = GetForegroundWindow() == hwnd;
        var elevationToken = IsProcessElevated() ? "ELEVATED" : "STANDARD";
        Emit($"READY {(isForeground ? "FOREGROUND" : "BACKGROUND")} {elevationToken}");

        if (exitAfterMs is not null)
        {
            SetTimer(hwnd, 1, (uint)exitAfterMs.Value, IntPtr.Zero);
        }

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        GC.KeepAlive(wndProc);
        return 0;
    }

    private static bool IsProcessElevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token))
        {
            throw new InvalidOperationException($"OpenProcessToken failed: {Marshal.GetLastWin32Error()}");
        }

        try
        {
            var elevation = 0;
            if (!GetTokenInformation(token, TokenElevation, ref elevation, sizeof(int), out _))
            {
                throw new InvalidOperationException($"GetTokenInformation failed: {Marshal.GetLastWin32Error()}");
            }

            return elevation != 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP = 0x0105;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_TIMER = 0x0113;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nuint SetTimer(IntPtr hWnd, nuint id, uint elapseMs, IntPtr callback);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr token, int informationClass, ref int information, int informationLength, out int returnLength);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

internal sealed class SendInputAcceptResult
{
    public required string Probe { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string OsVersion { get; init; }
    public required string TargetLabel { get; init; }
    public required bool TargetElevated { get; init; }
    public required bool ForegroundConfirmed { get; init; }
    public required List<SentKeyRecord> Sent { get; init; }
    public required List<ReceivedKeyRecord> Received { get; init; }
    public string? SendError { get; init; }
    public required bool AllReceivedInOrder { get; init; }
    public required string Verdict { get; init; }
}

internal sealed class SentKeyRecord
{
    public required string Edge { get; init; }
    public required string VirtualKey { get; init; }
}

internal sealed class ReceivedKeyRecord
{
    public required string Edge { get; init; }
    public required string VirtualKey { get; init; }
}
