using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Desktop;

/// <summary>
/// 遠隔表示（ゲームの映像と音を自分の中継サーバーへ送る）の受け付けの入り切りと状態表示。
/// 受け付けている間、見る URL を開いた端末がいる時だけ Host が送る。送信の処理は Host へ委ねる。
/// </summary>
public sealed class RemoteViewPanel : UserControl
{
    private const string StandardLabel = "標準（720p・約3Mbps）";
    private const string FineLabel = "きれい（1080p・約8Mbps）";
    private const string NoViewerUrl = "中継サーバーが未設定です";

    private readonly IRemoteViewIntents intents;
    private readonly Action<string> copyToClipboard;
    private readonly Button toggle = new() { Padding = new Thickness(20, 10, 20, 10) };
    private readonly TextBlock status = new() { FontSize = 26, FontWeight = FontWeights.Bold };
    private readonly TextBlock stalled = new()
    {
        Text = "映像のコマが届いていません（ゲームの画面が止まっているか、最小化されています）",
        TextWrapping = TextWrapping.Wrap, Foreground = Theme.Warn, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock operationError = new() { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Danger, Margin = new Thickness(0, 8, 0, 0) };
    private readonly RadioButton standard = new() { Content = StandardLabel, Margin = new Thickness(0, 0, 24, 0) };
    private readonly RadioButton fine = new() { Content = FineLabel };
    private readonly TextBlock viewerUrl = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button copy = new() { Content = "コピー", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBox publishUrl = new() { MinWidth = 380 };
    private readonly TextBox viewerUrlField = new() { MinWidth = 380 };
    private readonly TextBox publishUser = new() { MinWidth = 380 };
    private readonly PasswordBox publishPassword = new() { MinWidth = 380 };
    private readonly TextBlock passwordHint = new() { Foreground = Theme.Muted, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button save = new() { Content = "保存", Padding = new Thickness(20, 8, 20, 8) };
    private readonly TextBlock settingsMessage = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string? currentViewerUrl;
    private bool applyingQuality;

    /// <param name="copyToClipboard">見る URL をクリップボードへ置く処理。省略時は OS のクリップボード。</param>
    public RemoteViewPanel(IRemoteViewIntents intents, Action<string>? copyToClipboard = null)
    {
        this.intents = intents;
        this.copyToClipboard = copyToClipboard ?? Clipboard.SetText;
        Background = Theme.Bg;
        Foreground = Theme.Text;
        AutomationProperties.SetName(toggle, "遠隔表示の受け付けの入り切り");
        AutomationProperties.SetName(publishUrl, "送り先のURL");
        AutomationProperties.SetName(viewerUrlField, "見るURL");
        AutomationProperties.SetName(publishUser, "送信用のID");
        AutomationProperties.SetName(publishPassword, "送信用のパスワード");
        toggle.Click += async (_, _) => await ToggleAsync();
        standard.Checked += (_, _) => OnQualityChosen(RemoteViewQuality.Standard);
        fine.Checked += (_, _) => OnQualityChosen(RemoteViewQuality.Fine);
        copy.Click += (_, _) => CopyViewerUrl();
        save.Click += (_, _) => SaveSettings();
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "遠隔表示", FontSize = 28, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });
        root.Children.Add(new Border
        {
            Background = Theme.Raised, Padding = new Thickness(18), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 16),
            Child = new StackPanel { Children = { status, stalled, operationError, new Border { Margin = new Thickness(0, 14, 0, 0), Child = toggle, HorizontalAlignment = HorizontalAlignment.Left } } },
        });
        root.Children.Add(new TextBlock
        {
            Text = "受け付けている間、見る URL を開いた時だけ映像と音を自分の中継サーバーへ送ります。閉じると少しして止まります。",
            Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0),
        });
        root.Children.Add(BuildQuality());
        root.Children.Add(BuildViewerUrl());
        root.Children.Add(BuildSettings());
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        LoadSettingsIntoFields();
        Refresh();
    }

    private UIElement BuildQuality()
    {
        var radios = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        radios.Children.Add(standard);
        radios.Children.Add(fine);
        return new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 16),
            Children =
            {
                new TextBlock { Text = "画質", FontWeight = FontWeights.Bold },
                radios,
                new TextBlock { Text = "画質は、送っていない間だけ変えられます。配信中に変える時は、見ているページを閉じて止まるのを待ってください。", Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) },
            },
        };
    }

    private UIElement BuildViewerUrl()
    {
        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(viewerUrl);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        return new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 16),
            Children = { new TextBlock { Text = "見る URL", FontWeight = FontWeights.Bold }, row },
        };
    }

    private UIElement BuildSettings()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Row(int row, string label, UIElement field, UIElement? extra = null)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 10) };
            Grid.SetRow(text, row);
            var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            cell.Children.Add(field);
            if (extra is not null) cell.Children.Add(extra);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, 1);
            grid.Children.Add(text);
            grid.Children.Add(cell);
        }
        Row(0, "送り先の URL", publishUrl);
        Row(1, "見る URL", viewerUrlField);
        Row(2, "送信用の ID", publishUser);
        Row(3, "送信用のパスワード", publishPassword, passwordHint);
        var footer = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        footer.Children.Add(new Border { Child = save, HorizontalAlignment = HorizontalAlignment.Left });
        footer.Children.Add(settingsMessage);
        return new GroupBox { Header = "中継サーバーの設定", Content = new StackPanel { Children = { grid, footer } } };
    }

    public void Refresh()
    {
        var current = intents.Current();
        var running = current.Phase is RemoteViewPhase.Starting or RemoteViewPhase.Streaming;
        toggle.Content = current.AcceptingViewers ? "受け付けをやめる" : "遠隔表示を受け付ける";
        status.Text = current.Phase switch
        {
            RemoteViewPhase.Starting => "つないでいます",
            RemoteViewPhase.Streaming => $"配信中（{Elapsed(current.StreamedSeconds)}{(string.IsNullOrEmpty(current.TargetProcessName) ? "" : $"・対象: {current.TargetProcessName}")}{(current.Viewers > 0 ? $"・見ている端末: {current.Viewers}" : "")}）",
            RemoteViewPhase.Faulted => $"失敗: {current.Detail}",
            _ when current.AcceptingViewers => string.IsNullOrEmpty(current.Detail) ? "見に来るのを待っています" : current.Detail,
            _ => "受け付けていません",
        };
        status.Foreground = current.Phase switch
        {
            RemoteViewPhase.Streaming => Theme.Ok,
            RemoteViewPhase.Faulted => Theme.Danger,
            _ => Theme.Text,
        };
        stalled.Visibility = current.Phase == RemoteViewPhase.Streaming && current.VideoStalled ? Visibility.Visible : Visibility.Collapsed;
        standard.IsEnabled = fine.IsEnabled = !running;
        applyingQuality = true;
        try
        {
            standard.IsChecked = current.Quality == RemoteViewQuality.Standard;
            fine.IsChecked = current.Quality == RemoteViewQuality.Fine;
        }
        finally { applyingQuality = false; }
        currentViewerUrl = string.IsNullOrWhiteSpace(current.ViewerUrl) ? null : current.ViewerUrl;
        viewerUrl.Text = currentViewerUrl ?? NoViewerUrl;
        viewerUrl.Foreground = currentViewerUrl is null ? Theme.Muted : Theme.Text;
        copy.IsEnabled = currentViewerUrl is not null;
    }

    private static string Elapsed(double seconds)
    {
        var total = (int)Math.Max(0, seconds);
        return $"{total / 60}分{total % 60}秒";
    }

    private async Task ToggleAsync()
    {
        operationError.Text = "";
        var enable = !intents.Current().AcceptingViewers;
        toggle.IsEnabled = false;
        try
        {
            // 切にする時は、送っている配信が止まるまで Host が待つ。
            await intents.SetAcceptViewersAsync(enable);
        }
        catch (Exception error) { operationError.Text = error.Message; }
        finally { toggle.IsEnabled = true; }
        Refresh();
    }

    private void CopyViewerUrl()
    {
        if (currentViewerUrl is null) return;
        try { copyToClipboard(currentViewerUrl); }
        catch (Exception error) { operationError.Text = error.Message; }
    }

    private void OnQualityChosen(RemoteViewQuality quality)
    {
        if (applyingQuality) return;
        try
        {
            var saved = intents.LoadSettings();
            if (string.IsNullOrEmpty(saved.PublishUrl) || string.IsNullOrEmpty(saved.ViewerUrl) || string.IsNullOrEmpty(saved.PublishUser))
                throw new InvalidOperationException("先に中継サーバーの設定を保存してください。");
            intents.SaveSettings(saved.PublishUrl, saved.ViewerUrl, saved.PublishUser, null, quality);
            SetSettingsMessage("画質を保存しました。", error: false);
        }
        catch (Exception error) { SetSettingsMessage(error.Message, error: true); }
        Refresh();
    }

    private void SaveSettings()
    {
        try
        {
            var quality = fine.IsChecked == true ? RemoteViewQuality.Fine : RemoteViewQuality.Standard;
            var password = publishPassword.Password.Length == 0 ? null : publishPassword.Password;
            var view = intents.SaveSettings(publishUrl.Text.Trim(), viewerUrlField.Text.Trim(), publishUser.Text.Trim(), password, quality);
            publishPassword.Clear();
            ShowSettings(view);
            SetSettingsMessage("保存しました。", error: false);
        }
        catch (Exception error) { SetSettingsMessage(error.Message, error: true); }
        Refresh();
    }

    private void LoadSettingsIntoFields()
    {
        try { ShowSettings(intents.LoadSettings()); }
        catch (Exception error) { SetSettingsMessage(error.Message, error: true); }
    }

    private void ShowSettings(RemoteViewSettingsView view)
    {
        publishUrl.Text = view.PublishUrl ?? "";
        viewerUrlField.Text = view.ViewerUrl ?? "";
        publishUser.Text = view.PublishUser ?? "";
        passwordHint.Text = view.HasPublishPassword ? "保存済み（変える時だけ入力）" : "まだ保存されていません";
    }

    private void SetSettingsMessage(string text, bool error)
    {
        settingsMessage.Text = text;
        settingsMessage.Foreground = error ? Theme.Danger : Theme.Ok;
    }
}
