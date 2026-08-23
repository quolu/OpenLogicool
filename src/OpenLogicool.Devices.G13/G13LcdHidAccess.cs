using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenLogicool.Contracts.Devices.G13;

namespace OpenLogicool.Devices.G13;

public sealed record G13HidCollectionInfo(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    ushort InputReportByteLength,
    ushort OutputReportByteLength,
    ushort FeatureReportByteLength);

/// <summary>Windows標準HID stackからG13 LCD output collectionを列挙し、WriteFileでframeを送る。</summary>
public sealed class G13LcdHidAccess
{
    public IReadOnlyList<G13HidCollectionInfo> EnumerateCollections()
    {
        Native.HidD_GetHidGuid(out var hidGuid);
        var deviceInfo = Native.SetupDiGetClassDevs(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            Native.DigcfPresent | Native.DigcfDeviceInterface);
        if (deviceInfo == Native.InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HID device interface一覧を取得できませんでした。");
        }

        try
        {
            var result = new List<G13HidCollectionInfo>();
            for (var index = 0; ; index++)
            {
                var iface = new Native.SpDeviceInterfaceData { CbSize = Marshal.SizeOf<Native.SpDeviceInterfaceData>() };
                if (!Native.SetupDiEnumDeviceInterfaces(deviceInfo, IntPtr.Zero, ref hidGuid, index, ref iface))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == Native.ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "HID device interfaceの列挙に失敗しました。");
                }

                var path = GetInterfacePath(deviceInfo, ref iface);
                var collection = TryReadG13Collection(path);
                if (collection is not null)
                {
                    result.Add(collection);
                }
            }

            return result;
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(deviceInfo);
        }
    }

    public G13LcdWriteHandle Open()
    {
        var collection = SelectOutputCollection(EnumerateCollections());
        var file = Native.CreateFile(
            collection.DevicePath,
            Native.GenericRead | Native.GenericWrite,
            Native.FileShareRead | Native.FileShareWrite,
            IntPtr.Zero,
            Native.OpenExisting,
            0,
            IntPtr.Zero);
        if (file == Native.InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "G13 LCD output collectionをwrite用に開けませんでした。");
        }

        return new G13LcdWriteHandle(file, collection);
    }

    public static G13HidCollectionInfo SelectOutputCollection(IEnumerable<G13HidCollectionInfo> collections)
    {
        var candidates = collections
            .Where(collection => collection.OutputReportByteLength == G13LcdFrame.ReportLength)
            .ToArray();

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"G13に{G13LcdFrame.ReportLength}-byte output collectionがありません。"),
            _ => throw new InvalidOperationException(
                $"G13に{G13LcdFrame.ReportLength}-byte output collectionが{candidates.Length}件あり、一意に選べません。"),
        };
    }

    private static string GetInterfacePath(IntPtr deviceInfo, ref Native.SpDeviceInterfaceData iface)
    {
        uint required = 0;
        Native.SetupDiGetDeviceInterfaceDetail(deviceInfo, ref iface, IntPtr.Zero, 0, ref required, IntPtr.Zero);
        if (required == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HID device interface path長を取得できませんでした。");
        }

        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!Native.SetupDiGetDeviceInterfaceDetail(
                    deviceInfo,
                    ref iface,
                    buffer,
                    required,
                    ref required,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "HID device interface pathを取得できませんでした。");
            }

            return Marshal.PtrToStringUni(buffer + 4)
                   ?? throw new InvalidOperationException("HID device interface pathがnullでした。");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static G13HidCollectionInfo? TryReadG13Collection(string path)
    {
        var file = Native.CreateFile(
            path,
            0,
            Native.FileShareRead | Native.FileShareWrite,
            IntPtr.Zero,
            Native.OpenExisting,
            0,
            IntPtr.Zero);
        if (file == Native.InvalidHandleValue)
        {
            return null;
        }

        try
        {
            var attributes = new Native.HiddAttributes { Size = Marshal.SizeOf<Native.HiddAttributes>() };
            if (!Native.HidD_GetAttributes(file, ref attributes) ||
                attributes.VendorId != G13DeviceIdentity.VendorId ||
                attributes.ProductId != G13DeviceIdentity.ProductId)
            {
                return null;
            }

            if (!Native.HidD_GetPreparsedData(file, out var preparsed) || preparsed == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "G13 HID preparsed dataを取得できませんでした。");
            }

            try
            {
                if (Native.HidP_GetCaps(preparsed, out var caps) != Native.HidPStatusSuccess)
                {
                    throw new InvalidOperationException("G13 HID collection capsを取得できませんでした。");
                }

                return new G13HidCollectionInfo(
                    path,
                    attributes.VendorId,
                    attributes.ProductId,
                    caps.UsagePage,
                    caps.Usage,
                    caps.InputReportByteLength,
                    caps.OutputReportByteLength,
                    caps.FeatureReportByteLength);
            }
            finally
            {
                Native.HidD_FreePreparsedData(preparsed);
            }
        }
        finally
        {
            Native.CloseHandle(file);
        }
    }

    public sealed class G13LcdWriteHandle : IDisposable
    {
        private IntPtr file;

        internal G13LcdWriteHandle(IntPtr file, G13HidCollectionInfo collection)
        {
            this.file = file;
            Collection = collection;
        }

        public G13HidCollectionInfo Collection { get; }

        public int Write(ReadOnlySpan<byte> report)
        {
            if (report.Length != G13LcdFrame.ReportLength || report[0] != G13LcdFrame.ReportId)
            {
                throw new ArgumentException("G13 LCD wire reportの形状が不正です。", nameof(report));
            }

            var buffer = report.ToArray();
            if (!Native.WriteFile(file, buffer, buffer.Length, out var written, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "G13 LCD frameのWriteFileに失敗しました。");
            }

            if (written != buffer.Length)
            {
                throw new IOException($"G13 LCD frameが途中までしか書かれませんでした。{written}/{buffer.Length} bytes");
            }

            return written;
        }

        /// <summary>
        /// WriteFileとの差を判別する実験専用。製品runtimeからの自動fallbackには使わない。
        /// Microsoftが機器無応答の可能性を警告するため、実機probeの明示指定時だけ呼ぶ。
        /// </summary>
        public int SetOutputReportForExperiment(ReadOnlySpan<byte> report)
        {
            if (report.Length != G13LcdFrame.ReportLength || report[0] != G13LcdFrame.ReportId)
            {
                throw new ArgumentException("G13 LCD wire reportの形状が不正です。", nameof(report));
            }

            var buffer = report.ToArray();
            if (!Native.HidD_SetOutputReport(file, buffer, buffer.Length))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "G13 LCD frameのHidD_SetOutputReportに失敗しました。");
            }

            return buffer.Length;
        }

        public void Dispose()
        {
            if (file != IntPtr.Zero && file != Native.InvalidHandleValue)
            {
                Native.CloseHandle(file);
                file = IntPtr.Zero;
            }
        }
    }

    private static class Native
    {
        public const uint GenericRead = 0x80000000;
        public const uint GenericWrite = 0x40000000;
        public const uint FileShareRead = 1;
        public const uint FileShareWrite = 2;
        public const uint OpenExisting = 3;
        public const uint DigcfPresent = 2;
        public const uint DigcfDeviceInterface = 0x10;
        public const int ErrorNoMoreItems = 259;
        public const int HidPStatusSuccess = 0x0011_0000;
        public static readonly IntPtr InvalidHandleValue = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SpDeviceInterfaceData
        {
            public int CbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HiddAttributes
        {
            public int Size;
            public ushort VendorId;
            public ushort ProductId;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Explicit, Size = 64)]
        public struct HidpCaps
        {
            [FieldOffset(0)] public ushort Usage;
            [FieldOffset(2)] public ushort UsagePage;
            [FieldOffset(4)] public ushort InputReportByteLength;
            [FieldOffset(6)] public ushort OutputReportByteLength;
            [FieldOffset(8)] public ushort FeatureReportByteLength;
        }

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HiddAttributes attributes);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_SetOutputReport(
            IntPtr hidDeviceObject,
            byte[] reportBuffer,
            int reportBufferLength);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            int memberIndex,
            ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            ref uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteFile(
            IntPtr file,
            byte[] buffer,
            int numberOfBytesToWrite,
            out int numberOfBytesWritten,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
