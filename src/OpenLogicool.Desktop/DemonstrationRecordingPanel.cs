using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Exploration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;

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
    private readonly TextBlock statusText = new()
    {
        Text = "記録待機中",
        TextWrapping = TextWrapping.Wrap,
        FontSize = 16,
        FontWeight = FontWeights.Bold,
        Foreground = Theme.Muted,
    };
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
    private readonly Button reanalyzeButton = Button("保存した記録を解析し直す");
    private readonly Button reviewButton = Button("選んだ手順を確認・修復");
    private DemonstrationCandidate? candidate;
    private (string SessionId, string VersionId, int StepNumber)? repair;
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
        reanalyzeButton.Click += async (_, _) => await ReanalyzeAsync();
        reanalyzeButton.IsEnabled = false;
        reviewButton.Click += async (_, _) => await ReviewAsync();
        reviewButton.IsEnabled = false;
        steps.SelectionChanged += (_, _) => reviewButton.IsEnabled = !recording && candidate is not null && steps.SelectedItem is not null;

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
            Text = "記録後は全操作を候補に残します。判定できなかった手順は、前後画像を確認するか、その一手だけを記録し直せます。",
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

        Add(root, new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.Line2,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = statusText,
        }, 2);

        var lists = new Grid();
        sessions.Height = steps.Height = 280;
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
        reanalyzeButton.Margin = new Thickness(8, 0, 0, 0);
        footer.Children.Add(reanalyzeButton);
        reviewButton.Margin = new Thickness(8, 0, 0, 0);
        footer.Children.Add(reviewButton);
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
        statusText.Foreground = Theme.Text;
        statusText.Text = "記録を開始しています — ゲーム画面を確認中";
        try
        {
            _ = await workspace.StartAsync(goal.Text);
            recording = true;
            sessions.IsEnabled = createMacroButton.IsEnabled = reanalyzeButton.IsEnabled = reviewButton.IsEnabled = false;
            stopButton.IsEnabled = true;
            liveTimer.Start();
            RefreshStatus();
        }
        catch (Exception exception)
        {
            statusText.Foreground = Theme.Danger;
            statusText.Text = exception.Message;
            startButton.IsEnabled = true;
        }
    }

    private async Task StopAsync()
    {
        stopButton.IsEnabled = false;
        liveTimer.Start();
        statusText.Foreground = Theme.Text;
        statusText.Text = "記録を終了しています — 原本を保存しています";
        try
        {
            var stopped = await workspace.StopAsync();
            if (repair is { } pending)
            {
                candidate = workspace.ReplaceStep(pending.SessionId, pending.VersionId, pending.StepNumber, stopped.SessionId);
                statusText.Text = $"手順 {pending.StepNumber} だけを差し替え、版 {candidate.Macro.RevisionNumber} を保存しました。ほかの手順と原本を保持しています。";
                return;
            }
            var undetermined = workspace.ListSteps(stopped.SessionId).Count(step => step.TransitionLabel == "判定できず");
            statusText.Foreground = undetermined > 0 ? Theme.Warn : Theme.Ok;
            statusText.Text = $"記録を終了しました。{stopped.OperationCount} 操作を保存・解析しました。"
                + (undetermined > 0 ? $"　画面変化を判定できなかった操作が {undetermined} 件あります。" : "");
        }
        catch (Exception exception)
        {
            statusText.Foreground = Theme.Danger;
            statusText.Text = exception.Message;
        }
        finally
        {
            recording = false;
            liveTimer.Stop();
            startButton.IsEnabled = sessions.IsEnabled = true;
            var selectedId = repair?.SessionId;
            repair = null;
            RefreshSessions(selectedId);
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
            DemonstrationRecorderStatus.Analyzing => "原本を保存しました — 記録した画面を解析中",
            DemonstrationRecorderStatus.Fault => "記録に失敗しました",
            _ => "待機中",
        };
        if (recording)
        {
            if (status.Status == DemonstrationRecorderStatus.Analyzing)
            {
                statusText.Foreground = Theme.Text;
                statusText.Text = $"{label}　{status.AnalyzedOperations} / {status.TotalOperations} 操作";
                return;
            }
            if (status.Status == DemonstrationRecorderStatus.Fault)
            {
                statusText.Foreground = Theme.Danger;
                statusText.Text = $"{label}　{status.FailureReason}";
                return;
            }
            statusText.Foreground = status.Status == DemonstrationRecorderStatus.Paused ? Theme.Warn : Theme.Ok;
            statusText.Text = $"{label}　押しっぱなし {status.HeldPressCount} 件";
        }
    }

    private void RefreshSessions(string? selectedId = null)
    {
        var items = workspace.ListSessions();
        sessions.ItemsSource = items;
        if (items.Count > 0)
        {
            sessions.SelectedItem = items.FirstOrDefault(item => item.SessionId == selectedId) ?? items[0];
        }
    }

    private void RefreshSteps()
    {
        if (sessions.SelectedItem is DemonstrationSessionSummary selected)
        {
            candidate = workspace.LoadCandidate(selected.SessionId);
            steps.ItemsSource = candidate is not null ? candidate.Steps : workspace.ListSteps(selected.SessionId);
            if (steps.Items.Count > 0) steps.SelectedIndex = 0;
            reviewButton.IsEnabled = !recording && candidate is not null;
            createMacroButton.Content = candidate is null ? "このデモから候補を作る" : "この候補をマクロ画面で開く";
            createMacroButton.IsEnabled = selected.State == DemonstrationSessionState.Stopped;
            reanalyzeButton.IsEnabled = createMacroButton.IsEnabled;
        }
        else
        {
            steps.ItemsSource = null;
            candidate = null;
            reviewButton.IsEnabled = false;
            createMacroButton.IsEnabled = false;
            reanalyzeButton.IsEnabled = false;
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
            var openingExisting = candidate is not null;
            var macro = candidate?.Macro ?? workspace.CreateMacroFromSession(selected.SessionId);
            statusText.Text = $"「{macro.Goal}」の全 {macro.StepCount} 操作を候補に保存しました。版 {macro.RevisionNumber}・{macro.StatusLabel}。";
            RefreshSteps();
            if (openingExisting || candidate?.Steps.Any(step => step.StatusLabel == "確認待ち") != true) onMacroCreated(macro.RouteId);
        }
        catch (Exception exception)
        {
            statusText.Text = exception.Message;
        }
    }

    private async Task ReanalyzeAsync()
    {
        if (sessions.SelectedItem is not DemonstrationSessionSummary selected) return;
        recording = true;
        startButton.IsEnabled = sessions.IsEnabled = createMacroButton.IsEnabled = reanalyzeButton.IsEnabled = reviewButton.IsEnabled = false;
        statusText.Foreground = Theme.Text;
        statusText.Text = "保存した記録を解析中 — 元の記録は保持します";
        liveTimer.Start();
        try
        {
            var result = await workspace.ReanalyzeAsync(selected.SessionId);
            RefreshSessions(result.SessionId);
            statusText.Foreground = Theme.Ok;
            statusText.Text = $"再解析を終了しました。{result.OperationCount} 操作の新しい解析結果を保存しました。元の記録は保持しています。";
        }
        catch (Exception exception)
        {
            statusText.Foreground = Theme.Danger;
            statusText.Text = exception.Message;
        }
        finally
        {
            recording = false;
            liveTimer.Stop();
            startButton.IsEnabled = sessions.IsEnabled = true;
            RefreshSteps();
        }
    }

    private async Task ReviewAsync()
    {
        if (candidate is null || steps.SelectedItem is not DemonstrationCandidateStep selected
            || sessions.SelectedItem is not DemonstrationSessionSummary session) return;
        var current = candidate;
        var dialog = new Window
        {
            Title = $"手順 {selected.StepNumber} の確認・修復", Owner = Window.GetWindow(this),
            Width = 980, Height = 630,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        Theme.ApplyDialog(dialog);
        var root = new DockPanel { Margin = new Thickness(18) };
        var heading = new TextBlock { Text = $"{selected.DisplayLabel}\n{selected.Reason}",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var reason = new TextBox { Text = "前後画像を確認した", Background = Theme.Raised, Foreground = Theme.Text,
            Margin = new Thickness(0, 4, 0, 8), Padding = new Thickness(6) };
        footer.Children.Add(new TextBlock { Text = "期待する結果と確認メモを新版に保存します。実際の再生結果は毎回、画面で比較します。",
            TextWrapping = TextWrapping.Wrap });
        footer.Children.Add(reason);
        var buttons = new WrapPanel();
        var changed = Button("画面変化ありを期待する");
        var stayed = Button("画面変化なしを期待する");
        var rerecord = Button("この一手だけ記録し直す");
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Warn };
        var startRepair = false;
        void Confirm(GameTransitionJudgement expected)
        {
            try
            {
                candidate = workspace.ReviewStep(session.SessionId, current.Macro.VersionId, selected.StepNumber,
                    expected, reason.Text);
                statusText.Text = $"手順 {selected.StepNumber} の期待結果を版 {candidate.Macro.RevisionNumber} に保存しました。";
                dialog.Close(); RefreshSteps(); steps.SelectedIndex = selected.StepNumber - 1;
            }
            catch (Exception exception) { message.Text = exception.Message; }
        }
        changed.Click += (_, _) => Confirm(GameTransitionJudgement.Moved);
        stayed.Click += (_, _) => Confirm(GameTransitionJudgement.Stayed);
        rerecord.Click += (_, _) => { startRepair = true; dialog.Close(); };
        foreach (var button in new[] { changed, stayed, rerecord })
        {
            button.Margin = new Thickness(0, 0, 8, 8); buttons.Children.Add(button);
        }
        footer.Children.Add(buttons); footer.Children.Add(message);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var images = new Grid();
        images.ColumnDefinitions.Add(new ColumnDefinition()); images.ColumnDefinitions.Add(new ColumnDefinition());
        void AddImage(string label, string? path, int column)
        {
            var box = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
            var title = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(title, Dock.Top); box.Children.Add(title);
            if (path is null) box.Children.Add(new TextBlock { Text = "この区間の画像がありません。一手だけ記録し直してください。", TextWrapping = TextWrapping.Wrap });
            else
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                box.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform });
            }
            Grid.SetColumn(box, column); images.Children.Add(box);
        }
        try
        {
            AddImage("操作前", selected.BeforeImagePath, 0); AddImage("操作後（保存できた最後の画面）", selected.AfterImagePath, 1);
            changed.IsEnabled = stayed.IsEnabled = selected.BeforeImagePath is not null && selected.AfterImagePath is not null;
            root.Children.Add(images); dialog.Content = root; dialog.ShowDialog();
            if (startRepair)
            {
                repair = (session.SessionId, current.Macro.VersionId, selected.StepNumber);
                goal.Text = $"{session.Goal} — 手順 {selected.StepNumber} の再記録";
                await StartAsync();
                if (!recording) repair = null;
            }
        }
        catch (Exception exception) { statusText.Text = exception.Message; statusText.Foreground = Theme.Danger; }
    }

    private static Button Button(string label) => new() { Content = label, Padding = new Thickness(10, 5, 10, 5) };

    private static void Add(Grid grid, UIElement element, int row)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }
}
