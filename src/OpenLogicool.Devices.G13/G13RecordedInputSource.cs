using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.Shared;

namespace OpenLogicool.Devices.G13;

/// <summary>記録済み report（monotonic 時刻＋8 bytes）。</summary>
public readonly record struct G13RecordedReport(double MonotonicMs, byte[] Data);

/// <summary>
/// 記録済み report 列を live adapter と同じ G13ReportStream で replay する adapter。
/// fixture ベースの conformance・replay 試験用。
/// </summary>
public sealed class G13RecordedInputSource : IDeviceInputSource, IG13StickSource
{
    private readonly IReadOnlyList<DeviceInstance> devices;
    private readonly Queue<PhysicalInput> inputs = new();
    private readonly Queue<G13StickSample> stickSamples = new();

    public G13RecordedInputSource(DeviceInstance device, IEnumerable<G13RecordedReport> reports)
    {
        devices = [device];
        var stream = new G13ReportStream(device.DeviceInstanceId);
        var inputBuffer = new List<PhysicalInput>();

        foreach (var report in reports)
        {
            inputBuffer.Clear();
            stream.Feed(report.Data, report.MonotonicMs, inputBuffer, out var stickSample);
            foreach (var input in inputBuffer)
            {
                inputs.Enqueue(input);
            }

            if (stickSample is not null)
            {
                stickSamples.Enqueue(stickSample);
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

    public bool TryPullStick(out G13StickSample sample)
    {
        if (stickSamples.TryDequeue(out var next))
        {
            sample = next;
            return true;
        }

        sample = null!;
        return false;
    }
}
