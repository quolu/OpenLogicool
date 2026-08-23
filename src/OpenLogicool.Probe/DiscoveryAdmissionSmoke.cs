using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenLogicool.AI;
using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenLogicool.Probe;

internal static class DiscoveryAdmissionSmoke
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseWheel = 0x0800;
    private const uint KeyUp = 0x0002;
    private const ushort VirtualKeyEscape = 0x1B;
    private const ushort VirtualKeyF13 = 0x7C;

    public static int Run(string[] arguments)
    {
        if (arguments.Length > 1)
        {
            Console.Error.WriteLine("usage: discovery-admission-smoke [gamelab-exe]");
            return 1;
        }

        try
        {
            return RunAsync(arguments.Length == 1 ? Path.GetFullPath(arguments[0]) : DefaultGameLabPath())
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string gameLabPath)
    {
        if (!File.Exists(gameLabPath))
        {
            throw new FileNotFoundException("GameLab executableがありません。先にDebug buildしてください。", gameLabPath);
        }

        var receiptPath = Path.Combine(
            Path.GetTempPath(),
            $"openlogicool-discovery-admission-{Guid.NewGuid():N}.jsonl");
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(gameLabPath)
            {
                UseShellExecute = false,
            };
            start.ArgumentList.Add("--seed");
            start.ArgumentList.Add("5");
            start.ArgumentList.Add("--input-log");
            start.ArgumentList.Add(receiptPath);
            process = Process.Start(start)
                ?? throw new InvalidOperationException("GameLabを起動できませんでした。");
            process.WaitForInputIdle(5_000);

            var window = await WaitForWindowAsync(process, TimeSpan.FromSeconds(5));
            if (!SetForegroundWindow(window))
            {
                throw new InvalidOperationException("GameLabをforegroundへ移せませんでした。");
            }

            await WaitForReceiptAsync(receiptPath, "window", "rendered", TimeSpan.FromSeconds(5));
            await Task.Delay(250);
            var compositionResult = DwmFlush();
            if (compositionResult < 0)
            {
                Marshal.ThrowExceptionForHR(compositionResult);
            }

            using var source = WgcFrameSource.CreateForWindow(window, "window:gamelab-discovery-admission");
            var initial = await WaitForTextAsync(source, "state.main-menu", TimeSpan.FromSeconds(5));
            var target = initial.Snapshot.Words.SingleOrDefault(word =>
                FrameBoundLabelMatcher.Equals(word.Text, "OpenEvent"))
                ?? throw new InvalidOperationException("OpenEventのOCR bounding boxを取得できませんでした。");

            if (!GetWindowRect(window, out var windowRect))
            {
                throw new InvalidOperationException($"GetWindowRect failed: {Marshal.GetLastWin32Error()}");
            }

            var targetX = windowRect.Left + (int)Math.Round(
                (target.X + target.Width / 2) * windowRect.Width / initial.Frame.Width);
            var targetY = windowRect.Top + (int)Math.Round(
                (target.Y + target.Height / 2) * windowRect.Height / initial.Frame.Height);
            if (!SetCursorPos(targetX, targetY) || !GetCursorPos(out var observedCursor))
            {
                throw new InvalidOperationException($"pointer move failed: {Marshal.GetLastWin32Error()}");
            }

            var pointerConfirmed = Math.Abs(observedCursor.X - targetX) <= 2
                && Math.Abs(observedCursor.Y - targetY) <= 2;
            if (!pointerConfirmed)
            {
                throw new InvalidOperationException(
                    $"pointer readback mismatch: expected=({targetX},{targetY}) actual=({observedCursor.X},{observedCursor.Y})");
            }

            SendMouseButtonClick();
            var afterClick = await WaitForTextAsync(source, "state.main-menu.event-popup", TimeSpan.FromSeconds(5));

            SendKey(VirtualKeyEscape);
            var afterEscape = await WaitForTextAsync(source, "state.main-menu", TimeSpan.FromSeconds(5));

            SendKey(VirtualKeyF13);
            await Task.Delay(50);
            SendWheel(+120);
            await Task.Delay(50);
            SendWheel(-120);
            var receipts = await WaitForReceiptsAsync(receiptPath, TimeSpan.FromSeconds(3));

            var result = new
            {
                SchemaVersion = "1.0.0",
                Probe = "discovery-admission-smoke",
                Target = "GameLab Prototype",
                Seed = 5,
                Grounding = new
                {
                    Recognizer = "Windows.Media.Ocr",
                    TargetLabel = target.Text,
                    TargetBox = new { target.X, target.Y, target.Width, target.Height },
                    ScreenPoint = new { X = targetX, Y = targetY },
                    InitialOcrMs = initial.Snapshot.ElapsedMs,
                    initial.Snapshot.RecognizerLanguage,
                    initial.Snapshot.MaxImageDimension,
                },
                Routes = new
                {
                    PointerMove = new { Status = "Confirmed", Evidence = $"cursor readback ({observedCursor.X},{observedCursor.Y})" },
                    LeftClick = new { Status = "Confirmed", Evidence = afterClick.Snapshot.Text },
                    EscapeBack = new { Status = "Confirmed", Evidence = afterEscape.Snapshot.Text },
                    GenericKey = new { Status = "Confirmed", Evidence = "GameLab receiver receipt key:F13" },
                    Scroll = new { Status = "Confirmed", Evidence = "GameLab receiver receipts wheel:+120/-120" },
                },
                ReceiverReceipts = receipts,
                ExternalAiTransmissionCount = 0,
                ExternalAiApiCostUsd = 0,
            };
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            var outputDirectory = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "probe-output"));
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(
                outputDirectory,
                $"discovery-admission-smoke-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            File.WriteAllText(outputPath, json);
            Console.WriteLine(json);
            Console.WriteLine($"output: {outputPath}");
            return 0;
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(2_000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }

            process?.Dispose();
            if (File.Exists(receiptPath))
            {
                File.Delete(receiptPath);
            }
        }
    }

    private static async Task<nint> WaitForWindowAsync(Process process, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("GameLab main window handleの待機がtimeoutしました。");
    }

    private static async Task<(CapturedFrame Frame, WindowsOcrSnapshot Snapshot)> WaitForTextAsync(
        WgcFrameSource source,
        string expected,
        TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        WindowsOcrSnapshot? last = null;
        CapturedFrame? lastFrame = null;
        while (deadline.Elapsed < timeout)
        {
            if (source.PullDetailed().Result is FrameAvailable available)
            {
                lastFrame = available.Frame;
                last = await WindowsOcrSmoke.RecognizeFrameAsync(available.Frame);
                if (last.VisualLines.Any(line => FrameBoundLabelMatcher.Equals(line, expected)))
                {
                    return (available.Frame, last);
                }
            }

            await Task.Delay(20);
        }

        var diagnosticPath = lastFrame is null ? null : SaveFrame(lastFrame, expected);
        throw new TimeoutException(
            $"OCR text '{expected}' was not observed. last='{last?.Text}' frame='{diagnosticPath}'");
    }

    private static string SaveFrame(CapturedFrame frame, string label)
    {
        var pixels = frame.Pixels
            ?? throw new InvalidOperationException("diagnostic frame pixelsがありません。");
        var packedStride = checked(frame.Width * 4);
        var packed = new byte[checked(packedStride * frame.Height)];
        for (var y = 0; y < frame.Height; y++)
        {
            pixels.Bgra8.Span.Slice(y * pixels.Stride, packedStride)
                .CopyTo(packed.AsSpan(y * packedStride, packedStride));
        }

        var bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            frame.DpiX,
            frame.DpiY,
            PixelFormats.Bgra32,
            null,
            packed,
            packedStride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var outputDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "probe-output"));
        Directory.CreateDirectory(outputDirectory);
        var safeLabel = new string(label.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
        var path = Path.Combine(outputDirectory, $"discovery-admission-{safeLabel}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static async Task<string[]> WaitForReceiptsAsync(string path, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        string[] lastLines = [];
        while (deadline.Elapsed < timeout)
        {
            if (File.Exists(path))
            {
                lastLines = File.ReadAllLines(path);
                var receipts = lastLines.Select(ParseReceipt).ToArray();
                if (Contains(receipts, "button", "OpenEvent")
                    && Contains(receipts, "key", "Escape")
                    && Contains(receipts, "key", "F13")
                    && Contains(receipts, "wheel", "120")
                    && Contains(receipts, "wheel", "-120"))
                {
                    return lastLines;
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException(
            $"GameLab receiver receiptsが揃いませんでした。last={string.Join(" | ", lastLines)}");
    }

    private static async Task WaitForReceiptAsync(
        string path,
        string route,
        string value,
        TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            if (File.Exists(path)
                && Contains(File.ReadAllLines(path).Select(ParseReceipt), route, value))
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"GameLab receiver receipt {route}:{value}がありませんでした。");
    }

    private static (string Route, string Value) ParseReceipt(string line)
    {
        using var document = JsonDocument.Parse(line);
        return (
            document.RootElement.GetProperty("route").GetString() ?? string.Empty,
            document.RootElement.GetProperty("value").GetString() ?? string.Empty);
    }

    private static bool Contains(
        IEnumerable<(string Route, string Value)> receipts,
        string route,
        string value) => receipts.Any(item =>
            string.Equals(item.Route, route, StringComparison.Ordinal)
            && string.Equals(item.Value, value, StringComparison.Ordinal));

    private static void SendMouseButtonClick()
    {
        Send([
            MouseInput(MouseLeftDown),
            MouseInput(MouseLeftUp),
        ]);
    }

    private static void SendWheel(int delta) => Send([MouseInput(MouseWheel, unchecked((uint)delta))]);

    private static void SendKey(ushort virtualKey)
    {
        Send([
            KeyboardInput(virtualKey, 0),
            KeyboardInput(virtualKey, KeyUp),
        ]);
    }

    private static void Send(INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput failed: expected={inputs.Length} actual={sent} error={Marshal.GetLastWin32Error()}");
        }
    }

    private static INPUT MouseInput(uint flags, uint data = 0) => new()
    {
        Type = InputMouse,
        Value = new InputUnion
        {
            Mouse = new MOUSEINPUT { MouseData = data, Flags = flags },
        },
    };

    private static INPUT KeyboardInput(ushort virtualKey, uint flags) => new()
    {
        Type = InputKeyboard,
        Value = new InputUnion
        {
            Keyboard = new KEYBDINPUT { VirtualKey = virtualKey, Flags = flags },
        },
    };

    private static string DefaultGameLabPath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "OpenLogicool.GameLab.Prototype", "bin", "Debug", "net10.0-windows",
        "OpenLogicool.GameLab.Prototype.exe"));

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint window, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RECT
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct POINT
    {
        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Value;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}
