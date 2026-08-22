using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace OpenLogicool.Host;

public sealed record SerialHidCandidate(
    string DeviceInstanceId,
    string PortName,
    string DeviceInterfacePath,
    ushort VendorId,
    ushort ProductId)
{
    public string DisplayName => $"SparkFun Pro Micro（現在 {PortName}）";
}

public interface ISerialHidCandidateEnumerator
{
    IReadOnlyList<SerialHidCandidate> EnumerateCandidates();
}

/// <summary>GUID_DEVINTERFACE_COMPORTをSetupAPIで列挙し、SparkFun Pro Micro runtime VID/PIDだけを候補にする。</summary>
public sealed class SetupApiSerialCandidateEnumerator : ISerialHidCandidateEnumerator
{
    private static readonly Guid ComPortInterfaceGuid = new("86E0D1E0-8089-11D0-9CE4-08003E301F73");

    public IReadOnlyList<SerialHidCandidate> EnumerateCandidates()
    {
        var classGuid = ComPortInterfaceGuid;
        var infoSet = SetupDiGetClassDevsW(
            ref classGuid,
            null,
            IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (infoSet == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "COM port device interfaceを列挙できませんでした。");
        }

        try
        {
            var result = new List<SerialHidCandidate>();
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>(),
                };
                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref classGuid, index, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "COM port device interfaceの列挙に失敗しました。");
                }

                var deviceInfo = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                SetupDiGetDeviceInterfaceDetailW(
                    infoSet,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out var requiredSize,
                    ref deviceInfo);
                var sizeError = Marshal.GetLastWin32Error();
                if (requiredSize == 0 || sizeError != ERROR_INSUFFICIENT_BUFFER)
                {
                    throw new Win32Exception(sizeError, "COM port interface detailの必要長を取得できませんでした。");
                }

                var detailBuffer = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    deviceInfo.cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>();
                    if (!SetupDiGetDeviceInterfaceDetailW(
                            infoSet,
                            ref interfaceData,
                            detailBuffer,
                            requiredSize,
                            out _,
                            ref deviceInfo))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "COM port interface detailを取得できませんでした。");
                    }

                    var pathOffset = IntPtr.Size == 8 ? 8 : 4;
                    var interfacePath = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, pathOffset))
                        ?? throw new InvalidDataException("COM port device interface pathが空です。");
                    var instanceId = ReadDeviceInstanceId(infoSet, ref deviceInfo);
                    if (!TryParseSparkFunRuntimeIdentity(instanceId, out var vendorId, out var productId))
                    {
                        continue;
                    }

                    var portName = ReadPortName(infoSet, ref deviceInfo);
                    result.Add(new SerialHidCandidate(instanceId, portName, interfacePath, vendorId, productId));
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }

            return result
                .OrderBy(candidate => candidate.DeviceInstanceId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }
    }

    public static bool TryParseSparkFunRuntimeIdentity(
        string deviceInstanceId,
        out ushort vendorId,
        out ushort productId)
    {
        vendorId = 0;
        productId = 0;
        if (!deviceInstanceId.Contains("VID_1B4F", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        vendorId = 0x1B4F;
        if (deviceInstanceId.Contains("PID_9205", StringComparison.OrdinalIgnoreCase))
        {
            productId = 0x9205;
            return true;
        }

        if (deviceInstanceId.Contains("PID_9206", StringComparison.OrdinalIgnoreCase))
        {
            productId = 0x9206;
            return true;
        }

        vendorId = 0;
        return false;
    }

    private static string ReadDeviceInstanceId(IntPtr infoSet, ref SP_DEVINFO_DATA deviceInfo)
    {
        SetupDiGetDeviceInstanceIdW(infoSet, ref deviceInfo, null, 0, out var requiredCharacters);
        var error = Marshal.GetLastWin32Error();
        if (requiredCharacters == 0 || error != ERROR_INSUFFICIENT_BUFFER)
        {
            throw new Win32Exception(error, "COM port device instance IDの必要長を取得できませんでした。");
        }

        var buffer = new StringBuilder(requiredCharacters);
        if (!SetupDiGetDeviceInstanceIdW(infoSet, ref deviceInfo, buffer, buffer.Capacity, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "COM port device instance IDを取得できませんでした。");
        }

        return buffer.ToString();
    }

    private static string ReadPortName(IntPtr infoSet, ref SP_DEVINFO_DATA deviceInfo)
    {
        var keyHandle = SetupDiOpenDevRegKey(
            infoSet,
            ref deviceInfo,
            DICS_FLAG_GLOBAL,
            0,
            DIREG_DEV,
            KEY_READ);
        if (keyHandle == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "COM port device registryを開けませんでした。");
        }

        using var safeHandle = new SafeRegistryHandle(keyHandle, ownsHandle: true);
        using var key = RegistryKey.FromHandle(safeHandle);
        var portName = key.GetValue("PortName") as string;
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new InvalidDataException("COM port device registryにPortNameがありません。");
        }

        return portName;
    }

    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int ERROR_NO_MORE_ITEMS = 259;
    private const uint DICS_FLAG_GLOBAL = 0x00000001;
    private const uint DIREG_DEV = 0x00000001;
    private const int KEY_READ = 0x20019;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        out int requiredSize,
        ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        StringBuilder? deviceInstanceId,
        int deviceInstanceIdSize,
        out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiOpenDevRegKey(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        uint scope,
        uint hardwareProfile,
        uint keyType,
        int samDesired);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
