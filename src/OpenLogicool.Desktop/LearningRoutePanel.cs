using System.Windows;
using System.Windows.Controls;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Desktop;

/// <summary>AIが覚えた操作列を利用者が確認・訂正するGame Operatorの編集面。</summary>
internal sealed class LearningRoutePanel : UserControl
{
    private readonly LearningRouteWorkspace workspace;
    private readonly ISupervisedMacroIntents? runIntents;
    private readonly string? runUnavailableReason;
    private readonly ComboBox scopes = new() { MinWidth = 320, DisplayMemberPath = nameof(LearningRouteScopeOption.DisplayLabel) };
    private readonly ListBox available = new() { MinHeight = 260, DisplayMemberPath = nameof(LearningRouteEdgeItem.DisplayLabel) };
    private readonly ListBox steps = new() { MinHeight = 260, DisplayMemberPath = nameof(LearningRouteStepItem.DisplayLabel) };
    private readonly TextBox goal = new() { MinWidth = 320 };
    private readonly TextBox instruction = new() { MinWidth = 360, Text = "この順序で保存" };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock revision = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock macroState = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Button save = Button("保存");
    private readonly Button undo = Button("元に戻す");
    private readonly Button compile = Button("検証付きマクロを生成");
    private readonly Button startRun = Button("教師付きで開始");
    private readonly Button nextRun = Button("次の一手");
    private readonly Button stopRun = Button("停止");
    private readonly TextBlock runState = new() { TextWrapping = TextWrapping.Wrap };
    private LearningRouteScreenSnapshot? snapshot;
    private List<LearningRouteStepItem> draftSteps = [];

    public LearningRoutePanel(
        ILearningRouteIntents intents,
        ISupervisedMacroIntents? runIntents = null,
        string? runUnavailableReason = null)
    {
        workspace = new LearningRouteWorkspace(intents);
        this.runIntents = runIntents;
        this.runUnavailableReason = runUnavailableReason;
        Background = Theme.Bg;
        Foreground = Theme.Text;
        Content = Build();
        scopes.SelectionChanged += (_, _) => LoadSelected();
        available.SelectionChanged += (_, _) => ShowSelectedDetail();
        steps.SelectionChanged += (_, _) => ShowSelectedDetail();
        save.Click += (_, _) => Save();
        undo.Click += (_, _) => Undo();
        compile.Click += (_, _) => Compile();
        startRun.Click += (_, _) => StartRun();
        nextRun.Click += (_, _) => NextRun();
        stopRun.Click += (_, _) => StopRun();

        var options = workspace.ListScopes();
        scopes.ItemsSource = options;
        scopes.SelectedItem = options.FirstOrDefault();
        if (options.Count == 0)
        {
            status.Text = "保存済みのゲーム構造がありません。先に構造探索を行ってください。";
            SetButtonState();
        }
    }

    private UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "学習した操作ルート", FontSize = 24, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock
        {
            Text = "AIが見つけた操作順です。保存前に追加・削除・並べ替え・差し替えができます。",
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 4, 0, 10),
        });
        heading.Children.Add(scopes);
        heading.Children.Add(new TextBlock { Text = "達成したいこと", Foreground = Theme.Muted, Margin = new Thickness(0, 10, 0, 3) });
        heading.Children.Add(goal);
        heading.Children.Add(revision);
        revision.Margin = new Thickness(0, 8, 0, 12);
        root.Children.Add(heading);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });

        body.Children.Add(Card("見つけた操作", available));

        var addActions = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        var add = Button("追加 →");
        var replace = Button("差し替え →");
        add.Click += (_, _) => Add();
        replace.Click += (_, _) => Replace();
        addActions.Children.Add(add);
        addActions.Children.Add(replace);
        Grid.SetColumn(addActions, 1);
        body.Children.Add(addActions);

        var routeCard = new StackPanel();
        routeCard.Children.Add(new TextBlock { Text = "実行する順序", FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        routeCard.Children.Add(steps);
        var order = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var up = Button("上へ");
        var down = Button("下へ");
        var remove = Button("削除");
        up.Click += (_, _) => Move(-1);
        down.Click += (_, _) => Move(1);
        remove.Click += (_, _) => Remove();
        order.Children.Add(up);
        order.Children.Add(down);
        order.Children.Add(remove);
        routeCard.Children.Add(order);
        var wrappedRoute = Wrap(routeCard);
        Grid.SetColumn(wrappedRoute, 2);
        body.Children.Add(wrappedRoute);

        var detailCard = Card("選択したstepの根拠", detail);
        Grid.SetColumn(detailCard, 4);
        body.Children.Add(detailCard);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var reason = new StackPanel();
        reason.Children.Add(new TextBlock { Text = "修正内容・指示", Foreground = Theme.Muted });
        reason.Children.Add(instruction);
        reason.Children.Add(macroState);
        macroState.Margin = new Thickness(0, 6, 0, 0);
        var runButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        runButtons.Children.Add(startRun);
        runButtons.Children.Add(nextRun);
        runButtons.Children.Add(stopRun);
        reason.Children.Add(runButtons);
        reason.Children.Add(runState);
        reason.Children.Add(status);
        footer.Children.Add(reason);
        var footerButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        footerButtons.Children.Add(undo);
        footerButtons.Children.Add(compile);
        footerButtons.Children.Add(save);
        save.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(footerButtons, 1);
        footer.Children.Add(footerButtons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private void LoadSelected()
    {
        if (scopes.SelectedItem is not LearningRouteScopeOption scope)
        {
            return;
        }
        TryUpdate(() => workspace.Load(scope));
    }

    private void Save()
    {
        if (scopes.SelectedItem is not LearningRouteScopeOption scope || snapshot is null)
        {
            return;
        }
        TryUpdate(() => workspace.Save(
            scope,
            snapshot,
            goal.Text,
            draftSteps.Select(item => item.Edge.EdgeId).ToArray(),
            instruction.Text));
    }

    private void Undo()
    {
        if (scopes.SelectedItem is LearningRouteScopeOption scope && snapshot is not null)
        {
            TryUpdate(() => workspace.Undo(scope, snapshot));
        }
    }

    private void Compile()
    {
        if (scopes.SelectedItem is LearningRouteScopeOption scope && snapshot is not null)
        {
            TryUpdate(() => workspace.Compile(scope, snapshot));
        }
    }

    private void StartRun()
    {
        if (runIntents is null || snapshot?.RouteId is null || snapshot.VersionId is null)
        {
            return;
        }
        TryRun(() => runIntents.Start(
            snapshot.GameId,
            snapshot.EnvironmentScope,
            snapshot.RouteId,
            snapshot.VersionId));
    }

    private void NextRun()
    {
        if (runIntents is not null)
        {
            TryRun(runIntents.Next);
        }
    }

    private void StopRun()
    {
        if (runIntents is not null)
        {
            TryRun(runIntents.Stop);
        }
    }

    private void TryRun(Func<SupervisedMacroRunSnapshot> action)
    {
        try
        {
            RenderRun(action());
        }
        catch (Exception exception)
        {
            status.Text = exception.Message;
        }
    }

    private void RenderRun(SupervisedMacroRunSnapshot value)
    {
        var view = SupervisedMacroRunPresenter.Project(value, draftSteps);
        runState.Text = view.Text;
        startRun.IsEnabled = view.CanStart;
        nextRun.IsEnabled = view.CanDispatch;
        stopRun.IsEnabled = view.CanStop;
    }

    private void Add()
    {
        if (available.SelectedItem is LearningRouteEdgeItem edge)
        {
            draftSteps.Add(new LearningRouteStepItem(draftSteps.Count + 1, edge));
            RefreshDraft(draftSteps.Count - 1);
        }
    }

    private void Replace()
    {
        if (available.SelectedItem is not LearningRouteEdgeItem edge || steps.SelectedIndex < 0)
        {
            status.Text = "差し替えるstepと新しい操作を選んでください。";
            return;
        }
        var index = steps.SelectedIndex;
        draftSteps[index] = new LearningRouteStepItem(index + 1, edge);
        RefreshDraft(index);
    }

    private void Remove()
    {
        if (steps.SelectedIndex < 0)
        {
            return;
        }
        var index = steps.SelectedIndex;
        draftSteps.RemoveAt(index);
        RefreshDraft(Math.Min(index, draftSteps.Count - 1));
    }

    private void Move(int delta)
    {
        var from = steps.SelectedIndex;
        var to = from + delta;
        if (from < 0 || to < 0 || to >= draftSteps.Count)
        {
            return;
        }
        (draftSteps[from], draftSteps[to]) = (draftSteps[to], draftSteps[from]);
        RefreshDraft(to);
    }

    private void RefreshDraft(int selectedIndex)
    {
        draftSteps = draftSteps
            .Select((item, index) => new LearningRouteStepItem(index + 1, item.Edge))
            .ToList();
        steps.ItemsSource = null;
        steps.ItemsSource = draftSteps;
        steps.SelectedIndex = selectedIndex;
        status.Text = "未保存の変更があります。";
    }

    private void ShowSelectedDetail()
    {
        var edge = (steps.SelectedItem as LearningRouteStepItem)?.Edge
                   ?? available.SelectedItem as LearningRouteEdgeItem;
        detail.Text = edge is null
            ? "操作を選ぶと、場所・入力・期待する次画面・検証段階を表示します。"
            : string.Join("\n", new[]
            {
                $"前: {edge.SourceLabel}",
                $"操作場所: {edge.LocatorLabel}",
                $"入力: {edge.PrimitiveLabel}",
                $"期待する結果: {edge.ExpectedOutcomeLabel}",
                $"次: {edge.DestinationLabel}",
                $"検証段階: {edge.VerificationLabel}",
                $"注意: {edge.RiskLabel}",
            });
    }

    private void TryUpdate(Func<LearningRouteScreenSnapshot> action)
    {
        try
        {
            Render(action());
            status.Text = snapshot?.SaveStateLabel ?? string.Empty;
        }
        catch (Exception exception)
        {
            status.Text = exception.Message;
        }
    }

    private void Render(LearningRouteScreenSnapshot value)
    {
        snapshot = value;
        goal.Text = value.Goal;
        instruction.Text = value.UserInstruction;
        available.ItemsSource = value.AvailableEdges;
        draftSteps = value.Steps.ToList();
        steps.ItemsSource = draftSteps;
        revision.Text = $"構造版: {value.StructureRevisionId}　｜　保存版: {(value.RevisionNumber == 0 ? "未保存" : value.RevisionNumber)}";
        macroState.Text = $"マクロ: {value.MacroStateLabel}　｜　直近の画面監査: {value.LastAuditLabel}";
        SetButtonState();
        ShowSelectedDetail();
    }

    private void SetButtonState()
    {
        save.IsEnabled = snapshot is not null;
        undo.IsEnabled = snapshot?.CanUndo == true;
        compile.IsEnabled = !string.IsNullOrWhiteSpace(snapshot?.VersionId);
        startRun.IsEnabled = runIntents is not null && !string.IsNullOrWhiteSpace(snapshot?.VersionId);
        nextRun.IsEnabled = false;
        stopRun.IsEnabled = false;
        if (runIntents is null)
        {
            runState.Text = runUnavailableReason ?? "教師付き実行の実環境接続は利用できません。";
        }
    }

    private static Border Card(string heading, UIElement content)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(content);
        return Wrap(panel);
    }

    private static Border Wrap(UIElement content) => new()
    {
        Background = Theme.Panel,
        BorderBrush = Theme.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(12),
        Child = content,
    };

    private static Button Button(string label) => new()
    {
        Content = label,
        Padding = new Thickness(11, 6, 11, 6),
        Margin = new Thickness(0, 0, 6, 6),
    };
}
