using System.Runtime.InteropServices;

namespace OpenLogicool.Host;

/// <summary>対象game windowを一回だけ前面化して結果を確認するOS adapter。</summary>
public static class WindowsGameWindowActivator
{
    public static void Activate(nint window)
    {
        if (window == nint.Zero)
        {
            throw new ArgumentException("target windowが必要です。", nameof(window));
        }
        if (GetForegroundWindow() == window)
        {
            return;
        }
        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = foreground == nint.Zero
            ? 0u
            : GetWindowThreadProcessId(foreground, nint.Zero);
        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = BringWindowToTop(window);
            _ = SetForegroundWindow(window);
            Thread.Sleep(50);
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
        if (GetForegroundWindow() != window)
        {
            throw new InvalidOperationException("target game windowを一回のSetForegroundWindowで前面化できませんでした。");
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, nint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);
}
