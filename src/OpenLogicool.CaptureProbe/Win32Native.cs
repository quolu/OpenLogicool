using System.Runtime.InteropServices;
using System.Text;

namespace OpenLogicool.CaptureProbe;

internal static class Win32Native
{
    private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static IntPtr GetPrimaryMonitor() => MonitorFromWindow(GetDesktopWindow(), MONITOR_DEFAULTTOPRIMARY);

    // タイトル部分一致（大文字小文字を無視）で最初に見つかった可視 top-level window を返す。
    public static (IntPtr Hwnd, string Title)? FindWindowByTitleSubstring(string titleSubstring)
    {
        (IntPtr Hwnd, string Title)? found = null;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var length = GetWindowTextLength(hwnd);
            if (length == 0)
                return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
            {
                found = (hwnd, title);
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }
}
