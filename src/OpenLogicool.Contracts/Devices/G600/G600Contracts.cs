namespace OpenLogicool.Contracts.Devices.G600;

/// <summary>
/// G600 実機の識別値と vendor input report 形状。
/// 一次資料: docs/probes/g600-input-map-2026-08-15.md（実測・firmware release 7702）。
/// report 0x80 は vendor collection (MI_01/COL02, usage page 0xFF80) から届く。
/// </summary>
public static class G600DeviceIdentity
{
    public const int VendorId = 0x046D;
    public const int ProductId = 0xC24A;
    public const int VendorUsagePage = 0xFF80;
    public const byte InputReportId = 0x80;
    public const int InputReportLength = 32;
}

/// <summary>
/// G600 の canonical control ID。bit 位置は G 番号と連番一致（実測・確認済み）:
/// byte1 bit0〜7 = G1〜G8（左・右・ホイール押込み・左チルト・右チルト・G-Shift ボタン・ホイール後ろ手前・奥）、
/// byte2 bit0〜7 = G9〜G16、byte3 bit0〜3 = G17〜G20。
/// G6 の LGS 既定割当が G-Shift（押下中保持の修飾）だが、control としては物理ボタン G6 を正とする。
/// ホイール回転は button ではなく G600WheelTick として届く。
/// </summary>
public static class G600Controls
{
    public static readonly IReadOnlyList<string> Buttons =
    [
        "G1", "G2", "G3", "G4", "G5", "G6", "G7", "G8",
        "G9", "G10", "G11", "G12", "G13", "G14", "G15", "G16",
        "G17", "G18", "G19", "G20",
    ];

    /// <summary>
    /// 全20 control が「確認済み」: G0-Device-RO の live input route 実測（全20 control）と
    /// g600-adapter-smoke（2026-08-16）で成立。
    /// </summary>
    public static readonly IReadOnlySet<string> ConfirmedButtons =
        new HashSet<string>(Buttons, StringComparer.Ordinal);
}

/// <summary>ホイール1ノッチの回転（Delta: 上=+1／下=-1、report byte4 の符号付き値）。</summary>
public sealed record G600WheelTick(
    string SchemaVersion,
    string DeviceInstanceId,
    int Delta,
    double MonotonicMs,
    long ReportSequence);

public interface IG600WheelSource
{
    bool TryPullWheel(out G600WheelTick tick);
}
