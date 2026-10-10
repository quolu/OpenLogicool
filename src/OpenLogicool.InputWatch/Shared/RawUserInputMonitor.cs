using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenLogicool.InputWatch;

/// <summary>キーボードとマウスの手入力を観測する元。</summary>
internal interface IRawUserInputSource : IDisposable
{
    UserInputPauseSnapshot Snapshot();
}

/// <summary>
/// Raw Inputの入力元でBotの出力機器を除外し、キーボードとマウスの手入力とリモートの合成入力を観測する。
/// 自分より高い権限の窓が前面の間は、OSがこのprocessへ入力を渡さない。その時は管理者権限の監視processの中で使う。
/// 監視processとBot本体の両方でコンパイルするため、画面部品に頼らずmessage専用の窓で受け取る。
/// </summary>
internal sealed class RawUserInputMonitor : IRawUserInputSource
{
    private readonly Func<string, bool> isBotOutputDevice;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly UserInputPauseState state;
    private readonly Thread thread;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<nint, bool> botOutputDevices = [];
    private readonly Dictionary<nint, (int X, int Y)> absolutePositions = [];
    private readonly HookProc keyboardHook;
    private readonly HookProc mouseHook;
    private readonly WindowProcedure windowProcedure;
    private uint threadId;
    private nint window;
    private nint keyboardHandle;
    private nint mouseHandle;
    private volatile Exception? failure;
    private bool registered;
    private bool disposed;
    private long softwareMoves;
    private long softwarePositionChanges;
    private (int X, int Y)? softwarePosition;

    /// <param name="isBotOutputDevice">Raw Inputの機器のパスが、Botの出力機器（Nano）かを返す。</param>
    public RawUserInputMonitor(Func<string, bool> isBotOutputDevice)
    {
        this.isBotOutputDevice = isBotOutputDevice;
        // 押下状態の符号は、マウスのボタンだけ0x10000を足してある。OSへは仮想キーの番号で聞く。
        state = new(() => clock.ElapsedMilliseconds, code => (GetAsyncKeyState(code & 0xFFFF) & 0x8000) != 0);
        keyboardHook = Keyboard;
        mouseHook = Mouse;
        windowProcedure = WindowProc;
        thread = new Thread(Run) { IsBackground = true, Name = "手入力の監視" };
        thread.Start();
        ready.Task.GetAwaiter().GetResult();
    }

    public int StartupHeldCount { get; private set; }
    public long SoftwareMoves => Interlocked.Read(ref softwareMoves);
    public long SoftwarePositionChanges => Interlocked.Read(ref softwarePositionChanges);

    public UserInputPauseSnapshot Snapshot()
    {
        if (failure is { } error) throw new InvalidOperationException("手入力監視に失敗しました。", error);
        return state.Snapshot();
    }

    private void Run()
    {
        var instance = GetModuleHandleW(null);
        var className = "OpenLogicool.UserInput." + Guid.NewGuid().ToString("N");
        ushort classAtom = 0;
        try
        {
            threadId = GetCurrentThreadId();
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                WindowProc = Marshal.GetFunctionPointerForDelegate(windowProcedure),
                Instance = instance,
                ClassName = className,
            };
            classAtom = RegisterClassExW(ref windowClass);
            if (classAtom == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "手入力の監視の窓を登録できません。");
            window = CreateWindowExW(0, className, "OpenLogicool.Bot.UserInput", 0, 0, 0, 0, 0, new nint(-3), 0, instance, 0);
            if (window == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "手入力の監視の窓を作れません。");
            if (RegisteredDevices().Any(device => device.Page == 1 && device.Usage is 2 or 6))
                throw new InvalidOperationException("キーボード／マウスのRaw Inputは別の機能が登録済みです。登録を上書きしません。");
            Register(window, 0x2100);
            registered = true;
            keyboardHandle = SetWindowsHookExW(13, keyboardHook, instance, 0);
            mouseHandle = SetWindowsHookExW(14, mouseHook, instance, 0);
            if (keyboardHandle == 0 || mouseHandle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "手入力hookを登録できません。");
            for (var vk = 1; vk < 255; vk++)
            {
                if (vk is 16 or 17 or 18) continue; // 左右の修飾キーを別に扱う。
                if (!HasStartupPhysicalKey(vk, MapVirtualKeyW((uint)vk, 4))) continue;
                if ((GetAsyncKeyState(vk) & 0x8000) == 0) continue;
                StartupHeldCount++;
                state.SeedHeld(vk is 1 or 2 or 4 or 5 or 6 ? 0x10000 + vk : vk);
            }
            ready.SetResult();
            while (GetMessageW(out var message, 0, 0, 0) > 0) DispatchMessageW(ref message);
        }
        catch (Exception error) { failure = error; ready.TrySetException(error); }
        finally
        {
            if (keyboardHandle != 0) UnhookWindowsHookEx(keyboardHandle);
            if (mouseHandle != 0) UnhookWindowsHookEx(mouseHandle);
            if (window != 0)
            {
                try
                {
                    if (registered && RegisteredDevices().Where(device => device.Page == 1 && device.Usage is 2 or 6)
                        .All(device => device.Target == window)) Register(0, 1);
                }
                catch (Exception error) { failure ??= error; }
                DestroyWindow(window);
            }
            if (classAtom != 0) UnregisterClassW(className, instance);
        }
    }

    private static void Register(nint target, uint flags)
    {
        RawDevice[] devices = [new() { Page = 1, Usage = 2, Flags = flags, Target = target },
            new() { Page = 1, Usage = 6, Flags = flags, Target = target }];
        if (!RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<RawDevice>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "手入力のRaw Inputを登録できません。");
    }

    private static RawDevice[] RegisteredDevices()
    {
        uint count = 0;
        var size = (uint)Marshal.SizeOf<RawDevice>();
        if (GetRegisteredRawInputDevices(null, ref count, size) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (count == 0) return [];
        var devices = new RawDevice[count];
        if (GetRegisteredRawInputDevices(devices, ref count, size) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return devices;
    }

    private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (message == 0xFF) ReadRaw(lParam);
            if (message == 0xFE && wParam == 2)
            { state.Removed(lParam); botOutputDevices.Remove(lParam); absolutePositions.Remove(lParam); }
        }
        catch (Exception error) { failure = error; }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void ReadRaw(nint handle)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RawHeader>();
        if (GetRawInputData(handle, 0x10000003, 0, ref size, headerSize) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var data = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(handle, 0x10000003, data, ref size, headerSize) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var header = Marshal.PtrToStructure<RawHeader>(data);
            if (header.Type > 1) return;
            if (IsBotOutput(header.Device)) { state.NanoInput(); return; }
            var body = data + (int)headerSize;
            if (header.Type == 1)
            {
                var key = Marshal.PtrToStructure<RawKeyboard>(body);
                if (key.VKey == 255) return;
                Key(state, header.Device, KeyboardCode(key.VKey, key.MakeCode, key.Flags), (key.Flags & 1) == 0);
            }
            else
            {
                var mouse = Marshal.PtrToStructure<RawMouse>(body);
                var moved = mouse.X != 0 || mouse.Y != 0;
                if ((mouse.Flags & 1) != 0)
                {
                    moved = !absolutePositions.TryGetValue(header.Device, out var position) || position != (mouse.X, mouse.Y);
                    absolutePositions[header.Device] = (mouse.X, mouse.Y);
                }
                if (moved || ((mouse.ButtonFlags & 0xC00) != 0 && mouse.ButtonData != 0)) state.Activity();
                int[] buttons = [1, 2, 4, 5, 6];
                for (var i = 0; i < 5; i++)
                {
                    if ((mouse.ButtonFlags & (1 << (i * 2))) != 0) state.Button(header.Device, 0x10000 + buttons[i], true);
                    if ((mouse.ButtonFlags & (2 << (i * 2))) != 0) state.Button(header.Device, 0x10000 + buttons[i], false);
                }
            }
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    internal static int KeyboardCode(int vk, int scan, int flags) => vk switch
    {
        16 => scan == 0x36 ? 0xA1 : 0xA0,
        17 => (flags & 2) != 0 ? 0xA3 : 0xA2,
        18 => (flags & 2) != 0 ? 0xA5 : 0xA4,
        _ => vk
    };

    // VK_DBE_*はIMEのモード指示。押した合図に対になるkey upが届かず、OSも押下中と返し続ける。
    // 押下状態には入れず、起動時も起動後も「操作があった」事実だけを扱う。
    internal static bool IsImeModeKey(int vk) => vk is >= 0xF0 and <= 0xFB;

    internal static bool HasStartupPhysicalKey(int vk, uint scan) =>
        vk is 1 or 2 or 4 or 5 or 6 || scan != 0 && !IsImeModeKey(vk);

    internal static void Key(UserInputPauseState state, nint source, int code, bool down)
    {
        if (IsImeModeKey(code)) state.Activity();
        else state.Button(source, code, down);
    }

    private bool IsBotOutput(nint device)
    {
        if (device == 0) return false; // OSが機器handleを付けない入力も、利用者の入力として扱う。
        if (botOutputDevices.TryGetValue(device, out var result)) return result;
        uint size = 0;
        if (GetRawInputDeviceInfoW(device, 0x20000007, null, ref size) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var path = new StringBuilder((int)size + 1);
        if (GetRawInputDeviceInfoW(device, 0x20000007, path, ref size) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        result = isBotOutputDevice(path.ToString());
        botOutputDevices.Add(device, result);
        return result;
    }

    private nint Keyboard(int code, nint message, nint pointer)
    {
        try
        {
            if (code == 0)
            {
                var input = Marshal.PtrToStructure<KeyboardHookData>(pointer);
                // リモート操作等の合成入力を補足する。Botの出力機器の物理入力はRaw Inputで識別する。
                if ((input.Flags & 0x10) != 0)
                    Key(state, new nint(-2), KeyboardCode((int)input.VKey, (int)input.Scan, (input.Flags & 1) != 0 ? 2 : 0),
                        message is 0x100 or 0x104);
            }
        }
        catch (Exception error) { failure = error; }
        return CallNextHookEx(0, code, message, pointer);
    }

    private nint Mouse(int code, nint message, nint pointer)
    {
        try
        {
            if (code == 0)
            {
                var input = Marshal.PtrToStructure<MouseHookData>(pointer);
                if ((input.Flags & 1) != 0)
                {
                    if (message == 0x200)
                    {
                        Interlocked.Increment(ref softwareMoves);
                        if (softwarePosition != (input.X, input.Y)) Interlocked.Increment(ref softwarePositionChanges);
                        softwarePosition = (input.X, input.Y);
                    }
                    var button = message switch { 0x201 or 0x202 => 1, 0x204 or 0x205 => 2,
                        0x207 or 0x208 => 4, 0x20B or 0x20C => (input.MouseData >> 16) == 1 ? 5 : 6, _ => 0 };
                    if (button == 0) state.Activity();
                    else state.Button(new nint(-2), 0x10000 + button, message is 0x201 or 0x204 or 0x207 or 0x20B);
                }
            }
        }
        catch (Exception error) { failure = error; }
        return CallNextHookEx(0, code, message, pointer);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (threadId != 0 && thread.IsAlive) PostThreadMessageW(threadId, 0x12, 0, 0); // WM_QUIT
        if (!thread.Join(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("手入力監視を終了できません。");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style; public nint WindowProc; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background; public string? MenuName; public string ClassName; public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowMessage { public nint Window; public uint Id; public nint WParam, LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawHeader { public uint Type, Size; public nint Device, Parameter; }
    [StructLayout(LayoutKind.Sequential)] private struct RawKeyboard { public ushort MakeCode, Flags, Reserved, VKey; public uint Message, Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct RawMouse
    { [FieldOffset(0)] public ushort Flags; [FieldOffset(4)] public ushort ButtonFlags; [FieldOffset(6)] public ushort ButtonData;
      [FieldOffset(8)] public uint RawButtons; [FieldOffset(12)] public int X; [FieldOffset(16)] public int Y; [FieldOffset(20)] public uint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardHookData { public uint VKey, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseHookData { public int X, Y; public uint MouseData, Flags, Time; public nuint Extra; }
    private delegate nint HookProc(int code, nint message, nint data);
    private delegate nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterClassW(string className, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMessageW(out WindowMessage message, nint window, uint filterMin, uint filterMax);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref WindowMessage message);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRegisteredRawInputDevices([Out] RawDevice[]? devices, ref uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfoW(nint device, uint command, StringBuilder? data, ref uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint type);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
}
