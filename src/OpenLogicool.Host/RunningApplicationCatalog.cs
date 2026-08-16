using System.Runtime.InteropServices;
using System.Text;

namespace OpenLogicool.Host;

/// <summary>
/// 実行中 app の1件（可視 window を持つ process）。FullPath は実 path のまま（表示用）。
/// PackageFamilyName は MSIX/Store app のみ非 null（APP-004・package matcher の選択材料）。
/// </summary>
public sealed record RunningApplication(string FullPath, string WindowTitle, string? PackageFamilyName);

/// <summary>
/// 実行中 application の一覧（Journey B「実行中一覧から app を選ぶ」の選択ソース）。
/// 可視 top-level window を持つ process の EXE full path を、正規化 path で dedupe して返す。
/// 手打ち path は Store app redirect（例: Windows 11 の notepad.exe）で一致しない罠があるため、
/// 関連付けの path は必ずこの一覧（実行中 process からの取得値）を正とする。
/// </summary>
public static class RunningApplicationCatalog
{
    /// <summary>可視 window を持つ実行中 app を path 昇順で返す（自 process は除く）。</summary>
    public static IReadOnlyList<RunningApplication> ListVisibleApplications()
    {
        var byNormalizedPath = new SortedDictionary<string, RunningApplication>(StringComparer.Ordinal);
        var selfProcessId = (uint)Environment.ProcessId;

        EnumWindows((windowHandle, lparam) =>
        {
            if (!IsWindowVisible(windowHandle))
            {
                return true;
            }

            var titleLength = GetWindowTextLength(windowHandle);
            if (titleLength == 0)
            {
                return true;
            }

            _ = GetWindowThreadProcessId(windowHandle, out var processId);
            if (processId == 0 || processId == selfProcessId)
            {
                return true;
            }

            var fullPath = ForegroundAppTracker.GetProcessFullPath(processId);
            if (fullPath is null)
            {
                return true;
            }

            var normalized = AppProfileResolver.NormalizePath(fullPath);
            if (!byNormalizedPath.ContainsKey(normalized))
            {
                var title = new StringBuilder(titleLength + 1);
                _ = GetWindowText(windowHandle, title, title.Capacity);
                var packageFamilyName = ForegroundAppTracker.GetPackageFamilyName(processId);
                byNormalizedPath[normalized] = new RunningApplication(fullPath, title.ToString(), packageFamilyName);
            }

            return true;
        }, IntPtr.Zero);

        return byNormalizedPath.Values.ToList();
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
