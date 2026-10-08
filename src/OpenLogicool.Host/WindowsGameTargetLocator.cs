using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Host;

public sealed record WindowsGameTarget(
    nint Window,
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    GameCaptureScreenBounds Bounds,
    string ExecutablePath);

public static class WindowsGameTargetLocator
{
    public static FrameRect CaptureClientBounds(nint window)
    {
        var origin = new NativePoint();
        if (!GetClientRect(window, out var client) || !ClientToScreen(window, ref origin))
            throw new InvalidOperationException($"ゲームの描画領域を取得できません: {Marshal.GetLastWin32Error()}");
        var result = DwmGetWindowAttribute(window, 9, out var visible, Marshal.SizeOf<NativeRect>());
        Marshal.ThrowExceptionForHR(result);
        return new(origin.X - visible.Left, origin.Y - visible.Top, client.Right, client.Bottom);
    }

    public static WindowsGameTarget Locate(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        var matches = Process.GetProcessesByName(processName)
            .Where(process => process.MainWindowHandle != IntPtr.Zero)
            .ToArray();
        if (matches.Length != 1)
        {
            foreach (var process in matches) process.Dispose();
            throw new InvalidOperationException($"対象window '{processName}' は{matches.Length}件です。");
        }
        using var selected = matches[0];
        if (!GetWindowRect(selected.MainWindowHandle, out var rect))
        {
            throw new InvalidOperationException($"GetWindowRect failed: {Marshal.GetLastWin32Error()}");
        }
        return new WindowsGameTarget(
            selected.MainWindowHandle,
            selected.Id,
            selected.ProcessName,
            selected.MainWindowTitle,
            new GameCaptureScreenBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
            ForegroundAppTracker.GetProcessFullPath(checked((uint)selected.Id))
                ?? throw new InvalidOperationException("ゲーム本体のEXEパスを取得できませんでした。"));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect rect, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
}
