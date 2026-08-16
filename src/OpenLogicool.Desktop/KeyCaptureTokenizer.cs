using System.Windows.Input;

namespace OpenLogicool.Desktop;

/// <summary>
/// WPF <see cref="Key"/> を既存 output token 文法（<c>OpenLogicool.Input.OutputTokens</c>）の
/// "Key:&lt;名前&gt;" 表記へ変換する（pure）。Desktop の参照は Contracts + Domain だけ
/// （architecture test 固定）のため OutputTokens は直接呼べず、対応表をここで最小限複製する。
/// 対応表は OutputTokens.BuildKeyNames と同じ文字列集合（A-Z・0-9・F1-F24・主要修飾/制御キー）。
/// 未対応キーは "Vk:0xNN"（生 virtual key）へ fallback する——未知 token を握りつぶさない。
/// </summary>
public static class KeyCaptureTokenizer
{
    /// <summary>単一キーを output token（"Key:…" または "Vk:0x…"）へ変換する。</summary>
    public static string ToToken(Key key)
    {
        var name = TryKeyName(key);
        if (name is not null)
        {
            return $"Key:{name}";
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        return $"Vk:0x{virtualKey:X2}";
    }

    /// <summary>複数キー（同時押し）を空白区切りの output token 列へ変換する（押下順）。</summary>
    public static string ToChordText(IReadOnlyList<Key> keys) =>
        string.Join(" ", keys.Select(ToToken));

    /// <summary>キーの表示名（録画中の見た目用。"Ctrl" 等の短い表記）。</summary>
    public static string ToDisplayName(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => "Ctrl",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.LWin or Key.RWin => "Win",
        Key.Space => "Space",
        Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.Back => "Backspace",
        _ => TryKeyName(key) ?? key.ToString(),
    };

    private static string? TryKeyName(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return $"F{(int)key - (int)Key.F1 + 1}";
        }

        return key switch
        {
            Key.LeftShift => "LShift",
            Key.RightShift => "RShift",
            Key.LeftCtrl => "LCtrl",
            Key.RightCtrl => "RCtrl",
            Key.LeftAlt => "LAlt",
            Key.RightAlt => "RAlt",
            Key.LWin => "LWin",
            Key.RWin => "RWin",
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Tab => "Tab",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            _ => null,
        };
    }
}
