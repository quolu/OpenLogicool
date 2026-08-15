using OpenLogicool.Contracts.Devices.G600;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Devices.G600;

/// <summary>
/// report byte 列を PhysicalInput／G600WheelTick へ変換する状態機械。
/// recorded・live の両 adapter が同一経路を通るための共通実装。
///
/// ホイール判定（実測 rawinput-g600-normal-20260815-135438.jsonl より）:
/// device は event ごとに1 report を送り、byte4 は最後の回転差分を latch する。
/// - 回転1ノッチ = button 変化なしの report（byte4 = ±1、同値の連続も1 report=1ノッチ）
/// - button 変化ありの report の byte4 は latch 残存であり、回転 event ではない
/// </summary>
public sealed class G600ReportStream
{
    private readonly string deviceInstanceId;
    private readonly byte[] previous = G600ReportParser.IdleReport();
    private readonly List<(string ControlId, PhysicalInputEdge Edge)> edgeBuffer = new();
    private long sequence;

    public G600ReportStream(string deviceInstanceId) => this.deviceInstanceId = deviceInstanceId;

    /// <summary>1 report を消化し、発生した button edge と（回転 event 時のみ）wheel tick を返す。</summary>
    public void Feed(
        ReadOnlySpan<byte> report,
        double monotonicMs,
        List<PhysicalInput> inputs,
        out G600WheelTick? wheelTick)
    {
        G600ReportParser.ValidateReport(report);
        sequence++;

        edgeBuffer.Clear();
        G600ReportParser.Diff(previous, report, edgeBuffer);
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

        var wheelDelta = G600ReportParser.ReadWheelByte(report);
        wheelTick = edgeBuffer.Count == 0 && wheelDelta != 0
            ? new G600WheelTick(
                ContractSchemaVersions.Revision01,
                deviceInstanceId,
                wheelDelta,
                monotonicMs,
                sequence)
            : null;

        report.CopyTo(previous);
    }
}
