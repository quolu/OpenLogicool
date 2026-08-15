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
