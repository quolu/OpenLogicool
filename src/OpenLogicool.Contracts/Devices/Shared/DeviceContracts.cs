namespace OpenLogicool.Contracts.Devices.Shared;

public sealed record DeviceInstance(
    string SchemaVersion,
    string DeviceInstanceId,
    int VendorId,
    int ProductId,
    string DevicePath,
    string ContainerId,
    long Generation,
    IReadOnlyList<string> Capabilities);

public enum PhysicalInputEdge
{
    Down,
    Up,
}

public sealed record PhysicalInput(
    string SchemaVersion,
    string DeviceInstanceId,
    string ControlId,
    PhysicalInputEdge Edge,
    double MonotonicMs,
    long ReportSequence);

public interface IDeviceInputSource
{
    IReadOnlyList<DeviceInstance> EnumerateDevices();

    bool TryPull(out PhysicalInput input);
}

public enum DeviceChangeKind
{
    Arrival,
    Removal,
}

public sealed record DeviceChange(
    string SchemaVersion,
    string DeviceInstanceId,
    DeviceChangeKind Kind,
    double MonotonicMs);

/// <summary>
/// device の到着・切断を通知できる input source（live adapter が実装する。DEV-008 の切断検出面）。
/// </summary>
public interface IDeviceChangeSource
{
    bool TryPullDeviceChange(out DeviceChange change);
}
