using System.Runtime.InteropServices;
using System.Text.Json;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Host;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

// t02: Windows環境別mouse／keyboard recorderの実機確認（self-window）。
//
// 自分で作った窓を対象にして、WH_MOUSE_LL／WH_KEYBOARD_LL が実OS入力を拾い、
// desktop座標がその窓のclient frameへ正規化されることを確認する。
//
// 送信側の SendInput はこのprobeの測定器であって製品経路ではない——製品の出力は
// Nano（Serial HID）だけで、記録器は入力を一切送らない。ここでSendInputを使うのは、
// 人手を借りずにOS入力を発生させるためだけである。
internal static class DemonstrationRecorderSmoke
{
    public static int Run(string[] arguments, string outputDirectory)
    {
        string? label = null;
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == "--label" && i + 1 < arguments.Length)
            {
                label = arguments[++i];
            }
        }

        Directory.CreateDirectory(outputDirectory);

        SelfWindow window;
        try
        {
            window = SelfWindow.Create("OpenLogicool 操作デモ記録 self-window", 120, 120, 640, 480);
        }
        catch (InvalidOperationException exception)
        {
            // 前面を保護中のapp（anti-cheat等）が握っていると、self-windowを前面化できない。
            // low-level hookは自分より高い整合性levelのwindowへ向かう入力を観測できないので、
            // ここで別経路へ逃げず「未確認」として止める。
            var blockedPath = Path.Combine(
                outputDirectory,
                $"demonstration-recorder-smoke-blocked-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.json");
            File.WriteAllText(blockedPath, JsonSerializer.Serialize(
                new
                {
                    probe = "demonstration-recorder-smoke",
                    label = label ?? "self-window",
                    capturedAtUtc = DateTimeOffset.UtcNow,
                    verdict = "未確認: self-windowを前面化できなかった。",
                    reason = exception.Message,
                    foreground = ForegroundDescription(),
                    requirement = "保護されていない通常appが前面にある状態で再実行すること。",
                },
                new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine($"未確認: self-windowを前面化できませんでした。{ForegroundDescription()}");
            Console.Error.WriteLine($"report: {blockedPath}");
            return 2;
        }

        using var _window = window;
        var clientBounds = window.ClientBoundsOnScreen();
        var mapper = new WindowsGameInteractionCoordinateMapper(() => new GameCaptureScreenBounds(
            clientBounds.Left, clientBounds.Top, clientBounds.Width, clientBounds.Height));

        var sink = new CollectingSink();
        using var collector = new WindowsDemonstrationInputCollector();
        collector.Start(sink);

        // client frameの中央と右下寄りの2点。窓の外の点も1つ送って、正規化がnullになることを見る。
        var centre = (X: clientBounds.Left + (clientBounds.Width / 2), Y: clientBounds.Top + (clientBounds.Height / 2));
        var lower = (X: clientBounds.Left + (clientBounds.Width * 3 / 4), Y: clientBounds.Top + (clientBounds.Height * 3 / 4));

        SendInputInstrument.MoveTo(centre.X, centre.Y);
        Thread.Sleep(60);
        SendInputInstrument.LeftClick();
        Thread.Sleep(60);
        SendInputInstrument.MoveTo(lower.X, lower.Y);
        Thread.Sleep(60);
        SendInputInstrument.LeftDown();
        Thread.Sleep(40);
        SendInputInstrument.MoveTo(centre.X, centre.Y);
        Thread.Sleep(40);
        SendInputInstrument.LeftUp();
        Thread.Sleep(60);
        SendInputInstrument.TapKey(0x1B); // Escape
        Thread.Sleep(60);
        SendInputInstrument.WheelUp();
        Thread.Sleep(200);

        var keyboardHookCalls = collector.KeyboardHookCalls;
        var mouseHookCalls = collector.MouseHookCalls;
        collector.Stop();
        var observed = sink.Snapshot();
        Console.WriteLine($"hook呼び出し: keyboard={keyboardHookCalls} mouse={mouseHookCalls}");

        // 判定は観測列とclient frameだけから決まるので、純関数へ出してある。
        // 保存済み観測への再適用（tests/OpenLogicool.Probe.Tests）と同じ判定である。
        var checks = DemonstrationRecorderSmokeJudgement.Evaluate(
            new GameCaptureScreenBounds(
                clientBounds.Left, clientBounds.Top, clientBounds.Width, clientBounds.Height),
            observed);

        var passed = checks.All(check => check.Passed);
        var report = new
        {
            probe = "demonstration-recorder-smoke",
            label = label ?? "self-window",
            capturedAtUtc = DateTimeOffset.UtcNow,
            note = "SendInputはこのprobeの測定器であり製品経路ではない。記録器は入力を送らない。",
            clientBoundsOnScreen = new { clientBounds.Left, clientBounds.Top, clientBounds.Width, clientBounds.Height },
            keyboardHookCalls,
            mouseHookCalls,
            observedEdgeCount = observed.Count,
            observedEdges = observed.Select(edge => new
            {
                kind = edge.Kind.ToString(),
                source = edge.Source.ToString(),
                token = edge.OutputToken,
                screenPoint = edge.ScreenPoint is null ? null : new { edge.ScreenPoint.X, edge.ScreenPoint.Y },
                normalized = edge.ScreenPoint is null
                    ? null
                    : mapper.TryMapScreenToNormalized(edge.ScreenPoint.X, edge.ScreenPoint.Y),
                edge.WheelVerticalSteps,
                edge.WheelHorizontalSteps,
            }),
            checks,
            passed,
        };

        var path = Path.Combine(
            outputDirectory,
            $"demonstration-recorder-smoke-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        foreach (var check in checks)
        {
            Console.WriteLine($"{(check.Passed ? "OK  " : "NG  ")}{check.Name}: {check.Detail}");
        }

        Console.WriteLine($"report: {path}");
        return passed ? 0 : 1;
    }

    /// <summary>前面windowの持ち主を、取れる範囲でそのまま書く（取れないことも情報である）。</summary>
    private static string ForegroundDescription()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return "前面window=なし";
        }

        _ = GetWindowThreadProcessId(handle, out var processId);
        var path = OpenLogicool.Host.ForegroundAppTracker.GetProcessFullPath(processId);
        return $"前面window: pid={processId} path={path ?? "(取得不能)"}";
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>hook procedureから呼ばれるので、追加するだけで待たない。</summary>
    private sealed class CollectingSink : IDemonstrationInputSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<DemonstrationInputEdge> edges = new();

        public void Observe(DemonstrationInputEdge edge) => edges.Enqueue(edge);

        public IReadOnlyList<DemonstrationInputEdge> Snapshot() => edges.ToArray();
    }

    /// <summary>他のprobe（t07 journey smoke）からも使うのでassembly内へ公開する。</summary>
    internal sealed class SelfWindow : IDisposable
    {
        private readonly IntPtr handle;
        private readonly Thread thread;
        private readonly WndProcDelegate windowProcedure;
        private uint threadId;

        private SelfWindow(IntPtr handle, Thread thread, WndProcDelegate windowProcedure, uint threadId)
        {
            this.handle = handle;
            this.thread = thread;
            this.windowProcedure = windowProcedure;
            this.threadId = threadId;
        }

        public static SelfWindow Create(string title, int left, int top, int width, int height)
        {
            const uint WsOverlappedWindow = 0x00CF0000;
            const uint WsVisible = 0x10000000;

            var className = $"OpenLogicoolDemoRecorder{Guid.NewGuid():N}";
            IntPtr createdHandle = IntPtr.Zero;
            Exception? failure = null;
            uint createdThreadId = 0;
            using var ready = new ManualResetEventSlim(false);

            WndProcDelegate procedure = DefWindowProc;
            var thread = new Thread(() =>
            {
                try
                {
                    createdThreadId = GetCurrentThreadId();
                    var windowClass = new WindowClassEx
                    {
                        Size = Marshal.SizeOf<WindowClassEx>(),
                        WindowProcedure = Marshal.GetFunctionPointerForDelegate(procedure),
                        Instance = GetModuleHandle(null),
                        ClassName = className,
                    };
                    if (RegisterClassEx(ref windowClass) == 0)
                    {
                        throw new InvalidOperationException(
                            $"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
                    }

                    createdHandle = CreateWindowEx(
                        0, className, title, WsOverlappedWindow | WsVisible,
                        left, top, width, height, IntPtr.Zero, IntPtr.Zero, windowClass.Instance, IntPtr.Zero);
                    if (createdHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            $"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
                    }

                    // 前面化は製品と同じ既存adapterを使う（AttachThreadInput併用）。
                    WindowsGameWindowActivator.Activate(createdHandle);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    ready.Set();
                }

                while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                {
                    _ = TranslateMessage(ref message);
                    _ = DispatchMessage(ref message);
                }
            })
            { IsBackground = true, Name = "OpenLogicoolDemoRecorderSelfWindow" };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
            if (failure is not null)
            {
                throw failure;
            }

            Thread.Sleep(200);
            return new SelfWindow(createdHandle, thread, procedure, createdThreadId);
        }

        /// <summary>製品側が同じwindowを掴んでいるかを突合するために公開する。</summary>
        public IntPtr Handle => handle;

        public ScreenBounds ClientBoundsOnScreen()
        {
            if (!GetClientRect(handle, out var client))
            {
                throw new InvalidOperationException($"GetClientRect failed: {Marshal.GetLastWin32Error()}");
            }

            var origin = new NativePoint { X = client.Left, Y = client.Top };
            if (!ClientToScreen(handle, ref origin))
            {
                throw new InvalidOperationException("ClientToScreen failed.");
            }

            return new ScreenBounds(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        }

        public void Dispose()
        {
            if (threadId != 0)
            {
                _ = PostThreadMessage(threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
                threadId = 0;
            }

            _ = thread.Join(TimeSpan.FromSeconds(3));
            GC.KeepAlive(windowProcedure);
        }

        public sealed record ScreenBounds(int Left, int Top, int Width, int Height);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WndProcDelegate(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClassEx
        {
            public int Size;
            public uint Style;
            public IntPtr WindowProcedure;
            public int ClassExtra;
            public int WindowExtra;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr Background;
            [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
            public IntPtr SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr Window;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public NativePoint Point;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref NativeMessage message);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }

    /// <summary>OS入力を発生させるだけの測定器。製品の出力経路ではない。</summary>
    private static class SendInputInstrument
    {
        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;
        private const uint MouseEventMove = 0x0001;
        private const uint MouseEventAbsolute = 0x8000;
        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;
        private const uint MouseEventWheel = 0x0800;
        private const uint KeyEventKeyUp = 0x0002;
        private const int SmCxScreen = 0;
        private const int SmCyScreen = 1;

        public static void MoveTo(int x, int y)
        {
            var width = GetSystemMetrics(SmCxScreen);
            var height = GetSystemMetrics(SmCyScreen);
            Send(MouseInput(
                MouseEventMove | MouseEventAbsolute,
                (int)Math.Round(x * 65535.0 / (width - 1)),
                (int)Math.Round(y * 65535.0 / (height - 1))));
        }

        public static void LeftDown() => Send(MouseInput(MouseEventLeftDown));

        public static void LeftUp() => Send(MouseInput(MouseEventLeftUp));

        public static void LeftClick()
        {
            LeftDown();
            Thread.Sleep(30);
            LeftUp();
        }

        public static void WheelUp() => Send(MouseInput(MouseEventWheel, mouseData: 120));

        public static void TapKey(ushort virtualKey)
        {
            Send(KeyboardInput(virtualKey, 0));
            Thread.Sleep(30);
            Send(KeyboardInput(virtualKey, KeyEventKeyUp));
        }

        private static void Send(NativeInput input)
        {
            if (SendInput(1, [input], Marshal.SizeOf<NativeInput>()) != 1)
            {
                throw new InvalidOperationException($"SendInput failed: {Marshal.GetLastWin32Error()}");
            }
        }

        private static NativeInput MouseInput(uint flags, int dx = 0, int dy = 0, int mouseData = 0) =>
            new()
            {
                Type = InputMouse,
                Union = new NativeInputUnion
                {
                    Mouse = new NativeMouseInput
                    {
                        Dx = dx,
                        Dy = dy,
                        MouseData = (uint)mouseData,
                        Flags = flags,
                    },
                },
            };

        private static NativeInput KeyboardInput(ushort virtualKey, uint flags) =>
            new()
            {
                Type = InputKeyboard,
                Union = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        VirtualKey = virtualKey,
                        Flags = flags,
                    },
                },
            };

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeKeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct NativeInputUnion
        {
            [FieldOffset(0)] public NativeMouseInput Mouse;
            [FieldOffset(0)] public NativeKeyboardInput Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput
        {
            public uint Type;
            public NativeInputUnion Union;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, NativeInput[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
    }
}
