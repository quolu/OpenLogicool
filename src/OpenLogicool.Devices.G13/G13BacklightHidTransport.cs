using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenLogicool.Devices.G13;

public interface IG13BacklightTransport : IDisposable
{
    /// <summary>G13 が接続されていれば開く（開いていれば何もしない）。接続されていなければ false。</summary>
    bool TryOpen();

    G13BacklightColor Read();

    void Write(G13BacklightColor color);

    void Close();
}

/// <summary>
/// G13 のバックライト色を Windows 標準 HID の feature report 7 で読み書きする。
/// 色は本体に保存されず、抜き挿しで電源投入時の色へ戻る
/// （実測は evidence/g13-backlight/p1-standard-hid-feature-write-gate.md）。
/// </summary>
public sealed class G13BacklightHidTransport : IG13BacklightTransport
{
    private const byte ColorReportId = 7;
    private const uint GenericReadWrite = 0xC000_0000;
    private const uint FileShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    private readonly G13LcdHidAccess access = new();
    private IntPtr file = InvalidHandleValue;
    private byte[] report = [];

    public bool TryOpen()
    {
        if (file != InvalidHandleValue)
        {
            return true;
        }

        var candidates = access.EnumerateCollections()
            .Where(collection => collection.FeatureReportByteLength > 0)
            .ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        if (candidates.Length > 1)
        {
            throw new InvalidOperationException(
                $"G13にfeature reportを持つcollectionが{candidates.Length}件あり、一意に選べません。");
        }

        var opened = CreateFile(
            candidates[0].DevicePath, GenericReadWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (opened == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "G13をバックライト色の書き込み用に開けませんでした。");
        }

        file = opened;
        report = new byte[candidates[0].FeatureReportByteLength];
        return true;
    }

    public G13BacklightColor Read()
    {
        EnsureOpen();
        Array.Clear(report);
        report[0] = ColorReportId;
        if (!HidD_GetFeature(file, report, report.Length))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "G13のバックライト色を読めませんでした。");
        }

        return new G13BacklightColor(report[1], report[2], report[3]);
    }

    public void Write(G13BacklightColor color)
    {
        EnsureOpen();
        Array.Clear(report);
        report[0] = ColorReportId;
        report[1] = color.Red;
        report[2] = color.Green;
        report[3] = color.Blue;
        if (!HidD_SetFeature(file, report, report.Length))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "G13のバックライト色を書けませんでした。");
        }
    }

    public void Close()
    {
        if (file != InvalidHandleValue)
        {
            CloseHandle(file);
            file = InvalidHandleValue;
        }
    }

    public void Dispose() => Close();

    private void EnsureOpen()
    {
        if (file == InvalidHandleValue)
        {
            throw new InvalidOperationException("G13のバックライト色のhandleが開かれていません。");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);
}
