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
// 親（sendinput-accept）: 子 process（input-target）を起動 → READY/HWND 確認 → foreground 確認 →
//   製品 SendInputEmitter で 単一 key（F13 down/up）と chord（F13+F14）を送出 → 子の受信 log と突合。
// 子（input-target）: 可視 window を作って SetForegroundWindow し、受信した key message を
//   "RECV <DOWN|UP> <vkHex>" 行で stdout へ流す。WM_CLOSE で終了。
//
// この command は standard（非昇格）権限の分類だけを行う。elevated 側は UAC 承認（オーナー手番）が
// 必要なので、--target-exe で昇格済み target を差し替えられる構造だけ用意して今回は分類しない。
// mouse button の受信分類は cursor 位置に依存するため本 probe の対象外（未分類のまま）。
internal static class SendInputAcceptProbe
{
    public static int Run(string[] arguments, string outputDirectory)
    {
        var label = "standard";
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == "--label" && i + 1 < arguments.Length)
            {
                label = arguments[++i];
            }
        }

        var child = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = "input-target",
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        child.Start();

        var received = new List<ReceivedKeyRecord>();
        var readyLine = child.StandardOutput.ReadLine();
        if (readyLine is null || !readyLine.StartsWith("READY ", StringComparison.Ordinal))
        {
            try { child.Kill(); } catch { }
            Console.Error.WriteLine($"input-target did not become ready (got: {readyLine ?? "<eof>"}).");
            return 1;
        }

        var foregroundConfirmed = readyLine.EndsWith(" FOREGROUND", StringComparison.Ordinal);
        var receiveReader = new Thread(() =>
        {
            while (child.StandardOutput.ReadLine() is { } line)
            {
                var parts = line.Split(' ');
                if (parts.Length == 3 && parts[0] == "RECV")
                {
                    lock (received)
                    {
                        received.Add(new ReceivedKeyRecord { Edge = parts[1], VirtualKey = parts[2] });
                    }
                }
            }
        })
        { IsBackground = true };
        receiveReader.Start();

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

        child.CloseMainWindow();
        if (!child.WaitForExit(3000))
        {
            child.Kill();
        }

        receiveReader.Join(1000);

        List<ReceivedKeyRecord> receivedSnapshot;
        lock (received)
        {
            receivedSnapshot = [.. received];
        }

        var allReceivedInOrder = sent.Length > 0 &&
            sent.Length == receivedSnapshot.Count &&
            sent.Zip(receivedSnapshot).All(pair =>
                pair.First.Edge == pair.Second.Edge && pair.First.VirtualKey == pair.Second.VirtualKey);

        var verdict = (foregroundConfirmed, sendError, allReceivedInOrder) switch
        {
            (false, _, _) => "Unverified: target window could not take foreground; no classification made.",
            (_, not null, _) => "Fault: SendInput partial failure; no classification made.",
            (_, _, true) => $"Confirmed: keyboard SendInput is delivered to a {label} foreground window (single key and chord, exact order).",
            _ => $"Refuted: sent and received sequences differ for the {label} target.",
        };

        var result = new SendInputAcceptResult
        {
            Probe = "sendinput-accept",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            TargetLabel = label,
            ForegroundConfirmed = foregroundConfirmed,
            Sent = sent.ToList(),
            Received = receivedSnapshot,
            SendError = sendError,
            AllReceivedInOrder = allReceivedInOrder,
            Verdict = verdict,
        };

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"sendinput-accept-{label}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        var json = JsonSerializer.Serialize(result, JsonOptions.Value);
        File.WriteAllText(path, json);
        Console.WriteLine(json);
        Console.WriteLine($"output: {path}");

        return allReceivedInOrder ? 0 : 2;
    }

    // 子 process mode: 可視 window を作り foreground を取り、受信 key message を stdout へ流す。
    public static int RunTarget()
    {
        var className = $"OpenLogicoolInputTarget-{Environment.ProcessId}";
        WndProcDelegate wndProc = (hWnd, msg, wParam, lParam) =>
        {
            switch (msg)
            {
                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                    Console.WriteLine($"RECV DOWN {(int)wParam:X2}");
                    Console.Out.Flush();
                    return IntPtr.Zero;
                case WM_KEYUP:
                case WM_SYSKEYUP:
                    Console.WriteLine($"RECV UP {(int)wParam:X2}");
                    Console.Out.Flush();
                    return IntPtr.Zero;
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
        Console.WriteLine(isForeground ? "READY FOREGROUND" : "READY BACKGROUND");
        Console.Out.Flush();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        GC.KeepAlive(wndProc);
        return 0;
    }

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP = 0x0105;
    private const uint WM_CLOSE = 0x0010;

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

internal sealed class SendInputAcceptResult
{
    public required string Probe { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string OsVersion { get; init; }
    public required string TargetLabel { get; init; }
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
