using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Desktop;

/// <summary>Botの操作と状態表示。画像取得・判定・入力はHostへ委ねる。</summary>
public sealed class BotScriptPanel : UserControl
{
    private readonly IBotScriptIntents intents;
    private readonly ComboBox scripts = new() { MinWidth = 320, DisplayMemberPath = nameof(BotScriptItem.Name) };
    private readonly Button start = new() { Content = "Botを開始", Padding = new Thickness(20, 10, 20, 10) };
    private readonly Button stop = new() { Content = "Botを停止", Padding = new Thickness(20, 10, 20, 10), Margin = new Thickness(12, 0, 0, 0) };
    private readonly Button evidence = new() { Content = "記録フォルダーを開く", Padding = new Thickness(12, 6, 12, 6) };
    private readonly TextBlock status = new() { FontSize = 26, FontWeight = FontWeights.Bold };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock metrics = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 16) };
    private readonly TextBlock description = new() { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Muted, Margin = new Thickness(0, 12, 0, 20) };
    private readonly TextBlock operationError = new() { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Danger, Margin = new Thickness(0, 8, 0, 8) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public BotScriptPanel(IBotScriptIntents intents)
    {
        this.intents = intents;
        Background = Theme.Bg;
        Foreground = Theme.Text;
        AutomationProperties.SetName(start, "Botを開始");
        AutomationProperties.SetName(stop, "Botを停止");
        scripts.ItemsSource = intents.ListScripts();
        scripts.SelectionChanged += (_, _) => description.Text = (scripts.SelectedItem as BotScriptItem)?.Description ?? "利用できるBotがありません。";
        if (scripts.Items.Count > 0) scripts.SelectedIndex = 0;
        start.Click += (_, _) =>
        {
            operationError.Text = "";
            try { intents.Start(((BotScriptItem)scripts.SelectedItem).Id); Refresh(); }
            catch (Exception error) { operationError.Text = error.Message; }
        };
        stop.Click += async (_, _) => await StopAsync();
        evidence.Click += (_, _) =>
        {
            try { intents.OpenEvidence(); }
            catch (Exception error) { operationError.Text = error.Message; }
        };
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "Bot", FontSize = 28, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });
        root.Children.Add(scripts);
        root.Children.Add(description);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 24) };
        buttons.Children.Add(start); buttons.Children.Add(stop); root.Children.Add(buttons);
        root.Children.Add(operationError);
        root.Children.Add(new Border { Background = Theme.Raised, Padding = new Thickness(18), CornerRadius = new CornerRadius(6),
            Child = new StackPanel { Children = { status, detail, metrics, evidence } } });
        root.Children.Add(new TextBlock { Text = "判断が必要な画面では入力を止め、ここに理由を表示します。\n停止後は画面を確認してから再開してください。Game Operatorやアプリを閉じた時もBotを停止します。",
            Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0) });
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Refresh();
    }

    public async Task StopAsync()
    {
        operationError.Text = "";
        var pending = intents.StopAsync();
        Refresh();
        try { await pending; }
        catch (Exception error) { operationError.Text = error.Message; return; }
        Refresh();
    }

    public void Refresh()
    {
        var current = intents.Current();
        var busy = current.Phase is BotScriptPhase.Starting or BotScriptPhase.Running or BotScriptPhase.Stopping or BotScriptPhase.ReviewMonitoring;
        scripts.IsEnabled = !busy;
        start.IsEnabled = !busy && scripts.SelectedItem is not null;
        stop.IsEnabled = current.Phase is BotScriptPhase.Starting or BotScriptPhase.Running or BotScriptPhase.ReviewMonitoring;
        evidence.IsEnabled = current.EvidenceDirectory is not null;
        status.Text = current.Phase switch
        {
            BotScriptPhase.Starting => "開始しています",
            BotScriptPhase.Running => "実行中",
            BotScriptPhase.Stopping => "停止しています",
            BotScriptPhase.AwaitingReview => "画面の確認が必要です",
            BotScriptPhase.ReviewMonitoring => "確認待ち・監視継続中",
            BotScriptPhase.Faulted => "エラーで停止しました",
            _ => "停止済み"
        };
        status.Foreground = current.Phase switch
        {
            BotScriptPhase.Running => Theme.Ok,
            BotScriptPhase.Faulted => Theme.Danger,
            BotScriptPhase.AwaitingReview or BotScriptPhase.ReviewMonitoring => Theme.Warn,
            _ => Theme.Text
        };
        detail.Text = current.Detail;
        var health = current.HealthFraction is { } hp ? hp.ToString("P0") : "非表示・未判定";
        metrics.Text = $"HP: {health}　｜　回復観測: {current.ObservationCount}回　｜　直近間隔: {current.LastIntervalMs}ms\n"
            + $"進行入力: {current.InputCount}回　食事: {current.FoodCount}回　ポーション: {current.PotionCount}回　包帯: {current.BandageCount}回";
    }
}
