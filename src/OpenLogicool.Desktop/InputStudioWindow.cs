using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly InputStudioReport _report;
    private readonly IResidentApplyIntent? _residentApply;
    private DiagnosticsWindow? _diagnosticsWindow;
    private DispatcherTimer? _traceTimer;

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
    private readonly TextBlock _appPillLabel = new()
    {
        FontWeight = FontWeights.SemiBold,
        Foreground = Theme.Text,
        FontSize = 13,
        MaxWidth = 220,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    // Background を明示しないと ListBox は OS 既定の白下地になり、白文字の項目が読めなくなる
    // （popup の中は Window の配色を継がない前提で全部明示する）。
    private readonly ListBox _appPickerList = new()
    {
        Background = Theme.Raised,
        Foreground = Theme.Text,
        BorderThickness = new Thickness(0),
    };
    private readonly Popup _appPickerPopup = new() { StaysOpen = false, Placement = PlacementMode.Bottom };
    private readonly TextBlock _liveAssignmentValueText = new()
    {
        Foreground = Theme.Text,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 260,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Border _saveChip = new() { CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _saveChipText = new() { FontWeight = FontWeights.SemiBold, FontSize = 12 };
    private readonly Button _saveButton = new() { Content = "保存", Padding = new Thickness(14, 6, 14, 6) };
    private readonly Button _revertButton = new() { Content = "元に戻す", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };

    // 左ペイン: 操作一覧
    private readonly ListBox _actionList = new();

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
    // 操作の削除入口。Delete キーだけでは見つけられない（実利用の指摘 2026-08-22）ため画面に置く。
    private readonly Button _deleteActionButton = new()
    {
        Content = "この操作を削除",
        Foreground = Theme.Danger,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(6, 2, 6, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBox _inspectorNameBox = new()
    {
        Background = Theme.Sunken,
        Foreground = Theme.Text,
        BorderBrush = Theme.Line,
        CaretBrush = Theme.Text,
        Padding = new Thickness(4, 3, 4, 3),
    };
    private readonly Button _recordAddButton = new() { Content = "録って追加", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _recordUpdateButton = new() { Content = "録って更新", Padding = new Thickness(10, 6, 10, 6) };
    // 実機ボタン押しで割当先を指定する待機状態（録画確定後に自動で入る。常駐同居時のみ機能）。
    private bool _pendingAssign;
    private readonly TextBlock _assignHint = new()
    {
        Foreground = Theme.Warn,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
        Visibility = Visibility.Collapsed,
    };
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
        IWorkspaceEditorIntents intents,
        IResidentApplyIntent? residentApply = null)
    {
        _report = ledgerReport; // 旧 device 台帳は撤去済み。診断画面（DiagnosticsWindow）の中身として復活させる。
        _intents = intents;
        _residentApply = residentApply;
        _snapshot = snapshot;
        _selectedApplicationFullPath = initialSelectedApplicationFullPath;

        Title = "OpenLogicool Input Studio";
        Background = Theme.Bg;
        Foreground = Theme.Text;
        // OS 既定 Button テンプレートの無効時・ホバー時のライト塗り直しを全窓（modal 含む）で無効化する。
        var flatButtonStyle = Theme.CreateFlatButtonStyle();
        Resources[typeof(Button)] = flatButtonStyle;
        if (Application.Current is { } application && !application.Resources.Contains(typeof(Button)))
        {
            application.Resources[typeof(Button)] = flatButtonStyle;
        }
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

        if (_residentApply is not null)
        {
            _traceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _traceTimer.Tick += (_, _) => PollResidentTrace();
            _traceTimer.Start();
        }

        Closed += (_, _) =>
        {
            _traceTimer?.Stop();
            _diagnosticsWindow?.Close();
        };
    }

    private void PollResidentTrace()
    {
        var events = _residentApply!.DrainTraceEvents();
        foreach (var traceEvent in events)
        {
            if (_pendingAssign && traceEvent.IsDown)
            {
                AssignByPress(traceEvent.DeviceKind, traceEvent.ControlId);
            }
        }

        var lastLine = events.LastOrDefault(traceEvent => traceEvent.DisplayLine is not null)?.DisplayLine;
        if (lastLine is not null)
        {
            _testFieldHint.Text = lastLine;
        }
    }

    /// <summary>「録って追加」: 新しい操作を作って録画に入る。取り消されたら空の操作を残さない。</summary>
    private void RecordNewAction()
    {
        var newActionId = GenerateActionId("action");
        var newActionName = GenerateActionName();
        if (!TryMutateDocument(document => WorkspaceDocumentEditor.AddAction(document, newActionId, newActionName, [])))
        {
            return;
        }

        _selectedActionId = newActionId;
        Render();

        var dialog = new KeyCaptureDialog(newActionName, "（未設定）", overwritesExisting: false) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } token)
        {
            if (TryMutateDocument(document => SetOutputsWithAutoName(document, newActionId, WorkspaceEditorProjection.ParseOutputs(token))))
            {
                ArmAssignByPress();
                Render();
            }
        }
        else
        {
            _selectedActionId = null;
            if (TryMutateDocument(document => WorkspaceDocumentEditor.DeleteAction(document, newActionId)))
            {
                Render();
            }
        }
    }

    /// <summary>「録って更新」: 選んでいる操作のキーを録り直す（上書きは dialog で明示する）。</summary>
    private void RecordUpdateSelected()
    {
        if (_selectedActionId is null)
        {
            return;
        }

        var actionId = _selectedActionId;
        var action = _document.Actions.FirstOrDefault(candidate => candidate.ActionId == actionId);
        if (action is null)
        {
            return;
        }

        var currentLabel = WorkspaceEditorProjection.FormatOutputs(action.Outputs) is { Length: > 0 } label ? label : "（未設定）";
        var dialog = new KeyCaptureDialog(action.Name, currentLabel, overwritesExisting: action.Outputs.Count > 0) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } token)
        {
            if (TryMutateDocument(document => SetOutputsWithAutoName(document, actionId, WorkspaceEditorProjection.ParseOutputs(token))))
            {
                ArmAssignByPress();
                Render();
            }
        }
    }

    /// <summary>
    /// 録画確定後、選択中の操作にまだ割当が無ければ「実機のボタンを押して割当先を決める」待機に入る。
    /// 常駐同居時だけ機能する（fast path の trace を割当指定に使うため）。
    /// </summary>
    private void ArmAssignByPress()
    {
        if (_residentApply is null || _selectedActionId is null)
        {
            return;
        }

        var actionId = _selectedActionId;
        if (_document.Bindings.Any(binding => binding.ActionId == actionId))
        {
            return;
        }

        _pendingAssign = true;
    }

    /// <summary>実機ボタン押下で割当先を確定する（割当待機中のみ。層切替キーは対象外として待機を続ける）。</summary>
    private void AssignByPress(string deviceKind, string controlId)
    {
        if (_selectedActionId is null || deviceKind is not ("G13" or "G600"))
        {
            return;
        }

        var layout = _document.Devices.FirstOrDefault(device => device.DeviceKind == deviceKind);
        if (layout is not null &&
            layout.LatchSelectors.Concat(layout.HoldSelectors).Any(selector => selector.ControlId == controlId))
        {
            _assignHint.Text = $"{controlId} は層切替キーなので割り当てられません。別のボタンを押してください";
            return;
        }

        var actionId = _selectedActionId;
        var layerId = CurrentLayerFor(deviceKind);
        _pendingAssign = false;
        if (TryMutateDocument(document => WorkspaceDocumentEditor.SetBinding(document, actionId, deviceKind, controlId, layerId)))
        {
            Render();
        }
    }

    private string CurrentLayerFor(string deviceKind)
    {
        var layout = _document.Devices.FirstOrDefault(device => device.DeviceKind == deviceKind);
        var fallback = layout?.DefaultLayerId ?? "base";
        if (!_figureLayerByDevice.TryGetValue(deviceKind, out var layerId))
        {
            return fallback;
        }

        return layout is not null && layout.LayerIds.Contains(layerId) ? layerId : fallback;
    }

    /// <summary>
    /// outputs を差し替え、名前がまだ自動生成の既定名なら割り当てたキーの表示名へ改名する
    /// （オーナー要望 2026-08-22: 名前を付けずに保存した操作が「新しい操作」のまま残らないように）。
    /// </summary>
    private static WorkspaceDocument SetOutputsWithAutoName(WorkspaceDocument document, string actionId, IReadOnlyList<string> outputs)
    {
        var updated = WorkspaceDocumentEditor.SetActionOutputs(document, actionId, outputs);
        var action = updated.Actions.FirstOrDefault(candidate => candidate.ActionId == actionId);
        if (action is not null && outputs.Count > 0 && WorkspaceEditorProjection.IsDefaultActionName(action.Name))
        {
            updated = WorkspaceDocumentEditor.RenameAction(updated, actionId, WorkspaceEditorProjection.OutputsDisplayName(outputs));
        }

        return updated;
    }

    private void OpenDiagnostics()
    {
        if (_diagnosticsWindow is null || !_diagnosticsWindow.IsVisible)
        {
            _diagnosticsWindow = new DiagnosticsWindow(_report) { Owner = this };
            _diagnosticsWindow.Show();
        }
        else
        {
            _diagnosticsWindow.Activate();
        }
    }

    private void WireEvents()
    {
        _appPillButton.Click += (_, _) =>
        {
            _appPickerPopup.PlacementTarget = _appPillButton;
            _appPickerPopup.IsOpen = true;
        };
        _appPickerList.SelectionChanged += OnAppPickerSelectionChanged;
        _appPickerPopup.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _appPickerPopup.IsOpen = false;
                e.Handled = true;
            }
        };

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
        _deleteActionButton.Click += (_, _) =>
        {
            if (_selectedActionId is null)
            {
                return;
            }

            var actionId = _selectedActionId;
            _selectedActionId = null;
            if (TryMutateDocument(document => WorkspaceDocumentEditor.DeleteAction(document, actionId)))
            {
                Render();
            }
        };
        _inspectorNameBox.LostFocus += (_, _) => OnInspectorNameCommitted();
        _inspectorNameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { OnInspectorNameCommitted(); e.Handled = true; } };


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

        var liveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        liveRow.Children.Add(new TextBlock { Text = "いまゲームに届いている割当: ", Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center });
        liveRow.Children.Add(_liveAssignmentValueText);
        left.Children.Add(liveRow);
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
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(_inspectorTitleButton);
        Grid.SetColumn(_deleteActionButton, 1);
        titleRow.Children.Add(_deleteActionButton);
        stack.Children.Add(titleRow);
        stack.Children.Add(_inspectorNameBox);

        stack.Children.Add(_inspectorEmptyPanel);

        var recordRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 12) };
        AutomationProperties.SetName(_recordAddButton, "キーを録って新しい操作を追加する");
        _recordAddButton.ToolTip = "キーを録画して、新しい操作として追加します";
        _recordAddButton.Click += (_, _) => RecordNewAction();
        recordRow.Children.Add(_recordAddButton);
        AutomationProperties.SetName(_recordUpdateButton, "選んだ操作のキーを録り直す");
        _recordUpdateButton.ToolTip = "選んでいる操作のキーを、新しく録ったキーで上書きします";
        _recordUpdateButton.Click += (_, _) => RecordUpdateSelected();
        recordRow.Children.Add(_recordUpdateButton);
        stack.Children.Add(recordRow);
        stack.Children.Add(_assignHint);

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
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "動作チェック", FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 12, 0) });
        _testFieldHint.Text = "デバイスのボタンを押すと、ここに結果が流れます";
        _testFieldHint.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(_testFieldHint);
        grid.Children.Add(row);

        var diagnosticsButton = new Button
        {
            Content = "診断",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Theme.Muted,
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AutomationProperties.SetName(diagnosticsButton, "診断画面を開く");
        diagnosticsButton.Click += (_, _) => OpenDiagnostics();
        Grid.SetColumn(diagnosticsButton, 1);
        grid.Children.Add(diagnosticsButton);

        bar.Child = grid;
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
        _pendingAssign = false;
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

    private void OnFigureKeyClicked(string deviceKind, string controlId)
    {
        _pendingAssign = false;
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
            var outcome = _intents.Save(_document, _selectedApplicationFullPath);
            _residentApply?.ApplyIfResident(_document);
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
        _appPillLabel.ToolTip = view.Chrome.EditingLabel;
        _liveAssignmentValueText.Text = view.Chrome.LiveAssignmentLabel;
        _liveAssignmentValueText.ToolTip = view.Chrome.LiveAssignmentLabel;

        _appPickerList.Items.Clear();
        foreach (var row in view.RailRows)
        {
            var item = new ListBoxItem
            {
                Content = row.IsRunning ? $"{row.DisplayName}（実行中）" : row.DisplayName,
                Tag = row.ApplicationFullPath,
                IsSelected = row.IsSelected,
                Padding = new Thickness(10, 6, 10, 6),
                // 非アクティブ選択時に OS 既定の黒へ落ちないよう明示する（操作一覧と同じ欠陥）。
                Foreground = Theme.Text,
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
        // 無効時の見た目は共通 flat style の不透明度 trigger が担う（Opacity の手動設定は trigger を
        // 打ち消すためここでは触らない）。
    }

    private void RenderActionList(ActionBoardView boardView)
    {
        var actionIndexById = _document.Actions
            .Select((action, index) => (action.ActionId, index))
            .ToDictionary(pair => pair.ActionId, pair => pair.index, StringComparer.Ordinal);

        // Items の Clear/Add は ListBox の SelectionChanged を都度発火させる。ここで Render() へ
        // 再入すると（Clear 直後の SelectedItem=null による偽の選択変更など）無限再帰になるため、
        // 再構築中はハンドラを外す（選択状態そのものは IsSelected で個別に張り直すので機能は変わらない）。
        _actionList.SelectionChanged -= OnActionListSelectionChanged;
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
            // Foreground を明示しないと、一覧がフォーカスを失った時に選択行が
            // OS 既定の非アクティブ選択色（黒）へ落ちて暗背景で読めなくなる。
            labels.Children.Add(new TextBlock { Text = row.Name, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = Theme.Text });
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

        _actionList.SelectionChanged += OnActionListSelectionChanged;
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
            if (!layout.LayerIds.Contains(currentLayerId))
            {
                // G-Shift をボタン化した直後など、見ていた配置が layout から消えた場合は既定へ戻す。
                currentLayerId = layout.DefaultLayerId;
                _figureLayerByDevice[_selectedFigureDeviceKind] = currentLayerId;
            }

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

            var shiftIsButton = !isG13 && layout.HoldSelectors.Count == 0;
            if (!isG13)
            {
                var toggle = new Border
                {
                    BorderBrush = Theme.Line,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(12, 0, 0, 0),
                    Background = Brushes.Transparent,
                    Child = new TextBlock
                    {
                        Text = shiftIsButton ? "G-Shift を層切替に戻す" : "G-Shift をボタンにする",
                        Foreground = Theme.Muted,
                        FontSize = 12,
                    },
                    Cursor = Cursors.Hand,
                    ToolTip = shiftIsButton
                        ? "G6 を層切替に戻します（G6 の割当が残っている間は戻せません）"
                        : "G6 を通常のボタンとして割当可能にします（「G-Shift を押している間」の配置は無くなります。割当が残っている間は変えられません）",
                };
                toggle.MouseLeftButtonUp += (_, _) =>
                {
                    if (TryMutateDocument(shiftIsButton
                            ? WorkspaceDocumentEditor.SetG600ShiftAsSelector
                            : WorkspaceDocumentEditor.SetG600ShiftAsButton))
                    {
                        Render();
                    }
                };
                _layerChipRow.Children.Add(toggle);
            }

            var bindings = BuildFigureBindingLookup(_selectedFigureDeviceKind, currentLayerId);
            _figureHost.Child = isG13
                ? InputStudioFigures.BuildG13(bindings, controlId => OnFigureKeyClicked("G13", controlId))
                : InputStudioFigures.BuildG600(bindings, controlId => OnFigureKeyClicked("G600", controlId), shiftIsButton);
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

        _deleteActionButton.Visibility = inspector is null ? Visibility.Collapsed : Visibility.Visible;
        _recordUpdateButton.IsEnabled = inspector is not null;
        if (_pendingAssign && inspector is not null)
        {
            if (_assignHint.Text.Length == 0 || !_assignHint.Text.Contains("層切替"))
            {
                _assignHint.Text = $"デバイスのボタンを押すと『{inspector.Name}』をそのボタンへ割り当てます（絵のボタンをクリックでも可）";
            }

            _assignHint.Visibility = Visibility.Visible;
        }
        else
        {
            _pendingAssign = false;
            _assignHint.Text = string.Empty;
            _assignHint.Visibility = Visibility.Collapsed;
        }

        if (inspector is null)
        {
            _inspectorTitleButton.Visibility = Visibility.Collapsed;
            _inspectorNameBox.Visibility = Visibility.Collapsed;
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

        // 片方の device だけで使う操作は普通のため、警告は「どのボタンにも割り当てていない時だけ」1回にする
        // （device ごとに出すと割当済みでも片側の警告が残り続ける——実利用の指摘 2026-08-22）。
        if (inspector.Bindings.Count == 0)
        {
            _actionNotesPanel.Children.Add(NoteBlock(
                $"『{inspector.Name}』はまだどのボタンにも割り当てられていません（未割当でも保存できます）", Theme.Warn));
        }

        foreach (var deviceOptions in inspector.DeviceOptions)
        {
            var panel = deviceOptions.DeviceKind == "G13" ? _g13BindingsPanel : _g600BindingsPanel;
            var deviceBindings = inspector.Bindings.Where(binding => binding.DeviceKind == deviceOptions.DeviceKind).ToArray();

            if (deviceBindings.Length == 0)
            {
                panel.Children.Add(new TextBlock { Text = "（未割当）", Foreground = Theme.Muted, Margin = new Thickness(0, 4, 0, 4) });
            }

            foreach (var binding in deviceBindings)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var slot = new StackPanel();
                slot.Children.Add(new TextBlock { Text = ControlLabel(deviceOptions.DeviceKind, binding.ControlId), FontWeight = FontWeights.Bold, FontSize = 13 });
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

            // 割当先の指定は「実機のボタンを押す」または絵のボタンをクリック（pulldown は撤去・オーナー指示 2026-08-22）。
        }
    }

    private static TextBlock NoteBlock(string text, Brush accent) => new()
    {
        Text = text,
        Foreground = accent,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
    };

    /// <summary>control ID の表示名。絵と同じ場所を指せるよう、G番号に物理位置の呼び名を併記する。
    /// 呼び名を持たない control は G番号のまま表示する（fallback を隠さない）。</summary>
    private static string ControlLabel(string deviceKind, string controlId)
    {
        var physicalName = (deviceKind, controlId) switch
        {
            ("G600", "G1") => "左クリック",
            ("G600", "G2") => "右クリック",
            ("G600", "G3") => "ホイール押込み",
            ("G600", "G4") => "左チルト",
            ("G600", "G5") => "右チルト",
            ("G600", "G6") => "G-Shift",
            ("G600", "G7") => "上面ボタン上",
            ("G600", "G8") => "上面ボタン下",
            _ => null,
        };
        if (deviceKind == "G13" && controlId == "STICK_PRESS")
        {
            return "スティック押込み";
        }

        return physicalName is null ? controlId : $"{controlId}（{physicalName}）";
    }

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
