using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenLogicool.Desktop;

/// <summary>
/// G13／G600 の模式図。実機写真（オーナー提供・2026-08-22）を下敷きにした自前の線画で、
/// 写真そのものは埋め込まない（公開 repo に他社製品写真を入れない）。
/// キーは自前ヒットテストではなく実 <see cref="Button"/> で作る（Tab 到達可能・設計 §4）。
/// selector control（G13 の M1/M2/M3・G600 の G6=G-Shift 層切替時）は層切替を表すだけで、
/// ここでは通常の割当対象にしない（クリックしても割当は変わらない）。
/// </summary>
public static class InputStudioFigures
{
    /// <summary>ある control に「いま見ている配置」で載っている割当（色は左の操作一覧と同じ index 由来）。</summary>
    public sealed record FigureBinding(string ActionName, Brush Color);

    // ─────────────────────────── G13（実機写真: 上面・アーチ状 4 行＋右下スティックポッド） ───────────────────────────

    public static UIElement BuildG13(IReadOnlyDictionary<string, FigureBinding> bindings, Action<string> onAssign)
    {
        // 実機写真から生成した線画（548x850）。ボタンは写真上のキー位置へ透過で重ねる
        // （キーの G 番号は線画自体に写っているため、ボタン面には割当名だけを出す）。
        var canvas = new Canvas { Width = 548, Height = 850 };
        Place(canvas, LineArtImage("g13-lineart.png", 548, 850), 0, 0);

        // M 列（M1〜M3 は層切替・MR は割当可能）
        Place(canvas, ModeKey("M1", "層切替（いつも）"), 136, 212);
        Place(canvas, ModeKey("M2", "層切替（M2）"), 200, 212);
        Place(canvas, ModeKey("M3", "層切替（M3）"), 264, 212);
        Place(canvas, OverlayKey("MR", bindings, () => onAssign("MR"), width: 52, height: 24), 326, 212);

        // キー面 4 行（写真上の位置）
        string[] row1 = ["G1", "G2", "G3", "G4", "G5", "G6", "G7"];
        string[] row2 = ["G8", "G9", "G10", "G11", "G12", "G13", "G14"];
        for (var i = 0; i < 7; i++)
        {
            var c1 = row1[i];
            var c2 = row2[i];
            Place(canvas, OverlayKey(c1, bindings, () => onAssign(c1), width: 56, height: 40), 66 + i * 60, 265);
            Place(canvas, OverlayKey(c2, bindings, () => onAssign(c2), width: 56, height: 40), 66 + i * 60, 317);
        }

        string[] row3 = ["G15", "G16", "G17", "G18", "G19"];
        for (var i = 0; i < 5; i++)
        {
            var c3 = row3[i];
            Place(canvas, OverlayKey(c3, bindings, () => onAssign(c3), width: 56, height: 40), 167 + i * 58, 380);
        }

        string[] row4 = ["G20", "G21", "G22"];
        for (var i = 0; i < 3; i++)
        {
            var c4 = row4[i];
            Place(canvas, OverlayKey(c4, bindings, () => onAssign(c4), width: 74, height: 40), 161 + i * 73, 439);
        }

        // スティック押込み（右のポッド）
        var stick = OverlayKey("STICK_PRESS", bindings, () => onAssign("STICK_PRESS"), width: 74, height: 74, label: "スティック押込み");
        stick.Style = Theme.CreateFlatButtonStyle(37);
        Place(canvas, stick, 424, 516);

        return WrapFigure(canvas, maxHeight: 470);
    }

    // ─────────────────────────── G600（実機写真: 側面=親指 12 ボタンの傾き・上面=ホイール/G7/G8/G-Shift） ───────────────────────────

    public static UIElement BuildG600(IReadOnlyDictionary<string, FigureBinding> bindings, Action<string> onAssign, bool shiftIsButton)
    {
        var board = new StackPanel { Orientation = Orientation.Horizontal };

        // ── 側面（親指側）: 実機写真から生成した線画（719x315）に透過ボタンを重ねる ──
        var side = new Canvas { Width = 719, Height = 315 };
        Place(side, LineArtImage("g600-side-lineart.png", 719, 315), 0, 0);
        Place(side, new TextBlock
        {
            Text = "親指側（左＝手首側）",
            Foreground = Theme.Muted,
            FontSize = 11,
        }, 20, 8);

        (string ControlId, double X, double Y)[] sideKeys =
        [
            ("G11", 305, 116), ("G14", 361, 108), ("G17", 418, 101), ("G20", 465, 96),
            ("G10", 286, 169), ("G13", 341, 161), ("G16", 397, 152), ("G19", 450, 144),
            ("G9", 266, 218), ("G12", 320, 211), ("G15", 376, 203), ("G18", 428, 195),
        ];
        foreach (var (controlId, x, y) in sideKeys)
        {
            var nub = controlId is "G13" or "G16";
            var key = OverlayKey(controlId, bindings, () => onAssign(controlId), width: 50, height: 44,
                toolTipSuffix: nub ? "（親指のホーム位置）" : string.Empty);
            key.RenderTransform = new RotateTransform(-10);
            Place(side, key, x - 25, y - 22);
        }

        board.Children.Add(WrapFigure(side, maxHeight: 220));

        // ── 上面: 左右クリック・ホイール（押込み＋チルト）・G8/G7・G-Shift（右側面の細長ボタン） ──
        var top = new Canvas { Width = 312, Height = 430 };
        top.Children.Add(OutlinePath(
            "M 140,14 C 82,14 52,54 48,140 C 46,240 66,350 100,392 C 120,412 160,412 180,392 C 214,350 234,240 232,140 C 228,54 198,14 140,14 Z",
            Theme.Panel));
        // 左右ボタンの割れ目
        top.Children.Add(LinePath("M 140,12 L 140,120"));
        // 右側面の G-Shift 溝（写真右側のスリット）
        top.Children.Add(LinePath("M 232,120 C 246,180 244,240 226,300"));

        var g1 = Key("G1\n左クリック", "G1", bindings, false, () => onAssign("G1"), width: 72, height: 80);
        Place(top, g1, 58, 34);
        var g2 = Key("G2\n右クリック", "G2", bindings, false, () => onAssign("G2"), width: 72, height: 80);
        Place(top, g2, 150, 34);

        // ホイール＝G3 押込み・左右チルト＝G4/G5
        Place(top, Key("G4\n←左チルト", "G4", bindings, false, () => onAssign("G4"), width: 46, height: 40), 60, 122);
        var wheel = Key("G3\nホイール\n押込み", "G3", bindings, false, () => onAssign("G3"), width: 44, height: 66);
        wheel.Style = Theme.CreateFlatButtonStyle(18);
        Place(top, wheel, 118, 110);
        Place(top, Key("G5\n右チルト→", "G5", bindings, false, () => onAssign("G5"), width: 46, height: 40), 174, 122);

        Place(top, Key("G8", "G8", bindings, false, () => onAssign("G8"), width: 44, height: 26), 118, 186);
        Place(top, Key("G7", "G7", bindings, false, () => onAssign("G7"), width: 44, height: 26), 118, 218);

        // G-Shift（G6）: 右側面の細長ボタン。層切替のままなら ModeKey、ボタン化済みなら割当可能
        if (shiftIsButton)
        {
            var g6 = Key("G6 G-Shift", "G6", bindings, false, () => onAssign("G6"), width: 88, height: 44);
            Place(top, g6, 206, 316);
        }
        else
        {
            Place(top, ModeKey("G-Shift", "層切替（G-Shift を押している間）。ボタンとして使う切替は上の配置チップの右", wide: true), 216, 322);
        }

        // 溝と G6 ボタンをつなぐ引出線
        top.Children.Add(LinePath("M 228,290 L 250,316"));

        board.Children.Add(WrapFigure(top, maxHeight: 300));
        return board;
    }

    // ─────────────────────────── 線画部品 ───────────────────────────

    private static Path OutlinePath(string data, Brush fill) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = Theme.Line2,
        StrokeThickness = 2,
        Fill = fill,
    };

    private static Path LinePath(string data) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = Theme.Line2,
        StrokeThickness = 1.5,
    };

    private static Ellipse OutlineEllipse(double x, double y, double width, double height, Brush fill)
    {
        var ellipse = new Ellipse { Width = width, Height = height, Stroke = Theme.Line2, StrokeThickness = 2, Fill = fill };
        Canvas.SetLeft(ellipse, x);
        Canvas.SetTop(ellipse, y);
        return ellipse;
    }

    private static void Place(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }

    /// <summary>写真から生成した線画 asset（埋め込み Resource）を読み込む。</summary>
    private static Image LineArtImage(string assetName, double width, double height) => new()
    {
        Source = new System.Windows.Media.Imaging.BitmapImage(
            new Uri($"pack://application:,,,/OpenLogicool.Desktop;component/Assets/{assetName}")),
        Width = width,
        Height = height,
        Stretch = Stretch.Fill,
    };

    private static readonly Brush OverlayBorder = BuildOverlayBorderBrush();

    private static Brush BuildOverlayBorderBrush()
    {
        var brush = new SolidColorBrush(Theme.Line2Color) { Opacity = 0.45 };
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 線画の上へ重ねる透過ボタン。キーの G 番号は線画に写っているため面には割当名だけを出す。
    /// </summary>
    private static Button OverlayKey(
        string controlId, IReadOnlyDictionary<string, FigureBinding> bindings, Action onClick,
        double width, double height, string? label = null, string toolTipSuffix = "")
    {
        var hasBinding = bindings.TryGetValue(controlId, out var binding);
        var tipName = label ?? controlId;

        var content = new TextBlock
        {
            Text = hasBinding ? binding!.ActionName : string.Empty,
            Foreground = hasBinding ? binding!.Color : Theme.Text,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = width - 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var button = new Button
        {
            Content = content,
            Width = width,
            Height = height,
            Background = Brushes.Transparent,
            BorderBrush = hasBinding ? binding!.Color : OverlayBorder,
            BorderThickness = new Thickness(hasBinding ? 2 : 1),
            Foreground = Theme.Text,
            ToolTip = (hasBinding ? $"{tipName}：{binding!.ActionName}" : $"{tipName}：未割当（クリックで、左で選んでいる操作を載せます）") + toolTipSuffix,
        };
        AutomationProperties.SetName(button, hasBinding ? $"{tipName}（{binding!.ActionName}）" : $"{tipName}（未割当）");
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Canvas を Viewbox で包み、右ペインを圧迫しない高さへ等比縮小する。</summary>
    private static UIElement WrapFigure(Canvas canvas, double maxHeight) => new Viewbox
    {
        Child = canvas,
        MaxHeight = maxHeight,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(4),
    };

    private static Button Key(
        string kid, string controlId, IReadOnlyDictionary<string, FigureBinding> bindings, bool dimple, Action onClick,
        double width = 48, double height = 38, string toolTipSuffix = "")
    {
        var hasBinding = bindings.TryGetValue(controlId, out var binding);

        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            Text = kid,
            Foreground = Theme.Muted,
            FontSize = 9,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (hasBinding)
        {
            content.Children.Add(new TextBlock
            {
                Text = binding!.ActionName,
                Foreground = binding.Color,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = width - 4,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
        else if (dimple)
        {
            // 文字表記だとキー内で潰れるため、指のホーム位置は小さな窪み（凹み風の影）だけで示し、
            // 説明は ToolTip へ回す（オーナー目視レビュー指摘・t09 磨き残し①）。
            content.Children.Add(new Border
            {
                Width = 10,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = Theme.Sunken,
                BorderBrush = Theme.Line2,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        // ToolTip に内部 control ID をそのまま出さない（STICK_PRESS 等は表示名へ）。
        var tipName = controlId == "STICK_PRESS" ? "スティック押込み" : controlId;
        var dimpleToolTipSuffix = dimple && !hasBinding ? "（指のホーム位置）" : string.Empty;
        var button = new Button
        {
            Content = content,
            Width = width,
            Height = height,
            Background = Theme.Sunken,
            BorderBrush = hasBinding ? binding!.Color : Theme.Line2,
            BorderThickness = new Thickness(hasBinding ? 2 : 1),
            Foreground = Theme.Text,
            ToolTip = (hasBinding ? $"{tipName}：{binding!.ActionName}" : $"{tipName}：未割当（クリックで、左で選んでいる操作を載せます）") + dimpleToolTipSuffix + toolTipSuffix,
        };
        AutomationProperties.SetName(button, hasBinding ? $"{tipName}（{binding!.ActionName}）" : $"{tipName}（未割当）");
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Button ModeKey(string kid, string tooltip, bool wide = false)
    {
        var content = new TextBlock
        {
            Text = kid,
            Foreground = Theme.Muted,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var button = new Button
        {
            Content = content,
            Width = wide ? 66 : 56,
            Height = wide ? 30 : 26,
            Background = Theme.Panel,
            BorderBrush = Theme.Muted,
            BorderThickness = new Thickness(1),
            IsEnabled = true,
            ToolTip = tooltip,
        };
        AutomationProperties.SetName(button, $"{kid}：{tooltip}");
        // selector control は通常の割当対象にしない——クリックは何もしない（層切替 UI は上部の配置チップ）。
        button.Click += (_, _) => { };
        return button;
    }
}
