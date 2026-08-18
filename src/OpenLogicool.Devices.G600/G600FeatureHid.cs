using System.Runtime.InteropServices;
using OpenLogicool.Contracts.Devices.G600;

namespace OpenLogicool.Devices.G600;

/// <summary>
/// G600 vendor feature collection への fresh open。HidSharp は使わない（Devices.G600 は package 禁止）。
/// probe と同じ条件: VID/PID 一致かつ feature report 長が 154 以上。
/// </summary>
public sealed class G600FeatureHidAccess : IG600FeatureAccess
{
    public bool TryOpen(out IG600FeatureHandle? handle)
    {
        handle = null;
        var path = FindFeatureDevicePath();
        if (path is null)
        {
            return false;
        }

        var file = Native.CreateFile(
            path,
            Native.GenericRead | Native.GenericWrite,
            Native.FileShareRead | Native.FileShareWrite,
            IntPtr.Zero,
            Native.OpenExisting,
            0,
            IntPtr.Zero);
        if (file == Native.InvalidHandleValue)
        {
            return false;
        }

        handle = new HidFeatureHandle(file);
        return true;
    }

    public static bool IsPresent() => FindFeatureDevicePath() is not null;

    private static string? FindFeatureDevicePath()
    {
        Native.HidD_GetHidGuid(out var hidGuid);
        var deviceInfo = Native.SetupDiGetClassDevs(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            Native.DigcfPresent | Native.DigcfDeviceInterface);
        if (deviceInfo == Native.InvalidHandleValue)
        {
            return null;
        }

        try
        {
            var index = 0;
            while (true)
            {
                var iface = new Native.SpDeviceInterfaceData { CbSize = Marshal.SizeOf<Native.SpDeviceInterfaceData>() };
                if (!Native.SetupDiEnumDeviceInterfaces(deviceInfo, IntPtr.Zero, ref hidGuid, index, ref iface))
                {
                    break;
                }

                index++;
                if (!TryGetInterfacePath(deviceInfo, ref iface, out var path) || path is null)
                {
                    continue;
                }

                if (IsG600FeatureCollection(path))
                {
                    return path;
                }
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(deviceInfo);
        }

        return null;
    }

    private static bool TryGetInterfacePath(
        IntPtr deviceInfo,
        ref Native.SpDeviceInterfaceData iface,
        out string? path)
    {
        path = null;
        uint required = 0;
        Native.SetupDiGetDeviceInterfaceDetail(deviceInfo, ref iface, IntPtr.Zero, 0, ref required, IntPtr.Zero);
        if (required == 0)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!Native.SetupDiGetDeviceInterfaceDetail(deviceInfo, ref iface, buffer, required, ref required, IntPtr.Zero))
            {
                return false;
            }

            path = Marshal.PtrToStringUni(buffer + 4);
            return path is not null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool IsG600FeatureCollection(string path)
    {
        var file = Native.CreateFile(
            path,
            Native.GenericRead | Native.GenericWrite,
            Native.FileShareRead | Native.FileShareWrite,
            IntPtr.Zero,
            Native.OpenExisting,
            0,
            IntPtr.Zero);
        if (file == Native.InvalidHandleValue)
        {
            return false;
        }

        try
        {
            var attributes = new Native.HiddAttributes { Size = Marshal.SizeOf<Native.HiddAttributes>() };
            if (!Native.HidD_GetAttributes(file, ref attributes))
            {
                return false;
            }

            if (attributes.VendorId != G600DeviceIdentity.VendorId ||
                attributes.ProductId != G600DeviceIdentity.ProductId)
            {
                return false;
            }

            if (!Native.HidD_GetPreparsedData(file, out var preparsed) || preparsed == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (Native.HidP_GetCaps(preparsed, out var caps) != Native.HidPStatusSuccess)
                {
                    return false;
                }

                return caps.FeatureReportByteLength >= G600SideRemap.ReportLength;
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

    private sealed class HidFeatureHandle : IG600FeatureHandle
    {
        private IntPtr _file;

        public HidFeatureHandle(IntPtr file) => _file = file;

        public void SetFeature(byte[] report)
        {
            G600EvidenceWrite.EnsureWritableProfile(report);
            if (!Native.HidD_SetFeature(_file, report, report.Length))
            {
                throw new InvalidOperationException($"HidD_SetFeature 0x{report[0]:X2} failed: {Marshal.GetLastWin32Error()}");
            }
        }

        public byte[] GetFeature(byte reportId)
        {
            var buffer = new byte[G600SideRemap.ReportLength];
            buffer[0] = reportId;
            if (!Native.HidD_GetFeature(_file, buffer, buffer.Length))
            {
                throw new InvalidOperationException($"HidD_GetFeature 0x{reportId:X2} failed: {Marshal.GetLastWin32Error()}");
            }

            return buffer;
        }

        public void Dispose()
        {
            if (_file != IntPtr.Zero && _file != Native.InvalidHandleValue)
            {
                Native.CloseHandle(_file);
                _file = IntPtr.Zero;
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
            [FieldOffset(8)] public ushort FeatureReportByteLength;
        }

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HiddAttributes attributes);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            int memberIndex,
            ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            ref uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
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
        public static extern bool CloseHandle(IntPtr handle);
    }
}
