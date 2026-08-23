using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenLogicool.Desktop;

/// <summary>
/// 「ゲームに送るキー」を録る modal（docs/ui-mocks/flows.html #key 準拠）。
/// キーボードは <see cref="Window.PreviewKeyDown"/>/<see cref="Window.PreviewKeyUp"/> で同時押しを録り、
/// マウスボタンは選択肢ボタンで選ぶ（グローバル hook は導入しない）。
/// 確定した output token 文字列は <see cref="Result"/> に入る（キャンセル・Esc なら null のまま）。
/// </summary>
public sealed class KeyCaptureDialog : Window
{
    private readonly KeyCaptureSession _session = new();
    private readonly bool _canAssignByDevicePress;

    private readonly TextBlock _captureText = new()
    {
        FontSize = 22,
        FontWeight = FontWeights.Bold,
        Foreground = Theme.Text,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly TextBlock _nowText = new() { Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 16) };
    private readonly TextBlock _deviceHintText = new() { Foreground = Theme.Warn, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly Button _acceptButton = new() { Content = "これに決める", Padding = new Thickness(14, 6, 14, 6) };
    private readonly Button _resetButton = new() { Content = "録り直す", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0) };

    public string? Result { get; private set; }

    public KeyCaptureDialog(
        string actionName,
        string currentOutputsLabel,
        bool overwritesExisting = false,
        bool canAssignByDevicePress = false)
    {
        _canAssignByDevicePress = canAssignByDevicePress;
        Title = "ゲームに送るキー";
        Background = Theme.Raised;
        Foreground = Theme.Text;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        // IME がオンだと実キーが Key.ImeProcessed に化けて録れないため、この modal では IME を無効化する。
        InputMethod.SetIsInputMethodEnabled(this, false);

        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };

        stack.Children.Add(new TextBlock
        {
            Text = $"操作「{actionName}」",
            Foreground = Theme.Muted,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "ゲームに送るキー",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "キーボードのキーを押してください。同時押し（Ctrl + C など）もそのまま録ります。",
            Foreground = Theme.Muted,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
        });

        var well = new Border
        {
            Height = 100,
            Background = Theme.Sunken,
            BorderBrush = Theme.Line2,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 12),
        };
        var wellStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        wellStack.Children.Add(new TextBlock { Text = "いま押されたキー", Foreground = Theme.Muted, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
        _captureText.Text = "（未入力）";
        wellStack.Children.Add(_captureText);
        well.Child = wellStack;
        stack.Children.Add(well);

        _nowText.Text = overwritesExisting
            ? $"注意: いまの割当「{currentOutputsLabel}」を新しく録ったキーで上書きします"
            : $"いまの割当: {currentOutputsLabel}（もう一度押すと録り直します）";
        if (overwritesExisting)
        {
            _nowText.Foreground = Theme.Warn;
        }

        stack.Children.Add(_nowText);
        _deviceHintText.Text = canAssignByDevicePress
            ? "送るキーを押して離した後、割り当てたい G13 / G600 のボタンを押すと、その場で確定します。"
            : string.Empty;
        _deviceHintText.Visibility = canAssignByDevicePress ? Visibility.Visible : Visibility.Collapsed;
        stack.Children.Add(_deviceHintText);

        var mouseRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        mouseRow.Children.Add(new TextBlock { Text = "マウスボタン: ", Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        foreach (var (label, token) in new[]
                 {
                     ("左", "Mouse:Left"), ("右", "Mouse:Right"), ("中央", "Mouse:Middle"),
                     ("戻る（X1）", "Mouse:X1"), ("進む（X2）", "Mouse:X2"),
                 })
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 3, 8, 3) };
            button.Click += (_, _) => Commit(token);
            mouseRow.Children.Add(button);
        }

        stack.Children.Add(mouseRow);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _resetButton.IsEnabled = false;
        _resetButton.Click += (_, _) => ResetCapture();
        actions.Children.Add(_resetButton);
        var cancelButton = new Button { Content = "取り消す", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        cancelButton.Click += (_, _) => { Result = null; DialogResult = false; };
        actions.Children.Add(cancelButton);

        _acceptButton.Content = overwritesExisting ? "上書きして決める" : "これに決める";
        _acceptButton.Background = Theme.Accent;
        _acceptButton.Foreground = Brushes.White;
        _acceptButton.IsEnabled = false;
        _acceptButton.Click += (_, _) => Commit(_session.CandidateToken!);
        actions.Children.Add(_acceptButton);
        stack.Children.Add(actions);

        Content = stack;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Result = null;
            DialogResult = false;
            e.Handled = true;
            return;
        }

        _session.KeyDown(ResolveKey(e));
        RefreshCaptureState();
        e.Handled = true;
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        _session.KeyUp(ResolveKey(e));
        RefreshCaptureState();
        e.Handled = true;
    }

    public bool TryCommitFromDevicePress(double inputMonotonicMs)
    {
        if (!_canAssignByDevicePress || !_session.CanCommitFromDevicePress(inputMonotonicMs))
        {
            ShowDeviceHint("先にゲームへ送るキーを押して、すべて離してからデバイスのボタンを押してください。");
            return false;
        }

        Commit(_session.CandidateToken!);
        return true;
    }

    public void ShowDeviceHint(string message)
    {
        _deviceHintText.Text = message;
        _deviceHintText.Visibility = Visibility.Visible;
    }

    private void RefreshCaptureState()
    {
        _captureText.Text = _session.RecordedKeys.Count == 0
            ? "（未入力）"
            : string.Join(" + ", _session.RecordedKeys.Select(KeyCaptureTokenizer.ToDisplayName));
        _acceptButton.IsEnabled = _session.CandidateToken is not null;
        _resetButton.IsEnabled = _session.CandidateToken is not null;
        if (_canAssignByDevicePress && _session.IsReady)
        {
            ShowDeviceHint("記録できました。割り当てたい G13 / G600 のボタンを押してください。");
        }
    }

    private void ResetCapture()
    {
        _session.Reset();
        RefreshCaptureState();
        if (_canAssignByDevicePress)
        {
            ShowDeviceHint("送るキーを録り直してください。");
        }
    }

    /// <summary>Alt 系（Key.System）と IME 経由（Key.ImeProcessed）を実キーへ解決する。</summary>
    private static Key ResolveKey(KeyEventArgs e) => e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        _ => e.Key,
    };

    private void Commit(string token)
    {
        Result = token;
        DialogResult = true;
    }
}
