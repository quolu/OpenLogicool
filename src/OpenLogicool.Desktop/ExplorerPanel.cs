using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OpenLogicool.Desktop;

/// <summary>Game Operatorの構造探索面。表示とintent発行だけを担う。</summary>
internal sealed class ExplorerPanel : UserControl
{
    private readonly ExplorerWorkspace workspace;
    private readonly ComboBox scopes = new() { MinWidth = 300, DisplayMemberPath = nameof(ExplorerScopeOption.DisplayLabel) };
    private readonly TextBlock summary = NewValue();
    private readonly TextBlock revision = NewValue();
    private readonly TextBlock probe = NewValue();
    private readonly TextBlock risk = NewValue();
    private readonly TextBlock budget = NewValue();
    private readonly TextBlock recovery = NewValue();
    private readonly TextBlock stopReason = NewValue();
    private readonly TextBlock verification = NewValue();
    private readonly ListBox frontier = new() { MinHeight = 90 };
    private readonly ListBox nodes = new() { MinHeight = 120, DisplayMemberPath = nameof(ExplorerNodeItem.DisplayLabel) };
    private readonly TextBox correctedLabel = new() { MinWidth = 220 };
    private readonly TextBox correctionReason = new() { MinWidth = 260, Text = "利用者が画面上で訂正" };
    private readonly TextBlock status = new() { Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Button pause = ActionButton("一時停止");
    private readonly Button step = ActionButton("一手だけ進める");
    private readonly Button abandon = ActionButton("探索を終了");
    private readonly Button correct = ActionButton("名前を訂正");
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private ExplorerScreenSnapshot? snapshot;

    public ExplorerPanel(IExplorerIntents intents)
    {
        workspace = new ExplorerWorkspace(intents);
        Content = Build();
        scopes.SelectionChanged += (_, _) => LoadSelected();
        pause.Click += (_, _) => Run(scope => workspace.Pause(scope));
        step.Click += (_, _) => Run(scope => workspace.Step(scope));
        abandon.Click += (_, _) => Run(scope => workspace.Abandon(scope));
        correct.Click += (_, _) => Correct();
        refreshTimer.Tick += (_, _) => LoadSelected(clearStatus: false);
        Loaded += (_, _) =>
        {
            if (scopes.Items.Count > 0)
            {
                refreshTimer.Start();
            }
        };
        Unloaded += (_, _) => refreshTimer.Stop();

        var available = workspace.ListScopes();
        scopes.ItemsSource = available;
        scopes.SelectedItem = available.FirstOrDefault();
        if (available.Count == 0)
        {
            status.Text = "保存済みのゲーム構造はまだありません。探索を開始するとここに表示されます。";
            SetButtonState();
        }
    }

    private UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "構造探索", FontSize = 24, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock
        {
            Text = "AIが画面を観測して見つけた状態と遷移です。候補は別の探索回で再現してから確認済みになります。",
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 4, 0, 10),
        });
        heading.Children.Add(scopes);
        root.Children.Add(heading);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
        controls.Children.Add(pause);
        controls.Children.Add(step);
        controls.Children.Add(abandon);
        Grid.SetRow(controls, 1);
        root.Children.Add(controls);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = Card();
        left.Children.Add(Heading("いまの探索"));
        AddRow(left, "状態", summary);
        AddRow(left, "構造版", revision);
        AddRow(left, "次に調べる候補", frontier);
        AddRow(left, "実行待ちの一手", probe);
        AddRow(left, "危険度", risk);
        AddRow(left, "残り予算", budget);
        AddRow(left, "戻り道", recovery);
        AddRow(left, "停止理由", stopReason);
        body.Children.Add(WrapCard(left));

        var right = Card();
        right.Children.Add(Heading("覚えた構造"));
        AddRow(right, "検証段階", verification);
        AddRow(right, "画面状態", nodes);
        right.Children.Add(new TextBlock { Text = "選んだ状態の名前を訂正", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 5) });
        right.Children.Add(correctedLabel);
        right.Children.Add(new TextBlock { Text = "訂正理由", Foreground = Theme.Muted, Margin = new Thickness(0, 8, 0, 4) });
        right.Children.Add(correctionReason);
        right.Children.Add(correct);
        correct.Margin = new Thickness(0, 8, 0, 0);
        var rightCard = WrapCard(right);
        rightCard.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(rightCard, 1);
        body.Children.Add(rightCard);
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        Grid.SetRow(status, 3);
        status.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(status);
        return root;
    }

    private void LoadSelected(bool clearStatus = true)
    {
        if (scopes.SelectedItem is not ExplorerScopeOption scope)
        {
            return;
        }

        TryUpdate(() => workspace.Load(scope), clearStatus);
    }

    private void Run(Func<ExplorerScopeOption, ExplorerScreenSnapshot> action)
    {
        if (scopes.SelectedItem is ExplorerScopeOption scope)
        {
            TryUpdate(() => action(scope));
        }
    }

    private void Correct()
    {
        if (scopes.SelectedItem is not ExplorerScopeOption scope || nodes.SelectedItem is not ExplorerNodeItem node)
        {
            status.Text = "訂正する画面状態を選んでください。";
            return;
        }

        TryUpdate(() => workspace.Correct(
            scope,
            new ExplorerLabelCorrection(node.StateId, correctedLabel.Text.Trim(), correctionReason.Text.Trim())));
    }

    private void TryUpdate(Func<ExplorerScreenSnapshot> action, bool clearStatus = true)
    {
        try
        {
            Render(action());
            if (clearStatus)
            {
                status.Text = string.Empty;
            }
        }
        catch (Exception exception)
        {
            status.Text = exception.Message;
        }
    }

    private void Render(ExplorerScreenSnapshot value)
    {
        var selectedStateId = (nodes.SelectedItem as ExplorerNodeItem)?.StateId;
        snapshot = value;
        summary.Text = $"既知 {value.KnownStateCount}　｜　新しく見つけた候補 {value.NovelStateCount}";
        revision.Text = value.StructureRevisionId;
        frontier.ItemsSource = value.FrontierIds.Count == 0 ? new[] { "（なし）" } : value.FrontierIds;
        probe.Text = value.ActiveProbeLabel;
        risk.Text = value.RiskLabel;
        budget.Text = $"操作 {value.RemainingProbeCount}回　｜　時間 {value.RemainingElapsedMilliseconds}ms　｜　推論 {value.RemainingInferenceMilliseconds}ms";
        recovery.Text = value.RecoveryPathEdgeIds.Count == 0 ? "（確認できる戻り道なし）" : string.Join(" → ", value.RecoveryPathEdgeIds);
        stopReason.Text = value.StopReasonLabel;
        verification.Text = $"候補 {value.VerificationCounts.Candidate}　｜　再現済み {value.VerificationCounts.Replayed}　｜　確認済み {value.VerificationCounts.Verified}　｜　非対応 {value.VerificationCounts.Retired}";
        nodes.ItemsSource = value.Nodes;
        nodes.SelectedItem = value.Nodes.FirstOrDefault(node => node.StateId == selectedStateId);
        SetButtonState();
    }

    private void SetButtonState()
    {
        pause.IsEnabled = snapshot?.CanPause == true;
        step.IsEnabled = snapshot?.CanStep == true;
        abandon.IsEnabled = snapshot?.CanAbandon == true;
        correct.IsEnabled = snapshot?.CanCorrect == true;
    }

    private static StackPanel Card() => new();

    private static Border WrapCard(UIElement child) => new()
    {
        Background = Theme.Panel,
        BorderBrush = Theme.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(14),
        Child = child,
    };

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) };

    private static TextBlock NewValue() => new() { TextWrapping = TextWrapping.Wrap };

    private static void AddRow(Panel panel, string label, UIElement value)
    {
        panel.Children.Add(new TextBlock { Text = label, Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(0, 7, 0, 2) });
        panel.Children.Add(value);
    }

    private static Button ActionButton(string label) => new()
    {
        Content = label,
        Padding = new Thickness(12, 6, 12, 6),
        Margin = new Thickness(0, 0, 8, 0),
    };
}
