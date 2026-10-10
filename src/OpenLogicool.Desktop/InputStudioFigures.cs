using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using OpenLogicool.Contracts.Devices.G13;

namespace OpenLogicool.Desktop;

/// <summary>
/// G13／G600 の図（オーナー承認済みの見本 docs/ui-mocks/v2/input-studio.html 準拠）。
/// 実機写真を下敷きにした自前の線画の上へ、押せるキーキャップを重ねる（写真そのものは埋め込まない）。
/// 割り当てたキーは操作の色で光り、選んでいる操作のキーだけを強く光らせる。
/// キーは自前ヒットテストではなく実 <see cref="Button"/> で作る（Tab 到達可能）。
/// </summary>
public static class InputStudioFigures
{
    /// <summary>ある control に「いま見ている配置」で載っている割当。色は左の操作一覧と同じ。</summary>
    public sealed record FigureBinding(string ActionId, string ActionName, string KeyLegend, Color Color);

    /// <summary>図1枚分の入力。<paramref name="OnNotice"/> は割当できない場所を押した時の案内文。</summary>
    public sealed record FigureRequest(
        IReadOnlyDictionary<string, FigureBinding> Bindings,
        string? SelectedActionId,
        Action<string> OnKey,
        Action<string> OnNotice);

    /// <summary>組み上がった図。実機のボタンが押された時に、同じ場所を光らせられる。</summary>
    public sealed class FigureView
    {
        private readonly Dictionary<string, (Canvas Canvas, Button Key, Color Color)> keys = new(StringComparer.Ordinal);

        internal FigureView()
        {
        }

        public UIElement Root { get; internal set; } = null!;

        internal void Register(string controlId, Canvas canvas, Button key, Color color) => keys[controlId] = (canvas, key, color);

        /// <summary>control の位置から輪を広げる（押下の手応え）。図に無い control は何もしない。</summary>
        public void Pulse(string controlId)
        {
            if (!keys.TryGetValue(controlId, out var entry) || !SystemParameters.ClientAreaAnimation)
            {
                return;
            }

            var (canvas, key, color) = entry;
            var scale = new ScaleTransform(1, 1);
            var ring = new Border
            {
                Width = key.Width,
                Height = key.Height,
                BorderBrush = Theme.Freeze(color),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(Math.Min(key.Width, key.Height) >= 60 && Math.Abs(key.Width - key.Height) < 1 ? key.Width / 2 : 10),
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = scale,
            };
            Canvas.SetLeft(ring, Canvas.GetLeft(key));
            Canvas.SetTop(ring, Canvas.GetTop(key));
            canvas.Children.Add(ring);

            var duration = TimeSpan.FromMilliseconds(550);
            var grow = new DoubleAnimation(1, 1.5, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var fade = new DoubleAnimation(1, 0, duration);
            fade.Completed += (_, _) => canvas.Children.Remove(ring);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    // ─────────────────────────── G13（キーの並び＋右下のスティック＋手のひら側に倒す向き） ───────────────────────────

    /// <summary>
    /// G13 の図。<paramref name="layerBySelector"/> は M1〜M3 が切り替える配置で、図の M キーを押すと
    /// <paramref name="onLayer"/> でその配置へ切り替える。
    /// </summary>
    public static FigureView BuildG13(
        FigureRequest request,
        IReadOnlyDictionary<string, string> layerBySelector,
        string currentLayerId,
        Action<string> onLayer)
    {
        const double cropTop = 40;
        const double cropBottom = 748;
        var view = new FigureView();
        var canvas = new Canvas { Width = 554, Height = 854 };
        var art = LineArtImage("g13-lineart.png", 554, 854);
        // 手のひら側は下へ向かって消える（図の主役はキーの並び）。
        art.OpacityMask = new LinearGradientBrush(
            [new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black, 0.78), new GradientStop(Colors.Transparent, 0.88)],
            new Point(0, 0),
            new Point(0, 1));
        Place(canvas, art, 0, 0);

        foreach (var (kid, x) in new[] { ("M1", 122.0), ("M2", 200.0), ("M3", 283.0) })
        {
            Place(canvas, LayerKey(kid, layerBySelector, currentLayerId, onLayer), x, 217);
        }

        Place(canvas, Keycap(view, canvas, request, "MR", 62, 22, "MR", showText: false), 362, 217);

        (string ControlId, double X, double Y)[] keys =
        [
            ("G1", 97, 289), ("G2", 160, 289), ("G3", 220, 290), ("G4", 278, 291), ("G5", 335, 290), ("G6", 392, 289), ("G7", 458, 288),
            ("G8", 97, 344), ("G9", 156, 345), ("G10", 216, 346), ("G11", 276, 347), ("G12", 335, 346), ("G13", 395, 345), ("G14", 456, 343),
            ("G15", 146, 404), ("G16", 212, 405), ("G17", 276, 406), ("G18", 337, 405), ("G19", 407, 403),
            ("G20", 200, 462), ("G21", 273, 463), ("G22", 350, 462),
        ];
        foreach (var (controlId, x, y) in keys)
        {
            Place(canvas, Keycap(view, canvas, request, controlId, 56, 42, controlId), x - 28, y - 21);
        }

        Place(canvas, Keycap(view, canvas, request, "STICK_PRESS", 68, 68, "スティック押込み", radius: 34), 446, 503);

        // スティックを倒す4方向。絵のスティックは小さく4つを載せられないため、手のひら側へ十字に並べて点線でつなぐ。
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 392,668 L 424,668 L 458,572"),
            Stroke = Theme.Freeze(Color.FromArgb(0x55, 0xff, 0xff, 0xff)),
            StrokeThickness = 1.2,
            IsHitTestVisible = false,
        });
        (string ControlId, string EmptyLabel, double X, double Y)[] directions =
        [
            (G13Controls.StickUp, "↑ 上", 261, 600),
            (G13Controls.StickLeft, "← 左", 195, 646),
            (G13Controls.StickRight, "右 →", 327, 646),
            (G13Controls.StickDown, "↓ 下", 261, 692),
        ];
        foreach (var (controlId, emptyLabel, x, y) in directions)
        {
            Place(canvas, Keycap(view, canvas, request, controlId, 64, 44, G13StickName(controlId)!, emptyLabel), x, y);
        }

        Place(canvas, new TextBlock
        {
            Text = "スティックを倒す向き",
            Foreground = Theme.Muted,
            FontSize = 10.5,
            Width = 64,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false,
        }, 261, 653);

        var crop = new Canvas { Width = 554, Height = cropBottom - cropTop, ClipToBounds = true };
        Place(crop, canvas, 0, -cropTop);
        view.Root = Scaled(crop);
        return view;
    }

    /// <summary>G13 のスティック系 control の表示名（内部 control ID を画面へ出さない）。該当しなければ null。</summary>
    public static string? G13StickName(string controlId) => controlId switch
    {
        "STICK_PRESS" => "スティック押込み",
        G13Controls.StickUp => "スティック上",
        G13Controls.StickDown => "スティック下",
        G13Controls.StickLeft => "スティック左",
        G13Controls.StickRight => "スティック右",
        _ => null,
    };

    /// <summary>G600 上面の control の物理位置の呼び名。該当しなければ null。</summary>
    public static string? G600PhysicalName(string controlId) => controlId switch
    {
        "G1" => "左クリック",
        "G2" => "右クリック",
        "G3" => "ホイール押込み",
        "G4" => "左チルト",
        "G5" => "右チルト",
        "G6" => "G-Shift",
        "G7" => "上面ボタン上",
        "G8" => "上面ボタン下",
        _ => null,
    };

    // ─────────────────────────── G600（側面=親指 12 ボタン・上面=対応表つき） ───────────────────────────

    public static FigureView BuildG600(FigureRequest request, bool shiftIsButton)
    {
        const string shiftNotice = "G-Shift は配置の切替です。ボタンとして使う時は上のスイッチを入れてください";
        var view = new FigureView();
        var board = new StackPanel();
        board.Children.Add(new TextBlock
        {
            Text = "親指側（左が手首側）の 12 ボタン",
            Foreground = Theme.Faint,
            FontSize = 11,
            Margin = new Thickness(12, 0, 0, 4),
        });

        // ── 側面（親指側）: 線画（727x262）へキーを重ねる ──
        var side = new Canvas { Width = 727, Height = 270 };
        Place(side, LineArtImage("g600-side-lineart.png", 727, 262), 0, 0);
        (string ControlId, double X, double Y)[] sideKeys =
        [
            ("G11", 333, 97), ("G14", 382, 79), ("G17", 437, 73), ("G20", 492, 72),
            ("G10", 333, 139), ("G13", 392, 131), ("G16", 445, 124), ("G19", 501, 119),
            ("G9", 343, 186), ("G12", 400, 184), ("G15", 453, 180), ("G18", 508, 171),
        ];
        foreach (var (controlId, x, y) in sideKeys)
        {
            var home = controlId is "G13" or "G16";
            var key = Keycap(view, side, request, controlId, 48, 40, controlId, toolTipSuffix: home ? "（親指のホーム位置）" : string.Empty);
            key.RenderTransformOrigin = new Point(0.5, 0.5);
            key.RenderTransform = new RotateTransform(-5);
            Place(side, key, x - 24, y - 20);
        }

        board.Children.Add(new Border { Child = side, LayoutTransform = new ScaleTransform(0.95, 0.95), HorizontalAlignment = HorizontalAlignment.Center });

        // ── 上面: 小さいボタンは絵に色だけを載せ、名前は右の対応表で読む ──
        var top = new Canvas { Width = 300, Height = 466 };
        Place(top, LineArtImage("g600-top-lineart.png", 283, 466), 0, 0);
        (string ControlId, double X, double Y, double Width, double Height, double Radius)[] spots =
        [
            ("G1", 50, 62, 64, 96, 8), ("G2", 158, 62, 64, 96, 8), ("G3", 117, 92, 40, 72, 18), ("G4", 90, 112, 24, 32, 8), ("G5", 160, 112, 24, 32, 8),
            ("G8", 117, 175, 40, 28, 8), ("G7", 117, 205, 40, 28, 8),
        ];
        var spotByControl = new Dictionary<string, Button>(StringComparer.Ordinal);
        foreach (var (controlId, x, y, width, height, radius) in spots)
        {
            var spot = Keycap(view, top, request, controlId, width, height, G600Label(controlId), showText: false, radius: radius);
            spotByControl[controlId] = spot;
            Place(top, spot, x, y);
        }

        Button shiftSpot;
        if (shiftIsButton)
        {
            shiftSpot = Keycap(view, top, request, "G6", 70, 34, G600Label("G6"), showText: false);
        }
        else
        {
            shiftSpot = ModeKey("G-Shift", "配置の切替（G-Shift を押している間）", 70, 34, isOn: false);
            shiftSpot.Click += (_, _) => request.OnNotice(shiftNotice);
        }

        spotByControl["G6"] = shiftSpot;
        Place(top, shiftSpot, 208, 334);

        var legend = new StackPanel { Width = 264, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 0, 0) };
        foreach (var controlId in new[] { "G1", "G2", "G3", "G4", "G5", "G7", "G8", "G6" })
        {
            var isShiftSelector = controlId == "G6" && !shiftIsButton;
            var row = LegendRow(request, controlId, isShiftSelector);
            var spot = spotByControl[controlId];
            var restingBorder = spot.BorderBrush;
            row.MouseEnter += (_, _) => spot.BorderBrush = Theme.Text;
            row.MouseLeave += (_, _) => spot.BorderBrush = restingBorder;
            row.Click += (_, _) =>
            {
                if (isShiftSelector)
                {
                    request.OnNotice(shiftNotice);
                }
                else
                {
                    request.OnKey(controlId);
                }
            };
            legend.Children.Add(row);
        }

        var low = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        low.Children.Add(new Border { Child = top, LayoutTransform = new ScaleTransform(0.72, 0.72) });
        low.Children.Add(legend);
        board.Children.Add(low);

        view.Root = Scaled(board);
        return view;
    }

    private static string G600Label(string controlId) =>
        G600PhysicalName(controlId) is { } name ? $"{controlId}（{name}）" : controlId;

    private static Button LegendRow(FigureRequest request, string controlId, bool isShiftSelector)
    {
        var hasBinding = request.Bindings.TryGetValue(controlId, out var binding) && !isShiftSelector;
        var isSelected = hasBinding && binding!.ActionId == request.SelectedActionId;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = controlId, FontFamily = Theme.Display, FontWeight = FontWeights.SemiBold, FontSize = 10.5, Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center });
        var name = new TextBlock { Text = G600PhysicalName(controlId) ?? string.Empty, Foreground = Theme.Muted, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var state = new TextBlock
        {
            Text = isShiftSelector ? "配置の切替" : hasBinding ? binding!.ActionName : "空き",
            Foreground = hasBinding ? Theme.Freeze(binding!.Color) : Theme.Muted,
            FontWeight = hasBinding ? FontWeights.Bold : FontWeights.Normal,
            FontSize = 12,
            MaxWidth = 110,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(state, 2);
        grid.Children.Add(state);

        var row = new Button
        {
            Content = grid,
            Height = 31,
            Margin = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(9, 0, 9, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = hasBinding ? Theme.Freeze(Theme.WithAlpha(binding!.Color, 0.13)) : Brushes.Transparent,
            BorderBrush = hasBinding ? Theme.Freeze(Theme.WithAlpha(binding!.Color, 0.6)) : Brushes.Transparent,
            Opacity = isShiftSelector ? 0.55 : 1,
            ToolTip = isShiftSelector
                ? "G-Shift は配置の切替です"
                : hasBinding ? $"{G600Label(controlId)}：{binding!.ActionName}" : $"{G600Label(controlId)}：空き（クリックで、左で選んでいる操作を載せます）",
        };
        if (isSelected)
        {
            row.BorderBrush = Theme.Freeze(binding!.Color);
            row.Effect = Theme.Glow(binding.Color, 14, 0.7);
        }

        AutomationProperties.SetName(row, $"対応表 {G600Label(controlId)}");
        return row;
    }

    // ─────────────────────────── 部品 ───────────────────────────

    private static readonly Dictionary<double, Style> KeyStyles = [];

    private static Style KeyStyle(double radius) =>
        KeyStyles.TryGetValue(radius, out var style) ? style : KeyStyles[radius] = Theme.CreateFlatButtonStyle(radius);

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
        IsHitTestVisible = false,
    };

    /// <summary>図を、置かれた場所いっぱいへ等比で広げる（窓が大きいほど図も大きくなる）。</summary>
    private static Viewbox Scaled(UIElement child)
    {
        var viewbox = new Viewbox
        {
            Child = child,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // 図の中は拡大縮小されるため、等倍前提の文字描画（Display）を使わない。
        TextOptions.SetTextFormattingMode(viewbox, TextFormattingMode.Ideal);
        return viewbox;
    }

    /// <summary>
    /// 線画へ重ねる押せるキー。空きは透明（線画のボタン番号が見える）、割当済みは操作の色で光る。
    /// 選んでいる操作のキーは強く光り、ほかの割当済みキーは少し暗くなる。
    /// </summary>
    private static Button Keycap(
        FigureView view,
        Canvas canvas,
        FigureRequest request,
        string controlId,
        double width,
        double height,
        string tipName,
        string? emptyLabel = null,
        bool showText = true,
        double radius = 8,
        string toolTipSuffix = "")
    {
        var hasBinding = request.Bindings.TryGetValue(controlId, out var binding);
        var isSelected = hasBinding && binding!.ActionId == request.SelectedActionId;
        var anySelectedHere = request.SelectedActionId is not null
            && request.Bindings.Values.Any(candidate => candidate.ActionId == request.SelectedActionId);

        var button = new Button
        {
            Style = KeyStyle(radius),
            Width = width,
            Height = height,
            Padding = new Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            ToolTip = (hasBinding ? $"{tipName}：{binding!.ActionName}" : $"{tipName}：空き（クリックで、左で選んでいる操作を載せます）") + toolTipSuffix,
        };

        if (hasBinding)
        {
            var color = binding!.Color;
            // 選んでいる操作がこの配置にある間、ほかの割当済みキーは光を落とす。
            // 不透明度で暗くすると下の線画が透けるため、色そのものを弱める。
            var dimmed = anySelectedHere && !isSelected;
            button.Background = Theme.Freeze(Theme.Mix(Theme.KeyFaceColor, color, dimmed ? 0.17 : 0.26));
            button.BorderBrush = Theme.Freeze(dimmed ? Theme.Mix(Theme.KeyFaceColor, color, 0.5) : color);
            button.BorderThickness = new Thickness(isSelected ? 2 : 1);
            button.Effect = isSelected ? Theme.Glow(color, 28, 0.9) : dimmed ? null : Theme.Glow(color, 16, 0.55, 5);
            if (showText)
            {
                var face = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                face.Children.Add(new TextBlock
                {
                    Text = binding.ActionName,
                    Foreground = dimmed ? Theme.Muted : Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = width - 6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                if (height >= 36 && binding.KeyLegend.Length > 0)
                {
                    face.Children.Add(new TextBlock
                    {
                        Text = binding.KeyLegend,
                        Foreground = Theme.Freeze(dimmed ? Theme.Mix(Theme.KeyFaceColor, color, 0.6) : color),
                        FontFamily = Theme.Mono,
                        FontWeight = FontWeights.Bold,
                        FontSize = 9,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = width - 6,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    });
                }

                button.Content = face;
            }

            view.Register(controlId, canvas, button, color);
        }
        else
        {
            if (emptyLabel is not null)
            {
                button.Content = new TextBlock { Text = emptyLabel, Foreground = Theme.Muted, FontSize = 11 };
                button.BorderBrush = Theme.Line;
            }

            view.Register(controlId, canvas, button, Theme.TextColor);
        }

        AutomationProperties.SetName(button, hasBinding ? $"{tipName}（{binding!.ActionName}）" : $"{tipName}（未割当）");
        button.Click += (_, _) => request.OnKey(controlId);
        return button;
    }

    /// <summary>配置を切り替えるキー（G13 の M1〜M3）。いま見ている配置のキーが点灯する。</summary>
    private static Button LayerKey(
        string kid,
        IReadOnlyDictionary<string, string> layerBySelector,
        string currentLayerId,
        Action<string> onLayer)
    {
        var hasLayer = layerBySelector.TryGetValue(kid, out var layerId);
        var key = ModeKey(kid, hasLayer ? "この配置へ切り替えます" : "配置の切替", 62, 22, isOn: hasLayer && layerId == currentLayerId);
        if (hasLayer)
        {
            key.Click += (_, _) => onLayer(layerId!);
        }

        return key;
    }

    private static Button ModeKey(string kid, string toolTip, double width, double height, bool isOn)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = kid,
                FontFamily = Theme.Display,
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Foreground = isOn ? Theme.KeycapInk : Theme.Muted,
            },
            Width = width,
            Height = height,
            Padding = new Thickness(0),
            Background = isOn ? Theme.G13 : Theme.Sunken,
            BorderBrush = isOn ? Theme.G13 : Theme.Line,
            ToolTip = $"{kid}：{toolTip}",
        };
        if (isOn)
        {
            button.Effect = Theme.Glow(Theme.G13Color, 16, 0.8);
        }

        AutomationProperties.SetName(button, $"{kid}：{toolTip}");
        return button;
    }
}
