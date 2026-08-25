using OpenLogicool.Contracts.Playbooks;
using System.Windows;
using System.Windows.Controls;

namespace OpenLogicool.Desktop;

internal sealed class MacroAssignmentDialog : Window
{
    private sealed record ModeChoice(string Label, MacroPlaybackMode Value)
    {
        public override string ToString() => Label;
    }

    private readonly ListBox macros = new() { MinHeight = 220, DisplayMemberPath = nameof(MacroCatalogItem.DisplayLabel) };
    private readonly ComboBox mode = new() { MinWidth = 230 };

    public MacroAssignmentDialog(IReadOnlyList<MacroCatalogItem> items)
    {
        Title = "マクロを選ぶ";
        Width = 620;
        Height = 440;
        MinWidth = 500;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Bg;
        Foreground = Theme.Text;
        macros.ItemsSource = items;
        if (items.Count > 0) macros.SelectedIndex = 0;
        mode.ItemsSource = new[]
        {
            new ModeChoice("AI監視あり（問題stepを修復して更新）", MacroPlaybackMode.AiMonitored),
            new ModeChoice("AI監視なし（保存済み操作だけ）", MacroPlaybackMode.AiFree),
        };
        mode.SelectedIndex = 0;
        Content = Build();
    }

    public string? ResultToken { get; private set; }

    public MacroCatalogItem? SelectedMacro { get; private set; }

    private UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock
        {
            Text = "この操作として再生するマクロを選びます。修復で新しい版ができた場合は最新版を使います。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        Grid.SetRow(macros, 1);
        root.Children.Add(macros);
        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(mode);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = "キャンセル", Padding = new Thickness(12, 6, 12, 6), IsCancel = true };
        var apply = new Button { Content = "このマクロを使う", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        apply.Click += (_, _) => Apply();
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private void Apply()
    {
        if (macros.SelectedItem is not MacroCatalogItem selected)
        {
            return;
        }
        SelectedMacro = selected;
        ResultToken = MacroAssignment.CreateToken(selected, ((ModeChoice)mode.SelectedItem).Value);
        DialogResult = true;
    }
}

public static class MacroAssignment
{
    public static string CreateToken(MacroCatalogItem macro, MacroPlaybackMode mode)
    {
        ArgumentNullException.ThrowIfNull(macro);
        return MacroInvocationTokens.Create(new MacroVersionReference(macro.RouteId, null, mode));
    }
}
