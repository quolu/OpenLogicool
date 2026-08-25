using System.Runtime.InteropServices;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

/// <summary>Windows cursor readbackだけを所有するNano pointer用OS adapter。</summary>
public sealed class WindowsSerialHidCursorOracle : ISerialHidCursorOracle
{
    public SerialHidCursorPoint ReadCurrent()
    {
        if (!GetCursorPos(out var point))
        {
            throw new InvalidOperationException($"GetCursorPos failed: {Marshal.GetLastWin32Error()}");
        }
        return new SerialHidCursorPoint(point.X, point.Y);
    }

    public SerialHidCursorPoint ReadAfterDelta(SerialHidCursorPoint previous)
    {
        Thread.Sleep(8);
        return ReadCurrent();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
