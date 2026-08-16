using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Devices.G13;

/// <summary>
/// G13 の live adapter。message-only window で vendor page 0xFF00 の Raw Input を受信し、
/// VID/PID で G13 だけへ絞って G13ReportStream（recorded adapter と同一経路）で変換する。
/// 取得経路は Phase 0 実測（docs/probes/g13-input-map-2026-08-15.md）で成立確認済み。
/// </summary>
public sealed class G13RawInputSource : IDeviceInputSource, IDeviceChangeSource, IG13StickSource, IDisposable
{
    private const int MaxQueuedInputs = 4096;
    private const int MaxQueuedStickSamples = 1024;

    private readonly ConcurrentQueue<PhysicalInput> inputs = new();
    // 切断・到着は物理的な抜挿でしか増えないため cap を置かない（Removal の drop は release 漏れになる）
    private readonly ConcurrentQueue<DeviceChange> deviceChanges = new();
    private readonly ConcurrentQueue<G13StickSample> stickSamples = new();
    private readonly ConcurrentDictionary<IntPtr, G13ReportStream> streamsByHandle = new();
    private readonly ConcurrentDictionary<IntPtr, string> devicePathsByHandle = new();
    private readonly List<PhysicalInput> feedBuffer = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Thread pumpThread;
    private readonly WndProcDelegate wndProc;
    private readonly ManualResetEventSlim pumpReady = new(false);
    private IntPtr windowHandle;
    private Exception? pumpFailure;
    private long droppedInputCount;
    private bool disposed;

    public G13RawInputSource()
    {
        wndProc = WndProc;
        pumpThread = new Thread(MessagePump) { IsBackground = true, Name = "G13RawInput" };
        pumpThread.Start();
        pumpReady.Wait();
        if (pumpFailure is not null)
        {
            throw new InvalidOperationException("G13 Raw Input の受信窓を初期化できませんでした。", pumpFailure);
        }
    }

    /// <summary>consumer が追いつかず破棄した input 件数（0 でないことは overflow の明示）。</summary>
    public long DroppedInputCount => Interlocked.Read(ref droppedInputCount);

    public IReadOnlyList<DeviceInstance> EnumerateDevices()
    {
        var result = new List<DeviceInstance>();
        foreach (var (handle, info) in EnumerateRawInputHidDevices())
        {
            if (info.VendorId != G13DeviceIdentity.VendorId ||
                info.ProductId != G13DeviceIdentity.ProductId ||
                info.UsagePage != G13DeviceIdentity.VendorUsagePage)
            {
                continue;
            }

            var devicePath = GetDeviceName(handle);
            result.Add(new DeviceInstance(
                ContractSchemaVersions.Revision01,
                devicePath,
                info.VendorId,
                info.ProductId,
                devicePath,
                GetContainerId(devicePath),
                Generation: 1,
                Capabilities: ["g13.buttons", "g13.stick"]));
        }

        return result;
    }

    public bool TryPull(out PhysicalInput input)
    {
        ThrowIfPumpFailed();
        if (inputs.TryDequeue(out var next))
        {
            input = next;
            return true;
        }

        input = null!;
        return false;
    }

    public bool TryPullDeviceChange(out DeviceChange change)
    {
        ThrowIfPumpFailed();
        if (deviceChanges.TryDequeue(out var next))
        {
            change = next;
            return true;
        }

        change = null!;
        return false;
    }

    public bool TryPullStick(out G13StickSample sample)
    {
        ThrowIfPumpFailed();
        if (stickSamples.TryDequeue(out var next))
        {
            sample = next;
            return true;
        }

        sample = null!;
        return false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (windowHandle != IntPtr.Zero)
        {
            PostMessage(windowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        pumpThread.Join(TimeSpan.FromSeconds(5));
        pumpReady.Dispose();
    }

    private void ThrowIfPumpFailed()
    {
        if (pumpFailure is not null)
        {
            throw new InvalidOperationException("G13 Raw Input の受信 thread が停止しています。", pumpFailure);
        }
    }

    private void MessagePump()
    {
        try
        {
            var className = $"OpenLogicoolG13RawInput-{Environment.CurrentManagedThreadId}";
            var wndClass = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = className,
            };
            if (RegisterClassEx(ref wndClass) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
            }

            windowHandle = CreateWindowEx(0, className, "g13", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
            if (windowHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
            }

            var registration = new[]
            {
                new RAWINPUTDEVICE
                {
                    UsagePage = G13DeviceIdentity.VendorUsagePage,
                    Usage = 0,
                    Flags = RIDEV_INPUTSINK | RIDEV_PAGEONLY | RIDEV_DEVNOTIFY,
                    Target = windowHandle,
                },
            };
            if (!RegisterRawInputDevices(registration, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            {
                throw new InvalidOperationException($"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");
            }

            // 切断時は handle から情報を照会できないため、既接続 device の handle→path を先に確定させる
            foreach (var (handle, info) in EnumerateRawInputHidDevices())
            {
                if (info.VendorId == G13DeviceIdentity.VendorId &&
                    info.ProductId == G13DeviceIdentity.ProductId &&
                    info.UsagePage == G13DeviceIdentity.VendorUsagePage)
                {
                    devicePathsByHandle.TryAdd(handle, GetDeviceName(handle));
                }
            }
        }
        catch (Exception ex)
        {
            pumpFailure = ex;
            pumpReady.Set();
            return;
        }

        pumpReady.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        GC.KeepAlive(wndProc);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            HandleRawInput(lParam);
            return IntPtr.Zero;
        }

        if (msg == WM_INPUT_DEVICE_CHANGE)
        {
            HandleDeviceChange(wParam, lParam);
            return IntPtr.Zero;
        }

        if (msg == WM_CLOSE)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void HandleDeviceChange(IntPtr wParam, IntPtr deviceHandle)
    {
        var elapsedMs = clock.Elapsed.TotalMilliseconds;
        if (wParam == GIDC_ARRIVAL)
        {
            var info = GetDeviceInfo(deviceHandle);
            if (info.VendorId != G13DeviceIdentity.VendorId ||
                info.ProductId != G13DeviceIdentity.ProductId ||
                info.UsagePage != G13DeviceIdentity.VendorUsagePage)
            {
                return;
            }

            var devicePath = GetDeviceName(deviceHandle);
            devicePathsByHandle[deviceHandle] = devicePath;
            // handle 値が再利用された場合に切断前の report 状態を持ち越さないよう stream を破棄する
            streamsByHandle.TryRemove(deviceHandle, out _);
            deviceChanges.Enqueue(new DeviceChange(
                ContractSchemaVersions.Revision01, devicePath, DeviceChangeKind.Arrival, elapsedMs));
            return;
        }

        // path cache は残す: queue 上で removal より後ろに残った WM_INPUT が
        // 無効 handle への情報照会（失敗して throw）に落ちないようにするため
        if (wParam == GIDC_REMOVAL && devicePathsByHandle.TryGetValue(deviceHandle, out var removedPath))
        {
            streamsByHandle.TryRemove(deviceHandle, out _);
            deviceChanges.Enqueue(new DeviceChange(
                ContractSchemaVersions.Revision01, removedPath, DeviceChangeKind.Removal, elapsedMs));
        }
    }

    private void HandleRawInput(IntPtr rawInputHandle)
    {
        uint size = 0;
        GetRawInputData(rawInputHandle, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        if (size == 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInputHandle, RID_INPUT, buffer, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) != size)
            {
                return;
            }

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.Type != RIM_TYPEHID)
            {
                return;
            }

            if (!IsG13Device(header.Device))
            {
                return;
            }

            var dataPtr = buffer + Marshal.SizeOf<RAWINPUTHEADER>();
            var hid = Marshal.PtrToStructure<RAWHID>(dataPtr);
            if (hid.SizeHid != G13DeviceIdentity.InputReportLength)
            {
                return;
            }

            var elapsedMs = clock.Elapsed.TotalMilliseconds;
            var stream = streamsByHandle.GetOrAdd(
                header.Device,
                handle => new G13ReportStream(devicePathsByHandle[handle]));

            var report = new byte[hid.SizeHid];
            for (var i = 0; i < hid.Count; i++)
            {
                Marshal.Copy(dataPtr + Marshal.SizeOf<RAWHID>() + i * (int)hid.SizeHid, report, 0, report.Length);
                feedBuffer.Clear();
                stream.Feed(report, elapsedMs, feedBuffer, out var stickSample);
                foreach (var input in feedBuffer)
                {
                    if (inputs.Count >= MaxQueuedInputs)
                    {
                        inputs.TryDequeue(out _);
                        Interlocked.Increment(ref droppedInputCount);
                    }

                    inputs.Enqueue(input);
                }

                if (stickSample is not null)
                {
                    if (stickSamples.Count >= MaxQueuedStickSamples)
                    {
                        stickSamples.TryDequeue(out _);
                    }

                    stickSamples.Enqueue(stickSample);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private bool IsG13Device(IntPtr deviceHandle)
    {
        if (devicePathsByHandle.ContainsKey(deviceHandle))
        {
            return true;
        }

        var info = GetDeviceInfo(deviceHandle);
        if (info.VendorId != G13DeviceIdentity.VendorId ||
            info.ProductId != G13DeviceIdentity.ProductId ||
            info.UsagePage != G13DeviceIdentity.VendorUsagePage)
        {
            return false;
        }

        devicePathsByHandle.TryAdd(deviceHandle, GetDeviceName(deviceHandle));
        return true;
    }

    private static IEnumerable<(IntPtr Handle, HidDeviceInfo Info)> EnumerateRawInputHidDevices()
    {
        uint deviceCount = 0;
        var listItemSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        if (GetRawInputDeviceList(IntPtr.Zero, ref deviceCount, listItemSize) == unchecked((uint)-1))
        {
            throw new InvalidOperationException($"GetRawInputDeviceList (count) failed: {Marshal.GetLastWin32Error()}");
        }

        var list = new RAWINPUTDEVICELIST[deviceCount];
        var written = GetRawInputDeviceList(list, ref deviceCount, listItemSize);
        if (written == unchecked((uint)-1))
        {
            throw new InvalidOperationException($"GetRawInputDeviceList failed: {Marshal.GetLastWin32Error()}");
        }

        for (var i = 0; i < written; i++)
        {
            if (list[i].Type != RIM_TYPEHID)
            {
                continue;
            }

            yield return (list[i].Device, GetDeviceInfo(list[i].Device));
        }
    }

    private static HidDeviceInfo GetDeviceInfo(IntPtr deviceHandle)
    {
        var info = new RID_DEVICE_INFO { cbSize = Marshal.SizeOf<RID_DEVICE_INFO>() };
        var size = (uint)info.cbSize;
        var buffer = Marshal.AllocHGlobal(info.cbSize);
        try
        {
            Marshal.StructureToPtr(info, buffer, fDeleteOld: false);
            if (GetRawInputDeviceInfo(deviceHandle, RIDI_DEVICEINFO, buffer, ref size) == unchecked((uint)-1))
            {
                throw new InvalidOperationException($"GetRawInputDeviceInfo (RIDI_DEVICEINFO) failed: {Marshal.GetLastWin32Error()}");
            }

            info = Marshal.PtrToStructure<RID_DEVICE_INFO>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return new HidDeviceInfo(
            (int)info.hid.dwVendorId,
            (int)info.hid.dwProductId,
            info.hid.usUsagePage,
            info.hid.usUsage);
    }

    private static string GetDeviceName(IntPtr deviceHandle)
    {
        uint size = 0;
        GetRawInputDeviceInfo(deviceHandle, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0)
        {
            throw new InvalidOperationException($"GetRawInputDeviceInfo (RIDI_DEVICENAME size) failed: {Marshal.GetLastWin32Error()}");
        }

        var buffer = Marshal.AllocHGlobal((int)size * sizeof(char));
        try
        {
            if (GetRawInputDeviceInfo(deviceHandle, RIDI_DEVICENAME, buffer, ref size) == unchecked((uint)-1))
            {
                throw new InvalidOperationException($"GetRawInputDeviceInfo (RIDI_DEVICENAME) failed: {Marshal.GetLastWin32Error()}");
            }

            return Marshal.PtrToStringUni(buffer)
                ?? throw new InvalidOperationException("RIDI_DEVICENAME が null を返しました。");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string GetContainerId(string deviceInterfacePath)
    {
        var key = DevPropKeyContainerId;
        uint propertyType = 0;
        var buffer = new byte[16];
        var size = (uint)buffer.Length;
        var result = CM_Get_Device_Interface_PropertyW(deviceInterfacePath, ref key, ref propertyType, buffer, ref size, 0);
        if (result != 0)
        {
            throw new InvalidOperationException($"CM_Get_Device_Interface_PropertyW (ContainerId) failed: CR={result}");
        }

        if (propertyType != DEVPROP_TYPE_GUID)
        {
            throw new InvalidOperationException($"ContainerId の property type が GUID ではありません: 0x{propertyType:X8}");
        }

        return new Guid(buffer).ToString("B");
    }

    private readonly record struct HidDeviceInfo(int VendorId, int ProductId, int UsagePage, int Usage);

    private const int WM_INPUT = 0x00FF;
    private const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
    private const int WM_CLOSE = 0x0010;
    private static readonly IntPtr GIDC_ARRIVAL = new(1);
    private static readonly IntPtr GIDC_REMOVAL = new(2);
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_PAGEONLY = 0x00000020;
    private const uint RIDEV_DEVNOTIFY = 0x00002000;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIDI_DEVICEINFO = 0x2000000B;
    private const int RIM_TYPEHID = 2;
    private const uint DEVPROP_TYPE_GUID = 0x0000000D;
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private static DEVPROPKEY DevPropKeyContainerId = new()
    {
        fmtid = new Guid(0x8c7ed206, 0x3f8a, 0x4827, 0xb3, 0xab, 0xae, 0x9e, 0x1f, 0xae, 0xfc, 0x6c),
        pid = 2,
    };

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
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public IntPtr Device;
        public int Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public int Type;
        public int Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWHID
    {
        public uint SizeHid;
        public uint Count;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO_HID
    {
        public uint dwVendorId;
        public uint dwProductId;
        public uint dwVersionNumber;
        public ushort usUsagePage;
        public ushort usUsage;
    }

    // union の最大メンバー（RID_DEVICE_INFO_KEYBOARD, 24 bytes）に合わせて全体 32 bytes が必要
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct RID_DEVICE_INFO
    {
        [FieldOffset(0)] public int cbSize;
        [FieldOffset(4)] public int dwType;
        [FieldOffset(8)] public RID_DEVICE_INFO_HID hid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] devices, uint numDevices, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        [Out] RAWINPUTDEVICELIST[]? deviceList, ref uint numDevices, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        IntPtr deviceList, ref uint numDevices, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint sizeHeader);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

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
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_PropertyW(
        string deviceInterface,
        ref DEVPROPKEY propertyKey,
        ref uint propertyType,
        byte[] buffer,
        ref uint bufferSize,
        uint flags);
}
