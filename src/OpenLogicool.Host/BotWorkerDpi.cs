using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenLogicool.Host;

internal static class BotWorkerDpi
{
    // Windowsの既知のDPI contextだけを渡し、process固有のhandleを通信に載せない。
    public static int Context() => new[] { -1, -2, -3, -4, -5 }
        .First(context => AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), context));
    public static string Snapshot() => $"{Context()}:{GetDpiForSystem()}";
    public static void Configure(int context)
    {
        if (context is < -5 or > -1) throw new ArgumentException("未知のDPI contextです。");
        if (!SetProcessDpiAwarenessContext(context))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "BotのDPIを本体に合わせられませんでした。");
    }

    [DllImport("user32.dll")]
    private static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint context);
}
