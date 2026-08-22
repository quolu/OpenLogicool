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
        // 線画（554x854・オーナー提供の生成画像を透過化）。ボタンは絵のキー位置へ透過で重ねる
        // （キーの G 番号は線画に描かれているため、ボタン面には割当名だけを出す）。
        var canvas = new Canvas { Width = 554, Height = 854 };
        Place(canvas, LineArtImage("g13-lineart.png", 554, 854), 0, 0);

        // M 列（M1〜M3 は層切替・MR は割当可能）
        Place(canvas, ModeKey("M1", "層切替（いつも）"), 122, 219);
        Place(canvas, ModeKey("M2", "層切替（M2）"), 200, 219);
        Place(canvas, ModeKey("M3", "層切替（M3）"), 283, 219);
        Place(canvas, OverlayKey("MR", bindings, () => onAssign("MR"), width: 62, height: 20), 362, 219);

        (string ControlId, double X, double Y)[] keys =
        [
            ("G1", 97, 289), ("G2", 160, 289), ("G3", 220, 290), ("G4", 278, 291), ("G5", 335, 290), ("G6", 392, 289), ("G7", 458, 288),
            ("G8", 97, 344), ("G9", 156, 345), ("G10", 216, 346), ("G11", 276, 347), ("G12", 335, 346), ("G13", 395, 345), ("G14", 456, 343),
            ("G15", 146, 404), ("G16", 212, 405), ("G17", 276, 406), ("G18", 337, 405), ("G19", 407, 403),
            ("G20", 200, 462), ("G21", 273, 463), ("G22", 350, 462),
        ];
        foreach (var (controlId, x, y) in keys)
        {
            Place(canvas, OverlayKey(controlId, bindings, () => onAssign(controlId), width: 54, height: 40), x - 27, y - 20);
        }

        // スティック押込み（右のポッド）
        var stick = OverlayKey("STICK_PRESS", bindings, () => onAssign("STICK_PRESS"), width: 76, height: 76, label: "スティック押込み");
        stick.Style = Theme.CreateFlatButtonStyle(38);
        Place(canvas, stick, 442, 499);

        return WrapFigure(canvas, maxHeight: 470);
    }

    // ─────────────────────────── G600（側面=親指 12 ボタン・上面=ホイール/G7/G8/G-Shift） ───────────────────────────

    public static UIElement BuildG600(IReadOnlyDictionary<string, FigureBinding> bindings, Action<string> onAssign, bool shiftIsButton)
    {
        var board = new StackPanel { Orientation = Orientation.Horizontal };

        // ── 側面（親指側）: 線画（727x262）に透過ボタンを重ねる ──
        var side = new Canvas { Width = 727, Height = 292 };
        Place(side, LineArtImage("g600-side-lineart.png", 727, 262), 0, 22);
        Place(side, new TextBlock
        {
            Text = "親指側（左＝手首側）",
            Foreground = Theme.Muted,
            FontSize = 11,
        }, 16, 0);

        (string ControlId, double X, double Y)[] sideKeys =
        [
            ("G11", 333, 97), ("G14", 382, 79), ("G17", 437, 73), ("G20", 492, 72),
            ("G10", 333, 139), ("G13", 392, 131), ("G16", 445, 124), ("G19", 501, 119),
            ("G9", 343, 186), ("G12", 400, 184), ("G15", 453, 180), ("G18", 508, 171),
        ];
        foreach (var (controlId, x, y) in sideKeys)
        {
            var nub = controlId is "G13" or "G16";
            var key = OverlayKey(controlId, bindings, () => onAssign(controlId), width: 46, height: 40,
                toolTipSuffix: nub ? "（親指のホーム位置）" : string.Empty);
            key.RenderTransform = new RotateTransform(-5);
            Place(side, key, x - 23, y - 20 + 22);
        }

        board.Children.Add(WrapFigure(side, maxHeight: 196));

        // ── 上面: 線画（283x466）に左右クリック・ホイール・チルト・G8/G7・G-Shift を重ねる ──
        var top = new Canvas { Width = 300, Height = 466 };
        Place(top, LineArtImage("g600-top-lineart.png", 283, 466), 0, 0);

        Place(top, OverlayKey("G1", bindings, () => onAssign("G1"), width: 64, height: 96, label: "G1（左クリック）"), 50, 62);
        Place(top, OverlayKey("G2", bindings, () => onAssign("G2"), width: 64, height: 96, label: "G2（右クリック）"), 158, 62);

        // ホイール＝G3 押込み・左右チルト＝G4/G5（絵の ‹ › の位置）
        var wheel = OverlayKey("G3", bindings, () => onAssign("G3"), width: 40, height: 72, label: "G3（ホイール押込み）");
        wheel.Style = Theme.CreateFlatButtonStyle(18);
        Place(top, wheel, 117, 92);
        Place(top, OverlayKey("G4", bindings, () => onAssign("G4"), width: 24, height: 32, label: "G4（左チルト）"), 90, 112);
        Place(top, OverlayKey("G5", bindings, () => onAssign("G5"), width: 24, height: 32, label: "G5（右チルト）"), 160, 112);

        Place(top, OverlayKey("G8", bindings, () => onAssign("G8"), width: 40, height: 28, label: "G8"), 117, 175);
        Place(top, OverlayKey("G7", bindings, () => onAssign("G7"), width: 40, height: 28, label: "G7"), 117, 205);

        // G-Shift（G6）: 右側面の細長ボタン（絵の右端の溝）。層切替のままなら ModeKey、ボタン化済みなら割当可能
        if (shiftIsButton)
        {
            var g6 = Key("G6 G-Shift", "G6", bindings, false, () => onAssign("G6"), width: 88, height: 40);
            Place(top, g6, 205, 330);
        }
        else
        {
            Place(top, ModeKey("G-Shift", "層切替（G-Shift を押している間）。ボタンとして使う切替は上の配置チップの右", wide: true), 210, 336);
        }

        // 右端の溝と G6 をつなぐ引出線
        top.Children.Add(LinePath("M 232,300 L 248,330"));

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
