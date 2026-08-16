using System.Windows.Input;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

/// <summary>
/// WPF Key → output token 変換の pure test（t09 第4段残作業②）。
/// OutputTokens.Parse（OpenLogicool.Input）で受理される文字列集合と一致することを、
/// 代表 key 種別（英字・数字・F key・修飾 key・制御 key・未対応 key の fallback）で確認する。
/// Desktop は architecture 契約で Input を参照できないため、期待値はここに複製する
/// （複製元は OutputTokens.BuildKeyNames・OpenLogicool.Input/OutputTokens.cs）。
/// </summary>
public sealed class KeyCaptureTokenizerTests
{
    [Theory]
    [InlineData(Key.A, "Key:A")]
    [InlineData(Key.Z, "Key:Z")]
    [InlineData(Key.D0, "Key:0")]
    [InlineData(Key.D9, "Key:9")]
    [InlineData(Key.F1, "Key:F1")]
    [InlineData(Key.F24, "Key:F24")]
    [InlineData(Key.LeftCtrl, "Key:LCtrl")]
    [InlineData(Key.RightCtrl, "Key:RCtrl")]
    [InlineData(Key.LeftShift, "Key:LShift")]
    [InlineData(Key.RightShift, "Key:RShift")]
    [InlineData(Key.LeftAlt, "Key:LAlt")]
    [InlineData(Key.RightAlt, "Key:RAlt")]
    [InlineData(Key.LWin, "Key:LWin")]
    [InlineData(Key.RWin, "Key:RWin")]
    [InlineData(Key.Space, "Key:Space")]
    [InlineData(Key.Return, "Key:Enter")]
    [InlineData(Key.Tab, "Key:Tab")]
    [InlineData(Key.Escape, "Key:Esc")]
    [InlineData(Key.Back, "Key:Backspace")]
    [InlineData(Key.Insert, "Key:Insert")]
    [InlineData(Key.Delete, "Key:Delete")]
    [InlineData(Key.Home, "Key:Home")]
    [InlineData(Key.End, "Key:End")]
    [InlineData(Key.PageUp, "Key:PageUp")]
    [InlineData(Key.PageDown, "Key:PageDown")]
    [InlineData(Key.Up, "Key:Up")]
    [InlineData(Key.Down, "Key:Down")]
    [InlineData(Key.Left, "Key:Left")]
    [InlineData(Key.Right, "Key:Right")]
    public void Known_keys_map_to_the_matching_output_token(Key key, string expectedToken)
    {
        Assert.Equal(expectedToken, KeyCaptureTokenizer.ToToken(key));
    }

    [Fact]
    public void Unmapped_key_falls_back_to_raw_virtual_key_token()
    {
        // Key.OemPlus 等、名前表には無い key は Vk:0xNN へ fallback する（未知 token を握りつぶさない）。
        var token = KeyCaptureTokenizer.ToToken(Key.OemPlus);

        Assert.StartsWith("Vk:0x", token);
        Assert.Equal(7, token.Length); // "Vk:0x"(5) + 2桁16進
    }

    [Fact]
    public void Chord_is_space_joined_in_press_order()
    {
        var text = KeyCaptureTokenizer.ToChordText([Key.LeftCtrl, Key.C]);

        Assert.Equal("Key:LCtrl Key:C", text);
    }

    [Fact]
    public void Display_name_uses_short_labels_for_modifiers()
    {
        Assert.Equal("Ctrl", KeyCaptureTokenizer.ToDisplayName(Key.LeftCtrl));
        Assert.Equal("Shift", KeyCaptureTokenizer.ToDisplayName(Key.RightShift));
        Assert.Equal("C", KeyCaptureTokenizer.ToDisplayName(Key.C));
    }
}
