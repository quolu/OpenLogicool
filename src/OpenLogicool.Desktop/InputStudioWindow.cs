using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Desktop;

/// <summary>
/// Input Studio メイン画面（t09 第4段・オーナー承認済みモック docs/ui-mocks/main.html・states.html 準拠）。
/// 上部バー（設定中のアプリ pill・いまゲームに届いている割当・保存状態）＋
/// 左＝操作一覧・中央＝G13/G600 模式図・右＝割当パネル（唯一の editor）＋下部＝動作チェック strip。
/// Desktop は I/O を持たないため、document の読み込み・compile・保存・破棄はすべて
/// <see cref="IWorkspaceEditorIntents"/>（実装は Host）を通す。旧 device 台帳（<see cref="DeviceLedgerView"/>）は
/// この画面から撤去した（class 自体は次段の診断画面向けに残す）。
/// </summary>
public sealed class InputStudioWindow : Window
{
    private readonly IWorkspaceEditorIntents _intents;

    private WorkspaceScreenSnapshot _snapshot;
    private string _selectedApplicationFullPath;
    private WorkspaceDocument _document = null!;
    private string? _selectedActionId;
    private WorkspaceCompileOutcome _compileOutcome = null!;
    private bool _hasUnsavedChanges;
    private bool _justSaved;
    private string? _saveErrorMessage;
    private bool _isRenamingAction;

    private string _selectedFigureDeviceKind = "G13";
    private readonly Dictionary<string, string> _figureLayerByDevice = new(StringComparer.Ordinal);

    // 上部バー
    private readonly Button _appPillButton = new();
    private readonly TextBlock _appPillLabel = new() { FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, FontSize = 13 };
    private readonly ListBox _appPickerList = new();
    private readonly Popup _appPickerPopup = new() { StaysOpen = false, Placement = PlacementMode.Bottom };
    private readonly TextBlock _liveAssignmentText = new() { Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
    private readonly Border _saveChip = new() { CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _saveChipText = new() { FontWeight = FontWeights.SemiBold, FontSize = 12 };
    private readonly Button _saveButton = new() { Content = "保存", Padding = new Thickness(14, 6, 14, 6) };
    private readonly Button _revertButton = new() { Content = "元に戻す", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };

    // 左ペイン: 操作一覧
    private readonly ListBox _actionList = new();
    private readonly Button _addActionButton = new() { Content = "＋ 操作を追加" };

    // 中央ペイン: device タブ・配置チップ・模式図
    private readonly Button _g13TabButton = new();
    private readonly Button _g600TabButton = new();
    private readonly StackPanel _layerChipRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Border _figureHost = new()
    {
        Background = Theme.Raised,
        BorderBrush = Theme.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(0, 4, 4, 4),
        Padding = new Thickness(14, 12, 14, 10),
    };
    private readonly TextBlock _figureNoteText = new() { Foreground = Theme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), TextAlignment = TextAlignment.Center };

    // 右ペイン: 割当パネル（唯一の editor）
    private readonly Button _inspectorTitleButton = new() { HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly TextBox _inspectorNameBox = new();
    private readonly TextBox _outputsBox = new();
    private readonly StackPanel _conflictNotePanel = new();
    private readonly StackPanel _g13BindingsPanel = new();
    private readonly StackPanel _g600BindingsPanel = new();
    private readonly StackPanel _actionNotesPanel = new();
    private readonly StackPanel _inspectorEmptyPanel = new();

    // 下部: 動作チェック
    private readonly TextBlock _testFieldHint = new() { Foreground = Theme.Muted, FontSize = 12 };

    public InputStudioWindow(
        WorkspaceScreenSnapshot snapshot,
        InputStudioReport ledgerReport,
        string initialSelectedApplicationFullPath,
        IWorkspaceEditorIntents intents)
    {
        _ = ledgerReport; // 旧 device 台帳は撤去済み（DeviceLedgerView は次段の診断画面向けに残す）。
        _intents = intents;
        _snapshot = snapshot;
        _selectedApplicationFullPath = initialSelectedApplicationFullPath;

        Title = "OpenLogicool Input Studio";
        Background = Theme.Bg;
        Foreground = Theme.Text;
        MinWidth = 1100;
        MinHeight = 720;
        Width = 1360;
        Height = 840;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(268) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(292) });

        var actionPane = BuildActionListPane();
        Grid.SetColumn(actionPane, 0);
        body.Children.Add(actionPane);

        var figurePane = BuildFigurePane();
        Grid.SetColumn(figurePane, 1);
        body.Children.Add(figurePane);

        var bindingPane = BuildBindingPane();
        Grid.SetColumn(bindingPane, 2);
        body.Children.Add(bindingPane);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;

        WireEvents();
        LoadSelectedWorkspace();
        Render();
    }

    private void WireEvents()
    {
        _appPillButton.Click += (_, _) =>
        {
            _appPickerPopup.PlacementTarget = _appPillButton;
            _appPickerPopup.IsOpen = true;
        };
        _appPickerList.SelectionChanged += OnAppPickerSelectionChanged;

        _addActionButton.Click += (_, _) => OnAddAction();
        _actionList.SelectionChanged += OnActionListSelectionChanged;
        _actionList.PreviewKeyDown += OnActionListPreviewKeyDown;

        _g13TabButton.Click += (_, _) => { _selectedFigureDeviceKind = "G13"; Render(); };
        _g600TabButton.Click += (_, _) => { _selectedFigureDeviceKind = "G600"; Render(); };

        _inspectorTitleButton.Click += (_, _) =>
        {
            _isRenamingAction = true;
            Render();
            _inspectorNameBox.Focus();
            _inspectorNameBox.SelectAll();
        };
        _inspectorNameBox.LostFocus += (_, _) => OnInspectorNameCommitted();
        _inspectorNameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { OnInspectorNameCommitted(); e.Handled = true; } };

        _outputsBox.LostFocus += (_, _) => OnInspectorOutputsCommitted();
        _outputsBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { OnInspectorOutputsCommitted(); e.Handled = true; } };

        _saveButton.Click += (_, _) => SaveCurrentDocument();
        _revertButton.Click += (_, _) => DiscardUnsavedChanges();

        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    private void OnWindowPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl && e.Key == Key.S)
        {
            SaveCurrentDocument();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Z)
        {
            DiscardUnsavedChanges();
            e.Handled = true;
        }
    }

    // ─────────────────────────── header ───────────────────────────

    private UIElement BuildHeader()
    {
        var bar = new Border { Background = Theme.Chrome, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 8, 16, 8) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new TextBlock
        {
            Text = "INPUT STUDIO",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        left.Children.Add(new TextBlock
        {
            Text = "G13 ＋ G600",
            Foreground = Theme.Muted,
            FontSize = 12,
            Margin = new Thickness(8, 2, 20, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        AutomationProperties.SetName(_appPillButton, "設定中のアプリ");
        _appPillButton.Background = Theme.Raised;
        _appPillButton.BorderBrush = Theme.Line;
        _appPillButton.Padding = new Thickness(10, 5, 10, 5);
        var pillContent = new StackPanel { Orientation = Orientation.Horizontal };
        var pillLabel = new StackPanel();
        pillLabel.Children.Add(new TextBlock { Text = "設定中のアプリ", Foreground = Theme.Muted, FontSize = 10 });
        pillLabel.Children.Add(_appPillLabel);
        pillContent.Children.Add(pillLabel);
        pillContent.Children.Add(new TextBlock { Text = " ▾", Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        _appPillButton.Content = pillContent;
        left.Children.Add(_appPillButton);

        left.Children.Add(_liveAssignmentText);
        grid.Children.Add(left);

        _appPickerPopup.Child = new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { MaxHeight = 320, MinWidth = 260, Content = _appPickerList },
        };

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _saveChip.Child = _saveChipText;
        right.Children.Add(_saveChip);
        _saveButton.Background = Theme.Accent;
        _saveButton.Foreground = Brushes.White;
        right.Children.Add(_saveButton);
        right.Children.Add(_revertButton);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        bar.Child = grid;
        return bar;
    }

    // ─────────────────────────── 左ペイン: 操作 ───────────────────────────

    private UIElement BuildActionListPane()
    {
        var pane = new Border { Background = Theme.Panel, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0) };
        var dock = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };

        var header = new StackPanel { Margin = new Thickness(14, 0, 14, 8) };
        header.Children.Add(new TextBlock { Text = "操作", FontWeight = FontWeights.Bold, FontSize = 12 });
        header.Children.Add(new TextBlock { Text = "このアプリでやりたいこと", Foreground = Theme.Muted, FontSize = 11 });
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        var addRow = new Border
        {
            Margin = new Thickness(14, 8, 14, 0),
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
        };
        addRow.Child = _addActionButton;
        _addActionButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _addActionButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _addActionButton.Foreground = Theme.Muted;
        _addActionButton.Background = Brushes.Transparent;
        _addActionButton.BorderThickness = new Thickness(0);
        AutomationProperties.SetName(_addActionButton, "操作を追加");
        DockPanel.SetDock(addRow, Dock.Bottom);
        dock.Children.Add(addRow);

        AutomationProperties.SetName(_actionList, "操作一覧");
        _actionList.Background = Brushes.Transparent;
        _actionList.BorderThickness = new Thickness(0);
        dock.Children.Add(_actionList);

        pane.Child = dock;
        return pane;
    }

    // ─────────────────────────── 中央ペイン: 模式図 ───────────────────────────

    private UIElement BuildFigurePane()
    {
        var pane = new Border { Background = Theme.Panel };
        var dock = new DockPanel { Margin = new Thickness(14, 10, 14, 10) };

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
        ConfigureTabButton(_g13TabButton, Theme.G13);
        ConfigureTabButton(_g600TabButton, Theme.G600);
        tabs.Children.Add(_g13TabButton);
        tabs.Children.Add(_g600TabButton);
        DockPanel.SetDock(tabs, Dock.Top);
        dock.Children.Add(tabs);

        var stageBorder = new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0, 4, 4, 4),
            Padding = new Thickness(14, 12, 14, 10),
        };
        var stageStack = new StackPanel();
        stageStack.Children.Add(_layerChipRow);

        var figureScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520 };
        var figureCenter = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        _figureHost.Child = null;
        figureCenter.Children.Add(_figureHost);
        figureScroll.Content = figureCenter;
        stageStack.Children.Add(figureScroll);
        stageStack.Children.Add(_figureNoteText);
        stageBorder.Child = stageStack;
        dock.Children.Add(stageBorder);

        pane.Child = dock;
        return pane;
    }

    private static void ConfigureTabButton(Button button, Brush accent)
    {
        button.Padding = new Thickness(16, 7, 16, 7);
        button.Background = Theme.Raised;
        button.BorderBrush = Theme.Line;
        button.BorderThickness = new Thickness(1, 1, 1, 0);
        button.Margin = new Thickness(0, 0, 4, 0);
        button.Foreground = Theme.Text;
        button.Tag = accent;
    }

    // ─────────────────────────── 右ペイン: 割当パネル ───────────────────────────

    private UIElement BuildBindingPane()
    {
        var pane = new Border { Background = Theme.Panel, BorderBrush = Theme.Line, BorderThickness = new Thickness(1, 0, 0, 0) };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel { Margin = new Thickness(14, 10, 14, 14) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock { Text = "割当", FontWeight = FontWeights.Bold, FontSize = 12 });
        header.Children.Add(new TextBlock { Text = "選んだ操作をボタンへ", Foreground = Theme.Muted, FontSize = 11 });
        stack.Children.Add(header);

        stack.Children.Add(_conflictNotePanel);

        _inspectorTitleButton.FontSize = 16;
        _inspectorTitleButton.FontWeight = FontWeights.Bold;
        _inspectorNameBox.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(_inspectorTitleButton);
        stack.Children.Add(_inspectorNameBox);

        stack.Children.Add(_inspectorEmptyPanel);

        var outputsLabel = new TextBlock { Text = "ゲームに送るキー", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 8, 0, 4) };
        stack.Children.Add(outputsLabel);
        _outputsBox.Background = Theme.Sunken;
        _outputsBox.BorderBrush = Theme.Line;
        _outputsBox.Foreground = Theme.Text;
        _outputsBox.Padding = new Thickness(8, 6, 8, 6);
        _outputsBox.Margin = new Thickness(0, 0, 0, 12);
        AutomationProperties.SetName(_outputsBox, "ゲームに送るキー");
        _outputsBox.ToolTip = "空白区切りで複数キーを送れます（例: Key:LCtrl Key:C）";
        stack.Children.Add(_outputsBox);

        stack.Children.Add(BuildDeviceBlockHeader("G13 キーパッド", Theme.G13));
        stack.Children.Add(_g13BindingsPanel);
        stack.Children.Add(BuildDeviceBlockHeader("G600 マウス", Theme.G600));
        stack.Children.Add(_g600BindingsPanel);

        stack.Children.Add(_actionNotesPanel);

        scroll.Content = stack;
        pane.Child = scroll;
        return pane;
    }

    private static Border BuildDeviceBlockHeader(string label, Brush accent)
    {
        var header = new Border { Background = Theme.Raised, BorderBrush = Theme.Line, BorderThickness = new Thickness(1, 1, 1, 0), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 10, 0, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse2(accent));
        row.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(8, 0, 0, 0) });
        header.Child = row;
        return header;
    }

    // ─────────────────────────── footer ───────────────────────────

    private UIElement BuildFooter()
    {
        var bar = new Border { Background = Theme.Chrome, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 8, 16, 8) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "動作チェック", FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 12, 0) });
        _testFieldHint.Text = "デバイスのボタンを押すと、ここに結果が流れます";
        row.Children.Add(_testFieldHint);
        bar.Child = row;
        return bar;
    }

    // ─────────────────────────── data flow ───────────────────────────

    private void OnAppPickerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _appPickerPopup.IsOpen = false;
        if (_appPickerList.SelectedItem is not ListBoxItem { Tag: string applicationFullPath })
        {
            return;
        }

        if (applicationFullPath == _selectedApplicationFullPath)
        {
            return;
        }

        // アプリ切替は未保存の編集内容を破棄する（保存済み revision は revision store に残る）。
        _selectedApplicationFullPath = applicationFullPath;
        LoadSelectedWorkspace();
        Render();
    }

    private void LoadSelectedWorkspace()
    {
        var result = _intents.LoadDocument(_selectedApplicationFullPath);
        _document = result.Document;
        _selectedActionId = null;
        _isRenamingAction = false;
        _compileOutcome = _intents.Compile(_document);
        _snapshot = _snapshot with { SelectedWorkspaceRevisionNumber = result.RevisionNumber, Stages = result.Stages };
        _hasUnsavedChanges = false;
        _justSaved = false;
        _saveErrorMessage = null;

        _figureLayerByDevice.Clear();
        foreach (var device in _document.Devices)
        {
            _figureLayerByDevice[device.DeviceKind] = device.DefaultLayerId;
        }
    }

    private void OnActionListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selectedActionId = _actionList.SelectedItem is ListBoxItem { Tag: string actionId } ? actionId : null;
        if (selectedActionId == _selectedActionId)
        {
            return;
        }

        _selectedActionId = selectedActionId;
        _isRenamingAction = false;
        Render();
    }

    private void OnActionListPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_selectedActionId is null)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            var actionId = _selectedActionId;
            _selectedActionId = null;
            if (TryMutateDocument(document => WorkspaceDocumentEditor.DeleteAction(document, actionId)))
            {
                Render();
            }

            e.Handled = true;
        }
    }

    private void OnAddAction()
    {
        var actionId = GenerateActionId("action");
        var name = GenerateActionName();
        if (TryMutateDocument(document => WorkspaceDocumentEditor.AddAction(document, actionId, name, [])))
        {
            _selectedActionId = actionId;
            _isRenamingAction = true;
            Render();
            _inspectorNameBox.Focus();
            _inspectorNameBox.SelectAll();
        }
    }

    private void OnInspectorNameCommitted()
    {
        _isRenamingAction = false;
        if (_selectedActionId is null)
        {
            return;
        }

        var actionId = _selectedActionId;
        var newName = _inspectorNameBox.Text;
        if (TryMutateDocument(document => WorkspaceDocumentEditor.RenameAction(document, actionId, newName)))
        {
            Render();
        }
    }

    private void OnInspectorOutputsCommitted()
    {
        if (_selectedActionId is null)
        {
            return;
        }

        var actionId = _selectedActionId;
        var outputs = WorkspaceEditorProjection.ParseOutputs(_outputsBox.Text);
        if (TryMutateDocument(document => WorkspaceDocumentEditor.SetActionOutputs(document, actionId, outputs)))
        {
            Render();
        }
    }

    private void OnFigureKeyClicked(string deviceKind, string controlId)
    {
        if (_selectedActionId is null)
        {
            return;
        }

        var actionId = _selectedActionId;
        var layerId = _figureLayerByDevice.TryGetValue(deviceKind, out var currentLayer) ? currentLayer : "base";
        if (TryMutateDocument(document => WorkspaceDocumentEditor.SetBinding(document, actionId, deviceKind, controlId, layerId)))
        {
            Render();
        }
    }

    private void SaveCurrentDocument()
    {
        if (!_compileOutcome.IsValid || !_hasUnsavedChanges)
        {
            return;
        }

        try
        {
            var outcome = _intents.Save(_document);
            _snapshot = _snapshot with { SelectedWorkspaceRevisionNumber = outcome.RevisionNumber, Stages = outcome.Stages };
            _hasUnsavedChanges = false;
            _justSaved = true;
            _saveErrorMessage = null;
            Render();
        }
        catch (InvalidOperationException error)
        {
            _saveErrorMessage = $"保存できませんでした: {error.Message}";
            Render();
        }
    }

    private void DiscardUnsavedChanges()
    {
        if (!_hasUnsavedChanges)
        {
            return;
        }

        LoadSelectedWorkspace();
        Render();
    }

    /// <summary>
    /// document を変更する（<see cref="WorkspaceDocumentEditor"/> は構造エラーを ArgumentException で
    /// 投げる——ここで拾って画面へ出す。成功時は compile を取り直し、未保存 flag を立てるだけで、
    /// 呼び出し側が Render する）。
    /// </summary>
    private bool TryMutateDocument(Func<WorkspaceDocument, WorkspaceDocument> mutate)
    {
        WorkspaceDocument updated;
        try
        {
            updated = mutate(_document);
        }
        catch (ArgumentException error)
        {
            _saveErrorMessage = $"編集できません: {error.Message}";
            Render();
            return false;
        }

        _document = updated;
        _compileOutcome = _intents.Compile(_document);
        _hasUnsavedChanges = true;
        _justSaved = false;
        _saveErrorMessage = null;
        return true;
    }

    private string GenerateActionId(string baseSlug)
    {
        var existingIds = _document.Actions.Select(action => action.ActionId).ToHashSet(StringComparer.Ordinal);
        if (!existingIds.Contains(baseSlug))
        {
            return baseSlug;
        }

        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{baseSlug}-{suffix}";
            suffix++;
        }
        while (existingIds.Contains(candidate));

        return candidate;
    }

    private string GenerateActionName()
    {
        var existingNames = _document.Actions.Select(action => action.Name).ToHashSet(StringComparer.Ordinal);
        const string baseName = "新しい操作";
        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }
        while (existingNames.Contains(candidate));

        return candidate;
    }

    // ─────────────────────────── render ───────────────────────────

    private void Render()
    {
        var view = WorkspaceScreenProjection.Project(_snapshot, _selectedApplicationFullPath);
        var boardView = WorkspaceEditorProjection.Project(_document, _selectedActionId);

        RenderHeader(view);
        RenderActionList(boardView);
        RenderFigurePane();
        RenderBindingPane(boardView);
    }

    private void RenderHeader(WorkspaceScreenView view)
    {
        _appPillLabel.Text = view.Chrome.EditingLabel;
        _liveAssignmentText.Text = $"いまゲームに届いている割当: {view.Chrome.LiveAssignmentLabel}";

        _appPickerList.Items.Clear();
        foreach (var row in view.RailRows)
        {
            var item = new ListBoxItem
            {
                Content = row.IsRunning ? $"{row.DisplayName}（実行中）" : row.DisplayName,
                Tag = row.ApplicationFullPath,
                IsSelected = row.IsSelected,
                Padding = new Thickness(10, 6, 10, 6),
            };
            AutomationProperties.SetName(item, row.DisplayName);
            _appPickerList.Items.Add(item);
        }

        var chipText = !_compileOutcome.IsValid
            ? "保存できません"
            : _hasUnsavedChanges
                ? "未保存の変更あり"
                : _justSaved
                    ? "保存しました。ゲーム側への反映は再起動後"
                    : null;

        _saveChip.Visibility = chipText is null ? Visibility.Collapsed : Visibility.Visible;
        if (chipText is not null)
        {
            _saveChipText.Text = chipText;
            var brush = !_compileOutcome.IsValid ? Theme.Danger : _hasUnsavedChanges ? Theme.Warn : Theme.Ok;
            _saveChipText.Foreground = brush;
            _saveChip.BorderBrush = brush;
            _saveChip.BorderThickness = new Thickness(1);
        }

        _saveButton.IsEnabled = _compileOutcome.IsValid && _hasUnsavedChanges;
        _revertButton.IsEnabled = _hasUnsavedChanges;
    }

    private void RenderActionList(ActionBoardView boardView)
    {
        var actionIndexById = _document.Actions
            .Select((action, index) => (action.ActionId, index))
            .ToDictionary(pair => pair.ActionId, pair => pair.index, StringComparer.Ordinal);

        _actionList.Items.Clear();
        foreach (var row in boardView.Rows)
        {
            var colorIndex = actionIndexById.TryGetValue(row.ActionId, out var index) ? index : 0;

            var grid = new Grid { Margin = new Thickness(4, 4, 4, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var bar = new Border { Background = Theme.ActionColorAt(colorIndex), CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0) };
            grid.Children.Add(bar);

            var labels = new StackPanel();
            labels.Children.Add(new TextBlock { Text = row.Name, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            labels.Children.Add(new TextBlock
            {
                Text = row.OutputsLabel.Length == 0 ? "（送るキー未設定）" : $"{row.OutputsLabel} を送る",
                Foreground = Theme.Muted,
                FontSize = 11,
            });
            Grid.SetColumn(labels, 1);
            grid.Children.Add(labels);

            var badges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var cell in row.DeviceAssignments)
            {
                var isBound = cell.AssignmentLabel != "—";
                var accent = cell.DeviceKind == "G13" ? Theme.G13 : Theme.G600;
                badges.Children.Add(new Border
                {
                    BorderBrush = isBound ? accent : Theme.Line2,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(2, 0, 0, 0),
                    Child = new TextBlock
                    {
                        Text = isBound ? cell.DeviceKind : $"{cell.DeviceKind} なし",
                        Foreground = isBound ? accent : Theme.Muted,
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                    },
                });
            }

            Grid.SetColumn(badges, 2);
            grid.Children.Add(badges);

            var item = new ListBoxItem
            {
                Content = grid,
                Tag = row.ActionId,
                IsSelected = row.IsSelected,
                Background = row.IsSelected ? Theme.Raised : Brushes.Transparent,
                Padding = new Thickness(6, 4, 6, 4),
            };
            AutomationProperties.SetName(item, $"操作 {row.Name}");
            _actionList.Items.Add(item);
        }
    }

    private void RenderFigurePane()
    {
        var isG13 = _selectedFigureDeviceKind == "G13";
        _g13TabButton.Content = "G13 キーパッド　" + (_snapshot.G13ConnectedCount > 0 ? "接続中" : "未接続");
        _g600TabButton.Content = "G600 マウス　" + (_snapshot.G600ConnectedCount > 0 ? "接続中" : "未接続");
        StyleTab(_g13TabButton, isG13, Theme.G13);
        StyleTab(_g600TabButton, !isG13, Theme.G600);

        var layout = _document.Devices.FirstOrDefault(device => device.DeviceKind == _selectedFigureDeviceKind);
        _layerChipRow.Children.Clear();
        _layerChipRow.Children.Add(new TextBlock { Text = "いま見ている配置", Foreground = Theme.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        if (layout is not null)
        {
            var currentLayerId = _figureLayerByDevice.TryGetValue(_selectedFigureDeviceKind, out var layer) ? layer : layout.DefaultLayerId;
            foreach (var layerId in layout.LayerIds)
            {
                var isOn = layerId == currentLayerId;
                var chip = new Border
                {
                    BorderBrush = isOn ? (_selectedFigureDeviceKind == "G13" ? Theme.G13 : Theme.G600) : Theme.Line,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = isOn ? Theme.Sunken : Brushes.Transparent,
                    Child = new TextBlock
                    {
                        Text = LayerLabel(_selectedFigureDeviceKind, layerId),
                        Foreground = isOn ? Theme.Text : Theme.Muted,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 12,
                    },
                };
                var capturedLayerId = layerId;
                chip.MouseLeftButtonUp += (_, _) => { _figureLayerByDevice[_selectedFigureDeviceKind] = capturedLayerId; Render(); };
                chip.Cursor = Cursors.Hand;
                _layerChipRow.Children.Add(chip);
            }

            var lookupLayerId = _figureLayerByDevice.TryGetValue(_selectedFigureDeviceKind, out var lookupLayer) ? lookupLayer : layout.DefaultLayerId;
            var bindings = BuildFigureBindingLookup(_selectedFigureDeviceKind, lookupLayerId);
            _figureHost.Child = isG13
                ? InputStudioFigures.BuildG13(bindings, controlId => OnFigureKeyClicked("G13", controlId))
                : InputStudioFigures.BuildG600(bindings, controlId => OnFigureKeyClicked("G600", controlId));
        }

        _figureNoteText.Text = isG13
            ? "空のキーをクリックすると、左で選んでいる操作を載せます。色は左の操作と同じです。窪みは指のホーム位置です。"
            : "左が親指で押す 12 ボタンです。同じ色は左の操作と対応します。突起は親指のホーム位置です。";
    }

    private static void StyleTab(Button button, bool isOn, Brush accent)
    {
        button.Background = isOn ? Theme.Raised : Theme.Sunken;
        button.Foreground = isOn ? Theme.Text : Theme.Muted;
        button.BorderBrush = isOn ? accent : Theme.Line;
    }

    private Dictionary<string, InputStudioFigures.FigureBinding> BuildFigureBindingLookup(string deviceKind, string layerId)
    {
        var colors = _document.Actions
            .Select((action, index) => (action.ActionId, Color: Theme.ActionColorAt(index)))
            .ToDictionary(pair => pair.ActionId, pair => pair.Color, StringComparer.Ordinal);

        var result = new Dictionary<string, InputStudioFigures.FigureBinding>(StringComparer.Ordinal);
        foreach (var binding in _document.Bindings)
        {
            if (binding.DeviceKind != deviceKind || binding.LayerId != layerId)
            {
                continue;
            }

            var action = _document.Actions.FirstOrDefault(candidate => candidate.ActionId == binding.ActionId);
            if (action is null)
            {
                continue;
            }

            result[binding.ControlId] = new InputStudioFigures.FigureBinding(action.Name, colors[binding.ActionId]);
        }

        return result;
    }

    private void RenderBindingPane(ActionBoardView boardView)
    {
        _conflictNotePanel.Children.Clear();
        if (!_compileOutcome.IsValid)
        {
            _conflictNotePanel.Children.Add(NoteBlock(
                $"同じボタンに複数の操作が重なっています: {_compileOutcome.ErrorMessage}（解消するまで保存できません）", Theme.Danger));
        }

        if (_saveErrorMessage is not null)
        {
            _conflictNotePanel.Children.Add(NoteBlock(_saveErrorMessage, Theme.Danger));
        }

        var inspector = boardView.Inspector;
        _inspectorEmptyPanel.Children.Clear();
        _g13BindingsPanel.Children.Clear();
        _g600BindingsPanel.Children.Clear();
        _actionNotesPanel.Children.Clear();

        if (inspector is null)
        {
            _inspectorTitleButton.Visibility = Visibility.Collapsed;
            _inspectorNameBox.Visibility = Visibility.Collapsed;
            _outputsBox.IsEnabled = false;
            _outputsBox.Text = string.Empty;
            _inspectorEmptyPanel.Children.Add(new TextBlock
            {
                Text = "左の一覧から操作を選ぶと、ここに割当を出します。",
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var colorIndex = _document.Actions.ToList().FindIndex(action => action.ActionId == inspector.ActionId);
        var color = Theme.ActionColorAt(Math.Max(colorIndex, 0));

        if (_isRenamingAction)
        {
            _inspectorTitleButton.Visibility = Visibility.Collapsed;
            _inspectorNameBox.Visibility = Visibility.Visible;
            _inspectorNameBox.Text = inspector.Name;
        }
        else
        {
            _inspectorTitleButton.Visibility = Visibility.Visible;
            _inspectorNameBox.Visibility = Visibility.Collapsed;
            var titleContent = new StackPanel { Orientation = Orientation.Horizontal };
            titleContent.Children.Add(new Border { Width = 10, Height = 10, Background = color, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0) });
            titleContent.Children.Add(new TextBlock { Text = inspector.Name, FontSize = 16, FontWeight = FontWeights.Bold });
            _inspectorTitleButton.Content = titleContent;
            AutomationProperties.SetName(_inspectorTitleButton, $"操作名: {inspector.Name}（クリックで変更）");
        }

        _outputsBox.IsEnabled = true;
        if (!_outputsBox.IsFocused)
        {
            _outputsBox.Text = inspector.OutputsTokenText;
        }

        foreach (var deviceOptions in inspector.DeviceOptions)
        {
            var panel = deviceOptions.DeviceKind == "G13" ? _g13BindingsPanel : _g600BindingsPanel;
            var deviceBindings = inspector.Bindings.Where(binding => binding.DeviceKind == deviceOptions.DeviceKind).ToArray();

            if (deviceBindings.Length == 0)
            {
                panel.Children.Add(new TextBlock { Text = "（未割当）", Foreground = Theme.Muted, Margin = new Thickness(0, 4, 0, 4) });
                _actionNotesPanel.Children.Add(NoteBlock(
                    $"『{inspector.Name}』は{deviceOptions.DeviceKind}にまだ割り当てられていません（未割当でも保存できます）", Theme.Warn));
            }

            foreach (var binding in deviceBindings)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var slot = new StackPanel();
                slot.Children.Add(new TextBlock { Text = binding.ControlId, FontWeight = FontWeights.Bold, FontSize = 13 });
                slot.Children.Add(new TextBlock { Text = LayerLabel(deviceOptions.DeviceKind, binding.LayerId), Foreground = Theme.Muted, FontSize = 11 });
                row.Children.Add(slot);

                var removeButton = new Button { Content = "外す", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Theme.Accent, VerticalAlignment = VerticalAlignment.Top };
                var capturedDeviceKind = deviceOptions.DeviceKind;
                var capturedLayerId = binding.LayerId;
                removeButton.Click += (_, _) =>
                {
                    var actionId = inspector.ActionId;
                    if (TryMutateDocument(document => WorkspaceDocumentEditor.RemoveBinding(document, actionId, capturedDeviceKind, capturedLayerId)))
                    {
                        Render();
                    }
                };
                Grid.SetColumn(removeButton, 1);
                row.Children.Add(removeButton);

                panel.Children.Add(row);
            }

            panel.Children.Add(BuildAssignRow(inspector, deviceOptions));
        }
    }

    private UIElement BuildAssignRow(BindingInspectorView inspector, DeviceBindingOptionsView deviceOptions)
    {
        var layerCombo = new ComboBox { Margin = new Thickness(0, 4, 4, 0), MinWidth = 90 };
        foreach (var layerId in deviceOptions.LayerIds)
        {
            layerCombo.Items.Add(new ComboBoxItem { Content = LayerLabel(deviceOptions.DeviceKind, layerId), Tag = layerId });
        }

        if (layerCombo.Items.Count > 0)
        {
            layerCombo.SelectedIndex = 0;
        }

        var controlCombo = new ComboBox { Margin = new Thickness(0, 4, 4, 0), MinWidth = 90 };
        foreach (var control in deviceOptions.Controls)
        {
            controlCombo.Items.Add(new ComboBoxItem { Content = control.IsConfirmed ? control.ControlId : $"{control.ControlId}（強い推定）", Tag = control.ControlId });
        }

        if (controlCombo.Items.Count > 0)
        {
            controlCombo.SelectedIndex = 0;
        }

        var assignButton = new Button { Content = "割り当てる", Margin = new Thickness(0, 4, 0, 0) };
        assignButton.Click += (_, _) =>
        {
            if (layerCombo.SelectedItem is not ComboBoxItem { Tag: string layerId } ||
                controlCombo.SelectedItem is not ComboBoxItem { Tag: string controlId })
            {
                return;
            }

            var actionId = inspector.ActionId;
            var deviceKind = deviceOptions.DeviceKind;
            if (TryMutateDocument(document => WorkspaceDocumentEditor.SetBinding(document, actionId, deviceKind, controlId, layerId)))
            {
                Render();
            }
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        AutomationProperties.SetName(layerCombo, $"{deviceOptions.DeviceKind} 割当先の配置");
        AutomationProperties.SetName(controlCombo, $"{deviceOptions.DeviceKind} 割当先のボタン");
        row.Children.Add(layerCombo);
        row.Children.Add(controlCombo);
        row.Children.Add(assignButton);
        return row;
    }

    private static TextBlock NoteBlock(string text, Brush accent) => new()
    {
        Text = text,
        Foreground = accent,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
    };

    /// <summary>層 ID の表示名。既定 layer 構成（<see cref="WorkspaceDocumentEditor.CreateDraft"/>）に対応する固定表記で、
    /// 未知の layer ID はそのまま表示する（fallback を隠さない）。</summary>
    private static string LayerLabel(string deviceKind, string layerId) => (deviceKind, layerId) switch
    {
        ("G13", "base") => "いつも",
        ("G13", "m2") => "M2",
        ("G13", "m3") => "M3",
        ("G600", "base") => "いつも",
        ("G600", "shift") => "G-Shift を押している間",
        _ => layerId,
    };
}

/// <summary>device dot（mock の .devdot）を表す小さな丸。Ellipse を直接使わず単純化した Border ラッパー。</summary>
internal sealed class Ellipse2 : Border
{
    public Ellipse2(Brush color)
    {
        Width = 8;
        Height = 8;
        CornerRadius = new CornerRadius(4);
        Background = color;
        VerticalAlignment = VerticalAlignment.Center;
    }
}
