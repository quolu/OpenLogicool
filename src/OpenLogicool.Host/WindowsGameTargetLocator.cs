using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenLogicool.Host;

public sealed record WindowsGameTarget(
    nint Window,
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    GameCaptureScreenBounds Bounds);

public static class WindowsGameTargetLocator
{
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
            new GameCaptureScreenBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
}
