using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace OpenLogicool.Desktop;

/// <summary>
/// Input Studio 新シェル（設計 docs/ui-design-phase3.md 案A・§3.5 段階1〜2）。
/// ヘッダ（編集中／現在有効／対象window／適用revision／実行mode＋段階4セル）＋
/// 左 ApplicationRail＋右に現行 device 台帳（<see cref="DeviceLedgerView"/>）を暫定配置する。
/// 編集対象（ApplicationRail の選択）は Host の観測値（現在有効・foreground）が変わっても動かさない
/// ——これが「Alt+Tab で編集対象を失わない」構造（設計 §2.3）。
/// Action 盤／Inspector／図／test field は段階3以降の範囲外（本 Window では最小の placeholder のみ）。
/// </summary>
public sealed class InputStudioWindow : Window
{
    private readonly WorkspaceScreenSnapshot _snapshot;
    private string _selectedApplicationFullPath;

    private readonly TextBlock _editingText = new();
    private readonly TextBlock _currentEffectiveText = new();
    private readonly TextBlock _targetWindowText = new();
    private readonly TextBlock _revisionText = new();
    private readonly TextBlock _executionModeText = new();
    private readonly StackPanel _stageStrip = new() { Orientation = Orientation.Horizontal };
    private readonly ListBox _applicationRail = new();

    public InputStudioWindow(
        WorkspaceScreenSnapshot snapshot,
        InputStudioReport ledgerReport,
        string initialSelectedApplicationFullPath)
    {
        _snapshot = snapshot;
        _selectedApplicationFullPath = initialSelectedApplicationFullPath;

        Title = "OpenLogicool Input Studio";
        MinWidth = 1100;
        MinHeight = 720;
        Width = 1200;
        Height = 800;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var chromeHeader = BuildChromeHeader();
        Grid.SetRow(chromeHeader, 0);
        root.Children.Add(chromeHeader);

        _stageStrip.Margin = new Thickness(12, 0, 12, 8);
        Grid.SetRow(_stageStrip, 1);
        root.Children.Add(_stageStrip);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        AutomationProperties.SetName(_applicationRail, "Application Rail");
        _applicationRail.SelectionChanged += OnRailSelectionChanged;
        Grid.SetColumn(_applicationRail, 0);
        body.Children.Add(_applicationRail);

        var actionBoardPlaceholder = new TextBlock
        {
            Text = "Action 盤（次段で実装）",
            Margin = new Thickness(12),
            TextWrapping = TextWrapping.Wrap,
        };
        actionBoardPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, SystemColors.GrayTextBrushKey);
        Grid.SetColumn(actionBoardPlaceholder, 1);
        body.Children.Add(actionBoardPlaceholder);

        var ledgerView = new DeviceLedgerView(ledgerReport);
        Grid.SetColumn(ledgerView, 2);
        body.Children.Add(ledgerView);

        Grid.SetRow(body, 2);
        root.Children.Add(body);

        Content = root;

        Render();
    }

    private Grid BuildChromeHeader()
    {
        var grid = new Grid { Margin = new Thickness(12, 12, 12, 4) };
        for (var i = 0; i < 5; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        void Place(TextBlock block, int column, string automationName)
        {
            block.TextWrapping = TextWrapping.Wrap;
            block.Margin = new Thickness(0, 0, 8, 0);
            AutomationProperties.SetName(block, automationName);
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
        }

        Place(_editingText, 0, "編集中");
        Place(_currentEffectiveText, 1, "現在有効");
        Place(_targetWindowText, 2, "対象 window");
        Place(_revisionText, 3, "適用 revision");
        Place(_executionModeText, 4, "実行 mode");

        return grid;
    }

    private void OnRailSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applicationRail.SelectedItem is not ListBoxItem { Tag: string applicationFullPath })
        {
            return;
        }

        if (applicationFullPath == _selectedApplicationFullPath)
        {
            return;
        }

        _selectedApplicationFullPath = applicationFullPath;
        Render();
    }

    private void Render()
    {
        var view = WorkspaceScreenProjection.Project(_snapshot, _selectedApplicationFullPath);

        _editingText.Text = $"編集中: {view.Chrome.EditingLabel}";
        _currentEffectiveText.Text = $"現在有効: {view.Chrome.CurrentEffectiveLabel}";
        _targetWindowText.Text = $"対象 window: {view.Chrome.TargetWindowLabel}";
        _revisionText.Text = $"適用 revision: {view.Chrome.AppliedRevisionLabel}";
        _executionModeText.Text = $"実行: {view.Chrome.ExecutionModeLabel}";

        _stageStrip.Children.Clear();
        foreach (var stage in view.Chrome.StageCells)
        {
            var cell = new TextBlock
            {
                Text = $"{stage.Stage} {stage.State}",
                Margin = new Thickness(0, 0, 16, 0),
            };
            AutomationProperties.SetName(cell, $"{stage.Stage}: {stage.State}");
            ToolTipService.SetToolTip(cell, stage.Detail);
            _stageStrip.Children.Add(cell);
        }

        _applicationRail.Items.Clear();
        foreach (var row in view.RailRows)
        {
            var suffixParts = new List<string>();
            if (row.IsRunning)
            {
                suffixParts.Add("実行中");
            }

            if (row.IsAssociated)
            {
                suffixParts.Add("assoc");
            }

            var suffix = suffixParts.Count == 0 ? string.Empty : $" [{string.Join(", ", suffixParts)}]";

            var item = new ListBoxItem
            {
                Content = $"{row.DisplayName}{suffix}",
                Tag = row.ApplicationFullPath,
                IsSelected = row.IsSelected,
            };
            AutomationProperties.SetName(item, row.DisplayName);
            _applicationRail.Items.Add(item);
        }
    }
}
