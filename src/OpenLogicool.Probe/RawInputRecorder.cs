using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OpenLogicool.Probe;

// Raw Input を message-only window で全録りする read-only recorder。
// keyboard / mouse / vendor page (0xFF00, 0xFF80) / consumer page を登録し、
// 届いた report を時系列 JSONL でファイルへ流す。device への write はしない。
internal static class RawInputRecorder
{
    private const int WM_INPUT = 0x00FF;
    private const int WM_CLOSE = 0x0010;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_PAGEONLY = 0x00000020;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const int RIM_TYPEMOUSE = 0;
    private const int RIM_TYPEKEYBOARD = 1;
    private const int RIM_TYPEHID = 2;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private static StreamWriter? _writer;
    private static readonly object WriterLock = new();
    private static readonly ConcurrentDictionary<IntPtr, string> DeviceNames = new();
    private static readonly System.Diagnostics.Stopwatch Clock = new();
    private static long _eventCount;

    public static int Run(string label, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"rawinput-{label}-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");

        using var writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(false));
        _writer = writer;

        var pumpThread = new Thread(() => MessagePump(label)) { IsBackground = true };
        Clock.Start();
        pumpThread.Start();

        Console.WriteLine($"[record] session '{label}' 記録中 → {outputPath}");
        Console.WriteLine("[record] 対象デバイスのキーを押してください。終わったら Enter で停止。");
        Console.ReadLine();

        if (_windowHandle != IntPtr.Zero)
            PostMessage(_windowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        pumpThread.Join(TimeSpan.FromSeconds(5));

        lock (WriterLock)
        {
            writer.Flush();
            _writer = null;
        }

        Console.WriteLine($"[record] 停止。イベント {Interlocked.Read(ref _eventCount)} 件 → {outputPath}");
        return 0;
    }

    private static IntPtr _windowHandle;

    private static void MessagePump(string label)
    {
        var className = "OpenLogicoolProbeRawInput";
        var wndProc = new WndProcDelegate(WndProc);
        var wndClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = className,
        };
        if (RegisterClassEx(ref wndClass) == 0)
            throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");

        _windowHandle = CreateWindowEx(0, className, "probe", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
        if (_windowHandle == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");

        var registrations = new[]
        {
            new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_INPUTSINK, Target = _windowHandle }, // keyboard
            new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x02, Flags = RIDEV_INPUTSINK, Target = _windowHandle }, // mouse
            new RAWINPUTDEVICE { UsagePage = 0x0C, Usage = 0x01, Flags = RIDEV_INPUTSINK, Target = _windowHandle }, // consumer control
            new RAWINPUTDEVICE { UsagePage = 0xFF00, Usage = 0, Flags = RIDEV_INPUTSINK | RIDEV_PAGEONLY, Target = _windowHandle }, // G13 vendor page
            new RAWINPUTDEVICE { UsagePage = 0xFF80, Usage = 0, Flags = RIDEV_INPUTSINK | RIDEV_PAGEONLY, Target = _windowHandle }, // G600 vendor page
        };
        if (!RegisterRawInputDevices(registrations, (uint)registrations.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new InvalidOperationException($"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");

        WriteEvent(new { Event = "session-start", Label = label, CapturedAtUtc = DateTime.UtcNow.ToString("O") });

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        GC.KeepAlive(wndProc);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            HandleRawInput(lParam);
            return IntPtr.Zero;
        }
        if (msg == WM_CLOSE)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static void HandleRawInput(IntPtr rawInputHandle)
    {
        uint size = 0;
        GetRawInputData(rawInputHandle, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInputHandle, RID_INPUT, buffer, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) != size)
                return;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            var deviceName = DeviceNames.GetOrAdd(header.Device, GetDeviceName);
            var elapsedMs = Clock.Elapsed.TotalMilliseconds;
            var dataPtr = buffer + Marshal.SizeOf<RAWINPUTHEADER>();

            switch (header.Type)
            {
                case RIM_TYPEKEYBOARD:
                {
                    var kb = Marshal.PtrToStructure<RAWKEYBOARD>(dataPtr);
                    WriteEvent(new
                    {
                        T = elapsedMs,
                        Kind = "keyboard",
                        Device = deviceName,
                        MakeCode = kb.MakeCode,
                        Flags = kb.Flags,
                        VKey = kb.VKey,
                        Message = kb.Message,
                    });
                    break;
                }
                case RIM_TYPEMOUSE:
                {
                    var mouse = Marshal.PtrToStructure<RAWMOUSE>(dataPtr);
                    // 移動だけのイベントはノイズなので、ボタン／ホイール変化があるものだけ記録する
                    if (mouse.ButtonFlags == 0) return;
                    WriteEvent(new
                    {
                        T = elapsedMs,
                        Kind = "mouse",
                        Device = deviceName,
                        ButtonFlags = $"0x{mouse.ButtonFlags:X4}",
                        ButtonData = mouse.ButtonData,
                    });
                    break;
                }
                case RIM_TYPEHID:
                {
                    var hid = Marshal.PtrToStructure<RAWHID>(dataPtr);
                    var reportBytes = new byte[hid.SizeHid * hid.Count];
                    Marshal.Copy(dataPtr + Marshal.SizeOf<RAWHID>(), reportBytes, 0, reportBytes.Length);
                    WriteEvent(new
                    {
                        T = elapsedMs,
                        Kind = "hid",
                        Device = deviceName,
                        SizeHid = hid.SizeHid,
                        Count = hid.Count,
                        DataHex = Convert.ToHexString(reportBytes),
                    });
                    break;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void WriteEvent(object record)
    {
        var line = JsonSerializer.Serialize(record);
        lock (WriterLock)
        {
            if (_writer is null) return;
            _writer.WriteLine(line);
            _writer.Flush();
        }
        Interlocked.Increment(ref _eventCount);
    }

    private static string GetDeviceName(IntPtr device)
    {
        uint size = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0) return $"handle:0x{device:X}";
        var buffer = Marshal.AllocHGlobal((int)size * sizeof(char));
        try
        {
            if (GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buffer, ref size) == unchecked((uint)-1))
                return $"handle:0x{device:X}";
            return Marshal.PtrToStringUni(buffer) ?? $"handle:0x{device:X}";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

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
    private struct RAWINPUTHEADER
    {
        public int Type;
        public int Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RAWMOUSE
    {
        [FieldOffset(0)] public ushort MouseFlags;
        [FieldOffset(4)] public ushort ButtonFlags;
        [FieldOffset(6)] public ushort ButtonData;
        [FieldOffset(8)] public uint RawButtons;
        [FieldOffset(12)] public int LastX;
        [FieldOffset(16)] public int LastY;
        [FieldOffset(20)] public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWHID
    {
        public uint SizeHid;
        public uint Count;
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

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint sizeHeader);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
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
}
