using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OpenLogicool.Host;

/// <summary>Raw Inputの入力元でNanoを除外し、手入力とリモートの合成入力を観測する。</summary>
internal sealed class WindowsUserInputMonitor : IDisposable
{
    private readonly SerialHidCandidate nano;
    private readonly Guid nanoContainer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly UserInputPauseState state;
    private readonly Thread thread;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<nint, bool> nanoDevices = [];
    private readonly Dictionary<nint, (int X, int Y)> absolutePositions = [];
    private readonly HookProc keyboardHook;
    private readonly HookProc mouseHook;
    private Dispatcher? dispatcher;
    private HwndSource? window;
    private nint keyboardHandle;
    private nint mouseHandle;
    private volatile Exception? failure;
    private bool registered;
    private bool disposed;
    private long softwareMoves;
    private long softwarePositionChanges;
    private (int X, int Y)? softwarePosition;

    public WindowsUserInputMonitor(SerialHidCandidate nano)
    {
        this.nano = nano;
        nanoContainer = ContainerForInstance(nano.DeviceInstanceId);
        state = new(() => clock.ElapsedMilliseconds);
        keyboardHook = Keyboard;
        mouseHook = Mouse;
        thread = new Thread(Run) { IsBackground = true, Name = "Botの手入力監視" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Task.GetAwaiter().GetResult();
    }

    public Guid NanoContainer => nanoContainer;
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
        try
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            window = new HwndSource(new HwndSourceParameters("OpenLogicool.Bot.UserInput")
                { ParentWindow = new nint(-3), Width = 0, Height = 0, WindowStyle = 0 });
            window.AddHook(WindowProc);
            if (RegisteredDevices().Any(device => device.Page == 1 && device.Usage is 2 or 6))
                throw new InvalidOperationException("キーボード／マウスのRaw Inputは別の機能が登録済みです。登録を上書きしません。");
            Register(window.Handle, 0x2100);
            registered = true;
            keyboardHandle = SetWindowsHookExW(13, keyboardHook, GetModuleHandleW(null), 0);
            mouseHandle = SetWindowsHookExW(14, mouseHook, GetModuleHandleW(null), 0);
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
            Dispatcher.Run();
        }
        catch (Exception error) { failure = error; ready.TrySetException(error); }
        finally
        {
            if (keyboardHandle != 0) UnhookWindowsHookEx(keyboardHandle);
            if (mouseHandle != 0) UnhookWindowsHookEx(mouseHandle);
            if (window is not null)
            {
                try
                {
                    if (registered && RegisteredDevices().Where(device => device.Page == 1 && device.Usage is 2 or 6)
                        .All(device => device.Target == window.Handle)) Register(0, 1);
                }
                catch (Exception error) { failure ??= error; }
                window.Dispose();
            }
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

    private nint WindowProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        try
        {
            if (msg == 0xFF) ReadRaw(lParam);
            if (msg == 0xFE && wParam == 2)
            { state.Removed(lParam); nanoDevices.Remove(lParam); absolutePositions.Remove(lParam); }
        }
        catch (Exception error) { failure = error; }
        return 0;
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
            if (IsNano(header.Device)) { state.NanoInput(); return; }
            var body = data + (int)headerSize;
            if (header.Type == 1)
            {
                var key = Marshal.PtrToStructure<RawKeyboard>(body);
                if (key.VKey == 255) return;
                state.Button(header.Device, KeyboardCode(key.VKey, key.MakeCode, key.Flags), (key.Flags & 1) == 0);
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

    // VK_DBE_*はIMEのモード指示。スキャンコードがあっても起動時の押下状態へ取り込まない。
    // 起動後に届く実際のdown/upはRaw Inputで従来どおり追跡する。
    internal static bool HasStartupPhysicalKey(int vk, uint scan) =>
        vk is 1 or 2 or 4 or 5 or 6 || scan != 0 && vk is not (>= 0xF0 and <= 0xFB);

    private bool IsNano(nint device)
    {
        if (device == 0) return false; // OSが機器handleを付けない入力も、利用者の入力として扱う。
        if (nanoDevices.TryGetValue(device, out var result)) return result;
        uint size = 0;
        if (GetRawInputDeviceInfoW(device, 0x20000007, null, ref size) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var path = new StringBuilder((int)size + 1);
        if (GetRawInputDeviceInfoW(device, 0x20000007, path, ref size) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var sameModel = path.ToString().Contains($"VID_{nano.VendorId:X4}&PID_{nano.ProductId:X4}", StringComparison.OrdinalIgnoreCase);
        result = sameModel && ContainerForInterface(path.ToString()) == nanoContainer;
        nanoDevices.Add(device, result);
        return result;
    }

    private nint Keyboard(int code, nint message, nint pointer)
    {
        try
        {
            if (code == 0)
            {
                var input = Marshal.PtrToStructure<KeyboardHookData>(pointer);
                // リモート操作等の合成入力を補足する。Nanoの物理入力はRaw Inputで識別する。
                if ((input.Flags & 0x10) != 0)
                    state.Button(new nint(-2), KeyboardCode((int)input.VKey, (int)input.Scan, (input.Flags & 1) != 0 ? 2 : 0),
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

    private static Guid ContainerForInstance(string instance)
    {
        var result = CM_Locate_DevNodeW(out var node, instance, 0);
        if (result != 0) throw new InvalidOperationException($"Nanoのdevnodeを取得できません: CR={result}");
        var key = ContainerKey;
        var bytes = new byte[16]; uint size = 16;
        result = CM_Get_DevNode_PropertyW(node, ref key, out var type, bytes, ref size, 0);
        if (result != 0 || type != 13 || size != 16) throw new InvalidOperationException($"NanoのContainerIdを取得できません: CR={result}");
        return new Guid(bytes);
    }

    private static Guid ContainerForInterface(string path)
    {
        var key = ContainerKey;
        var bytes = new byte[16]; uint size = 16;
        var result = CM_Get_Device_Interface_PropertyW(path, ref key, out var type, bytes, ref size, 0);
        if (result != 0 || type != 13 || size != 16) throw new InvalidOperationException($"HIDのContainerIdを取得できません: CR={result}");
        return new Guid(bytes);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (dispatcher is { HasShutdownStarted: false }) dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        if (!thread.Join(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("手入力監視を終了できません。");
    }

    private static readonly DevicePropertyKey ContainerKey = new() { Format = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), Id = 2 };
    [StructLayout(LayoutKind.Sequential)] private struct DevicePropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawHeader { public uint Type, Size; public nint Device, Parameter; }
    [StructLayout(LayoutKind.Sequential)] private struct RawKeyboard { public ushort MakeCode, Flags, Reserved, VKey; public uint Message, Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct RawMouse
    { [FieldOffset(0)] public ushort Flags; [FieldOffset(4)] public ushort ButtonFlags; [FieldOffset(6)] public ushort ButtonData;
      [FieldOffset(8)] public uint RawButtons; [FieldOffset(12)] public int X; [FieldOffset(16)] public int Y; [FieldOffset(20)] public uint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardHookData { public uint VKey, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseHookData { public int X, Y; public uint MouseData, Flags, Time; public nuint Extra; }
    private delegate nint HookProc(int code, nint message, nint data);
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
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNodeW(out uint node, string instance, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_DevNode_PropertyW(uint node, ref DevicePropertyKey key, out uint type, byte[] value, ref uint size, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_Interface_PropertyW(string path, ref DevicePropertyKey key, out uint type, byte[] value, ref uint size, uint flags);
}
