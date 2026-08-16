namespace OpenLogicool.Input;

public enum ResolvedOutputKind
{
    Key,
    MouseButton,
}

public enum MouseButton
{
    Left,
    Right,
    Middle,
    X1,
    X2,
}

/// <summary>output token を解釈した結果。Key は virtual key（拡張 key flag 付き）、MouseButton は物理ボタン種別。</summary>
public readonly record struct ResolvedOutput(
    ResolvedOutputKind Kind,
    ushort VirtualKey,
    bool IsExtendedKey,
    MouseButton MouseButton);

/// <summary>
/// output token（binding の outputs 文字列）の解釈。pure。
/// 文法: "Key:&lt;名前&gt;"（下表）・"Vk:0xNN"（生 virtual key）・"Mouse:&lt;Left|Right|Middle|X1|X2&gt;"。
/// 未知の token はエラー（fallback しない）。
/// </summary>
public static class OutputTokens
{
    /// <summary>有限 sequence の1段（DEV-006）。"Tap:Key:A"・chord 段は "Tap:Key:LCtrl+Key:C"。</summary>
    public const string SequenceStepPrefix = "Tap:";

    private static readonly IReadOnlyDictionary<string, (ushort Vk, bool Extended)> KeyNames = BuildKeyNames();

    public static bool IsSequenceStep(string token) =>
        token.StartsWith(SequenceStepPrefix, StringComparison.Ordinal);

    /// <summary>
    /// sequence step を構成 token 列へ分解する（'+' 区切り）。各構成 token は Parse で検証し、
    /// 不正なら例外（fallback しない）。
    /// </summary>
    public static IReadOnlyList<string> SplitSequenceStep(string token)
    {
        if (!IsSequenceStep(token))
        {
            throw new ArgumentException($"'{token}' は sequence step（{SequenceStepPrefix}…）ではありません。", nameof(token));
        }

        var components = token[SequenceStepPrefix.Length..].Split('+');
        if (components.Length == 0 || components.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException($"sequence step '{token}' に空の構成 token が含まれています。", nameof(token));
        }

        foreach (var component in components)
        {
            Parse(component);
        }

        return components;
    }

    public static ResolvedOutput Parse(string token)
    {
        var separator = token.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == token.Length - 1)
        {
            throw new ArgumentException($"output token '{token}' は '種別:値' 形式でなければなりません。", nameof(token));
        }

        var kind = token[..separator];
        var value = token[(separator + 1)..];

        switch (kind)
        {
            case "Key":
                if (!KeyNames.TryGetValue(value, out var key))
                {
                    throw new ArgumentException($"key 名 '{value}' は未対応です。Vk:0xNN で生 virtual key を指定できます。", nameof(token));
                }

                return new ResolvedOutput(ResolvedOutputKind.Key, key.Vk, key.Extended, default);

            case "Vk":
                var vk = Convert.ToUInt16(value.Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
                if (vk is 0 or > 0xFE)
                {
                    throw new ArgumentException($"virtual key 0x{vk:X2} は範囲外です（0x01〜0xFE）。", nameof(token));
                }

                return new ResolvedOutput(ResolvedOutputKind.Key, vk, IsExtendedVk(vk), default);

            case "Mouse":
                return new ResolvedOutput(
                    ResolvedOutputKind.MouseButton,
                    0,
                    false,
                    Enum.Parse<MouseButton>(value, ignoreCase: false));

            default:
                throw new ArgumentException($"output token 種別 '{kind}' は未対応です（Key／Vk／Mouse）。", nameof(token));
        }
    }

    private static bool IsExtendedVk(ushort vk) =>
        vk is 0xA3 or 0xA5 // RCtrl, RAlt
            or 0x2D or 0x2E or 0x24 or 0x23 or 0x21 or 0x22 // Insert, Delete, Home, End, PageUp, PageDown
            or 0x25 or 0x26 or 0x27 or 0x28 // 矢印
            or 0x5B or 0x5C; // LWin, RWin

    private static Dictionary<string, (ushort, bool)> BuildKeyNames()
    {
        var names = new Dictionary<string, (ushort, bool)>(StringComparer.Ordinal);

        for (var c = 'A'; c <= 'Z'; c++)
        {
            names[c.ToString()] = ((ushort)c, false);
        }

        for (var d = '0'; d <= '9'; d++)
        {
            names[d.ToString()] = ((ushort)d, false);
        }

        for (var f = 1; f <= 24; f++)
        {
            names[$"F{f}"] = ((ushort)(0x70 + f - 1), false);
        }

        names["LShift"] = (0xA0, false);
        names["RShift"] = (0xA1, false);
        names["LCtrl"] = (0xA2, false);
        names["RCtrl"] = (0xA3, true);
        names["LAlt"] = (0xA4, false);
        names["RAlt"] = (0xA5, true);
        names["LWin"] = (0x5B, true);
        names["RWin"] = (0x5C, true);
        names["Space"] = (0x20, false);
        names["Enter"] = (0x0D, false);
        names["Tab"] = (0x09, false);
        names["Esc"] = (0x1B, false);
        names["Backspace"] = (0x08, false);
        names["Insert"] = (0x2D, true);
        names["Delete"] = (0x2E, true);
        names["Home"] = (0x24, true);
        names["End"] = (0x23, true);
        names["PageUp"] = (0x21, true);
        names["PageDown"] = (0x22, true);
        names["Up"] = (0x26, true);
        names["Down"] = (0x28, true);
        names["Left"] = (0x25, true);
        names["Right"] = (0x27, true);

        return names;
    }
}
