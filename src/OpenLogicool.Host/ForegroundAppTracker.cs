using System.Runtime.InteropServices;
using System.Text;
using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// foreground window の process identity 取得（app-first 切替の観測点・APP-004）。
/// 取得できなかった要素は null を返し、呼び出し側（AppProfileResolver）はそれを
/// 「識別不能」として既定 profile へ解決する。ここでは推測・丸めをしない。
/// </summary>
public static class ForegroundAppTracker
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInsufficientBuffer = 122;

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

        return GetProcessFullPath(processId);
    }

    /// <summary>foreground window の process の観測 identity（取得不能な window は null）。</summary>
    public static ForegroundApplicationIdentity? GetForegroundIdentity()
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

        return GetIdentity(processId);
    }

    /// <summary>process ID から観測 identity を取得する（handle が開けない場合は全要素 null）。</summary>
    public static ForegroundApplicationIdentity GetIdentity(uint processId)
    {
        var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return new ForegroundApplicationIdentity(null, null, (int)processId, null);
        }

        try
        {
            var fullPath = QueryFullPathFromHandle(processHandle);
            var normalizedFullPath = fullPath is null ? null : AppProfileResolver.NormalizePath(fullPath);
            var packageFamilyName = QueryPackageFamilyName(processHandle);
            var startTimeUtc = QueryProcessStartTimeUtc(processHandle);
            return new ForegroundApplicationIdentity(normalizedFullPath, packageFamilyName, (int)processId, startTimeUtc);
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    /// <summary>process ID から EXE full path を取得する（取得不能は null）。</summary>
    public static string? GetProcessFullPath(uint processId)
    {
        var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return QueryFullPathFromHandle(processHandle);
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    /// <summary>process ID から package family name を取得する（非 package app・取得不能は null）。</summary>
    public static string? GetPackageFamilyName(uint processId)
    {
        var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return QueryPackageFamilyName(processHandle);
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    private static string? QueryFullPathFromHandle(IntPtr processHandle)
    {
        var buffer = new StringBuilder(1024);
        var size = buffer.Capacity;
        return QueryFullProcessImageName(processHandle, 0, buffer, ref size)
            ? buffer.ToString(0, size)
            : null;
    }

    /// <summary>
    /// GetPackageFamilyName（kernel32）は length=0 の1回目呼び出しで必要 buffer 長を返す2回呼び出しパターン。
    /// 非 package app は APPMODEL_ERROR_NO_PACKAGE(15700) を返す＝null で表す。
    /// </summary>
    private static string? QueryPackageFamilyName(IntPtr processHandle)
    {
        uint length = 0;
        var probeResult = GetPackageFamilyName(processHandle, ref length, null!);
        if (probeResult != ErrorInsufficientBuffer || length == 0)
        {
            return null;
        }

        var buffer = new StringBuilder((int)length);
        var result = GetPackageFamilyName(processHandle, ref length, buffer);
        return result == 0 ? buffer.ToString() : null;
    }

    private static DateTime? QueryProcessStartTimeUtc(IntPtr processHandle)
    {
        if (!GetProcessTimes(processHandle, out var creationTime, out _, out _, out _))
        {
            return null;
        }

        try
        {
            return DateTime.FromFileTimeUtc(creationTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// 前面 window のタイトル（取得不能・空・自 process の window は null）。ヘッダ表示の追従用で、
    /// 自 process を除くのは「いまゲームに届いている割当」が編集画面自身を指しても意味がないため。
    /// </summary>
    public static string? GetForegroundWindowTitle()
    {
        var windowHandle = GetForegroundWindow();
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == (uint)Environment.ProcessId)
        {
            return null;
        }

        var buffer = new StringBuilder(512);
        return GetWindowText(windowHandle, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxLength);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(IntPtr hProcess, ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);

    [DllImport("kernel32.dll")]
    private static extern bool GetProcessTimes(
        IntPtr hProcess, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);
}
