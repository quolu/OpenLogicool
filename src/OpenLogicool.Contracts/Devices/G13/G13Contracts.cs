namespace OpenLogicool.Contracts.Devices.G13;

/// <summary>
/// G13 実機の識別値と input report 形状。
/// 一次資料: docs/probes/g13-input-map-2026-08-15.md（実測・firmware release 0203）。
/// </summary>
public static class G13DeviceIdentity
{
    public const int VendorId = 0x046D;
    public const int ProductId = 0xC21C;
    public const int VendorUsagePage = 0xFF00;
    public const byte InputReportId = 0x01;
    public const int InputReportLength = 8;
}

/// <summary>
/// G13 の canonical control ID。実測台帳で「確認済み」または「強い推定」の control だけを含む。
/// 未確認 bit（byte5 bit6-7・byte7 bit1-2・bit4-7）は contract に載せない。
/// </summary>
public static class G13Controls
{
    public static readonly IReadOnlyList<string> Buttons =
    [
        "G1", "G2", "G3", "G4", "G5", "G6", "G7", "G8",
        "G9", "G10", "G11", "G12", "G13", "G14", "G15", "G16",
        "G17", "G18", "G19", "G20", "G21", "G22",
        "LCD_AUX", "LCD1", "LCD2", "LCD3", "LCD4",
        "M1", "M2", "M3", "MR",
        "STICK_PRESS",
    ];

    /// <summary>
    /// 実測台帳で「確認済み」の button（G20・STICK_PRESS は recheck 単独押下、G1/G2 は chord 実験）。
    /// 残りの button は「強い推定」（押下順序と間隔の一致）。DEV-005 の表示はこの区別を根拠にする。
    /// </summary>
    public static readonly IReadOnlySet<string> ConfirmedButtons =
        new HashSet<string>(["G1", "G2", "G20", "STICK_PRESS"], StringComparer.Ordinal);
}

/// <summary>スティック X/Y の生値サンプル（0〜255、中立 X≈143 / Y≈120）。</summary>
public sealed record G13StickSample(
    string SchemaVersion,
    string DeviceInstanceId,
    byte X,
    byte Y,
    double MonotonicMs,
    long ReportSequence);

public interface IG13StickSource
{
    bool TryPullStick(out G13StickSample sample);
}
