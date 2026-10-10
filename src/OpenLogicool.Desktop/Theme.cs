using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace OpenLogicool.Desktop;

/// <summary>
/// 画面全体の配色・書体・共通部品（オーナー承認済みの見本 docs/ui-mocks/v2/input-studio.html の移植）。
/// 暗色ひとつに決めた意匠で、OS の配色には追従しない。標準コントロールの template は
/// <c>ThemeResources.xaml</c> が持ち、色はここの値を参照する（値の正本はこの class だけ）。
/// </summary>
public static class Theme
{
    public static readonly Color BgColor = Color.FromRgb(0x0b, 0x0d, 0x13);
    public static readonly Color ChromeColor = Color.FromRgb(0x0a, 0x0c, 0x11);
    public static readonly Color PanelColor = Color.FromRgb(0x10, 0x13, 0x1b);
    public static readonly Color RaisedColor = Color.FromRgb(0x1a, 0x1f, 0x2c);
    public static readonly Color SunkenColor = Color.FromRgb(0x08, 0x0a, 0x0f);
    public static readonly Color LineColor = Color.FromArgb(0x14, 0xff, 0xff, 0xff);
    public static readonly Color Line2Color = Color.FromArgb(0x2b, 0xff, 0xff, 0xff);
    public static readonly Color TextColor = Color.FromRgb(0xee, 0xf1, 0xf7);
    public static readonly Color MutedColor = Color.FromRgb(0x8f, 0x98, 0xac);
    public static readonly Color FaintColor = Color.FromRgb(0x5b, 0x63, 0x77);
    public static readonly Color G13Color = Color.FromRgb(0x3e, 0xc9, 0xf0);
    public static readonly Color G600Color = Color.FromRgb(0xf0, 0xa4, 0x4a);
    public static readonly Color OkColor = Color.FromRgb(0x4e, 0xd0, 0x8a);
    public static readonly Color WarnColor = Color.FromRgb(0xf0, 0xc0, 0x4a);
    public static readonly Color DangerColor = Color.FromRgb(0xff, 0x8a, 0x8a);
    public static readonly Color AccentColor = Color.FromRgb(0x4c, 0x7d, 0xff);
    public static readonly Color SpotlightColor = Color.FromRgb(0x1b, 0x23, 0x38);
    public static readonly Color KeyFaceColor = Color.FromRgb(0x0c, 0x0f, 0x17);

    public static readonly Brush Bg = Freeze(BgColor);
    public static readonly Brush Chrome = Freeze(ChromeColor);
    public static readonly Brush Panel = Freeze(PanelColor);
    public static readonly Brush Raised = Freeze(RaisedColor);
    public static readonly Brush Sunken = Freeze(SunkenColor);
    public static readonly Brush Line = Freeze(LineColor);
    public static readonly Brush Line2 = Freeze(Line2Color);
    public static readonly Brush Text = Freeze(TextColor);
    public static readonly Brush Muted = Freeze(MutedColor);
    public static readonly Brush Faint = Freeze(FaintColor);
    public static readonly Brush G13 = Freeze(G13Color);
    public static readonly Brush G600 = Freeze(G600Color);
    public static readonly Brush Ok = Freeze(OkColor);
    public static readonly Brush Warn = Freeze(WarnColor);
    public static readonly Brush Danger = Freeze(DangerColor);
    public static readonly Brush Accent = Freeze(AccentColor);
    public static readonly Brush HoverWash = Freeze(Color.FromArgb(0x12, 0xff, 0xff, 0xff));
    public static readonly Brush CardFill = Freeze(Color.FromArgb(0x06, 0xff, 0xff, 0xff));
    public static readonly Brush SideFill = Freeze(Color.FromArgb(0x8c, 0x0c, 0x0e, 0x14));
    public static readonly Brush BarFill = Freeze(Color.FromArgb(0xb8, 0x09, 0x0b, 0x10));
    public static readonly Brush KeycapInk = Freeze(Color.FromRgb(0x0a, 0x0c, 0x11));

    public static readonly Brush DialogGround = Freeze(Color.FromRgb(0x14, 0x18, 0x22));

    /// <summary>画面の地。中央へ向かって少し明るくなる（図の背後の照明）。</summary>
    public static readonly Brush WindowGround = BuildWindowGround();

    private static readonly Uri FontBase = new("pack://application:,,,/OpenLogicool.Desktop;component/Assets/Fonts/");

    /// <summary>
    /// 本文。Windows に入っている書体だけを使う。BIZ UDPゴシックは行間を持たない書体なので、
    /// 行の高さ（文字の1.4倍）と基線の位置をここで決め、複数行の文章が詰まらないようにする。
    /// </summary>
    public static readonly FontFamily Body = BuildBodyFont();

    /// <summary>英数字の見出しとボタン番号。同梱書体が無い時は本文の書体で出る。</summary>
    public static readonly FontFamily Display = new(FontBase, "./#Oxanium, BIZ UDPGothic, Yu Gothic UI");

    /// <summary>キーキャップの文字。同梱書体が無い時は Consolas で出る。</summary>
    public static readonly FontFamily Mono = new(FontBase, "./#JetBrains Mono, Consolas, BIZ UDPGothic");

    private static FontFamily BuildBodyFont()
    {
        var family = new FontFamily { LineSpacing = 1.4, Baseline = 1.08 };
        family.FamilyMaps.Add(new FontFamilyMap { Target = "BIZ UDPGothic, Yu Gothic UI, Meiryo UI" });
        return family;
    }

    private static readonly Color[] ActionColors =
    [
        Color.FromRgb(0x46, 0xd6, 0xa4), Color.FromRgb(0xa9, 0x8b, 0xff), Color.FromRgb(0xff, 0x7a, 0xb8), Color.FromRgb(0x5a, 0xa8, 0xff),
        Color.FromRgb(0xff, 0xd1, 0x66), Color.FromRgb(0xff, 0x9a, 0x5a), Color.FromRgb(0xff, 0x6b, 0x6b), Color.FromRgb(0xc4, 0xe5, 0x6a),
    ];

    /// <summary>操作の色を index で循環させる（8色）。</summary>
    public static Color ActionColorValueAt(int index) =>
        ActionColors[((index % ActionColors.Length) + ActionColors.Length) % ActionColors.Length];

    public static Brush ActionColorAt(int index) => Freeze(ActionColorValueAt(index));

    public static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>from へ to を amount（0〜1）だけ混ぜた不透明色。</summary>
    public static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + ((to.R - from.R) * amount)),
        (byte)(from.G + ((to.G - from.G) * amount)),
        (byte)(from.B + ((to.B - from.B) * amount)));

    public static Color WithAlpha(Color color, double alpha) => Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B);

    /// <summary>色のついた光（発光の表現）。</summary>
    public static DropShadowEffect Glow(Color color, double blur, double opacity = 0.7, double depth = 0) => new()
    {
        Color = color,
        BlurRadius = blur,
        ShadowDepth = depth,
        Direction = 270,
        Opacity = opacity,
    };

    // ─────────────────────────── 窓 ───────────────────────────

    /// <summary>
    /// 窓へ共通の見た目を当てる（地・書体・標準コントロールの template・暗色のタイトルバー）。
    /// 全ての窓とダイアログの構築子から最初に呼ぶ。
    /// </summary>
    public static void Apply(Window window)
    {
        window.Background = WindowGround;
        window.Foreground = Text;
        window.FontFamily = Body;
        window.FontSize = 13;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        window.UseLayoutRounding = true;
        window.Resources.MergedDictionaries.Add(ControlResources());
        window.Resources[typeof(Button)] = CreateFlatButtonStyle();
        window.SourceInitialized += (_, _) => UseDarkTitleBar(new WindowInteropHelper(window).Handle);
    }

    /// <summary>小窓（ダイアログ）へ共通の見た目を当てる。地は照明なしの一枚の面にする。</summary>
    public static void ApplyDialog(Window window)
    {
        Apply(window);
        window.Background = DialogGround;
    }

    private static ResourceDictionary? controlResources;

    private static ResourceDictionary ControlResources() => controlResources ??=
        (ResourceDictionary)Application.LoadComponent(new Uri("/OpenLogicool.Desktop;component/ThemeResources.xaml", UriKind.Relative));

    private static void UseDarkTitleBar(IntPtr handle)
    {
        // DWMWA_USE_IMMERSIVE_DARK_MODE。未対応の Windows では失敗するだけで、タイトルバーが明色のまま残る。
        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static Brush BuildWindowGround()
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.46),
            Center = new Point(0.5, 0.46),
            RadiusX = 0.62,
            RadiusY = 0.6,
        };
        brush.GradientStops.Add(new GradientStop(SpotlightColor, 0));
        brush.GradientStops.Add(new GradientStop(PanelColor, 0.72));
        brush.GradientStops.Add(new GradientStop(PanelColor, 1));
        brush.Freeze();
        return brush;
    }

    // ─────────────────────────── ボタン ───────────────────────────

    /// <summary>
    /// 全 Button 共通の描画 style。OS 既定 template は無効時・ホバー時に独自の明色で塗り直すため使わない。
    /// 各ボタンの Background／Foreground をそのまま使い、ホバーは薄い光の膜、押下は 1px 沈み、無効は不透明度で表す。
    /// </summary>
    public static Style CreateFlatButtonStyle(double cornerRadius = 8)
    {
        var root = new FrameworkElementFactory(typeof(Grid)) { Name = "root" };

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
        root.AppendChild(border);

        var wash = new FrameworkElementFactory(typeof(Border)) { Name = "wash" };
        wash.SetValue(Border.BackgroundProperty, HoverWash);
        wash.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
        wash.SetValue(UIElement.OpacityProperty, 0.0);
        wash.SetValue(UIElement.IsHitTestVisibleProperty, false);
        root.AppendChild(wash);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        root.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = root };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "wash"));
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.RenderTransformProperty, new TranslateTransform(0, 1.5), "root"));
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82, "root"));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Raised));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Line2));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, FocusRing()));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.38));
        style.Triggers.Add(disabled);
        return style;
    }

    private static Style FocusRing()
    {
        var ring = new FrameworkElementFactory(typeof(Border));
        ring.SetValue(Border.BorderBrushProperty, Accent);
        ring.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        ring.SetValue(FrameworkElement.MarginProperty, new Thickness(-2));
        var style = new Style(typeof(Control));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate { VisualTree = ring }));
        return style;
    }

    /// <summary>主操作（保存・決定）。</summary>
    public static T Primary<T>(T button) where T : Button
    {
        button.Background = Accent;
        button.BorderBrush = Brushes.Transparent;
        button.Foreground = Brushes.White;
        button.FontWeight = FontWeights.Bold;
        button.Effect = Glow(AccentColor, 18, 0.45, 5);
        return button;
    }

    /// <summary>目立たせない操作（枠だけ・文字は控えめ）。</summary>
    public static T Quiet<T>(T button) where T : Button
    {
        button.Background = Brushes.Transparent;
        button.BorderBrush = Line;
        button.Foreground = Muted;
        return button;
    }

    /// <summary>取り返しのつかない操作（削除）。</summary>
    public static T DangerQuiet<T>(T button) where T : Button
    {
        button.Background = Brushes.Transparent;
        button.BorderBrush = Freeze(WithAlpha(DangerColor, 0.3));
        button.Foreground = Danger;
        return button;
    }

    // ─────────────────────────── 部品 ───────────────────────────

    /// <summary>キーキャップ形の札（送るキーの表示）。</summary>
    public static Border Keycap(string legend, Color color, double height = 34, double minWidth = 38, double fontSize = 12)
    {
        var face = new Grid();
        face.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Background = Freeze(Color.FromArgb(0x40, 0, 0, 0)), CornerRadius = new CornerRadius(0, 0, 8, 8) });
        face.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Freeze(Color.FromArgb(0x73, 0xff, 0xff, 0xff)), Margin = new Thickness(7, 0, 7, 0) });
        face.Children.Add(new TextBlock
        {
            Text = legend,
            FontFamily = Mono,
            FontWeight = FontWeights.Bold,
            FontSize = fontSize,
            Foreground = KeycapInk,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 1),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return new Border
        {
            Height = height,
            MinWidth = minWidth,
            MaxWidth = 132,
            CornerRadius = new CornerRadius(8),
            Background = Freeze(color),
            Child = face,
            Effect = Glow(color, 14, 0.45, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>囲み（薄い枠と、ごく薄い面）。</summary>
    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Background = CardFill,
        Padding = padding ?? new Thickness(12, 11, 12, 11),
        Child = child,
    };

    /// <summary>光る小さな丸（デバイス・状態の印）。</summary>
    public static Border Dot(Color color, double size = 8) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = Freeze(color),
        Effect = Glow(color, 8, 0.9),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>ゆっくり明滅させる（稼働中・未保存の印）。</summary>
    public static void Breathe(UIElement element, double from, double to, double seconds)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }
}
