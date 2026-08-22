using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenLogicool.Desktop;

/// <summary>出力方式を保存し、Serial HIDだけはprotocol handshakeをその場で確認する最小設定window。</summary>
public sealed class SerialHidSettingsWindow : Window
{
    private readonly ISerialHidSettingsIntent _intent;
    private readonly RadioButton _sendInput = new() { Content = "Windows出力（SendInput）", Margin = new Thickness(0, 0, 0, 8) };
    private readonly RadioButton _serialHid = new() { Content = "USB出力（SparkFun Pro Micro）", Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox _candidates = new() { MinWidth = 360, Margin = new Thickness(22, 0, 0, 10) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Muted, Margin = new Thickness(0, 12, 0, 0) };

    public SerialHidSettingsWindow(ISerialHidSettingsIntent intent)
    {
        _intent = intent;
        Title = "出力方式";
        Width = 520;
        Height = 330;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Bg;
        Foreground = Theme.Text;
        Resources[typeof(Button)] = Theme.CreateFlatButtonStyle();

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "出力方式", FontSize = 18, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock
        {
            Text = "変更は保存され、次に常駐を起動したときから使われます。",
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 4, 0, 18),
        });
        root.Children.Add(_sendInput);
        root.Children.Add(_serialHid);
        root.Children.Add(_candidates);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button
        {
            Content = "接続を確認して保存",
            Background = Theme.Accent,
            Foreground = Brushes.White,
            Padding = new Thickness(14, 7, 14, 7),
        };
        var close = new Button { Content = "閉じる", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7) };
        buttons.Children.Add(save);
        buttons.Children.Add(close);
        root.Children.Add(buttons);
        root.Children.Add(_status);
        Content = root;

        _sendInput.Checked += (_, _) => UpdateCandidateEnabled();
        _serialHid.Checked += (_, _) => UpdateCandidateEnabled();
        save.Click += (_, _) => Save();
        close.Click += (_, _) => Close();
        LoadSnapshot(_intent.Load());
    }

    private void Save()
    {
        var route = _serialHid.IsChecked == true ? OutputRouteChoice.SerialHid : OutputRouteChoice.SendInput;
        var selected = (_candidates.SelectedItem as SerialHidCandidateChoice)?.DeviceInstanceId;
        var result = _intent.SaveAndTest(route, selected);
        LoadSnapshot(result.Snapshot);
        _status.Foreground = result.Success ? Theme.Ok : Theme.Warn;
    }

    private void LoadSnapshot(SerialHidSettingsSnapshot snapshot)
    {
        _sendInput.IsChecked = snapshot.RequestedRoute == OutputRouteChoice.SendInput;
        _serialHid.IsChecked = snapshot.RequestedRoute == OutputRouteChoice.SerialHid;
        _candidates.ItemsSource = snapshot.Candidates;
        _candidates.SelectedItem = snapshot.Candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.DeviceInstanceId, snapshot.SelectedDeviceInstanceId, StringComparison.OrdinalIgnoreCase));
        if (_candidates.SelectedItem is null && snapshot.Candidates.Count == 1)
        {
            _candidates.SelectedIndex = 0;
        }

        _status.Text = snapshot.StatusLine;
        UpdateCandidateEnabled();
    }

    private void UpdateCandidateEnabled() => _candidates.IsEnabled = _serialHid.IsChecked == true;
}
