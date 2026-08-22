using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Devices.G600;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

/// <summary>onboard 変換計画: 全 button×両層の cell（未割当は 00 00 00）と変換不能の全列挙。</summary>
public sealed record G600OnboardPlan(
    IReadOnlyList<G600OnboardCell> Cells,
    int? ShiftSelectorButton,
    IReadOnlyList<string> Errors)
{
    public bool CanApply => Errors.Count == 0;
}

/// <summary>
/// G600 の MappingProfileDocument を onboard button cell 列へ変換する。pure。
/// onboard cell（mouseCode/modifiers/hidKey）で表現できない binding は黙って落とさず全列挙して拒否する:
/// `Tap:` sequence・modifier＋単キー以外の chord・HID 変換表にない virtual key・マウスとキーの混在。
/// 層は base（DefaultLayerId）と G-Shift（hold selector の対象 layer・最大1つ）だけを対象とし、
/// shift 層の未割当は software runtime と同じく無動作（00 00 00）にする。
/// G1（左クリック）と selector control への binding は G600OnboardImage の固定と衝突するため拒否する。
/// </summary>
public static class G600OnboardPlanner
{
    public static G600OnboardPlan Build(MappingProfileDocument document)
    {
        var errors = new List<string>();

        if (document.DeviceKind != "G600")
        {
            errors.Add($"device 種別 '{document.DeviceKind}' は onboard 書込み対象外です（G600 のみ）。");
            return new G600OnboardPlan([], null, errors);
        }

        if (document.LatchSelectors.Count > 0)
        {
            errors.Add("G600 の latch layer は onboard で表現できません。");
        }

        int? shiftSelectorButton = null;
        string? shiftLayerId = null;
        if (document.HoldSelectors.Count > 1)
        {
            errors.Add($"G-Shift selector が {document.HoldSelectors.Count} 個あります（onboard は1つだけ）。");
        }
        else if (document.HoldSelectors.Count == 1)
        {
            var selector = document.HoldSelectors[0];
            if (!TryParseButton(selector.ControlId, out var selectorButton))
            {
                errors.Add($"G-Shift selector control '{selector.ControlId}' を G 番号として解釈できません。");
            }
            else if (selectorButton == G600OnboardImage.LeftClickButton)
            {
                errors.Add("G1 を G-Shift selector として onboard へ書けません（左クリック固定）。");
            }
            else
            {
                shiftSelectorButton = selectorButton;
                shiftLayerId = selector.LayerId;
            }
        }

        var cellByKey = new Dictionary<(int Button, bool Shift), G600OnboardCell>();
        foreach (var binding in document.Bindings)
        {
            if (!TryParseButton(binding.ControlId, out var button))
            {
                errors.Add($"control '{binding.ControlId}' を G 番号として解釈できません。");
                continue;
            }

            bool shift;
            if (binding.LayerId == document.DefaultLayerId)
            {
                shift = false;
            }
            else if (shiftLayerId is not null && binding.LayerId == shiftLayerId)
            {
                shift = true;
            }
            else
            {
                errors.Add($"{binding.ControlId} の layer '{binding.LayerId}' は onboard で表現できません（通常と G-Shift のみ）。");
                continue;
            }

            if (button == G600OnboardImage.LeftClickButton)
            {
                errors.Add("G1 の割当は onboard へ書けません（左クリック固定）。");
                continue;
            }

            if (button == shiftSelectorButton)
            {
                errors.Add($"G{button} は G-Shift selector のため割当を onboard へ書けません。");
                continue;
            }

            if (!TryConvertOutputs(binding, out var mouseCode, out var modifiers, out var hidKey, errors))
            {
                continue;
            }

            cellByKey[(button, shift)] = new G600OnboardCell(button, shift, mouseCode, modifiers, hidKey);
        }

        if (errors.Count > 0)
        {
            return new G600OnboardPlan([], shiftSelectorButton, errors);
        }

        // 全 button×両層を明示 cell 化する（未割当 00 00 00 ＝出荷割当の legacy 配送を残さない）。
        var cells = new List<G600OnboardCell>();
        for (var button = G600OnboardImage.FirstButton; button <= G600OnboardImage.LastButton; button++)
        {
            if (button == G600OnboardImage.LeftClickButton || button == shiftSelectorButton)
            {
                continue;
            }

            foreach (var shift in new[] { false, true })
            {
                cells.Add(cellByKey.TryGetValue((button, shift), out var cell)
                    ? cell
                    : new G600OnboardCell(button, shift, 0x00, 0x00, 0x00));
            }
        }

        return new G600OnboardPlan(cells, shiftSelectorButton, errors);
    }

    private static bool TryConvertOutputs(
        MappingBindingEntry binding,
        out byte mouseCode,
        out byte modifiers,
        out byte hidKey,
        List<string> errors)
    {
        mouseCode = 0;
        modifiers = 0;
        hidKey = 0;
        var label = $"{binding.ControlId}（{binding.LayerId}）";

        if (binding.Outputs.Count == 0)
        {
            errors.Add($"{label}: outputs が空です。");
            return false;
        }

        MouseButton? mouse = null;
        var nonModifierKeys = new List<byte>();
        foreach (var token in binding.Outputs)
        {
            if (OutputTokens.IsSequenceStep(token))
            {
                errors.Add($"{label}: 連続入力（{token}）は onboard で表現できません。");
                return false;
            }

            ResolvedOutput resolved;
            try
            {
                resolved = OutputTokens.Parse(token);
            }
            catch (ArgumentException exception)
            {
                errors.Add($"{label}: {exception.Message}");
                return false;
            }

            if (resolved.Kind == ResolvedOutputKind.MouseButton)
            {
                if (mouse is not null)
                {
                    errors.Add($"{label}: マウスボタンを2つ以上同時に onboard へ書けません。");
                    return false;
                }

                mouse = resolved.MouseButton;
                continue;
            }

            if (KeyboardHidUsage.TryGetModifierBit(resolved.VirtualKey, out var bit))
            {
                modifiers |= bit;
                continue;
            }

            if (!KeyboardHidUsage.TryGetUsage(resolved.VirtualKey, out var usage))
            {
                errors.Add($"{label}: '{token}'（VK 0x{resolved.VirtualKey:X2}）は onboard の HID 変換表にありません。");
                return false;
            }

            nonModifierKeys.Add(usage);
        }

        if (mouse is not null && (modifiers != 0 || nonModifierKeys.Count > 0))
        {
            errors.Add($"{label}: マウスボタンとキーの混在は onboard で表現できません。");
            return false;
        }

        if (nonModifierKeys.Count > 1)
        {
            errors.Add($"{label}: 修飾キー以外を2つ以上含む同時押しは onboard で表現できません。");
            return false;
        }

        if (mouse is { } mouseButton)
        {
            mouseCode = mouseButton switch
            {
                MouseButton.Left => 0x01,
                MouseButton.Right => 0x02,
                MouseButton.Middle => 0x03,
                MouseButton.X1 => 0x04, // Back
                MouseButton.X2 => 0x05, // Forward
                _ => throw new ArgumentOutOfRangeException(nameof(binding)),
            };
            return true;
        }

        hidKey = nonModifierKeys.Count == 1 ? nonModifierKeys[0] : (byte)0x00;
        return true;
    }

    private static bool TryParseButton(string controlId, out int button)
    {
        button = 0;
        return controlId.Length >= 2
            && controlId[0] == 'G'
            && int.TryParse(controlId[1..], out button)
            && button is >= G600OnboardImage.FirstButton and <= G600OnboardImage.LastButton;
    }
}
