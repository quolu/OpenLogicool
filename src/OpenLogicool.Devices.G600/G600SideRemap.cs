namespace OpenLogicool.Devices.G600;

/// <summary>
/// 方式B変種（主経路・オーナー裁定 2026-08-15）の write payload 構築:
/// side 12ボタン（G9〜G20）の onboard 割当を中間 usage F13〜F24 へ書き換えて legacy キー配送を無害化する。
/// 通常層と G-Shift 層の両方を書き換える（G6 押下中の shifted 配送も無害化対象）。
/// レイアウト一次資料: docs/probes/g600-profile-decode-2026-08-15.md
/// （154 bytes・通常層 button map 31–90・G-Shift 層 button map 94–153・1項目 = mouseCode/modifiers/hidKey の3 bytes）。
/// 上記以外の byte は解釈せず bytes ごと保持する（read-modify-write）。
/// 実機実証: EXP-G600-02 write 拡張（G9→F13 で legacy 配送が中間 usage へ変化・raw route 無影響・LGS 巻き戻しなし）。
/// </summary>
public static class G600SideRemap
{
    public const int ReportLength = 154;
    public const byte ProfileReportIdF3 = 0xF3;

    public const int NormalLayerBaseOffset = 31;
    public const int ShiftLayerBaseOffset = 94;
    public const int BytesPerButton = 3;

    public const int FirstSideButton = 9;   // G9
    public const int LastSideButton = 20;   // G20
    public const byte F13Usage = 0x68;      // keyboard usage F13。G9=F13 … G20=F24（0x73）

    /// <summary>clean な F3 report から side remap 済み payload を構築する（入力は変更しない）。</summary>
    public static byte[] Build(byte[] profileF3)
    {
        EnsureProfileReport(profileF3);

        var modified = profileF3.ToArray();
        for (var button = FirstSideButton; button <= LastSideButton; button++)
        {
            var usage = (byte)(F13Usage + (button - FirstSideButton));
            WriteCell(modified, NormalLayerBaseOffset, button, usage);
            WriteCell(modified, ShiftLayerBaseOffset, button, usage);
        }

        return modified;
    }

    /// <summary>F3 の side 12ボタンが両層とも F13〜F24 の中間 usage になっているかを判定する。</summary>
    public static bool IsApplied(byte[] profileF3)
    {
        EnsureProfileReport(profileF3);

        for (var button = FirstSideButton; button <= LastSideButton; button++)
        {
            var usage = (byte)(F13Usage + (button - FirstSideButton));
            if (!CellMatches(profileF3, NormalLayerBaseOffset, button, usage)
                || !CellMatches(profileF3, ShiftLayerBaseOffset, button, usage))
            {
                return false;
            }
        }

        return true;
    }

    internal static int CellOffset(int layerBase, int button) => layerBase + (button - 1) * BytesPerButton;

    private static void WriteCell(byte[] report, int layerBase, int button, byte usage)
    {
        var offset = CellOffset(layerBase, button);
        report[offset] = 0x00;     // mouseCode: keyboard
        report[offset + 1] = 0x00; // modifiers: none
        report[offset + 2] = usage;
    }

    private static bool CellMatches(byte[] report, int layerBase, int button, byte usage)
    {
        var offset = CellOffset(layerBase, button);
        return report[offset] == 0x00 && report[offset + 1] == 0x00 && report[offset + 2] == usage;
    }

    internal static void EnsureProfileReport(byte[] profileF3)
    {
        if (profileF3.Length != ReportLength || profileF3[0] != ProfileReportIdF3)
        {
            throw new ArgumentException(
                $"profile report must be a {ReportLength}-byte report starting with 0x{ProfileReportIdF3:X2}.",
                nameof(profileF3));
        }
    }
}
