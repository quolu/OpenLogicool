using System.Windows.Media;

namespace OpenLogicool.Desktop;

/// <summary>
/// mock（docs/ui-mocks/main.html・states.html）の CSS 変数を WPF Brush へ移植したもの（t09 第4段）。
/// mock が dark 固定デザインのため SystemColors 主義はこの段で撤回する（装飾は磨きフェーズへ後置・オーナー裁定）。
/// </summary>
public static class Theme
{
    public static readonly Color BgColor = Color.FromRgb(0x0c, 0x0e, 0x12);
    public static readonly Color ChromeColor = Color.FromRgb(0x16, 0x18, 0x1d);
    public static readonly Color PanelColor = Color.FromRgb(0x14, 0x17, 0x1c);
    public static readonly Color RaisedColor = Color.FromRgb(0x1b, 0x1f, 0x26);
    public static readonly Color SunkenColor = Color.FromRgb(0x0f, 0x12, 0x17);
    public static readonly Color LineColor = Color.FromRgb(0x2a, 0x2f, 0x38);
    public static readonly Color Line2Color = Color.FromRgb(0x3a, 0x41, 0x50);
    public static readonly Color TextColor = Color.FromRgb(0xec, 0xef, 0xf4);
    public static readonly Color MutedColor = Color.FromRgb(0x8b, 0x93, 0xa1);
    public static readonly Color G13Color = Color.FromRgb(0x3e, 0xc9, 0xf0);
    public static readonly Color G600Color = Color.FromRgb(0xe8, 0xa0, 0x4a);
    public static readonly Color OkColor = Color.FromRgb(0x4e, 0xc8, 0x7a);
    public static readonly Color WarnColor = Color.FromRgb(0xe6, 0xb3, 0x4d);
    public static readonly Color DangerColor = Color.FromRgb(0xe2, 0x5b, 0x5b);
    public static readonly Color AccentColor = Color.FromRgb(0x2f, 0x6f, 0xed);

    public static readonly Color Act1Color = Color.FromRgb(0x3d, 0xcc, 0x9a);
    public static readonly Color Act2Color = Color.FromRgb(0xa7, 0x8b, 0xfa);
    public static readonly Color Act3Color = Color.FromRgb(0xf4, 0x72, 0xb6);
    public static readonly Color Act4Color = Color.FromRgb(0x60, 0xa5, 0xfa);

    public static readonly Brush Bg = Freeze(BgColor);
    public static readonly Brush Chrome = Freeze(ChromeColor);
    public static readonly Brush Panel = Freeze(PanelColor);
    public static readonly Brush Raised = Freeze(RaisedColor);
    public static readonly Brush Sunken = Freeze(SunkenColor);
    public static readonly Brush Line = Freeze(LineColor);
    public static readonly Brush Line2 = Freeze(Line2Color);
    public static readonly Brush Text = Freeze(TextColor);
    public static readonly Brush Muted = Freeze(MutedColor);
    public static readonly Brush G13 = Freeze(G13Color);
    public static readonly Brush G600 = Freeze(G600Color);
    public static readonly Brush Ok = Freeze(OkColor);
    public static readonly Brush Warn = Freeze(WarnColor);
    public static readonly Brush Danger = Freeze(DangerColor);
    public static readonly Brush Accent = Freeze(AccentColor);

    private static readonly Color[] ActionColors = [Act1Color, Act2Color, Act3Color, Act4Color];

    /// <summary>操作4色を index で循環させる（mock の act1〜act4 と同じ割当規則）。</summary>
    public static Brush ActionColorAt(int index) => Freeze(ActionColors[((index % ActionColors.Length) + ActionColors.Length) % ActionColors.Length]);

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
