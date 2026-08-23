using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

internal static class SerialHidDirectSmoke
{
    public static int Run(string[] arguments, string outputDirectory)
    {
        var port = RequiredArgument(arguments, "--port");
        var deviceInstanceId = RequiredArgument(arguments, "--device-instance-id");
        var powerCycle = arguments.Contains("--power-cycle", StringComparer.Ordinal);
        Directory.CreateDirectory(outputDirectory);

        var result = new DirectSmokeResult
        {
            Schema = "openlogicool.serial-hid.direct-smoke.v1",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            DeviceInstanceId = deviceInstanceId,
            TransientPort = port,
        };

        try
        {
            using var observer = HidObservationWindow.Start();
            try
            {
                observer.Clear();
                RunFunctionalChecks(port, observer, result);
                RunLeaseCheck(port, observer, result);
                if (powerCycle)
                {
                    RunPowerCycleCheck(deviceInstanceId, observer, result);
                }

                result.Passed = result.HelloReady
                    && result.KeyObserved
                    && result.ChordObserved
                    && result.MouseObserved
                    && result.SequenceObserved
                    && result.AllUpObserved
                    && result.LeaseReleaseObserved
                    && (!powerCycle || result.PowerCycleAllUpObserved);
            }
            finally
            {
                result.Events = observer.Events.ToArray();
            }
        }
        catch (Exception exception)
        {
            result.Error = $"{exception.GetType().Name}: {exception.Message}";
        }

        var path = Path.Combine(outputDirectory, $"serial-hid-direct-smoke-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine);
        Console.WriteLine(path);
        Console.WriteLine(result.Passed ? "PASS" : $"FAIL: {result.Error ?? "one or more checks failed"}");
        return result.Passed ? 0 : 1;
    }

    private static void RunFunctionalChecks(string port, HidObservationWindow observer, DirectSmokeResult result)
    {
        using var exchange = new ProbeSerialPortFrameExchange(port);
        var session = SerialHidProtocolSession.Connect(
            exchange,
            new SerialHidSemanticVersion(1, 0, 0),
            TimeSpan.FromMilliseconds(300));
        result.HelloReady = true;
        result.FirmwareVersion = $"{session.ReadyInfo.FirmwareVersion.Major}.{session.ReadyInfo.FirmwareVersion.Minor}.{session.ReadyInfo.FirmwareVersion.Patch}";
        result.Capabilities = $"0x{(ushort)session.ReadyInfo.Capabilities:X4}";
        result.LeaseMilliseconds = session.ReadyInfo.LeaseMilliseconds;

        session.SendAllUp();
        var emitter = new SerialHidEmitter(session);

        EmitAndWait(emitter, observer,
            [Down("Key:F13")], [Event("key", "down", 0x7C)]);
        EmitAndWait(emitter, observer,
            [Up("Key:F13")], [Event("key", "up", 0x7C)]);
        result.KeyObserved = true;

        EmitAndWaitGroups(emitter, observer,
            [Down("Key:LCtrl"), Down("Key:F14")],
            [[Event("key", "down", 0xA2), Event("key", "down", 0x7D)]]);
        EmitAndWaitGroups(emitter, observer,
            [Up("Key:F14"), Up("Key:LCtrl")],
            [[Event("key", "up", 0x7D), Event("key", "up", 0xA2)]]);
        result.ChordObserved = true;

        EmitAndWait(emitter, observer,
            [Down("Mouse:Middle")], [Event("mouse", "down", 0x04)]);
        EmitAndWait(emitter, observer,
            [Up("Mouse:Middle")], [Event("mouse", "up", 0x04)]);
        result.MouseObserved = true;

        EmitAndWaitGroups(emitter, observer,
            [Down("Key:F15"), Up("Key:F15"), Down("Key:LCtrl"), Down("Key:F16"), Up("Key:F16"), Up("Key:LCtrl")],
            [
                [Event("key", "down", 0x7E)],
                [Event("key", "up", 0x7E)],
                [Event("key", "down", 0xA2), Event("key", "down", 0x7F)],
                [Event("key", "up", 0x7F), Event("key", "up", 0xA2)],
            ]);
        result.SequenceObserved = true;

        EmitAndWait(emitter, observer,
            [Down("Key:F17")], [Event("key", "down", 0x80)]);
        var marker = observer.Count;
        session.SendAllUp();
        observer.WaitFor([Event("key", "up", 0x80)], marker, TimeSpan.FromSeconds(2));
        result.AllUpObserved = true;
    }

    private static void RunLeaseCheck(string port, HidObservationWindow observer, DirectSmokeResult result)
    {
        var exchange = new ProbeSerialPortFrameExchange(port);
        var session = SerialHidProtocolSession.Connect(
            exchange,
            new SerialHidSemanticVersion(1, 0, 0),
            TimeSpan.FromMilliseconds(300));
        session.SendAllUp();
        var emitter = new SerialHidEmitter(session);
        EmitAndWait(emitter, observer,
            [Down("Key:F18")], [Event("key", "down", 0x81)]);

        var marker = observer.Count;
        var leaseClock = Stopwatch.StartNew();
        exchange.Dispose();
        observer.WaitFor([Event("key", "up", 0x81)], marker, TimeSpan.FromSeconds(2));
        leaseClock.Stop();
        result.LeaseReleaseElapsedMilliseconds = leaseClock.Elapsed.TotalMilliseconds;
        result.LeaseReleaseObserved = leaseClock.Elapsed <= TimeSpan.FromMilliseconds(500);
    }

    private static void RunPowerCycleCheck(string deviceInstanceId, HidObservationWindow observer, DirectSmokeResult result)
    {
        result.PowerCycleAttempted = true;
        Console.WriteLine("Pro MicroのUSBだけを抜き、5秒待ってから同じportへ挿し直してください。再列挙を監視しています。");
        var disappeared = WaitForPresence(deviceInstanceId, expected: false, TimeSpan.FromSeconds(60));
        result.PowerCycleDisconnectObserved = disappeared;
        var marker = observer.Count;
        var reappeared = disappeared && WaitForPresence(deviceInstanceId, expected: true, TimeSpan.FromSeconds(60));
        result.PowerCycleReconnectObserved = reappeared;
        Thread.Sleep(500);
        result.PowerCycleUnexpectedDownEvents = observer.Events.Skip(marker).Where(entry => entry.Edge == "down").ToArray();
        result.PowerCycleAllUpObserved = reappeared && result.PowerCycleUnexpectedDownEvents.Count == 0;
    }

    private static bool WaitForPresence(string deviceInstanceId, bool expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var present = PnpDevicePresence.IsPresent(deviceInstanceId);
            if (present == expected)
            {
                return true;
            }
            Thread.Sleep(100);
        }
        return false;
    }

    private static void EmitAndWait(
        SerialHidEmitter emitter,
        HidObservationWindow observer,
        IReadOnlyList<MappedOutputEdge> edges,
        IReadOnlyList<ObservedHidEvent> expected)
    {
        var marker = observer.Count;
        emitter.Emit(edges);
        observer.WaitFor(expected, marker, TimeSpan.FromSeconds(2));
    }

    private static void EmitAndWaitGroups(
        SerialHidEmitter emitter,
        HidObservationWindow observer,
        IReadOnlyList<MappedOutputEdge> edges,
        IReadOnlyList<IReadOnlyList<ObservedHidEvent>> expectedGroups)
    {
        var marker = observer.Count;
        emitter.Emit(edges);
        observer.WaitForGroups(expectedGroups, marker, TimeSpan.FromSeconds(2));
    }

    private static MappedOutputEdge Down(string output) => new(output, PhysicalInputEdge.Down);
    private static MappedOutputEdge Up(string output) => new(output, PhysicalInputEdge.Up);
    private static ObservedHidEvent Event(string kind, string edge, int code) => new(kind, edge, code, false, 0);

    private static string RequiredArgument(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        if (index < 0 || index == arguments.Length - 1 || string.IsNullOrWhiteSpace(arguments[index + 1]))
        {
            throw new ArgumentException($"{name} is required.");
        }
        return arguments[index + 1];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

internal static class PnpDevicePresence
{
    private const uint CrSuccess = 0;

    public static bool IsPresent(string deviceInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        return CmLocateDevNode(out _, deviceInstanceId, 0) == CrSuccess;
    }

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode)]
    private static extern uint CmLocateDevNode(out uint deviceInstance, string deviceInstanceId, uint flags);
}

internal sealed class ProbeSerialPortFrameExchange : ISerialHidFrameExchange
{
    private readonly SerialPort _port;
    private bool _disposed;

    public ProbeSerialPortFrameExchange(string portName)
    {
        _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = false,
            ReadBufferSize = 256,
            WriteBufferSize = 256,
        };
        try
        {
            _port.Open();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _port.Dispose();
            throw new SerialHidTransportException($"serial port {portName} を開けませんでした。", exception);
        }
    }

    public byte[] Exchange(ReadOnlyMemory<byte> requestFrame, TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        var timeoutMilliseconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds));
        try
        {
            _port.WriteTimeout = timeoutMilliseconds;
            var request = requestFrame.ToArray();
            _port.Write(request, 0, request.Length);
            return ReadFrame(timeout);
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new SerialHidTransportException("serial frameのwrite/readに失敗しました。", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _port.Dispose();
    }

    private byte[] ReadFrame(TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        var bytes = new List<byte>(SerialHidProtocolV1.HeaderLength + SerialHidProtocolV1.MaximumPayloadLength + SerialHidProtocolV1.CrcLength);
        while (true)
        {
            var value = ReadByte(clock, timeout);
            if (bytes.Count == 0 && value != SerialHidProtocolV1.Magic0)
            {
                continue;
            }
            if (bytes.Count == 1 && value != SerialHidProtocolV1.Magic1)
            {
                bytes.Clear();
                if (value == SerialHidProtocolV1.Magic0)
                {
                    bytes.Add(value);
                }
                continue;
            }
            bytes.Add(value);
            if (bytes.Count < SerialHidProtocolV1.HeaderLength)
            {
                continue;
            }
            var payloadLength = bytes[6] | bytes[7] << 8;
            if (payloadLength > SerialHidProtocolV1.MaximumPayloadLength)
            {
                throw new SerialHidProtocolException(
                    SerialHidFaultCode.LengthMismatch,
                    (ushort)(bytes[4] | bytes[5] << 8),
                    bytes[3],
                    "response payload lengthが上限を超えました。");
            }
            var frameLength = SerialHidProtocolV1.HeaderLength + payloadLength + SerialHidProtocolV1.CrcLength;
            if (bytes.Count == frameLength)
            {
                return bytes.ToArray();
            }
        }
    }

    private byte ReadByte(Stopwatch clock, TimeSpan timeout)
    {
        var remaining = timeout - clock.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException("serial response frameの期限を超えました。");
        }
        _port.ReadTimeout = Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds));
        var value = _port.ReadByte();
        if (value < 0)
        {
            throw new SerialHidTransportException("serial portがresponse frameの途中で閉じました。");
        }
        return (byte)value;
    }
}

internal sealed class HidObservationWindow : IDisposable
{
    private const uint WmClose = 0x0010;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmMButtonUp = 0x0208;
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsVisible = 0x10000000;
    private readonly ConcurrentQueue<ObservedHidEvent> _events = new();
    private readonly ConcurrentQueue<ObservedHidEvent> _injectedEvents = new();
    private readonly AutoResetEvent _changed = new(false);
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private WndProcDelegate? _wndProc;
    private HookProcDelegate? _keyboardHookProc;
    private HookProcDelegate? _mouseHookProc;
    private IntPtr _window;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    private HidObservationWindow()
    {
        _thread = new Thread(MessagePump) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public IReadOnlyList<ObservedHidEvent> Events => _events.ToArray();
    public IReadOnlyList<ObservedHidEvent> InjectedEvents => _injectedEvents.ToArray();
    public int Count => _events.Count;

    public static HidObservationWindow Start()
    {
        var observer = new HidObservationWindow();
        observer._thread.Start();
        if (!observer._ready.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("HID observation window did not start.");
        }
        return observer;
    }

    public void Clear()
    {
        while (_events.TryDequeue(out _)) { }
        while (_injectedEvents.TryDequeue(out _)) { }
    }

    public void WaitFor(IReadOnlyList<ObservedHidEvent> expected, int fromIndex, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var actual = Events.Skip(fromIndex).ToArray();
            if (ContainsInOrder(actual, expected))
            {
                return;
            }
            _changed.WaitOne(TimeSpan.FromMilliseconds(20));
        }
        throw new TimeoutException($"Expected HID events were not observed: {string.Join(", ", expected)}");
    }

    public void WaitForGroups(
        IReadOnlyList<IReadOnlyList<ObservedHidEvent>> expectedGroups,
        int fromIndex,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ContainsGroupsInOrder(Events.Skip(fromIndex).ToArray(), expectedGroups))
            {
                return;
            }
            _changed.WaitOne(TimeSpan.FromMilliseconds(20));
        }
        throw new TimeoutException($"Expected HID checkpoint groups were not observed: {JsonSerializer.Serialize(expectedGroups)}");
    }

    public void WaitForGroups(
        IReadOnlyList<IReadOnlyList<ObservedHidEvent>> expectedGroups,
        int fromIndex,
        Func<Exception?> failureProvider)
    {
        while (true)
        {
            if (ContainsGroupsInOrder(Events.Skip(fromIndex).ToArray(), expectedGroups))
            {
                return;
            }
            var failure = failureProvider();
            if (failure is not null)
            {
                throw new InvalidOperationException(
                    $"Host fault before expected HID checkpoint groups were observed: {failure.GetType().Name}: {failure.Message}",
                    failure);
            }
            _changed.WaitOne(TimeSpan.FromMilliseconds(20));
        }
    }

    public void Dispose()
    {
        if (_window != IntPtr.Zero)
        {
            PostMessage(_window, WmClose, IntPtr.Zero, IntPtr.Zero);
        }
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
        _changed.Dispose();
    }

    private static bool ContainsInOrder(
        IReadOnlyList<ObservedHidEvent> actual,
        IReadOnlyList<ObservedHidEvent> expected)
    {
        var cursor = 0;
        foreach (var item in actual)
        {
            if (cursor < expected.Count
                && item.Kind == expected[cursor].Kind
                && item.Edge == expected[cursor].Edge
                && item.Code == expected[cursor].Code)
            {
                cursor++;
            }
        }
        return cursor == expected.Count;
    }

    private static bool ContainsGroupsInOrder(
        IReadOnlyList<ObservedHidEvent> actual,
        IReadOnlyList<IReadOnlyList<ObservedHidEvent>> groups)
    {
        var actualIndex = 0;
        foreach (var group in groups)
        {
            var remaining = group.ToList();
            while (actualIndex < actual.Count && remaining.Count > 0)
            {
                var current = actual[actualIndex++];
                var match = remaining.FindIndex(expected =>
                    current.Kind == expected.Kind && current.Edge == expected.Edge && current.Code == expected.Code);
                if (match >= 0)
                {
                    remaining.RemoveAt(match);
                }
            }
            if (remaining.Count > 0)
            {
                return false;
            }
        }
        return true;
    }

    private void MessagePump()
    {
        var className = $"OpenLogicoolSerialHidObserver-{Environment.ProcessId}";
        _wndProc = WindowProc;
        var windowClass = new WndClassEx
        {
            Size = Marshal.SizeOf<WndClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_wndProc),
            Instance = GetModuleHandle(null),
            ClassName = className,
        };
        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }
        _window = CreateWindowEx(0, className, "OpenLogicool Serial HID direct smoke",
            WsOverlappedWindow | WsVisible, 100, 100, 420, 180,
            IntPtr.Zero, IntPtr.Zero, windowClass.Instance, IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }
        _keyboardHookProc = KeyboardHook;
        _mouseHookProc = MouseHook;
        _keyboardHook = SetWindowsHookEx(13, _keyboardHookProc, GetModuleHandle(null), 0);
        _mouseHook = SetWindowsHookEx(14, _mouseHookProc, GetModuleHandle(null), 0);
        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        }
        SetForegroundWindow(_window);
        SetFocus(_window);
        SetCursorPos(260, 190);
        _ready.Set();
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        UnhookWindowsHookEx(_keyboardHook);
        UnhookWindowsHookEx(_mouseHook);
        GC.KeepAlive(_wndProc);
        GC.KeepAlive(_keyboardHookProc);
        GC.KeepAlive(_mouseHookProc);
    }

    private IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmClose)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (unchecked((uint)wParam) is WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp))
        {
            var data = Marshal.PtrToStructure<KeyboardHookData>(lParam);
            var edge = unchecked((uint)wParam) is WmKeyDown or WmSysKeyDown ? "down" : "up";
            var injected = (data.Flags & 0x10) != 0;
            var observed = new ObservedHidEvent("key", edge, unchecked((int)data.VirtualKey), injected, Stopwatch.GetTimestamp());
            (injected ? _injectedEvents : _events).Enqueue(observed);
            _changed.Set();
        }
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (unchecked((uint)wParam) is WmMButtonDown or WmMButtonUp))
        {
            var data = Marshal.PtrToStructure<MouseHookData>(lParam);
            var edge = unchecked((uint)wParam) == WmMButtonDown ? "down" : "up";
            var injected = (data.Flags & 0x01) != 0;
            var observed = new ObservedHidEvent("mouse", edge, 0x04, injected, Stopwatch.GetTimestamp());
            (injected ? _injectedEvents : _events).Enqueue(observed);
            _changed.Set();
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProcDelegate(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProcDelegate(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
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
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, HookProcDelegate hookProcedure, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

public sealed record ObservedHidEvent(string Kind, string Edge, int Code, bool IsInjected, long StopwatchTicks);

internal sealed class DirectSmokeResult
{
    public required string Schema { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string DeviceInstanceId { get; init; }
    public required string TransientPort { get; init; }
    public string? FirmwareVersion { get; set; }
    public string? Capabilities { get; set; }
    public ushort LeaseMilliseconds { get; set; }
    public bool HelloReady { get; set; }
    public bool KeyObserved { get; set; }
    public bool ChordObserved { get; set; }
    public bool MouseObserved { get; set; }
    public bool SequenceObserved { get; set; }
    public bool AllUpObserved { get; set; }
    public bool LeaseReleaseObserved { get; set; }
    public double LeaseReleaseElapsedMilliseconds { get; set; }
    public bool PowerCycleAttempted { get; set; }
    public bool PowerCycleDisconnectObserved { get; set; }
    public bool PowerCycleReconnectObserved { get; set; }
    public bool PowerCycleAllUpObserved { get; set; }
    public IReadOnlyList<ObservedHidEvent> PowerCycleUnexpectedDownEvents { get; set; } = [];
    public IReadOnlyList<ObservedHidEvent> Events { get; set; } = [];
    public bool Passed { get; set; }
    public string? Error { get; set; }
}
