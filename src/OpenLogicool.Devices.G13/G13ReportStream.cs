using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Devices.G13;

/// <summary>
/// report byte 列を PhysicalInput／G13StickSample へ変換する状態機械。
/// recorded・live の両 adapter が同一経路を通るための共通実装。
/// </summary>
public sealed class G13ReportStream
{
    private readonly string deviceInstanceId;
    private readonly byte[] previous = G13ReportParser.IdleReport();
    private readonly List<(string ControlId, PhysicalInputEdge Edge)> edgeBuffer = new();
    private long sequence;
    private byte lastStickX;
    private byte lastStickY;
    private bool hasStick;

    public G13ReportStream(string deviceInstanceId) => this.deviceInstanceId = deviceInstanceId;

    /// <summary>1 report を消化し、発生した button edge と（変化時のみ）stick sample を返す。</summary>
    public void Feed(
        ReadOnlySpan<byte> report,
        double monotonicMs,
        List<PhysicalInput> inputs,
        out G13StickSample? stickSample)
    {
        G13ReportParser.ValidateReport(report);
        sequence++;

        edgeBuffer.Clear();
        G13ReportParser.Diff(previous, report, edgeBuffer);
        foreach (var (controlId, edge) in edgeBuffer)
        {
            inputs.Add(new PhysicalInput(
                ContractSchemaVersions.Revision01,
                deviceInstanceId,
                controlId,
                edge,
                monotonicMs,
                sequence));
        }

        var (x, y) = G13ReportParser.ReadStick(report);
        if (!hasStick || x != lastStickX || y != lastStickY)
        {
            stickSample = new G13StickSample(
                ContractSchemaVersions.Revision01,
                deviceInstanceId,
                x,
                y,
                monotonicMs,
                sequence);
            lastStickX = x;
            lastStickY = y;
            hasStick = true;
        }
        else
        {
            stickSample = null;
        }

        report.CopyTo(previous);
    }
}
