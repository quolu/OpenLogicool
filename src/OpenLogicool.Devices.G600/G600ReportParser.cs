using OpenLogicool.Contracts.Devices.G600;
using OpenLogicool.Contracts.Devices.Shared;

namespace OpenLogicool.Devices.G600;

/// <summary>
/// G600 の 32-byte vendor input report 0x80 を control 単位の edge へ変換する pure parser。
/// bit 対応の一次資料は docs/probes/g600-input-map-2026-08-15.md（実測）。
/// byte4 のホイール差分は「最後の回転差分を latch する」実測挙動のため、
/// 解釈（回転 event か latch 残存か）は G600ReportStream 側の diff 文脈で行う。
/// byte5（定数 0x16 観測・意味未特定）と byte6 以降の padding は無視する。
/// </summary>
public static class G600ReportParser
{
    private static readonly (int ByteIndex, int Bit, string ControlId)[] ButtonBits =
    [
        (1, 0, "G1"), (1, 1, "G2"), (1, 2, "G3"), (1, 3, "G4"),
        (1, 4, "G5"), (1, 5, "G6"), (1, 6, "G7"), (1, 7, "G8"),
        (2, 0, "G9"), (2, 1, "G10"), (2, 2, "G11"), (2, 3, "G12"),
        (2, 4, "G13"), (2, 5, "G14"), (2, 6, "G15"), (2, 7, "G16"),
        (3, 0, "G17"), (3, 1, "G18"), (3, 2, "G19"), (3, 3, "G20"),
    ];

    /// <summary>report 形状の検証。境界（device からの外部入力）なので不一致は明示的に失敗させる。</summary>
    public static void ValidateReport(ReadOnlySpan<byte> report)
    {
        if (report.Length != G600DeviceIdentity.InputReportLength)
        {
            throw new ArgumentException(
                $"G600 input report は {G600DeviceIdentity.InputReportLength} bytes でなければなりません。実際: {report.Length}");
        }

        if (report[0] != G600DeviceIdentity.InputReportId)
        {
            throw new ArgumentException(
                $"G600 input report ID は 0x{G600DeviceIdentity.InputReportId:X2} でなければなりません。実際: 0x{report[0]:X2}");
        }
    }

    /// <summary>previous → current の button edge を台帳の table 順で列挙する。</summary>
    public static void Diff(
        ReadOnlySpan<byte> previous,
        ReadOnlySpan<byte> current,
        List<(string ControlId, PhysicalInputEdge Edge)> edges)
    {
        ValidateReport(previous);
        ValidateReport(current);

        foreach (var (byteIndex, bit, controlId) in ButtonBits)
        {
            var mask = 1 << bit;
            var wasDown = (previous[byteIndex] & mask) != 0;
            var isDown = (current[byteIndex] & mask) != 0;
            if (wasDown != isDown)
            {
                edges.Add((controlId, isDown ? PhysicalInputEdge.Down : PhysicalInputEdge.Up));
            }
        }
    }

    /// <summary>byte4 の符号付きホイール値（latch 挙動のため、event 判定は呼び出し側の責務）。</summary>
    public static int ReadWheelByte(ReadOnlySpan<byte> report)
    {
        ValidateReport(report);
        return unchecked((sbyte)report[4]);
    }

    /// <summary>全 button が離された状態の report（diff の初期 previous）。</summary>
    public static byte[] IdleReport()
    {
        var report = new byte[G600DeviceIdentity.InputReportLength];
        report[0] = G600DeviceIdentity.InputReportId;
        return report;
    }
}
