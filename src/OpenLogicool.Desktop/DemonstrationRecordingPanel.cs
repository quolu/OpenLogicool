using OpenLogicool.Contracts.Playbooks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenLogicool.Desktop;

/// <summary>
/// 操作デモの記録開始／停止、記録中の状態、記録済みsession一覧・step一覧、
/// そのデモからのmacro作成をまとめた画面。内部id・tokenは画面へ出さない。
/// マクロの2 mode再生・進捗・停止理由は既存の「マクロ」tab（<see cref="MacroAutomationPanel"/>）を
/// 作り直さずそのまま使う——作成成功後はそちらのtabへ切り替えて選択状態にするだけ。
/// </summary>
internal sealed class DemonstrationRecordingPanel : UserControl
{
    private readonly DemonstrationRecordingWorkspace workspace;
    private readonly Action<string> onMacroCreated;
    private readonly TextBox goal = new()
    {
        MinWidth = 420,
        Background = Theme.Raised,
        Foreground = Theme.Text,
        BorderBrush = Theme.Line,
        CaretBrush = Theme.Text,
        Padding = new Thickness(6, 4, 6, 4),
    };
    private readonly Button startButton = Button("記録開始");
    private readonly Button stopButton = Button("記録終了");
    private readonly TextBlock statusText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly ListBox sessions = new()
    {
        MinHeight = 150,
        DisplayMemberPath = nameof(DemonstrationSessionSummary.DisplayLabel),
        Background = Theme.Sunken,
        Foreground = Theme.Text,
        BorderBrush = Theme.Line,
    };
    private readonly ListBox steps = new()
    {
        MinHeight = 150,
        DisplayMemberPath = nameof(DemonstrationStepSummary.DisplayLabel),
        Background = Theme.Sunken,
        Foreground = Theme.Text,
        BorderBrush = Theme.Line,
    };
    private readonly Button createMacroButton = Button("このデモからマクロを作る");
    private readonly DispatcherTimer liveTimer;
    private bool recording;

    public DemonstrationRecordingPanel(IDemonstrationRecordingIntents intents, Action<string> onMacroCreated)
    {
        ArgumentNullException.ThrowIfNull(intents);
        Background = Theme.Bg;
        Foreground = Theme.Text;
        workspace = new DemonstrationRecordingWorkspace(intents);
        this.onMacroCreated = onMacroCreated ?? throw new ArgumentNullException(nameof(onMacroCreated));

        startButton.Click += async (_, _) => await StartAsync();
        stopButton.Click += async (_, _) => await StopAsync();
        stopButton.IsEnabled = false;
        sessions.SelectionChanged += (_, _) => RefreshSteps();
        createMacroButton.Click += (_, _) => CreateMacro();
        createMacroButton.IsEnabled = false;

        liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        liveTimer.Tick += (_, _) => RefreshStatus();
        Unloaded += (_, _) => liveTimer.Stop();

        Content = Build();
        RefreshSessions();
        RefreshStatus();
    }

    private UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(20) };
        for (var i = 0; i < 3; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "デモから操作を覚えさせる", FontSize = 18, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock
        {
            Text = "目的を決めて記録を開始し、実際に操作してください。停止するとそのデモから操作手順（マクロ）を作れます。",
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 4, 0, 14),
            TextWrapping = TextWrapping.Wrap,
        });
        Add(root, heading, 0);

        var recordRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        recordRow.Children.Add(new TextBlock { Text = "目的", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        recordRow.Children.Add(goal);
        startButton.Margin = new Thickness(8, 0, 0, 0);
        stopButton.Margin = new Thickness(8, 0, 0, 0);
        recordRow.Children.Add(startButton);
        recordRow.Children.Add(stopButton);
        Add(root, recordRow, 1);

        Add(root, statusText, 2);

        var lists = new Grid();
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(new TextBlock { Text = "記録済みデモ", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        left.Children.Add(sessions);
        lists.Children.Add(left);
        var right = new StackPanel();
        right.Children.Add(new TextBlock { Text = "選んだデモの操作一覧", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        right.Children.Add(steps);
        Grid.SetColumn(right, 1);
        lists.Children.Add(right);
        Add(root, lists, 3);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(createMacroButton);
        Add(root, footer, 4);

        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(goal.Text))
        {
            statusText.Text = "目的を入力してください。";
            return;
        }

        startButton.IsEnabled = false;
        try
        {
            _ = await workspace.StartAsync(goal.Text);
            recording = true;
            stopButton.IsEnabled = true;
            liveTimer.Start();
            RefreshStatus();
        }
        catch (Exception exception)
        {
            statusText.Text = exception.Message;
            startButton.IsEnabled = true;
        }
    }

    private async Task StopAsync()
    {
        stopButton.IsEnabled = false;
        try
        {
            var stopped = await workspace.StopAsync();
            statusText.Text = $"記録を終了しました（利用者が記録を停止しました）。{stopped.OperationCount} 操作を記録しました。";
        }
        catch (Exception exception)
        {
            statusText.Text = exception.Message;
        }
        finally
        {
            recording = false;
            liveTimer.Stop();
            startButton.IsEnabled = true;
            RefreshSessions();
        }
    }

    private void RefreshStatus()
    {
        var status = workspace.Status();
        var label = status.Status switch
        {
            DemonstrationRecorderStatus.Recording => "記録中",
            DemonstrationRecorderStatus.Paused => "対象アプリから外れたため一時停止中",
            DemonstrationRecorderStatus.Stopped => "停止済み",
            _ => "待機中",
        };
        if (recording)
        {
            statusText.Text = $"{label}　押しっぱなし {status.HeldPressCount} 件";
        }
    }

    private void RefreshSessions()
    {
        var items = workspace.ListSessions();
        sessions.ItemsSource = items;
        if (items.Count > 0)
        {
            sessions.SelectedIndex = 0;
        }
    }

    private void RefreshSteps()
    {
        if (sessions.SelectedItem is DemonstrationSessionSummary selected)
        {
            steps.ItemsSource = workspace.ListSteps(selected.SessionId);
            createMacroButton.IsEnabled = selected.State == DemonstrationSessionState.Stopped;
        }
        else
        {
            steps.ItemsSource = null;
            createMacroButton.IsEnabled = false;
        }
    }

    private void CreateMacro()
    {
        if (sessions.SelectedItem is not DemonstrationSessionSummary selected)
        {
            statusText.Text = "マクロを作るデモを選んでください。";
            return;
        }

        try
        {
            var macro = workspace.CreateMacroFromSession(selected.SessionId);
            statusText.Text = $"マクロ「{macro.Goal}」を作りました。マクロtabで対象アプリと再生方法を選んでください。";
            onMacroCreated(macro.RouteId);
        }
        catch (Exception exception)
        {
            statusText.Text = exception.Message;
        }
    }

    private static Button Button(string label) => new() { Content = label, Padding = new Thickness(10, 5, 10, 5) };

    private static void Add(Grid grid, UIElement element, int row)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }
}
