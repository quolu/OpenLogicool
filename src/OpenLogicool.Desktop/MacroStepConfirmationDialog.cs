using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Desktop;

/// <summary>入力後の実画面を一手ごとに確認する。OKに既定のキー操作を割り当てない。</summary>
public sealed class MacroStepConfirmationDialog : Window
{
    private MacroStepDecision? decision;
    private bool dismissed;
    public string ConfirmationId { get; }
    public event Action<MacroStepDecision>? Decided;

    public MacroStepConfirmationDialog(MacroStepConfirmationRequest request)
    {
        ConfirmationId = request.ConfirmationId;
        Title = $"操作結果の確認 — 手順 {request.StepNumber}";
        Width = 1180; Height = 680; MinWidth = 780; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.ApplyDialog(this); Topmost = true;
        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock { Text = $"手順 {request.StepNumber} を操作しました（{request.ActionLabel}）",
            FontSize = 22, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock { Text = "この操作結果でよいですか？",
            FontSize = 18, Margin = new Thickness(0, 10, 0, 5) });
        heading.Children.Add(new TextBlock { Text = "OKを押すまで、次の操作は実行しません。", Foreground = Theme.Muted });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(new TextBlock { Text = request.ComparisonDetail, TextWrapping = TextWrapping.Wrap });
        footer.Children.Add(new TextBlock { Text = "録画と違っていても、この結果でよければOKを押してください。原本は変更しません。",
            TextWrapping = TextWrapping.Wrap, Foreground = Theme.Muted, Margin = new Thickness(0, 4, 0, 12) });
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var accept = Button("OK・次の手順へ", MacroStepDecision.Accept);
        buttons.Children.Add(Button("違う・ここで補正", MacroStepDecision.Correct));
        buttons.Children.Add(Button("中止", MacroStepDecision.Stop));
        buttons.Children.Add(accept);
        footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var images = new Grid(); images.ColumnDefinitions.Add(new()); images.ColumnDefinitions.Add(new());
        AddImage(images, "録画時の結果", request.RecordedImagePath, 0);
        accept.IsEnabled = AddImage(images, "今回の操作結果", request.ActualImagePath, 1);
        root.Children.Add(images); Content = root;
        Closed += (_, _) => { if (!dismissed) Decided?.Invoke(decision ?? MacroStepDecision.Stop); };
    }

    public void Dismiss() { dismissed = true; Close(); }

    private Button Button(string label, MacroStepDecision choice)
    {
        var button = new Button { Content = label, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (_, _) => { decision = choice; Close(); };
        return button;
    }

    private static bool AddImage(Grid images, string label, string? path, int column)
    {
        var box = new DockPanel { Margin = new Thickness(column == 0 ? 0 : 8, 0, column == 0 ? 8 : 0, 0) };
        var title = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(title, Dock.Top); box.Children.Add(title);
        Grid.SetColumn(box, column); images.Children.Add(box);
        if (path is null)
        {
            box.Children.Add(new TextBlock { Text = "この結果の画像がありません。", TextWrapping = TextWrapping.Wrap });
            return false;
        }
        try
        {
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
            box.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform });
            return true;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or UriFormatException)
        {
            box.Children.Add(new TextBlock { Text = $"結果画像を開けませんでした。{error.Message}", TextWrapping = TextWrapping.Wrap });
            return false;
        }
    }
}
