using OpenLogicool.Contracts.Devices.G600;
using OpenLogicool.Contracts.Devices.Shared;

namespace OpenLogicool.Devices.G600;

/// <summary>記録済み report（monotonic 時刻＋32 bytes）。</summary>
public readonly record struct G600RecordedReport(double MonotonicMs, byte[] Data);

/// <summary>
/// 記録済み report 列を live adapter と同じ G600ReportStream で replay する adapter。
/// fixture ベースの conformance・replay 試験用。
/// </summary>
public sealed class G600RecordedInputSource : IDeviceInputSource, IG600WheelSource
{
    private readonly IReadOnlyList<DeviceInstance> devices;
    private readonly Queue<PhysicalInput> inputs = new();
    private readonly Queue<G600WheelTick> wheelTicks = new();

    public G600RecordedInputSource(DeviceInstance device, IEnumerable<G600RecordedReport> reports)
    {
        devices = [device];
        var stream = new G600ReportStream(device.DeviceInstanceId);
        var inputBuffer = new List<PhysicalInput>();

        foreach (var report in reports)
        {
            inputBuffer.Clear();
            stream.Feed(report.Data, report.MonotonicMs, inputBuffer, out var wheelTick);
            foreach (var input in inputBuffer)
            {
                inputs.Enqueue(input);
            }

            if (wheelTick is not null)
            {
                wheelTicks.Enqueue(wheelTick);
            }
        }
    }

    public IReadOnlyList<DeviceInstance> EnumerateDevices() => devices;

    public bool TryPull(out PhysicalInput input)
    {
        if (inputs.TryDequeue(out var next))
        {
            input = next;
            return true;
        }

        input = null!;
        return false;
    }

    public bool TryPullWheel(out G600WheelTick tick)
    {
        if (wheelTicks.TryDequeue(out var next))
        {
            tick = next;
            return true;
        }

        tick = null!;
        return false;
    }
}
