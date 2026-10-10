using OpenLogicool.Contracts.Research;
using OpenLogicool.Contracts.Playbooks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenLogicool.Desktop;

/// <summary>Game OperatorのSTEP 0 Web調査と構造探索をまとめた画面。</summary>
public sealed class GameOperatorWindow : Window
{
    private sealed record Choice<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    private readonly WebResearchWorkspace _workspace;
    private readonly TextBox _url = new() { MinWidth = 520 };
    private readonly ComboBox _terms = new() { MinWidth = 190 };
    private readonly ComboBox _robots = new() { MinWidth = 190 };
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Button _start = new() { Content = "調査開始", IsEnabled = false, Padding = new Thickness(12, 6, 12, 6) };
    private readonly ListBox _documents = new() { MinHeight = 150 };
    private readonly TextBox _markdown = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 220,
    };
    private MacroAutomationPanel? _macroPanel;

    public GameOperatorWindow(
        IWebResearchIntent intent,
        IExplorerIntents? explorerIntents = null,
        ILearningRouteIntents? learningRouteIntents = null,
        ISupervisedMacroIntents? supervisedMacroIntents = null,
        string? supervisedUnavailableReason = null,
        IMacroAutomationIntents? macroAutomationIntents = null,
        bool openMacroTab = false,
        IDemonstrationRecordingIntents? demonstrationRecordingIntents = null,
        IBotScriptIntents? botScriptIntents = null,
        IRemoteViewIntents? remoteViewIntents = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        _workspace = new WebResearchWorkspace(intent);
        Title = "OpenLogicool — Game Operator";
        // 左の一覧（208）の分だけ、以前の上タブの頃より幅を広げる。
        Width = 1190;
        Height = 780;
        MinWidth = 1030;
        MinHeight = 640;
        Theme.Apply(this);

        _terms.ItemsSource = new[]
        {
            new Choice<SourceTermsDisposition>("本文保存の許可を確認済み", SourceTermsDisposition.FullTextAllowed),
            new Choice<SourceTermsDisposition>("要約利用の許可を確認済み", SourceTermsDisposition.SummaryAllowed),
            new Choice<SourceTermsDisposition>("利用条件を判断できない", SourceTermsDisposition.Unknown),
            new Choice<SourceTermsDisposition>("利用条件に到達できない", SourceTermsDisposition.Unavailable),
            new Choice<SourceTermsDisposition>("利用条件で拒否", SourceTermsDisposition.Rejected),
        };
        _terms.SelectedIndex = 2;
        _robots.ItemsSource = new[]
        {
            new Choice<RobotsDisposition>("取得許可を確認済み", RobotsDisposition.Allowed),
            new Choice<RobotsDisposition>("取得可否を判断できない", RobotsDisposition.Unknown),
            new Choice<RobotsDisposition>("取得可否に到達できない", RobotsDisposition.Unavailable),
            new Choice<RobotsDisposition>("取得拒否", RobotsDisposition.Rejected),
        };
        _robots.SelectedIndex = 1;
        _documents.DisplayMemberPath = nameof(WebResearchDocumentItem.DisplayLabel);
        _documents.SelectionChanged += (_, _) => ShowSelectedMarkdown();
        _start.Click += async (_, _) => await StartAsync();

        Content = BuildContent(
            explorerIntents,
            learningRouteIntents,
            supervisedMacroIntents,
            supervisedUnavailableReason,
            macroAutomationIntents,
            demonstrationRecordingIntents);
        RefreshDocuments();
        if (botScriptIntents is not null && Content is TabControl botTabs)
        {
            var botPanel = new BotScriptPanel(botScriptIntents);
            botTabs.Items.Insert(0, new TabItem { Header = "Bot", Content = botPanel });
            if (!openMacroTab) botTabs.SelectedIndex = 0;
            var closingAfterStop = false;
            var stoppingForClose = false;
            Closing += async (_, eventArgs) =>
            {
                if (closingAfterStop) return;
                if (botScriptIntents.Current().Phase is not (BotScriptPhase.Starting or BotScriptPhase.Running or BotScriptPhase.Stopping or BotScriptPhase.ReviewMonitoring or BotScriptPhase.UserPaused)) return;
                eventArgs.Cancel = true;
                if (stoppingForClose) return;
                stoppingForClose = true;
                await botPanel.StopAsync();
                closingAfterStop = true;
                Close();
            };
        }
        if (remoteViewIntents is not null && Content is TabControl remoteViewTabs)
        {
            // Bot の次に置く。足しても最初に開く項目は変えない。
            var selected = remoteViewTabs.SelectedItem;
            remoteViewTabs.Items.Insert(botScriptIntents is not null ? 1 : 0,
                new TabItem { Header = "遠隔表示", Content = new RemoteViewPanel(remoteViewIntents) });
            remoteViewTabs.SelectedItem = selected;
        }
    }

    private UIElement BuildContent(
        IExplorerIntents? explorerIntents,
        ILearningRouteIntents? learningRouteIntents,
        ISupervisedMacroIntents? supervisedMacroIntents,
        string? supervisedUnavailableReason,
        IMacroAutomationIntents? macroAutomationIntents,
        IDemonstrationRecordingIntents? demonstrationRecordingIntents)
    {
        // 左の一覧で画面を切り替える（並びはよく使う順: Bot・マクロ・記録・学習した操作・構造探索・Web調査）。
        // TabControl のまま見た目だけを左の一覧へ変えるので、選択の仕組みは変わらない。
        var tabs = new TabControl { Style = (Style)FindResource("NavRailTabs") };
        if (macroAutomationIntents is not null)
        {
            _macroPanel = new MacroAutomationPanel(macroAutomationIntents);
            tabs.Items.Add(new TabItem { Header = "マクロ", Content = _macroPanel });
        }
        if (demonstrationRecordingIntents is not null)
        {
            tabs.Items.Add(new TabItem
            {
                Header = "記録",
                Content = new DemonstrationRecordingPanel(demonstrationRecordingIntents, OnMacroCreatedFromDemonstration),
            });
        }
        if (learningRouteIntents is not null)
        {
            tabs.Items.Add(new TabItem
            {
                Header = "学習した操作",
                Content = new LearningRoutePanel(
                    learningRouteIntents,
                    supervisedMacroIntents,
                    supervisedUnavailableReason),
            });
        }
        if (explorerIntents is not null)
        {
            tabs.Items.Add(new TabItem { Header = "構造探索", Content = new ExplorerPanel(explorerIntents) });
        }
        tabs.Items.Add(new TabItem { Header = "STEP 0　Web調査", Content = BuildResearchContent() });
        tabs.SelectedIndex = 0;
        return tabs;
    }

    private void OnMacroCreatedFromDemonstration(string routeId)
    {
        _macroPanel?.RefreshFromExternalCreation(routeId);
        SelectMacroTab();
    }

    public void SelectMacroTab()
    {
        if (Content is not TabControl tabs) return;
        var macro = tabs.Items.Cast<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "マクロ", StringComparison.Ordinal));
        if (macro is not null) tabs.SelectedItem = macro;
    }

    public void SelectControlPanel(string panel)
    {
        var header = panel switch { "bot" => "Bot", "remoteview" => "遠隔表示", "macro" => "マクロ", "recording" => "記録",
            "explorer" => "構造探索", "learning" => "学習した操作", "research" => "STEP 0　Web調査",
            _ => throw new ArgumentException($"表示先がありません: {panel}") };
        var tabs = (TabControl)Content;
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().SingleOrDefault(item => item.Header?.ToString() == header)
            ?? throw new InvalidOperationException($"この起動モードでは表示できません: {panel}");
    }

    private UIElement BuildResearchContent()
    {
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "STEP 0　Web調査", FontSize = 24, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock
        {
            Text = "Web情報は参考仮説です。ゲーム内の観測なしに操作許可やVerifiedへ昇格しません。",
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 4, 0, 0),
        });
        heading.Children.Add(new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 12, 0, 12),
            Child = new TextBlock
            {
                Text = "AI処理: このPC内　｜　外部AI送信: なし　｜　外部AI API費用: 0円",
                FontWeight = FontWeights.SemiBold,
            },
        });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var input = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _url.Text = "https://gamewith.jp/";
        input.Children.Add(_url);
        var previewButton = new Button { Content = "取得内容を確認", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
        previewButton.Click += (_, _) => Preview();
        Grid.SetColumn(previewButton, 1);
        input.Children.Add(previewButton);
        Grid.SetRow(input, 1);
        root.Children.Add(input);

        var policy = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        policy.Children.Add(new TextBlock { Text = "利用条件", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        policy.Children.Add(_terms);
        policy.Children.Add(new TextBlock { Text = "取得許可", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 8, 0) });
        policy.Children.Add(_robots);
        policy.Children.Add(_start);
        _start.Margin = new Thickness(18, 0, 0, 0);
        var exclude = new Button { Content = "このURLを除外", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(8, 0, 0, 0) };
        exclude.Click += (_, _) => Exclude();
        policy.Children.Add(exclude);
        Grid.SetRow(policy, 2);
        root.Children.Add(policy);

        var previewBox = new Border
        {
            Background = Theme.Chrome,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel { Children = { _preview, _status } },
        };
        Grid.SetRow(previewBox, 3);
        root.Children.Add(previewBox);

        var saved = new Grid();
        saved.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        saved.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(new TextBlock { Text = "保存済みReference", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        left.Children.Add(_documents);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var reacquire = new Button { Content = "再取得", Padding = new Thickness(10, 5, 10, 5) };
        reacquire.Click += async (_, _) => await ReacquireAsync();
        var delete = new Button { Content = "削除", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0) };
        delete.Click += (_, _) => DeleteSelected();
        actions.Children.Add(reacquire);
        actions.Children.Add(delete);
        left.Children.Add(actions);
        saved.Children.Add(left);
        var right = new StackPanel();
        right.Children.Add(new TextBlock { Text = "Markdown", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        right.Children.Add(_markdown);
        Grid.SetColumn(right, 1);
        saved.Children.Add(right);
        Grid.SetRow(saved, 4);
        root.Children.Add(saved);
        return root;
    }

    private void Preview()
    {
        if (!Uri.TryCreate(_url.Text.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            _status.Text = "HTTP(S) URLを入力してください。";
            _start.IsEnabled = false;
            return;
        }

        try
        {
            var result = _workspace.Preview(
                uri,
                ((Choice<SourceTermsDisposition>)_terms.SelectedItem).Value,
                ((Choice<RobotsDisposition>)_robots.SelectedItem).Value,
                DateTimeOffset.UtcNow.AddDays(30));
            _preview.Text = string.Join("\n", new[]
            {
                $"取得方針: {result.PolicyLabel}",
                $"保存内容: {result.SavedContentLabel}",
                $"引用: {result.QuoteLabel}",
                $"AI処理: {result.LocalAiLabel}",
                $"外部AI送信: {result.ExternalTransmissionLabel}",
                $"外部AI API費用: {result.ExternalApiCostLabel}",
                $"保存期限: {result.ExpiryLabel}",
            });
            _status.Text = string.Empty;
            _start.IsEnabled = true;
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _start.IsEnabled = false;
        }
    }

    private async Task StartAsync()
    {
        _start.IsEnabled = false;
        try
        {
            var result = await _workspace.StartAsync();
            _status.Text = result.StatusLabel;
            RefreshDocuments(result.DocumentId);
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
        }
        finally
        {
            _start.IsEnabled = _workspace.CurrentPreview is not null;
        }
    }

    private void Exclude()
    {
        if (!Uri.TryCreate(_url.Text.Trim(), UriKind.Absolute, out var uri))
        {
            _status.Text = "除外するURLを入力してください。";
            return;
        }

        _workspace.Exclude(uri, "利用者がSTEP 0画面で除外");
        _status.Text = "このURLを調査対象から除外しました。";
    }

    private async Task ReacquireAsync()
    {
        if (_documents.SelectedItem is not WebResearchDocumentItem item)
        {
            _status.Text = "再取得するReferenceを選んでください。";
            return;
        }

        try
        {
            var result = await _workspace.ReacquireAsync(item.SourceId);
            _status.Text = result.StatusLabel;
            RefreshDocuments(result.DocumentId);
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
        }
    }

    private void DeleteSelected()
    {
        if (_documents.SelectedItem is not WebResearchDocumentItem item)
        {
            _status.Text = "削除するReferenceを選んでください。";
            return;
        }

        var preview = _workspace.PreviewDelete(item.SourceId);
        var answer = MessageBox.Show(
            $"文書 {preview.DocumentIds.Count}件、候補fact {preview.FactIds.Count}件、{preview.PayloadBytes} bytesを削除します。",
            "STEP 0 Referenceの削除",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        _workspace.Delete(item.SourceId, "利用者がSTEP 0画面で削除");
        _status.Text = "Reference payloadを削除し、削除記録を残しました。";
        RefreshDocuments();
    }

    private void RefreshDocuments(string? selectDocumentId = null)
    {
        var items = _workspace.ListDocuments();
        _documents.ItemsSource = items;
        _documents.SelectedItem = items.FirstOrDefault(item => item.DocumentId == selectDocumentId)
                                  ?? items.FirstOrDefault();
        if (items.Count == 0)
        {
            _markdown.Text = string.Empty;
        }
    }

    private void ShowSelectedMarkdown()
    {
        _markdown.Text = _documents.SelectedItem is WebResearchDocumentItem item
            ? _workspace.GetMarkdown(item.DocumentId)
            : string.Empty;
    }
}
