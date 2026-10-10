using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace OpenLogicool.Desktop.Tests;

/// <summary>
/// 窓を本当に表示する必要があるテストを、利用者に見えない別のデスクトップで走らせる。
/// 利用者の画面へ窓を出さず、前面の窓（ゲーム）からフォーカスも奪わない。
/// </summary>
internal static class HiddenDesktop
{
    private const uint GenericAll = 0x10000000;
    private const uint Infinite = 0xFFFFFFFF;

    public static void RunSta(Action action)
    {
        var desktop = CreateDesktop($"OpenLogicoolTests-{Guid.NewGuid():N}", null, IntPtr.Zero, 0, GenericAll, IntPtr.Zero);
        if (desktop == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "テスト用のデスクトップを作れません。");
        }

        try
        {
            Exception? failure = null;
            // .NET の Thread は開始時に COM を初期化し、STA なら見えない窓を作る。窓を持つ thread は
            // デスクトップを替えられないため、OS の thread を直接作り、切り替えた後で STA にする。
            ThreadProc body = _ =>
            {
                try
                {
                    if (!SetThreadDesktop(desktop))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "テスト用のデスクトップへ切り替えられません。");
                    }

                    if (!Thread.CurrentThread.TrySetApartmentState(ApartmentState.STA))
                    {
                        throw new InvalidOperationException("テスト用の thread を STA にできません。");
                    }

                    action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    // この thread が作った窓を、デスクトップを閉じる前に全部片づける。
                    Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown();
                }

                return 0;
            };

            var handle = CreateThread(IntPtr.Zero, UIntPtr.Zero, body, IntPtr.Zero, 0, out _);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "テスト用の thread を作れません。");
            }

            _ = WaitForSingleObject(handle, Infinite);
            CloseHandle(handle);
            GC.KeepAlive(body);
            if (failure is not null)
            {
                throw new Xunit.Sdk.XunitException(failure.ToString());
            }
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ThreadProc(IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, string? device, IntPtr deviceMode, uint flags, uint access, IntPtr security);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadDesktop(IntPtr desktop);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateThread(IntPtr attributes, UIntPtr stackSize, ThreadProc start, IntPtr parameter, uint flags, out uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
