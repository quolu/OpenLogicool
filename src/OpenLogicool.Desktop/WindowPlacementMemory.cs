using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OpenLogicool.Desktop;

/// <summary>
/// 窓の通常時の矩形（Win32 workspace 座標）と最大化の有無。
/// 画面構成が変わって矩形が画面外になった時の補正は OS（SetWindowPlacement）が行う。
/// </summary>
public sealed record WindowPlacement(int Left, int Top, int Right, int Bottom, bool Maximized);

/// <summary>
/// 窓を閉じた時の位置と大きさを覚え、次に開く時に戻す。保存先は呼び出し側（Host）が所有する。
/// </summary>
public sealed class WindowPlacementMemory
{
    private const int SwHide = 0;
    private const int SwShowMinimized = 2;
    private const int SwShowMaximized = 3;
    private const int WpfRestoreToMaximized = 0x0002;

    private readonly Func<string, WindowPlacement?> load;
    private readonly Action<string, WindowPlacement> save;

    public WindowPlacementMemory(Func<string, WindowPlacement?> load, Action<string, WindowPlacement> save)
    {
        this.load = load;
        this.save = save;
    }

    /// <summary>窓の生成時に前回の配置を戻し、閉じる時に現在の配置を保存する。<paramref name="windowKey"/> は窓の種類ごとの固定名。</summary>
    public void Attach(Window window, string windowKey)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);

        window.SourceInitialized += (_, _) =>
        {
            if (load(windowKey) is not { } placement)
            {
                return;
            }

            var native = new NativeWindowPlacement
            {
                Length = Marshal.SizeOf<NativeWindowPlacement>(),
                // 表示は WPF の Show に任せる（ここで表示すると ShowActivated=false の窓まで前面化する）。
                ShowCommand = SwHide,
                MinPosition = new NativePoint { X = -1, Y = -1 },
                MaxPosition = new NativePoint { X = -1, Y = -1 },
                NormalPosition = new NativeRect { Left = placement.Left, Top = placement.Top, Right = placement.Right, Bottom = placement.Bottom },
            };
            if (!SetWindowPlacement(new WindowInteropHelper(window).Handle, ref native))
            {
                throw new InvalidOperationException($"窓の配置を戻せません（{windowKey}）: Win32 error {Marshal.GetLastWin32Error()}");
            }

            if (placement.Maximized)
            {
                window.WindowState = WindowState.Maximized;
            }
        };

        window.Closing += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var native = new NativeWindowPlacement { Length = Marshal.SizeOf<NativeWindowPlacement>() };
            if (!GetWindowPlacement(handle, ref native))
            {
                throw new InvalidOperationException($"窓の配置を取得できません（{windowKey}）: Win32 error {Marshal.GetLastWin32Error()}");
            }

            var maximized = native.ShowCommand == SwShowMaximized
                || (native.ShowCommand == SwShowMinimized && (native.Flags & WpfRestoreToMaximized) != 0);
            save(windowKey, new WindowPlacement(
                native.NormalPosition.Left,
                native.NormalPosition.Top,
                native.NormalPosition.Right,
                native.NormalPosition.Bottom,
                maximized));
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref NativeWindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref NativeWindowPlacement placement);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect NormalPosition;
    }
}
