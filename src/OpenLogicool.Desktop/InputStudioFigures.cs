using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OpenLogicool.Desktop;

/// <summary>
/// G13／G600 の模式図（mock docs/ui-mocks/main.html の .g13-shell / .g600-board 相当）。
/// キーは自前ヒットテストではなく実 <see cref="Button"/> で作る（Tab 到達可能・設計 §4）。
/// selector control（G13 の M1/M2/M3・G600 の G6=G-Shift）は層切替を表すだけで、
/// ここでは通常の割当対象にしない（クリックしても割当は変わらない）。
/// LCD1〜4／LCD_AUX（G13）は mock 同様この図には描かない——右ペインの割当先ピッカーからは選べる。
/// </summary>
public static class InputStudioFigures
{
    /// <summary>ある control に「いま見ている配置」で載っている割当（色は左の操作一覧と同じ index 由来）。</summary>
    public sealed record FigureBinding(string ActionName, Brush Color);

    public static UIElement BuildG13(IReadOnlyDictionary<string, FigureBinding> bindings, Action<string> onAssign)
    {
        var shell = new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.Line2,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14, 14, 30, 10),
            Padding = new Thickness(14, 10, 14, 12),
        };

        var stack = new StackPanel();

        var lcd = new Border
        {
            Background = Theme.Sunken,
            BorderBrush = Theme.G13,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Width = 180,
            Height = 36,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        lcd.Child = new TextBlock
        {
            Text = "画面：接続中",
            Foreground = Theme.G13,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(lcd);

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        modeRow.Children.Add(ModeKey("M1", "層切替（いつも）"));
        modeRow.Children.Add(ModeKey("M2", "層切替（M2）"));
        modeRow.Children.Add(ModeKey("M3", "層切替（M3）"));
        modeRow.Children.Add(Key("MR", "MR", bindings, dimple: false, () => onAssign("MR")));
        stack.Children.Add(modeRow);

        var well = new Border { Background = Theme.Sunken, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 10, 8, 8) };
        var wellStack = new StackPanel();

        wellStack.Children.Add(Row(
            Key("G1", "G1", bindings, false, () => onAssign("G1")),
            Key("G2", "G2", bindings, false, () => onAssign("G2")),
            Key("G3", "G3", bindings, false, () => onAssign("G3")),
            Key("G4", "G4", bindings, true, () => onAssign("G4")),
            Key("G5", "G5", bindings, false, () => onAssign("G5")),
            Key("G6", "G6", bindings, false, () => onAssign("G6")),
            Key("G7", "G7", bindings, false, () => onAssign("G7"))));

        wellStack.Children.Add(Row(
            Key("G8", "G8", bindings, false, () => onAssign("G8")),
            Key("G9", "G9", bindings, false, () => onAssign("G9")),
            Key("G10", "G10", bindings, true, () => onAssign("G10")),
            Key("G11", "G11", bindings, true, () => onAssign("G11")),
            Key("G12", "G12", bindings, true, () => onAssign("G12")),
            Key("G13", "G13", bindings, false, () => onAssign("G13")),
            Key("G14", "G14", bindings, false, () => onAssign("G14"))));

        wellStack.Children.Add(Row(
            Key("G15", "G15", bindings, false, () => onAssign("G15")),
            Key("G16", "G16", bindings, false, () => onAssign("G16")),
            Key("G17", "G17", bindings, false, () => onAssign("G17")),
            Key("G18", "G18", bindings, false, () => onAssign("G18")),
            Key("G19", "G19", bindings, false, () => onAssign("G19"))));

        wellStack.Children.Add(Row(
            Key("G20", "G20", bindings, false, () => onAssign("G20")),
            Key("G21", "G21", bindings, false, () => onAssign("G21")),
            Key("G22", "G22", bindings, false, () => onAssign("G22"))));

        well.Child = wellStack;
        stack.Children.Add(well);

        var stick = Key("スティック\n押込み", "STICK_PRESS", bindings, false, () => onAssign("STICK_PRESS"), width: 60, height: 60);
        stick.HorizontalAlignment = HorizontalAlignment.Right;
        stick.Margin = new Thickness(0, 10, 4, 0);
        stack.Children.Add(stick);

        shell.Child = stack;
        return shell;
    }

    public static UIElement BuildG600(IReadOnlyDictionary<string, FigureBinding> bindings, Action<string> onAssign, bool shiftIsButton)
    {
        var board = new StackPanel { Orientation = Orientation.Horizontal };

        var side = new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.Line2,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16, 8, 8, 24),
            Padding = new Thickness(10, 8, 10, 10),
            Margin = new Thickness(0, 0, 14, 0),
        };
        var sideStack = new StackPanel();
        sideStack.Children.Add(new TextBlock
        {
            Text = "親指側 · 横 4 列 × 縦 3 段（手前＝手首側）",
            Foreground = Theme.Muted,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var grid = new UniformGrid { Columns = 4, Rows = 3 };
        foreach (var controlId in new[]
                 {
                     "G11", "G14", "G17", "G20",
                     "G10", "G13", "G16", "G19",
                     "G9", "G12", "G15", "G18",
                 })
        {
            var nub = controlId is "G13" or "G16";
            grid.Children.Add(SideKey(controlId, bindings, nub, () => onAssign(controlId)));
        }

        sideStack.Children.Add(grid);
        side.Child = sideStack;
        board.Children.Add(side);

        var mouse = new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.Line2,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(48),
            Padding = new Thickness(12, 12, 12, 16),
            Width = 220,
        };
        var mouseStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        mouseStack.Children.Add(new TextBlock
        {
            Text = "上面",
            Foreground = Theme.Muted,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var topRow = new StackPanel { Orientation = Orientation.Horizontal };
        topRow.Children.Add(Key("G1\n左クリック", "G1", bindings, false, () => onAssign("G1"), width: 62, height: 72));
        topRow.Children.Add(Key("G3\nホイール\n押込み", "G3", bindings, false, () => onAssign("G3"), width: 56, height: 72));
        topRow.Children.Add(Key("G2\n右クリック", "G2", bindings, false, () => onAssign("G2"), width: 62, height: 72));
        mouseStack.Children.Add(topRow);

        var tiltRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 4) };
        tiltRow.Children.Add(Key("G4 ←左チルト", "G4", bindings, false, () => onAssign("G4"), width: 88, height: 30));
        tiltRow.Children.Add(Key("G5 右チルト→", "G5", bindings, false, () => onAssign("G5"), width: 88, height: 30));
        mouseStack.Children.Add(tiltRow);

        var g78 = new StackPanel { Width = 56 };
        g78.Children.Add(Key("G7", "G7", bindings, false, () => onAssign("G7"), width: 56, height: 22));
        g78.Children.Add(Key("G8", "G8", bindings, false, () => onAssign("G8"), width: 56, height: 22));
        mouseStack.Children.Add(g78);

        var bodyRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        var palm = new Border
        {
            Width = 70,
            Height = 36,
            Background = Theme.Sunken,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1, 0, 1, 1),
            CornerRadius = new CornerRadius(0, 0, 36, 36),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        bodyRow.Children.Add(palm);
        if (shiftIsButton)
        {
            var g6 = Key("G6 G-Shift", "G6", bindings, false, () => onAssign("G6"), width: 72, height: 44);
            g6.HorizontalAlignment = HorizontalAlignment.Right;
            g6.VerticalAlignment = VerticalAlignment.Bottom;
            bodyRow.Children.Add(g6);
        }
        else
        {
            bodyRow.Children.Add(ModeKey("G-Shift", "層切替（G-Shift を押している間）。ボタンとして使う切替は上の配置チップの右", wide: true));
        }

        mouseStack.Children.Add(bodyRow);

        mouse.Child = mouseStack;
        board.Children.Add(mouse);

        return board;
    }

    private static StackPanel Row(params UIElement[] keys)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var key in keys)
        {
            row.Children.Add(key);
        }

        return row;
    }

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

        var dimpleToolTipSuffix = dimple && !hasBinding ? "（指のホーム位置）" : string.Empty;
        var button = new Button
        {
            Content = content,
            Width = width,
            Height = height,
            Margin = new Thickness(2),
            Background = Theme.Sunken,
            BorderBrush = hasBinding ? binding!.Color : Theme.Line2,
            BorderThickness = new Thickness(hasBinding ? 2 : 1),
            Foreground = Theme.Text,
            ToolTip = (hasBinding ? $"{controlId}：{binding!.ActionName}" : $"{controlId}：未割当（クリックで、左で選んでいる操作を載せます）") + dimpleToolTipSuffix + toolTipSuffix,
        };
        AutomationProperties.SetName(button, hasBinding ? $"{controlId}（{binding!.ActionName}）" : $"{controlId}（未割当）");
        button.Click += (_, _) => onClick();
        return button;
    }

    private static UIElement SideKey(string controlId, IReadOnlyDictionary<string, FigureBinding> bindings, bool nub, Action onClick) =>
        Key(controlId, controlId, bindings, dimple: false, onClick, width: 46, height: 36,
            toolTipSuffix: nub ? "（親指のホーム位置）" : string.Empty);

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
            Width = wide ? 60 : 40,
            Height = wide ? 30 : 26,
            Margin = new Thickness(2),
            Background = Theme.Panel,
            BorderBrush = Theme.Muted,
            BorderThickness = new Thickness(1),
            IsEnabled = true,
            ToolTip = tooltip,
        };
        if (wide)
        {
            button.HorizontalAlignment = HorizontalAlignment.Right;
            button.VerticalAlignment = VerticalAlignment.Bottom;
        }

        AutomationProperties.SetName(button, $"{kid}：{tooltip}");
        // selector control は通常の割当対象にしない——クリックは何もしない（層切替 UI は上部の配置チップ）。
        button.Click += (_, _) => { };
        return button;
    }
}
