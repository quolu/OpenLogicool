using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.Shared;

namespace OpenLogicool.Devices.G13;

/// <summary>
/// G13 の 8-byte vendor input report を control 単位の edge へ変換する pure parser。
/// bit 対応の一次資料は docs/probes/g13-input-map-2026-08-15.md（実測）。
/// 台帳で「確認済み／強い推定」の bit だけを control として扱い、
/// 未確認 bit（byte5 bit6-7・byte7 bit1-2・bit4-6）と jitter bit（byte7 bit7）は無視する。
/// </summary>
public static class G13ReportParser
{
    private static readonly (int ByteIndex, int Bit, string ControlId)[] ButtonBits =
    [
        (3, 0, "G1"), (3, 1, "G2"), (3, 2, "G3"), (3, 3, "G4"),
        (3, 4, "G5"), (3, 5, "G6"), (3, 6, "G7"), (3, 7, "G8"),
        (4, 0, "G9"), (4, 1, "G10"), (4, 2, "G11"), (4, 3, "G12"),
        (4, 4, "G13"), (4, 5, "G14"), (4, 6, "G15"), (4, 7, "G16"),
        (5, 0, "G17"), (5, 1, "G18"), (5, 2, "G19"), (5, 3, "G20"),
        (5, 4, "G21"), (5, 5, "G22"),
        (6, 0, "LCD_AUX"), (6, 1, "LCD1"), (6, 2, "LCD2"), (6, 3, "LCD3"), (6, 4, "LCD4"),
        (6, 5, "M1"), (6, 6, "M2"), (6, 7, "M3"),
        (7, 0, "MR"), (7, 3, "STICK_PRESS"),
    ];

    /// <summary>report 形状の検証。境界（device からの外部入力）なので不一致は明示的に失敗させる。</summary>
    public static void ValidateReport(ReadOnlySpan<byte> report)
    {
        if (report.Length != G13DeviceIdentity.InputReportLength)
        {
            throw new ArgumentException(
                $"G13 input report は {G13DeviceIdentity.InputReportLength} bytes でなければなりません。実際: {report.Length}");
        }

        if (report[0] != G13DeviceIdentity.InputReportId)
        {
            throw new ArgumentException(
                $"G13 input report ID は 0x{G13DeviceIdentity.InputReportId:X2} でなければなりません。実際: 0x{report[0]:X2}");
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

    public static (byte X, byte Y) ReadStick(ReadOnlySpan<byte> report)
    {
        ValidateReport(report);
        return (report[1], report[2]);
    }

    /// <summary>全 button が離された状態の report（diff の初期 previous）。</summary>
    public static byte[] IdleReport()
    {
        var report = new byte[G13DeviceIdentity.InputReportLength];
        report[0] = G13DeviceIdentity.InputReportId;
        return report;
    }
}
