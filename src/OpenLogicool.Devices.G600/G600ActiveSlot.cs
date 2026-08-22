namespace OpenLogicool.Devices.G600;

/// <summary>
/// F0 active slot の表現（EXP-G600-03 実機実証・libratbag 一次コード）:
/// write は {0xF0, 0x80|(index&lt;&lt;4), 0, 0}（154-byte 枠）、read の第2 byte は (index&lt;&lt;4) | 状態flags
/// のため照合は上位 nibble の index bit だけで行う。index 3 は無効（入力全喪失の実機ログあり）。
/// onboard 書込み（方式A）は F3（slot 0）を対象とするため、書込み前に slot 0 を強制する
/// （実測 2026-08-22: LGS が active slot を 2 へ変えた状態では F3 write が verify 一致後に
/// 巻き戻る——slot 0 active が persist の実証済み条件）。
/// </summary>
public static class G600ActiveSlot
{
    public const byte ReportId = 0xF0;

    public static int ReadIndex(byte f0Byte1) => (f0Byte1 >> 4) & 0x03;

    public static byte[] BuildSwitch(int targetSlot)
    {
        if (targetSlot is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSlot), targetSlot, "slot index must be 0, 1, or 2.");
        }

        var report = new byte[G600SideRemap.ReportLength];
        report[0] = ReportId;
        report[1] = (byte)(0x80 | (targetSlot << 4));
        return report;
    }
}
