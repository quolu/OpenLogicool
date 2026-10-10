using System.Runtime.InteropServices;

namespace OpenLogicool.Host;

/// <summary>
/// Raw Inputの機器のパスが、選ばれているNanoのものかを見分ける。同じ型の基板が複数つながっていても、
/// 機器のコンテナIDで選ばれている1台だけを当てる。Bot本体と同じprocessで手入力を観測する時に使う。
/// </summary>
internal sealed class NanoRawInputMatcher
{
    private readonly string model;

    public NanoRawInputMatcher(SerialHidCandidate nano)
    {
        model = $"VID_{nano.VendorId:X4}&PID_{nano.ProductId:X4}";
        Container = ContainerForInstance(nano.DeviceInstanceId);
    }

    public Guid Container { get; }

    public bool Matches(string devicePath) =>
        devicePath.Contains(model, StringComparison.OrdinalIgnoreCase) && ContainerForInterface(devicePath) == Container;

    private static Guid ContainerForInstance(string instance)
    {
        var result = CM_Locate_DevNodeW(out var node, instance, 0);
        if (result != 0) throw new InvalidOperationException($"Nanoのdevnodeを取得できません: CR={result}");
        var key = ContainerKey;
        var bytes = new byte[16]; uint size = 16;
        result = CM_Get_DevNode_PropertyW(node, ref key, out var type, bytes, ref size, 0);
        if (result != 0 || type != 13 || size != 16) throw new InvalidOperationException($"NanoのContainerIdを取得できません: CR={result}");
        return new Guid(bytes);
    }

    private static Guid ContainerForInterface(string path)
    {
        var key = ContainerKey;
        var bytes = new byte[16]; uint size = 16;
        var result = CM_Get_Device_Interface_PropertyW(path, ref key, out var type, bytes, ref size, 0);
        if (result != 0 || type != 13 || size != 16) throw new InvalidOperationException($"HIDのContainerIdを取得できません: CR={result}");
        return new Guid(bytes);
    }

    private static readonly DevicePropertyKey ContainerKey = new() { Format = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), Id = 2 };
    [StructLayout(LayoutKind.Sequential)] private struct DevicePropertyKey { public Guid Format; public uint Id; }
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNodeW(out uint node, string instance, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_DevNode_PropertyW(uint node, ref DevicePropertyKey key, out uint type, byte[] value, ref uint size, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_Interface_PropertyW(string path, ref DevicePropertyKey key, out uint type, byte[] value, ref uint size, uint flags);
}
