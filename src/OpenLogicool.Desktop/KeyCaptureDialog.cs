using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenLogicool.Desktop;

/// <summary>
/// 「ゲームに送るキー」を録る modal（docs/ui-mocks/flows.html #key 準拠）。
/// キーボードは <see cref="Window.PreviewKeyDown"/>/<see cref="Window.PreviewKeyUp"/> で同時押しを録り、
/// マウスボタンは選択肢ボタンで選ぶ（グローバル hook は導入しない）。
/// 確定した output token 文字列は <see cref="Result"/> に入る（取り消しなら null のまま）。Esc は閉じる操作にせず、送るキーとして録る。
/// </summary>
public sealed class KeyCaptureDialog : Window
{
    private readonly KeyCaptureSession _session = new();
    private readonly bool _canAssignByDevicePress;

    private readonly StackPanel _captureKeys = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, MinHeight = 44 };
    private readonly Border _well = new()
    {
        Height = 124,
        Background = Theme.Sunken,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Margin = new Thickness(0, 0, 0, 12),
    };
    private readonly TextBlock _nowText = new() { Foreground = Theme.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock _deviceHintText = new() { Foreground = Theme.Warn, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly Button _acceptButton = Theme.Primary(new Button { Content = "これに決める", Height = 34, Padding = new Thickness(16, 0, 16, 0) });
    private readonly Button _resetButton = Theme.Quiet(new Button { Content = "録り直す", Height = 34, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0) });

    private static readonly Color CapturedKeyColor = Color.FromRgb(0xdf, 0xe5, 0xf2);

    public string? Result { get; private set; }

    public KeyCaptureDialog(
        string actionName,
        string currentOutputsLabel,
        bool overwritesExisting = false,
        bool canAssignByDevicePress = false)
    {
        _canAssignByDevicePress = canAssignByDevicePress;
        Title = "ゲームに送るキー";
        Theme.ApplyDialog(this);
        Width = 470;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        // IME がオンだと実キーが Key.ImeProcessed に化けて録れないため、この modal では IME を無効化する。
        InputMethod.SetIsInputMethodEnabled(this, false);

        var stack = new StackPanel { Margin = new Thickness(26, 24, 26, 20) };

        stack.Children.Add(new TextBlock
        {
            Text = $"操作「{actionName}」",
            Foreground = Theme.Muted,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "ゲームに送るキー",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "キーボードのキーを押してください。同時押し（Ctrl + C など）も Esc もそのまま録ります。やめる時は「取り消す」を押してください。",
            Foreground = Theme.Muted,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        var wellStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        wellStack.Children.Add(new TextBlock { Text = "いま押されたキー", Foreground = Theme.Muted, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        wellStack.Children.Add(_captureKeys);
        _well.Child = wellStack;
        stack.Children.Add(_well);

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

        var mouseRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        mouseRow.Children.Add(new TextBlock { Text = "マウスボタン", Foreground = Theme.Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        foreach (var (label, token) in new[]
                 {
                     ("左", "Mouse:Left"), ("右", "Mouse:Right"), ("中央", "Mouse:Middle"),
                     ("戻る（X1）", "Mouse:X1"), ("進む（X2）", "Mouse:X2"),
                 })
        {
            var button = Theme.Quiet(new Button { Content = label, Height = 30, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 0, 10, 0) });
            button.Click += (_, _) => Commit(token);
            mouseRow.Children.Add(button);
        }

        stack.Children.Add(mouseRow);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _resetButton.IsEnabled = false;
        _resetButton.Click += (_, _) => ResetCapture();
        actions.Children.Add(_resetButton);
        var cancelButton = Theme.Quiet(new Button { Content = "取り消す", Height = 34, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 0) });
        cancelButton.Click += (_, _) => { Result = null; DialogResult = false; };
        actions.Children.Add(cancelButton);

        _acceptButton.Content = overwritesExisting ? "上書きして決める" : "これに決める";
        _acceptButton.IsEnabled = false;
        _acceptButton.Click += (_, _) => Commit(_session.CandidateToken!);
        actions.Children.Add(_acceptButton);
        stack.Children.Add(actions);

        RefreshCaptureState();

        Content = stack;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Esc も送るキーとして録る（Esc で閉じると Esc を割り当てられない）。閉じるのは「取り消す」と窓の×だけ。
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
        _captureKeys.Children.Clear();
        if (_session.RecordedKeys.Count == 0)
        {
            _captureKeys.Children.Add(new TextBlock { Text = "（未入力）", Foreground = Theme.Faint, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        }

        foreach (var key in _session.RecordedKeys)
        {
            if (_captureKeys.Children.Count > 0)
            {
                _captureKeys.Children.Add(new TextBlock { Text = "+", Foreground = Theme.Muted, FontFamily = Theme.Mono, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            _captureKeys.Children.Add(Theme.Keycap(KeyCaptureTokenizer.ToDisplayName(key), CapturedKeyColor, height: 44, minWidth: 50, fontSize: 15));
        }

        // 録る準備ができている間（未入力・押している最中）は枠を光らせ、録り終えたら落ち着かせる。
        var armed = !_session.IsReady;
        _well.BorderBrush = armed ? Theme.Accent : Theme.Line;
        _well.Effect = armed ? Theme.Glow(Theme.AccentColor, 22, 0.35) : null;
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
