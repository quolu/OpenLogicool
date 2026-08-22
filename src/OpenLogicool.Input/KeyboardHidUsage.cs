namespace OpenLogicool.Input;

/// <summary>
/// Windows virtual key → USB HID Keyboard/Keypad usage（usage page 0x07）の変換表。pure。
/// G600 onboard profile の button cell（mouseCode/modifiers/hidKey）構築に使う
/// （レイアウト一次資料: docs/probes/g600-profile-decode-2026-08-15.md）。
/// 表にない virtual key は変換不能として false を返す（fallback しない・呼び手が明示エラーにする）。
/// </summary>
public static class KeyboardHidUsage
{
    private static readonly IReadOnlyDictionary<ushort, byte> UsageByVk = BuildUsageByVk();

    private static readonly IReadOnlyDictionary<ushort, byte> ModifierBitByVk = new Dictionary<ushort, byte>
    {
        [0xA2] = 0x01, // LCtrl
        [0xA0] = 0x02, // LShift
        [0xA4] = 0x04, // LAlt
        [0x5B] = 0x08, // LWin
        [0xA3] = 0x10, // RCtrl
        [0xA1] = 0x20, // RShift
        [0xA5] = 0x40, // RAlt
        [0x5C] = 0x80, // RWin
    };

    /// <summary>modifier key（Ctrl/Shift/Alt/Win）なら HID modifier bitmask の bit を返す。</summary>
    public static bool TryGetModifierBit(ushort virtualKey, out byte bit) =>
        ModifierBitByVk.TryGetValue(virtualKey, out bit);

    /// <summary>modifier 以外の key の HID usage を返す。表にない key は false（変換不能）。</summary>
    public static bool TryGetUsage(ushort virtualKey, out byte usage) =>
        UsageByVk.TryGetValue(virtualKey, out usage);

    private static Dictionary<ushort, byte> BuildUsageByVk()
    {
        var table = new Dictionary<ushort, byte>();

        for (var c = 'A'; c <= 'Z'; c++)
        {
            table[(ushort)c] = (byte)(0x04 + (c - 'A'));
        }

        for (var d = '1'; d <= '9'; d++)
        {
            table[(ushort)d] = (byte)(0x1E + (d - '1'));
        }

        table[(ushort)'0'] = 0x27;

        for (var f = 0; f < 12; f++)
        {
            table[(ushort)(0x70 + f)] = (byte)(0x3A + f); // F1〜F12
        }

        for (var f = 0; f < 12; f++)
        {
            table[(ushort)(0x7C + f)] = (byte)(0x68 + f); // F13〜F24
        }

        table[0x0D] = 0x28; // Enter
        table[0x1B] = 0x29; // Esc
        table[0x08] = 0x2A; // Backspace
        table[0x09] = 0x2B; // Tab
        table[0x20] = 0x2C; // Space
        table[0x2D] = 0x49; // Insert
        table[0x2E] = 0x4C; // Delete
        table[0x24] = 0x4A; // Home
        table[0x23] = 0x4D; // End
        table[0x21] = 0x4B; // PageUp
        table[0x22] = 0x4E; // PageDown
        table[0x27] = 0x4F; // Right
        table[0x25] = 0x50; // Left
        table[0x28] = 0x51; // Down
        table[0x26] = 0x52; // Up

        return table;
    }
}
