using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Host;

/// <summary>
/// mouse／keyboardのOS取得を所有するWindows環境別adapter。
/// WH_MOUSE_LL／WH_KEYBOARD_LL を専用threadのmessage loop上に張り、
/// 生edgeを<see cref="IDemonstrationInputSink"/>へ渡すだけを行う。
///
/// hook procedureはOSの入力配送経路上で走るため、ここで待つとdesktop全体の入力が遅れる。
/// sinkは非blockingでなければならない（contractどおり）。
///
/// injected flagでNano出力と物理入力を区別しない——low-level hookのその旗は
/// 送信元を確定できないため、自己記録は<see cref="DemonstrationRecordingGate"/>の
/// 記録・再生排他で構造的に防ぐ。
/// </summary>
public sealed class WindowsDemonstrationInputCollector : IDemonstrationInputCollector
{
    private const int WhKeyboardLowLevel = 13;
    private const int WhMouseLowLevel = 14;
    private const int HcAction = 0;
    private const uint WmQuit = 0x0012;


    private readonly TimeProvider timeProvider;
    private readonly object lifecycle = new();

    private Thread? worker;
    private uint workerThreadId;
    private IDemonstrationInputSink? sink;
    private IntPtr keyboardHook;
    private IntPtr mouseHook;
    private LowLevelProc? keyboardProc;
    private LowLevelProc? mouseProc;
    private Exception? startFailure;
    private long keyboardHookCalls;
    private long mouseHookCalls;
    private ManualResetEventSlim? started;
    private bool disposed;

    public WindowsDemonstrationInputCollector(TimeProvider? timeProvider = null) =>
        this.timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>hook procedureが呼ばれた回数（設置できたかと配送されているかを分けて見るための観測点）。</summary>
    public long KeyboardHookCalls => Interlocked.Read(ref keyboardHookCalls);

    public long MouseHookCalls => Interlocked.Read(ref mouseHookCalls);

    public void Start(IDemonstrationInputSink inputSink)
    {
        ArgumentNullException.ThrowIfNull(inputSink);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker is not null)
            {
                throw new InvalidOperationException("既に取得を開始しています。");
            }

            sink = inputSink;
            startFailure = null;
            started = new ManualResetEventSlim(false);
            worker = new Thread(HookLoop)
            {
                IsBackground = true,
                Name = "OpenLogicoolDemonstrationInput",
            };
            worker.Start();
            started.Wait();
            if (startFailure is not null)
            {
                var failure = startFailure;
                worker.Join(TimeSpan.FromSeconds(2));
                worker = null;
                sink = null;
                throw failure;
            }
        }
    }

    public void Stop()
    {
        lock (lifecycle)
        {
            if (worker is null)
            {
                return;
            }

            if (workerThreadId != 0)
            {
                _ = PostThreadMessage(workerThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            }

            if (!worker.Join(TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("入力取得threadが5秒以内に停止しませんでした。");
            }

            worker = null;
            sink = null;
            workerThreadId = 0;
        }
    }

    public void Dispose()
    {
        Stop();
        lock (lifecycle)
        {
            disposed = true;
            started?.Dispose();
            started = null;
        }
    }

    private void HookLoop()
    {
        workerThreadId = GetCurrentThreadId();
        keyboardProc = KeyboardHookProc;
        mouseProc = MouseHookProc;
        var moduleHandle = GetModuleHandle(null);

        try
        {
            keyboardHook = SetWindowsHookEx(WhKeyboardLowLevel, keyboardProc, moduleHandle, 0);
            if (keyboardHook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WH_KEYBOARD_LL を設置できませんでした。");
            }

            mouseHook = SetWindowsHookEx(WhMouseLowLevel, mouseProc, moduleHandle, 0);
            if (mouseHook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WH_MOUSE_LL を設置できませんでした。");
            }
        }
        catch (Exception exception)
        {
            startFailure = exception;
            ReleaseHooks();
            started!.Set();
            return;
        }

        started!.Set();

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            _ = TranslateMessage(ref message);
            _ = DispatchMessage(ref message);
        }

        ReleaseHooks();
    }

    private void ReleaseHooks()
    {
        if (keyboardHook != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(keyboardHook);
            keyboardHook = IntPtr.Zero;
        }

        if (mouseHook != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        Interlocked.Increment(ref keyboardHookCalls);
        if (code == HcAction)
        {
            var data = Marshal.PtrToStructure<KeyboardLowLevelHookStruct>(lParam);
            var edge = WindowsDemonstrationInputEdgeFactory.FromKeyboardMessage(
                (int)wParam,
                (ushort)data.VkCode,
                data.Flags,
                data.Time,
                timeProvider.GetUtcNow());
            if (edge is not null)
            {
                Publish(edge);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        Interlocked.Increment(ref mouseHookCalls);
        if (code == HcAction)
        {
            var data = Marshal.PtrToStructure<MouseLowLevelHookStruct>(lParam);
            var edge = WindowsDemonstrationInputEdgeFactory.FromMouseMessage(
                (int)wParam,
                data.MouseData,
                data.Point.X,
                data.Point.Y,
                data.Time,
                timeProvider.GetUtcNow());
            if (edge is not null)
            {
                Publish(edge);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private void Publish(DemonstrationInputEdge edge) => sink?.Observe(edge);

    private delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardLowLevelHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseLowLevelHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
}
