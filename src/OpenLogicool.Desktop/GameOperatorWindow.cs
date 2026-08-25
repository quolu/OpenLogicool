using OpenLogicool.Contracts.Research;
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

    public GameOperatorWindow(
        IWebResearchIntent intent,
        IExplorerIntents? explorerIntents = null,
        ILearningRouteIntents? learningRouteIntents = null,
        ISupervisedMacroIntents? supervisedMacroIntents = null,
        string? supervisedUnavailableReason = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        _workspace = new WebResearchWorkspace(intent);
        Title = "OpenLogicool — Game Operator";
        Width = 980;
        Height = 760;
        MinWidth = 820;
        MinHeight = 640;
        Background = Theme.Bg;
        Foreground = Theme.Text;

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
            supervisedUnavailableReason);
        RefreshDocuments();
    }

    private UIElement BuildContent(
        IExplorerIntents? explorerIntents,
        ILearningRouteIntents? learningRouteIntents,
        ISupervisedMacroIntents? supervisedMacroIntents,
        string? supervisedUnavailableReason)
    {
        var tabs = new TabControl
        {
            Background = Theme.Bg,
            Foreground = Theme.Text,
        };
        tabs.Items.Add(new TabItem { Header = "STEP 0　Web調査", Content = BuildResearchContent(), MinWidth = 130 });
        if (explorerIntents is not null)
        {
            tabs.Items.Add(new TabItem { Header = "構造探索", Content = new ExplorerPanel(explorerIntents), MinWidth = 130 });
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
                MinWidth = 130,
            });
        }
        return tabs;
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
