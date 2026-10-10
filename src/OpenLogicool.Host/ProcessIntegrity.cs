using System.Runtime.InteropServices;

namespace OpenLogicool.Host;

/// <summary>
/// processの整合性の水準（権限の高さ）を読む。Windowsは、高い水準の窓が前面の間、低い水準のprocessへ
/// キーボードとマウスの入力を渡さない。
/// </summary>
internal static class ProcessIntegrity
{
    /// <summary>管理者として実行したprocessの水準。</summary>
    public const int High = 0x3000;

    public static int Current() => Level(Environment.ProcessId)
        ?? throw new InvalidOperationException("自processの整合性の水準を読めません。");

    /// <summary>水準を返す。対象を開けない・読めない時はnull。</summary>
    public static int? Level(int processId)
    {
        var process = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0) return null;
        try
        {
            if (!OpenProcessToken(process, 8, out var token)) return null; // TOKEN_QUERY
            try
            {
                _ = GetTokenInformation(token, 25, 0, 0, out var size); // TokenIntegrityLevel
                if (size == 0) return null;
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(token, 25, buffer, size, out _)) return null;
                    var sid = Marshal.ReadIntPtr(buffer);
                    return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(Marshal.ReadByte(GetSidSubAuthorityCount(sid)) - 1)));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    /// <summary>対象が自分より高い水準か、対象の水準を読めない時は、管理者権限の監視が要る。</summary>
    public static bool NeedsElevatedWatch(int? target, int own) => target is null || target > own;

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(nint token, int kind, nint buffer, int length, out int size);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthority(nint sid, uint index);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthorityCount(nint sid);
}
