using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Desktop;

/// <summary>
/// Input Studio 新シェル（設計 docs/ui-design-phase3.md 案A・§3.5 段階1〜3）。
/// ヘッダ（編集中／現在有効／対象window／適用revision／実行mode＋段階4セル）＋
/// 左 ApplicationRail＋中央 Action 盤＋右 Binding Inspector（唯一の editor）＋現行 device 台帳を配置する。
/// 編集対象（ApplicationRail の選択）は Host の観測値（現在有効・foreground）が変わっても動かさない
/// ——これが「Alt+Tab で編集対象を失わない」構造（設計 §2.3）。
/// Desktop は I/O を持たないため、document の読み込み・compile・保存・undo はすべて
/// <see cref="IWorkspaceEditorIntents"/>（実装は Host）を通す（設計 §3.1）。
/// 模式図（device figure）・test field は段階4以降の範囲外。
/// </summary>
public sealed class InputStudioWindow : Window
{
    private readonly IWorkspaceEditorIntents _intents;

    private WorkspaceScreenSnapshot _snapshot;
    private string _selectedApplicationFullPath;
    private WorkspaceDocument _document = null!;
    private string? _selectedActionId;
    private WorkspaceCompileOutcome _compileOutcome = null!;

    private readonly TextBlock _editingText = new();
    private readonly TextBlock _currentEffectiveText = new();
    private readonly TextBlock _targetWindowText = new();
    private readonly TextBlock _revisionText = new();
    private readonly TextBlock _executionModeText = new();
    private readonly StackPanel _stageStrip = new() { Orientation = Orientation.Horizontal };
    private readonly ListBox _applicationRail = new();

    // Action 盤（設計 §1 案A: 行=Semantic Action、列=出力／device ごとの割当）
    private readonly TextBox _newActionNameBox = new() { Width = 110, Margin = new Thickness(0, 0, 4, 0) };
    private readonly TextBox _newActionOutputsBox = new() { Width = 150, Margin = new Thickness(0, 0, 4, 0) };
    private readonly Button _addActionButton = new() { Content = "＋ action" };
    private readonly ListBox _actionBoard = new();

    // Binding Inspector（唯一の editor・設計 §2.2「右ペイン常時」）
    private readonly TextBlock _inspectorTitle = new() { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBox _inspectorNameBox = new() { Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBox _inspectorOutputsBox = new() { Margin = new Thickness(0, 0, 0, 4) };
    private readonly StackPanel _inspectorBindingsPanel = new();
    private readonly ComboBox _bindingDeviceCombo = new() { Margin = new Thickness(0, 0, 4, 0) };
    private readonly ComboBox _bindingLayerCombo = new() { Margin = new Thickness(0, 0, 4, 0) };
    private readonly ComboBox _bindingControlCombo = new() { Margin = new Thickness(0, 0, 4, 0) };
    private readonly Button _assignButton = new() { Content = "このキーへ割当" };
    private readonly TextBlock _compileStatusText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _saveButton = new() { Content = "保存（revision）" };
    private readonly Button _undoButton = new() { Content = "undo" };
    private readonly TextBox _undoRevisionBox = new() { Width = 48, ToolTip = "revision 番号（未入力なら最新の一つ前）" };
    private readonly TextBlock _saveStatusText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    public InputStudioWindow(
        WorkspaceScreenSnapshot snapshot,
        InputStudioReport ledgerReport,
        string initialSelectedApplicationFullPath,
        IWorkspaceEditorIntents intents)
    {
        _intents = intents;
        _snapshot = snapshot;
        _selectedApplicationFullPath = initialSelectedApplicationFullPath;

        Title = "OpenLogicool Input Studio";
        MinWidth = 1100;
        MinHeight = 720;
        Width = 1360;
        Height = 840;

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
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AutomationProperties.SetName(_applicationRail, "Application Rail");
        _applicationRail.SelectionChanged += OnRailSelectionChanged;
        Grid.SetColumn(_applicationRail, 0);
        body.Children.Add(_applicationRail);

        var actionBoardColumn = BuildActionBoardColumn();
        Grid.SetColumn(actionBoardColumn, 1);
        body.Children.Add(actionBoardColumn);

        var inspectorColumn = BuildInspectorColumn();
        Grid.SetColumn(inspectorColumn, 2);
        body.Children.Add(inspectorColumn);

        var ledgerView = new DeviceLedgerView(ledgerReport);
        Grid.SetColumn(ledgerView, 3);
        body.Children.Add(ledgerView);

        Grid.SetRow(body, 2);
        root.Children.Add(body);

        Content = root;

        PreviewKeyDown += OnWindowPreviewKeyDown;
        _addActionButton.Click += (_, _) => OnAddAction();
        _newActionOutputsBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { OnAddAction(); e.Handled = true; } };
        _actionBoard.SelectionChanged += OnActionBoardSelectionChanged;
        _actionBoard.PreviewKeyDown += OnActionBoardPreviewKeyDown;
        _inspectorNameBox.LostFocus += (_, _) => OnInspectorNameCommitted();
        _inspectorNameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { OnInspectorNameCommitted(); e.Handled = true; } };
        _inspectorOutputsBox.LostFocus += (_, _) => OnInspectorOutputsCommitted();
        _inspectorOutputsBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { OnInspectorOutputsCommitted(); e.Handled = true; } };
        _bindingDeviceCombo.SelectionChanged += OnBindingDeviceChanged;
        _assignButton.Click += (_, _) => AssignSelectedBinding();
        _bindingControlCombo.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AssignSelectedBinding(); e.Handled = true; } };
        _saveButton.Click += (_, _) => SaveCurrentDocument();
        _undoButton.Click += (_, _) => UndoCurrentWorkspace(ParseRevisionNumber());

        LoadSelectedWorkspace();
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

    private UIElement BuildActionBoardColumn()
    {
        var panel = new DockPanel { Margin = new Thickness(12, 0, 12, 12) };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetName(_newActionNameBox, "新規 action 名");
        _newActionNameBox.ToolTip = "action 名（例: 回避）";
        AutomationProperties.SetName(_newActionOutputsBox, "新規 action 出力");
        _newActionOutputsBox.ToolTip = "出力 token（例: Key:LShift）";
        toolbar.Children.Add(_newActionNameBox);
        toolbar.Children.Add(_newActionOutputsBox);
        toolbar.Children.Add(_addActionButton);
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);

        AutomationProperties.SetName(_actionBoard, "Action 盤");
        panel.Children.Add(_actionBoard);

        return panel;
    }

    private UIElement BuildInspectorColumn()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };

        panel.Children.Add(_inspectorTitle);

        panel.Children.Add(new TextBlock { Text = "名前" });
        AutomationProperties.SetName(_inspectorNameBox, "action 名");
        panel.Children.Add(_inspectorNameBox);

        panel.Children.Add(new TextBlock { Text = "出力 token（空白区切り・canonical 表記）" });
        AutomationProperties.SetName(_inspectorOutputsBox, "action 出力 token");
        panel.Children.Add(_inspectorOutputsBox);

        panel.Children.Add(new TextBlock { Text = "割当", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4) });
        panel.Children.Add(_inspectorBindingsPanel);

        var assignRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(_bindingDeviceCombo, "割当先 device");
        AutomationProperties.SetName(_bindingLayerCombo, "割当先 layer");
        AutomationProperties.SetName(_bindingControlCombo, "割当先 control");
        assignRow.Children.Add(_bindingDeviceCombo);
        assignRow.Children.Add(_bindingLayerCombo);
        assignRow.Children.Add(_bindingControlCombo);
        assignRow.Children.Add(_assignButton);
        panel.Children.Add(assignRow);

        AutomationProperties.SetName(_compileStatusText, "compile 状態");
        panel.Children.Add(_compileStatusText);

        var saveRow = new StackPanel { Orientation = Orientation.Horizontal };
        saveRow.Children.Add(_saveButton);
        saveRow.Children.Add(new TextBlock { Text = "  undo:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
        saveRow.Children.Add(_undoRevisionBox);
        saveRow.Children.Add(_undoButton);
        panel.Children.Add(saveRow);

        panel.Children.Add(_saveStatusText);

        return panel;
    }

    private void OnWindowPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && shift && e.Key == Key.Z)
        {
            _undoRevisionBox.Focus();
            _undoRevisionBox.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Z)
        {
            UndoCurrentWorkspace(revisionNumber: null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.S)
        {
            SaveCurrentDocument();
            e.Handled = true;
        }
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

        // 選択切替は未保存の編集内容を破棄する（Phase 3 は確認ダイアログを持たない——
        // 保存済み revision は revision store に残るため失われるのは未保存の下書きだけ）。
        _selectedApplicationFullPath = applicationFullPath;
        LoadSelectedWorkspace();
        Render();
    }

    private void LoadSelectedWorkspace()
    {
        var result = _intents.LoadDocument(_selectedApplicationFullPath);
        _document = result.Document;
        _selectedActionId = null;
        _compileOutcome = _intents.Compile(_document);
        _snapshot = _snapshot with { SelectedWorkspaceRevisionNumber = result.RevisionNumber, Stages = result.Stages };
        _saveStatusText.Text = string.Empty;
    }

    private void OnActionBoardSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selectedActionId = _actionBoard.SelectedItem is ListBoxItem { Tag: string actionId } ? actionId : null;
        if (selectedActionId == _selectedActionId)
        {
            return;
        }

        _selectedActionId = selectedActionId;
        RenderInspector();
    }

    private void OnActionBoardPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_selectedActionId is null)
        {
            return;
        }

        if (e.Key == Key.F2)
        {
            _inspectorNameBox.Focus();
            _inspectorNameBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
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
        var name = _newActionNameBox.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var actionId = GenerateActionId(name);
        var outputs = WorkspaceEditorProjection.ParseOutputs(_newActionOutputsBox.Text);
        if (TryMutateDocument(document => WorkspaceDocumentEditor.AddAction(document, actionId, name, outputs)))
        {
            _newActionNameBox.Text = string.Empty;
            _newActionOutputsBox.Text = string.Empty;
            _selectedActionId = actionId;
            Render();
        }
    }

    private void OnInspectorNameCommitted()
    {
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
        var outputs = WorkspaceEditorProjection.ParseOutputs(_inspectorOutputsBox.Text);
        if (TryMutateDocument(document => WorkspaceDocumentEditor.SetActionOutputs(document, actionId, outputs)))
        {
            Render();
        }
    }

    private void OnBindingDeviceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_bindingDeviceCombo.SelectedItem is not ComboBoxItem { Tag: DeviceBindingOptionsView deviceOptions })
        {
            _bindingLayerCombo.Items.Clear();
            _bindingControlCombo.Items.Clear();
            return;
        }

        _bindingLayerCombo.Items.Clear();
        foreach (var layerId in deviceOptions.LayerIds)
        {
            var label = layerId == deviceOptions.DefaultLayerId ? $"{layerId}（既定）" : layerId;
            _bindingLayerCombo.Items.Add(new ComboBoxItem { Content = label, Tag = layerId });
        }

        if (_bindingLayerCombo.Items.Count > 0)
        {
            _bindingLayerCombo.SelectedIndex = 0;
        }

        _bindingControlCombo.Items.Clear();
        foreach (var control in deviceOptions.Controls)
        {
            var label = control.IsConfirmed ? control.ControlId : $"{control.ControlId}（強い推定）";
            _bindingControlCombo.Items.Add(new ComboBoxItem { Content = label, Tag = control });
        }

        if (_bindingControlCombo.Items.Count > 0)
        {
            _bindingControlCombo.SelectedIndex = 0;
        }
    }

    private void AssignSelectedBinding()
    {
        if (_selectedActionId is null)
        {
            return;
        }

        if (_bindingDeviceCombo.SelectedItem is not ComboBoxItem { Tag: DeviceBindingOptionsView deviceOptions } ||
            _bindingLayerCombo.SelectedItem is not ComboBoxItem { Tag: string layerId } ||
            _bindingControlCombo.SelectedItem is not ComboBoxItem { Tag: ControlOptionView control })
        {
            return;
        }

        var actionId = _selectedActionId;
        if (TryMutateDocument(document =>
                WorkspaceDocumentEditor.SetBinding(document, actionId, deviceOptions.DeviceKind, control.ControlId, layerId)))
        {
            Render();
        }
    }

    private void SaveCurrentDocument()
    {
        if (!_compileOutcome.IsValid)
        {
            _saveStatusText.Text = $"保存できません: {_compileOutcome.ErrorMessage}";
            return;
        }

        try
        {
            var outcome = _intents.Save(_document);
            _snapshot = _snapshot with { SelectedWorkspaceRevisionNumber = outcome.RevisionNumber, Stages = outcome.Stages };
            _saveStatusText.Text = $"保存しました（revision {outcome.RevisionNumber}）";
            Render();
        }
        catch (InvalidOperationException error)
        {
            _saveStatusText.Text = $"保存に失敗しました: {error.Message}";
        }
    }

    private void UndoCurrentWorkspace(long? revisionNumber)
    {
        try
        {
            var outcome = _intents.Undo(_document.WorkspaceId, revisionNumber);
            _document = outcome.Document;
            _selectedActionId = null;
            _compileOutcome = _intents.Compile(_document);
            _snapshot = _snapshot with { SelectedWorkspaceRevisionNumber = outcome.RevisionNumber, Stages = outcome.Stages };
            _saveStatusText.Text = $"undo: revision {outcome.RevisionNumber} として再適用しました";
            Render();
        }
        catch (InvalidOperationException error)
        {
            _saveStatusText.Text = $"undo に失敗しました: {error.Message}";
        }
    }

    private long? ParseRevisionNumber() =>
        long.TryParse(_undoRevisionBox.Text, out var parsed) ? parsed : null;

    /// <summary>
    /// document を変更する（<see cref="WorkspaceDocumentEditor"/> は構造エラーを ArgumentException で
    /// 投げる——ここで拾って画面へ出す。成功時は compile を取り直すだけで、呼び出し側が Render する）。
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
            _saveStatusText.Text = $"編集できません: {error.Message}";
            return false;
        }

        _document = updated;
        _compileOutcome = _intents.Compile(_document);
        return true;
    }

    private string GenerateActionId(string name)
    {
        var slugChars = name.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var slug = new string(slugChars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        if (slug.Length == 0)
        {
            slug = "action";
        }

        var existingIds = _document.Actions.Select(action => action.ActionId).ToHashSet(StringComparer.Ordinal);
        if (!existingIds.Contains(slug))
        {
            return slug;
        }

        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{slug}-{suffix}";
            suffix++;
        }
        while (existingIds.Contains(candidate));

        return candidate;
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

        RenderActionBoard();
        RenderInspector();
    }

    private void RenderActionBoard()
    {
        var boardView = WorkspaceEditorProjection.Project(_document, _selectedActionId);

        _actionBoard.Items.Clear();
        foreach (var row in boardView.Rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = row.Name, Width = 110, TextWrapping = TextWrapping.Wrap });
            line.Children.Add(new TextBlock
            {
                Text = row.OutputsLabel.Length == 0 ? "（出力未設定）" : row.OutputsLabel,
                Width = 150,
                TextWrapping = TextWrapping.Wrap,
            });
            foreach (var cell in row.DeviceAssignments)
            {
                line.Children.Add(new TextBlock
                {
                    Text = $"{cell.DeviceKind}: {cell.AssignmentLabel}",
                    Width = 110,
                    Margin = new Thickness(8, 0, 0, 0),
                });
            }

            var item = new ListBoxItem { Content = line, Tag = row.ActionId, IsSelected = row.IsSelected };
            AutomationProperties.SetName(item, $"action {row.Name}");
            _actionBoard.Items.Add(item);
        }

        RenderCompileStatus();
    }

    private void RenderCompileStatus()
    {
        _saveButton.IsEnabled = _compileOutcome.IsValid;

        if (!_compileOutcome.IsValid)
        {
            _compileStatusText.Text = $"衝突: {_compileOutcome.ErrorMessage}";
            return;
        }

        _compileStatusText.Text = _compileOutcome.Warnings.Count == 0
            ? $"compile 成立（profile {_compileOutcome.ProfileCount} 件・警告なし）"
            : $"compile 成立（profile {_compileOutcome.ProfileCount} 件）\n" + string.Join("\n", _compileOutcome.Warnings.Select(warning => $"警告: {warning}"));
    }

    private void RenderInspector()
    {
        var view = WorkspaceEditorProjection.Project(_document, _selectedActionId);
        var inspector = view.Inspector;

        if (inspector is null)
        {
            _inspectorTitle.Text = "action を選択してください";
            _inspectorNameBox.IsEnabled = false;
            _inspectorNameBox.Text = string.Empty;
            _inspectorOutputsBox.IsEnabled = false;
            _inspectorOutputsBox.Text = string.Empty;
            _inspectorBindingsPanel.Children.Clear();
            _bindingDeviceCombo.Items.Clear();
            _bindingLayerCombo.Items.Clear();
            _bindingControlCombo.Items.Clear();
            _assignButton.IsEnabled = false;
            return;
        }

        _inspectorTitle.Text = $"action: {inspector.Name}";
        _inspectorNameBox.IsEnabled = true;
        _inspectorNameBox.Text = inspector.Name;
        _inspectorOutputsBox.IsEnabled = true;
        _inspectorOutputsBox.Text = inspector.OutputsTokenText;
        _assignButton.IsEnabled = true;

        _inspectorBindingsPanel.Children.Clear();
        if (inspector.Bindings.Count == 0)
        {
            _inspectorBindingsPanel.Children.Add(new TextBlock { Text = "（未割当）" });
        }

        foreach (var binding in inspector.Bindings)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var removeButton = new Button { Content = "削除", Margin = new Thickness(4, 0, 0, 0) };
            removeButton.Click += (_, _) =>
            {
                var actionId = inspector.ActionId;
                if (TryMutateDocument(document => WorkspaceDocumentEditor.RemoveBinding(document, actionId, binding.DeviceKind, binding.LayerId)))
                {
                    Render();
                }
            };
            DockPanel.SetDock(removeButton, Dock.Right);
            row.Children.Add(removeButton);
            row.Children.Add(new TextBlock { Text = $"{binding.DeviceKind} {binding.ControlId} ・ {binding.LayerId}" });
            _inspectorBindingsPanel.Children.Add(row);
        }

        _bindingDeviceCombo.Items.Clear();
        foreach (var deviceOptions in inspector.DeviceOptions)
        {
            _bindingDeviceCombo.Items.Add(new ComboBoxItem { Content = deviceOptions.DeviceKind, Tag = deviceOptions });
        }

        if (_bindingDeviceCombo.Items.Count > 0)
        {
            _bindingDeviceCombo.SelectedIndex = 0;
        }
    }
}
