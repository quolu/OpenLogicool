namespace OpenLogicool.Devices.G600;

/// <summary>onboard button cell 1件（button=G番号 1〜20・ShiftLayer=G-Shift 層か・3 bytes は decode 台帳の形式）。</summary>
public sealed record G600OnboardCell(int Button, bool ShiftLayer, byte MouseCode, byte Modifiers, byte HidKey);

/// <summary>
/// 方式A（補完経路・オーナー裁定 2026-08-15、NIKKE 実測 2026-08-22 で採用確定）の write payload 構築:
/// workspace の G600 割当を onboard F3 profile の button cell へ焼く。
/// baseline（出荷状態の F3）を起点に read-modify-write し、cell 以外の byte（LED/DPI/未確認領域）は保持する。
/// 安全強制: G1 は両層とも左クリック固定（マウス操作不能の防止）。G-Shift selector の control は
/// 両層とも G-Shift（mouseCode 0x17）固定（layer 崩壊の防止）。これらへの cell 指定は例外（黙って上書きしない）。
/// レイアウト一次資料: docs/probes/g600-profile-decode-2026-08-15.md（G600SideRemap と同じ offset 体系）。
/// </summary>
public static class G600OnboardImage
{
    public const byte MouseCodeLeftClick = 0x01;
    public const byte MouseCodeGShift = 0x17;
    public const int FirstButton = 1;
    public const int LastButton = 20;
    public const int LeftClickButton = 1;

    /// <summary>
    /// baseline F3 から onboard payload を構築する（入力は変更しない）。
    /// shiftSelectorButton は workspace の G-Shift selector（無い場合は null＝どの button も 0x17 を書かない）。
    /// </summary>
    public static byte[] Build(byte[] baselineF3, IReadOnlyList<G600OnboardCell> cells, int? shiftSelectorButton)
    {
        EnsureProfileReport(baselineF3);
        EnsureCells(cells, shiftSelectorButton);

        var modified = baselineF3.ToArray();

        WriteBothLayers(modified, LeftClickButton, MouseCodeLeftClick, 0x00, 0x00);
        if (shiftSelectorButton is { } selector)
        {
            WriteBothLayers(modified, selector, MouseCodeGShift, 0x00, 0x00);
        }

        foreach (var cell in cells)
        {
            var layerBase = cell.ShiftLayer ? G600SideRemap.ShiftLayerBaseOffset : G600SideRemap.NormalLayerBaseOffset;
            WriteCell(modified, layerBase, cell.Button, cell.MouseCode, cell.Modifiers, cell.HidKey);
        }

        return modified;
    }

    private static void EnsureCells(IReadOnlyList<G600OnboardCell> cells, int? shiftSelectorButton)
    {
        if (shiftSelectorButton is { } selector && (selector is < FirstButton or > LastButton || selector == LeftClickButton))
        {
            throw new ArgumentException($"G-Shift selector G{selector} は onboard へ書けません（G2〜G20 のみ）。", nameof(shiftSelectorButton));
        }

        var seen = new HashSet<(int, bool)>();
        foreach (var cell in cells)
        {
            if (cell.Button is < FirstButton or > LastButton)
            {
                throw new ArgumentException($"button G{cell.Button} は範囲外です（G1〜G20）。", nameof(cells));
            }

            if (cell.Button == LeftClickButton)
            {
                throw new ArgumentException("G1 は左クリック固定のため onboard 割当を書けません。", nameof(cells));
            }

            if (cell.Button == shiftSelectorButton)
            {
                throw new ArgumentException($"G{cell.Button} は G-Shift selector のため onboard 割当を書けません。", nameof(cells));
            }

            if (!seen.Add((cell.Button, cell.ShiftLayer)))
            {
                throw new ArgumentException($"button G{cell.Button} の cell が重複しています。", nameof(cells));
            }
        }
    }

    private static void WriteBothLayers(byte[] report, int button, byte mouseCode, byte modifiers, byte hidKey)
    {
        WriteCell(report, G600SideRemap.NormalLayerBaseOffset, button, mouseCode, modifiers, hidKey);
        WriteCell(report, G600SideRemap.ShiftLayerBaseOffset, button, mouseCode, modifiers, hidKey);
    }

    private static void WriteCell(byte[] report, int layerBase, int button, byte mouseCode, byte modifiers, byte hidKey)
    {
        var offset = layerBase + (button - 1) * G600SideRemap.BytesPerButton;
        report[offset] = mouseCode;
        report[offset + 1] = modifiers;
        report[offset + 2] = hidKey;
    }

    private static void EnsureProfileReport(byte[] profileF3)
    {
        if (profileF3.Length != G600SideRemap.ReportLength || profileF3[0] != G600SideRemap.ProfileReportIdF3)
        {
            throw new ArgumentException(
                $"profile report must be a {G600SideRemap.ReportLength}-byte report starting with 0x{G600SideRemap.ProfileReportIdF3:X2}.",
                nameof(profileF3));
        }
    }
}
