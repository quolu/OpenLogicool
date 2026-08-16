using System.Runtime.InteropServices;
using System.Text;

namespace OpenLogicool.Host;

/// <summary>
/// foreground window の process EXE full path の取得（app-first 切替の観測点）。
/// 取得不能（window なし・process 終了・アクセス拒否）は null を返し、呼び出し側は
/// 「識別不能 app」として既定 profile を適用する（AppProfileResolver の規則どおり）。
/// </summary>
public static class ForegroundAppTracker
{
    public static string? GetForegroundProcessFullPath()
    {
        var windowHandle = GetForegroundWindow();
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == 0)
        {
            return null;
        }

        var processHandle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(processHandle, 0, buffer, ref size)
                ? buffer.ToString(0, size)
                : null;
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);
}
