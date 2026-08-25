using OpenLogicool.Contracts.Playbooks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenLogicool.Desktop;

internal sealed class MacroAutomationPanel : UserControl
{
    private sealed record ModeChoice(string Label, MacroPlaybackMode Value)
    {
        public override string ToString() => Label;
    }

    private readonly MacroAutomationWorkspace workspace;
    private readonly IMacroAutomationIntents intents;
    private readonly ComboBox targets = new() { MinWidth = 320, DisplayMemberPath = nameof(MacroTargetOption.DisplayLabel), Background = Brushes.White, Foreground = Brushes.Black, BorderBrush = Theme.Line };
    private readonly TextBox goal = new() { MinWidth = 420, Background = Theme.Raised, Foreground = Theme.Text, BorderBrush = Theme.Line, CaretBrush = Theme.Text, Padding = new Thickness(6, 4, 6, 4) };
    private readonly ListBox catalog = new() { MinHeight = 190, DisplayMemberPath = nameof(MacroCatalogItem.DisplayLabel), Background = Theme.Sunken, Foreground = Theme.Text, BorderBrush = Theme.Line };
    private readonly ComboBox mode = new() { MinWidth = 180, Background = Brushes.White, Foreground = Brushes.Black, BorderBrush = Theme.Line };
    private readonly ListBox composition = new() { MinHeight = 110, DisplayMemberPath = nameof(MacroCatalogItem.DisplayLabel), Background = Theme.Sunken, Foreground = Theme.Text, BorderBrush = Theme.Line };
    private readonly TextBox compositionGoal = new() { MinWidth = 320, Background = Theme.Raised, Foreground = Theme.Text, BorderBrush = Theme.Line, CaretBrush = Theme.Text, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button create = Button("AIに作ってもらう");
    private readonly Button play = Button("再生");
    private readonly Button stop = Button("停止");
    private CancellationTokenSource? running;
    private List<MacroCatalogItem> compositionItems = [];

    public MacroAutomationPanel(IMacroAutomationIntents intents)
    {
        Background = Theme.Bg;
        Foreground = Theme.Text;
        this.intents = intents;
        workspace = new MacroAutomationWorkspace(intents);
        mode.ItemsSource = new[]
        {
            new ModeChoice("AI監視あり（問題stepを修復）", MacroPlaybackMode.AiMonitored),
            new ModeChoice("AI監視なし（保存済み操作のみ）", MacroPlaybackMode.AiFree),
        };
        mode.SelectedIndex = 0;
        create.Click += async (_, _) => await CreateAsync();
        play.Click += async (_, _) => await PlayAsync();
        stop.Click += (_, _) => Stop();
        catalog.SelectionChanged += (_, _) => SelectMatchingTarget();
        intents.StateChanged += OnStateChanged;
        Unloaded += (_, _) => intents.StateChanged -= OnStateChanged;
        stop.IsEnabled = false;
        Content = Build();
        Refresh();
    }

    private UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(20) };
        for (var i = 0; i < 5; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "マクロ", FontSize = 24, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock
        {
            Text = "AIに目的を伝えて操作を覚えさせ、保存済みマクロを監視あり／なしで再生します。",
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 4, 0, 14),
        });
        Add(root, heading, 0);

        var targetRow = Row("操作するアプリ", targets);
        Add(root, targetRow, 1);

        var createRow = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        createRow.Children.Add(new TextBlock { Text = "AIにやってほしいことを話す", FontWeight = FontWeights.SemiBold });
        var createActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        createActions.Children.Add(goal);
        createActions.Children.Add(create);
        create.Margin = new Thickness(8, 0, 0, 0);
        createRow.Children.Add(createActions);
        Add(root, createRow, 2);

        var playback = new Grid();
        playback.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        playback.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        playback.Children.Add(catalog);
        var playbackActions = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        playbackActions.Children.Add(new TextBlock { Text = "再生モード", FontWeight = FontWeights.SemiBold });
        playbackActions.Children.Add(mode);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(play);
        stop.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(stop);
        playbackActions.Children.Add(buttons);
        Grid.SetColumn(playbackActions, 1);
        playback.Children.Add(playbackActions);
        Add(root, playback, 3);

        var compose = new GroupBox
        {
            Header = "複数マクロを順番に統合",
            Margin = new Thickness(0, 14, 0, 0),
            Foreground = Theme.Text,
            BorderBrush = Theme.Line,
            Background = Theme.Panel,
        };
        var composeBody = new Grid { Margin = new Thickness(10) };
        composeBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        composeBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        composeBody.Children.Add(composition);
        var composeActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var add = Button("選択中を末尾へ"); add.Click += (_, _) => AddComposition();
        var remove = Button("外す"); remove.Margin = new Thickness(6, 0, 0, 0); remove.Click += (_, _) => RemoveComposition();
        var up = Button("上へ"); up.Margin = new Thickness(6, 0, 0, 0); up.Click += (_, _) => MoveComposition(-1);
        var down = Button("下へ"); down.Margin = new Thickness(6, 0, 0, 0); down.Click += (_, _) => MoveComposition(1);
        composeActions.Children.Add(add); composeActions.Children.Add(remove); composeActions.Children.Add(up); composeActions.Children.Add(down);
        composeActions.Children.Add(compositionGoal);
        compositionGoal.Margin = new Thickness(18, 0, 0, 0);
        compositionGoal.ToolTip = "統合後のマクロ名・目的";
        var save = Button("統合して保存"); save.Margin = new Thickness(8, 0, 0, 0); save.Click += (_, _) => Compose();
        composeActions.Children.Add(save);
        Grid.SetRow(composeActions, 1);
        composeBody.Children.Add(composeActions);
        compose.Content = composeBody;
        Add(root, compose, 4);
        Add(root, status, 5);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async Task CreateAsync()
    {
        if (targets.SelectedItem is not MacroTargetOption target) { status.Text = "操作するアプリを選んでください。"; return; }
        await RunAsync(token => workspace.CreateAsync(target, goal.Text, Progress(), token));
        Refresh();
    }

    private async Task PlayAsync()
    {
        if (targets.SelectedItem is not MacroTargetOption target) { status.Text = "操作するアプリを選んでください。"; return; }
        if (catalog.SelectedItem is not MacroCatalogItem macro) { status.Text = "再生するマクロを選んでください。"; return; }
        var selectedMode = ((ModeChoice)mode.SelectedItem).Value;
        await RunAsync(token => workspace.PlayAsync(target, macro, selectedMode, Progress(), token));
        Refresh(macro.RouteId);
    }

    private async Task RunAsync(Func<CancellationToken, Task<MacroRunSnapshot>> action)
    {
        if (running is not null) return;
        running = new CancellationTokenSource();
        SetRunning(true);
        try { Render(await action(running.Token)); }
        catch (OperationCanceledException) { status.Text = "停止しました。"; }
        catch (Exception error) { status.Text = error.Message; }
        finally { running.Dispose(); running = null; SetRunning(false); }
    }

    private IProgress<MacroRunSnapshot> Progress() => new Progress<MacroRunSnapshot>(Render);

    private void Render(MacroRunSnapshot snapshot) =>
        status.Text = $"{snapshot.Detail}\nstep {snapshot.StepNumber}　{snapshot.ActionLabel}　{snapshot.TransitionLabel}　AI {snapshot.AiCallCount}回　版 {snapshot.RouteRevision}";

    private void OnStateChanged(MacroRunSnapshot snapshot)
    {
        if (Dispatcher.CheckAccess()) Render(snapshot);
        else _ = Dispatcher.BeginInvoke(() => Render(snapshot));
    }

    private void Stop()
    {
        running?.Cancel();
        Render(workspace.Stop());
    }

    private void SetRunning(bool value)
    {
        create.IsEnabled = !value;
        play.IsEnabled = !value;
        stop.IsEnabled = value;
    }

    private void AddComposition()
    {
        if (catalog.SelectedItem is not MacroCatalogItem item) return;
        compositionItems.Add(item);
        RenderComposition(compositionItems.Count - 1);
    }

    private void RemoveComposition()
    {
        if (composition.SelectedIndex < 0) return;
        var index = composition.SelectedIndex;
        compositionItems.RemoveAt(index);
        RenderComposition(Math.Min(index, compositionItems.Count - 1));
    }

    private void MoveComposition(int offset)
    {
        var from = composition.SelectedIndex;
        var to = from + offset;
        if (from < 0 || to < 0 || to >= compositionItems.Count) return;
        (compositionItems[from], compositionItems[to]) = (compositionItems[to], compositionItems[from]);
        RenderComposition(to);
    }

    private void Compose()
    {
        try
        {
            var saved = workspace.Compose(compositionGoal.Text, compositionItems);
            status.Text = $"統合マクロ「{saved.Goal}」を保存しました。";
            compositionItems = [];
            RenderComposition(-1);
            Refresh(saved.RouteId);
        }
        catch (Exception error) { status.Text = error.Message; }
    }

    private void Refresh(string? selectRouteId = null)
    {
        var targetItems = workspace.ListTargets();
        targets.ItemsSource = targetItems;
        if (targets.SelectedIndex < 0 && targetItems.Count > 0) targets.SelectedIndex = 0;
        var macros = workspace.ListMacros();
        catalog.ItemsSource = macros;
        if (selectRouteId is not null)
            catalog.SelectedItem = macros.FirstOrDefault(item => item.RouteId == selectRouteId);
        if (catalog.SelectedIndex < 0 && macros.Count > 0) catalog.SelectedIndex = 0;
    }

    private void SelectMatchingTarget()
    {
        if (catalog.SelectedItem is not MacroCatalogItem macro) return;
        var match = targets.Items.Cast<MacroTargetOption>().FirstOrDefault(target =>
            string.Equals(target.ProcessName, macro.GameId, StringComparison.OrdinalIgnoreCase));
        if (match is not null) targets.SelectedItem = match;
    }

    private void RenderComposition(int selectedIndex)
    {
        composition.ItemsSource = null;
        composition.ItemsSource = compositionItems;
        composition.SelectedIndex = selectedIndex;
    }

    private static StackPanel Row(string label, UIElement value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(value);
        return row;
    }

    private static Button Button(string label) => new() { Content = label, Padding = new Thickness(10, 5, 10, 5) };

    private static void Add(Grid grid, UIElement element, int row)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }
}
