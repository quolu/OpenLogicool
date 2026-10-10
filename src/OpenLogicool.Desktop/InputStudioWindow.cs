using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Desktop;

/// <summary>
/// Input Studio メイン画面（オーナー承認済みの見本 docs/ui-mocks/v2/input-studio.html 準拠）。
/// 上の帯（設定中のアプリ・いまゲームに届いている割当）＋左＝操作一覧・中央＝G13/G600 の図・
/// 右＝割当パネル（唯一の editor・保存）＋下の帯＝動作チェック。
/// Desktop は I/O を持たないため、document の読み込み・compile・保存・破棄はすべて
/// <see cref="IWorkspaceEditorIntents"/>（実装は Host）を通す。device 台帳（<see cref="DeviceLedgerView"/>）は
/// 診断画面が持つ。
/// </summary>
public sealed class InputStudioWindow : Window
{
    private readonly IWorkspaceEditorIntents _intents;
    private readonly InputStudioReport _report;
    private readonly IResidentApplyIntent? _residentApply;
    private readonly ISerialHidSettingsIntent? _serialHidSettingsIntent;
    private readonly IG13LcdSettingsIntent? _g13LcdSettingsIntent;
    private readonly IWebResearchIntent? _webResearchIntent;
    private readonly IDemonstrationRecordingIntents? _demonstrationRecordingIntents;
    private readonly IExplorerIntents? _explorerIntents;
    private readonly ILearningRouteIntents? _learningRouteIntents;
    private readonly ISupervisedMacroIntents? _supervisedMacroIntents;
    private readonly IMacroAutomationIntents? _macroAutomationIntents;
    private readonly IBotScriptIntents? _botScriptIntents;
    private readonly WindowPlacementMemory? _windowPlacementMemory;
    private readonly string? _supervisedUnavailableReason;
    // 窓の最小幅を広げたため、以前の幅で覚えた配置を引き継がない名前にする。
    private const string GameOperatorPlacementKey = "game-operator-rail";
    private DiagnosticsWindow? _diagnosticsWindow;
    private GameOperatorWindow? _gameOperatorWindow;
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
    private readonly TextBlock _appPillIcon = new()
    {
        FontFamily = Theme.Display,
        FontWeight = FontWeights.Bold,
        FontSize = 12,
        Foreground = Theme.G13,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _appPillLabel = new()
    {
        FontWeight = FontWeights.Bold,
        Foreground = Theme.Text,
        FontSize = 13.5,
        MaxWidth = 270,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly ListBox _appPickerList = new() { BorderThickness = new Thickness(0), Width = 300, MaxHeight = 360 };
    private readonly Popup _appPickerPopup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true, VerticalOffset = 6 };
    private readonly TextBlock _liveAssignmentValueText = new()
    {
        Foreground = Theme.Text,
        FontWeight = FontWeights.Bold,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 260,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Button _outputSettingsButton = Theme.Quiet(new Button
    {
        Content = "出力方式",
        Height = 32,
        Padding = new Thickness(12, 0, 12, 0),
        Margin = new Thickness(8, 0, 0, 0),
    });
    private readonly TextBlock _outputStatusText = new()
    {
        Foreground = Theme.Muted,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 4, 0),
        MaxWidth = 210,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    // G600 本体書き込み（方式A）: 合成入力を受け付けないゲームでも割当を効かせる。入口は G600 の図の右上。
    private readonly IG600OnboardIntent? _onboardIntent;
    private readonly Button _onboardMenuButton = Theme.Quiet(new Button
    {
        Content = "G600本体 ▾",
        Height = 30,
        Padding = new Thickness(12, 0, 12, 0),
        Margin = new Thickness(10, 0, 0, 0),
    });
    private readonly Popup _onboardPopup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true, VerticalOffset = 6 };
    private readonly Button _onboardWriteButton = new()
    {
        Content = "G600本体に書き込む",
        Height = 32,
        Padding = new Thickness(12, 0, 12, 0),
        ToolTip = "この設定の G600 割当を G600 本体のメモリに書き込みます。ハードウェアのキー入力として"
            + "送信されるため、割当が効かないゲームでも動作します（G600 のみ・書き込み前の状態は記録されいつでも解除できます）。",
    };
    private readonly Button _onboardRestoreButton = Theme.Quiet(new Button
    {
        Content = "本体の書き込みを解除",
        Height = 32,
        Padding = new Thickness(12, 0, 12, 0),
        Margin = new Thickness(0, 6, 0, 0),
    });
    private readonly TextBlock _onboardStatusText = new()
    {
        Foreground = Theme.Muted,
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 280,
        Margin = new Thickness(0, 0, 0, 8),
    };
    private bool _onboardBusy;

    // 左ペイン: 操作一覧
    private readonly ListBox _actionList = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly Button _recordAddButton = new();

    // 中央ペイン: デバイスの切替・配置の切替・図
    private readonly Button _g13TabButton = new();
    private readonly Button _g600TabButton = new();
    private readonly StackPanel _layerChipRow = new() { Orientation = Orientation.Horizontal };
    private readonly Button _shiftSwitchButton = new();
    private readonly Button _lcdMenuButton = Theme.Quiet(new Button
    {
        Content = "LCDと明かり ▾",
        Height = 30,
        Padding = new Thickness(12, 0, 12, 0),
        Margin = new Thickness(10, 0, 0, 0),
    });
    private readonly Popup _lcdPopup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true, VerticalOffset = 6 };
    private readonly Border _g13LcdSettingsPanel = new()
    {
        Background = Theme.Raised,
        BorderBrush = Theme.Line2,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(14, 12, 14, 12),
        Width = 500,
    };
    private readonly TextBlock _g13LcdSummary = new() { Foreground = Theme.Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _g13LcdTextBox = new() { MaxLength = 120, MinWidth = 150 };
    private readonly Button _g13LcdImageButton = new() { Content = "画像を選ぶ", Height = 32, Padding = new Thickness(10, 0, 10, 0) };
    private readonly Button _g13LcdTextButton = new() { Content = "テキストを表示", Height = 32, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0) };
    private readonly Button _g13LcdClearButton = Theme.Quiet(new Button { Content = "共通表示に戻す", Height = 32, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0) });
    private readonly Button _g13BacklightAudioButton = new()
    {
        Background = Brushes.Transparent,
        BorderBrush = Brushes.Transparent,
        Padding = new Thickness(0, 4, 6, 4),
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly Border _figureHost = new() { Margin = new Thickness(18, 10, 18, 4) };
    private readonly TextBlock _figureNoteText = new()
    {
        Foreground = Theme.Muted,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(18, 0, 18, 10),
    };
    private InputStudioFigures.FigureView? _figureView;

    // 右ペイン: 割当パネル（唯一の editor）
    private readonly Button _inspectorTitleButton = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Background = Brushes.Transparent,
        BorderBrush = Brushes.Transparent,
        Padding = new Thickness(0, 2, 0, 2),
        ToolTip = "クリックで名前を変えます",
    };
    // 操作の削除入口。Delete キーだけでは見つけられない（実利用の指摘 2026-08-22）ため画面に置く。
    private readonly Button _deleteActionButton = Theme.DangerQuiet(new Button { Content = "この操作を削除", Height = 32 });
    private readonly TextBox _inspectorNameBox = new() { FontSize = 15 };
    private readonly StackPanel _sendKeyRow = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _recordUpdateButton = new()
    {
        Content = "キーを録る",
        Height = 32,
        Padding = new Thickness(12, 0, 12, 0),
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Right,
    };
    private readonly Button _selectMacroButton = Theme.Quiet(new Button { Content = "保存済みマクロを選ぶ", Height = 32 });
    private readonly Border _selectMacroHostInCard = new();
    private readonly Border _selectMacroHostWhenEmpty = new() { Margin = new Thickness(0, 10, 0, 0) };
    // 実機ボタン押しで割当先を指定する待機状態（録画確定後に自動で入る。常駐同居時のみ機能）。
    private bool _pendingAssign;
    private KeyCaptureDialog? _activeKeyCaptureDialog;
    private PhysicalAssignmentTarget? _capturedPhysicalAssignment;
    private readonly TextBlock _assignHint = new()
    {
        Foreground = Theme.Warn,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
    };
    private readonly StackPanel _conflictNotePanel = new();
    private readonly StackPanel _inspectorBody = new();
    private readonly StackPanel _g13BindingsPanel = new();
    private readonly StackPanel _g600BindingsPanel = new();
    private readonly StackPanel _actionNotesPanel = new();
    private readonly StackPanel _inspectorEmptyPanel = new();

    // 保存の状態と操作
    private readonly Border _saveDot = new() { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _saveChipText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _saveButton = Theme.Primary(new Button { Content = "保存", Height = 38 });
    private readonly Button _revertButton = Theme.Quiet(new Button { Content = "元に戻す", Height = 38 });
    private bool _saveGlowBreathing;

    // 下部: 動作チェック（実機の押下が左から流れる）
    private readonly StackPanel _tickerPanel = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _testFieldHint = new() { Foreground = Theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private int _traceTick;

    // 短い知らせ（割当・保存の結果）
    private readonly TextBlock _toastText = new() { Foreground = Theme.KeycapInk, FontWeight = FontWeights.Bold, FontSize = 12.5 };
    private readonly Border _toast = new()
    {
        Background = Theme.Freeze(Color.FromRgb(0xf2, 0xf5, 0xfb)),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16, 9, 16, 9),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 62),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };
    private DispatcherTimer? _toastTimer;

    public InputStudioWindow(
        WorkspaceScreenSnapshot snapshot,
        InputStudioReport ledgerReport,
        string initialSelectedApplicationFullPath,
        IWorkspaceEditorIntents intents,
        IResidentApplyIntent? residentApply = null,
        IG600OnboardIntent? onboardIntent = null,
        ISerialHidSettingsIntent? serialHidSettingsIntent = null,
        IG13LcdSettingsIntent? g13LcdSettingsIntent = null,
        IWebResearchIntent? webResearchIntent = null,
        IExplorerIntents? explorerIntents = null,
        ILearningRouteIntents? learningRouteIntents = null,
        ISupervisedMacroIntents? supervisedMacroIntents = null,
        string? supervisedUnavailableReason = null,
        IMacroAutomationIntents? macroAutomationIntents = null,
        IDemonstrationRecordingIntents? demonstrationRecordingIntents = null,
        IBotScriptIntents? botScriptIntents = null,
        WindowPlacementMemory? windowPlacementMemory = null)
    {
        _report = ledgerReport; // 旧 device 台帳は撤去済み。診断画面（DiagnosticsWindow）の中身として復活させる。
        _intents = intents;
        _residentApply = residentApply;
        _onboardIntent = onboardIntent;
        _serialHidSettingsIntent = serialHidSettingsIntent;
        _g13LcdSettingsIntent = g13LcdSettingsIntent;
        _webResearchIntent = webResearchIntent;
        _demonstrationRecordingIntents = demonstrationRecordingIntents;
        _explorerIntents = explorerIntents;
        _learningRouteIntents = learningRouteIntents;
        _supervisedMacroIntents = supervisedMacroIntents;
        _macroAutomationIntents = macroAutomationIntents;
        _botScriptIntents = botScriptIntents;
        _windowPlacementMemory = windowPlacementMemory;
        _supervisedUnavailableReason = supervisedUnavailableReason;
        _snapshot = snapshot;
        _selectedApplicationFullPath = initialSelectedApplicationFullPath;

        Title = "OpenLogicool Input Studio";
        Theme.Apply(this);
        // この窓を親にしない窓（MessageBox 以外の小窓）でも、OS 既定 Button の明色塗り直しを起こさない。
        if (Application.Current is { } application && !application.Resources.Contains(typeof(Button)))
        {
            application.Resources[typeof(Button)] = Theme.CreateFlatButtonStyle();
        }
        MinWidth = 1100;
        MinHeight = 720;
        Width = 1360;
        Height = 840;
        _windowPlacementMemory?.Attach(this, "input-studio");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(296) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(316) });

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

        _toast.Child = _toastText;
        Grid.SetRowSpan(_toast, 3);
        root.Children.Add(_toast);

        Content = root;

        WireEvents();
        LoadSelectedWorkspace();
        Render();
        RefreshOnboardState();
        RefreshOutputStatus();

        if (_residentApply is not null)
        {
            // 実機の押下を図へすぐ返すため短い間隔で読む（前面 window の追従は PollResidentTrace 側で間引く）。
            _traceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _traceTimer.Tick += (_, _) => PollResidentTrace();
            _traceTimer.Start();
        }

        if (_macroAutomationIntents is not null)
            _macroAutomationIntents.StateChanged += OnMacroStateChanged;
        Closed += (_, _) =>
        {
            if (_macroAutomationIntents is not null)
                _macroAutomationIntents.StateChanged -= OnMacroStateChanged;
            _traceTimer?.Stop();
            _toastTimer?.Stop();
            _diagnosticsWindow?.Close();
            _gameOperatorWindow?.Close();
        };
    }

    private void PollResidentTrace()
    {
        var events = _residentApply!.DrainTraceEvents();
        foreach (var traceEvent in events)
        {
            if (!traceEvent.IsDown)
            {
                continue;
            }

            // 実機で押したボタンと同じ場所を図で光らせ、下の帯へ流す。
            if (traceEvent.DeviceKind == _selectedFigureDeviceKind)
            {
                _figureView?.Pulse(traceEvent.ControlId);
            }

            AddTickerEntry(traceEvent);

            if (_activeKeyCaptureDialog is not null)
            {
                if (!TryResolvePhysicalAssignment(
                        traceEvent.DeviceKind,
                        traceEvent.ControlId,
                        out var target,
                        out var rejectionMessage))
                {
                    _activeKeyCaptureDialog.ShowDeviceHint(rejectionMessage);
                    continue;
                }

                if (_activeKeyCaptureDialog.TryCommitFromDevicePress(traceEvent.InputMonotonicMs))
                {
                    _capturedPhysicalAssignment = target;
                }
            }
            else if (_pendingAssign)
            {
                AssignByPress(traceEvent.DeviceKind, traceEvent.ControlId);
            }
        }

        // 前面 window の追従は 300ms ごとで足りる（60ms ごとに OS へ問い合わせない）。
        _traceTick++;
        if (_traceTick % 5 != 0)
        {
            return;
        }

        // 「いまゲームに届いている割当」を前面 window に追従させる（配送の切替本体は常駐 fast path が
        // 行っており、ここは表示だけを最新化する。文言は WorkspaceScreenProjection と同じ規則）。
        // null は「取得不能または自画面が前面」= 直前のゲーム名を保持する
        var foregroundTitle = _residentApply!.CurrentForegroundWindowTitle();
        if (foregroundTitle is not null && foregroundTitle != _snapshot.ForegroundWindowTitle)
        {
            _snapshot = _snapshot with { ForegroundWindowTitle = foregroundTitle };
            var label = $"{foregroundTitle} 用";
            _liveAssignmentValueText.Text = label;
            _liveAssignmentValueText.ToolTip = label;
        }
    }

    /// <summary>動作チェックの帯へ1件流す（押したボタン → 操作 → 送ったキー）。新しいものが左。</summary>
    private void AddTickerEntry(ResidentTraceEvent traceEvent)
    {
        _testFieldHint.Visibility = Visibility.Collapsed;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = traceEvent.DeviceKind,
            FontFamily = Theme.Display,
            FontWeight = FontWeights.SemiBold,
            FontSize = 10.5,
            Foreground = traceEvent.DeviceKind == "G13" ? Theme.G13 : Theme.G600,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });
        row.Children.Add(new TextBlock { Text = ControlLabel(traceEvent.DeviceKind, traceEvent.ControlId), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

        var tokens = traceEvent.OutputTokens ?? [];
        if (tokens.Count == 0)
        {
            row.Children.Add(new TextBlock { Text = "割当なし", Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            var actionIndex = _document.Actions.ToList().FindIndex(action =>
                action.Outputs.SequenceEqual(tokens, StringComparer.Ordinal)
                && _document.Bindings.Any(binding =>
                    binding.ActionId == action.ActionId
                    && binding.DeviceKind == traceEvent.DeviceKind
                    && binding.ControlId == traceEvent.ControlId));
            if (actionIndex >= 0)
            {
                row.Children.Add(TickerArrow());
                row.Children.Add(new TextBlock
                {
                    Text = _document.Actions[actionIndex].Name,
                    Foreground = Theme.ActionColorAt(actionIndex),
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            row.Children.Add(TickerArrow());
            row.Children.Add(new Border
            {
                Background = Theme.Raised,
                BorderBrush = Theme.Line2,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = KeyLegend(tokens), FontFamily = Theme.Mono, FontWeight = FontWeights.Bold, FontSize = 11 },
            });
        }

        var slide = new TranslateTransform(-14, 0);
        var chip = new Border
        {
            Height = 28,
            CornerRadius = new CornerRadius(14),
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 8, 0),
            Child = row,
            RenderTransform = slide,
        };
        _tickerPanel.Children.Insert(0, chip);
        while (_tickerPanel.Children.Count > 6)
        {
            _tickerPanel.Children.RemoveAt(_tickerPanel.Children.Count - 1);
        }

        var duration = TimeSpan.FromMilliseconds(250);
        slide.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(-14, 0, duration));
        chip.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration));
    }

    private static TextBlock TickerArrow() => new()
    {
        Text = "→",
        Foreground = Theme.Faint,
        FontSize = 12,
        Margin = new Thickness(7, 0, 7, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>送るキーをキーキャップへ書く短い文字（「Key:W」→「W」、同時押しは「+」でつなぐ）。</summary>
    private static string KeyLegend(IReadOnlyList<string> outputs)
    {
        if (outputs.Count == 0)
        {
            return "—";
        }

        if (outputs.Count == 1 && MacroInvocationTokens.IsMacro(outputs[0]))
        {
            return "マクロ";
        }

        return string.Join("+", outputs.Select(output => WorkspaceEditorProjection.OutputsDisplayName([output])));
    }

    /// <summary>画面の下へ短い知らせを出す（数秒で消える）。</summary>
    private void ShowToast(string message)
    {
        _toastText.Text = message;
        _toast.Visibility = Visibility.Visible;
        _toastTimer ??= CreateToastTimer();
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private DispatcherTimer CreateToastTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _toast.Visibility = Visibility.Collapsed;
        };
        return timer;
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

        var dialog = new KeyCaptureDialog(
            newActionName,
            "（未設定）",
            overwritesExisting: false,
            canAssignByDevicePress: _residentApply is not null) { Owner = this };
        _capturedPhysicalAssignment = null;
        if (ShowKeyCaptureDialog(dialog) && dialog.Result is { } token)
        {
            var assignment = _capturedPhysicalAssignment;
            if (TryMutateDocument(document =>
                {
                    var updated = SetOutputsWithAutoName(document, newActionId, WorkspaceEditorProjection.ParseOutputs(token));
                    return assignment is { } target
                        ? WorkspaceDocumentEditor.SetBinding(
                            updated,
                            newActionId,
                            target.DeviceKind,
                            target.ControlId,
                            target.LayerId)
                        : updated;
                }))
            {
                if (assignment is null)
                {
                    ArmAssignByPress();
                }
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

        var currentLabel = action.Outputs.Count > 0 ? WorkspaceEditorProjection.OutputsDisplayName(action.Outputs) : "（未設定）";
        var dialog = new KeyCaptureDialog(
            action.Name,
            currentLabel,
            overwritesExisting: action.Outputs.Count > 0,
            canAssignByDevicePress: _residentApply is not null) { Owner = this };
        _capturedPhysicalAssignment = null;
        if (ShowKeyCaptureDialog(dialog) && dialog.Result is { } token)
        {
            var assignment = _capturedPhysicalAssignment;
            if (TryMutateDocument(document =>
                {
                    var updated = SetOutputsWithAutoName(document, actionId, WorkspaceEditorProjection.ParseOutputs(token));
                    return assignment is { } target
                        ? WorkspaceDocumentEditor.SetBinding(
                            updated,
                            actionId,
                            target.DeviceKind,
                            target.ControlId,
                            target.LayerId)
                        : updated;
                }))
            {
                if (assignment is null)
                {
                    ArmAssignByPress();
                }
                Render();
            }
        }
    }

    private void SelectMacroForCurrentAction()
    {
        if (_macroAutomationIntents is null)
        {
            return;
        }

        var dialog = new MacroAssignmentDialog(_macroAutomationIntents.ListMacros()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultToken is null || dialog.SelectedMacro is null)
        {
            return;
        }

        var macro = dialog.SelectedMacro;
        if (_selectedActionId is null)
        {
            var newActionId = GenerateActionId("macro");
            if (TryMutateDocument(document => WorkspaceDocumentEditor.AddAction(
                document, newActionId, macro.Goal, [dialog.ResultToken])))
            {
                _selectedActionId = newActionId;
                ArmAssignByPress();
                Render();
            }
            return;
        }

        var actionId = _selectedActionId;
        if (TryMutateDocument(document =>
            {
                var updated = WorkspaceDocumentEditor.SetActionOutputs(document, actionId, [dialog.ResultToken]);
                var action = updated.Actions.First(candidate => candidate.ActionId == actionId);
                return WorkspaceEditorProjection.IsDefaultActionName(action.Name)
                    ? WorkspaceDocumentEditor.RenameAction(updated, actionId, macro.Goal)
                    : updated;
            }))
        {
            Render();
        }
    }

    private bool ShowKeyCaptureDialog(KeyCaptureDialog dialog)
    {
        _activeKeyCaptureDialog = dialog;
        try
        {
            return dialog.ShowDialog() == true;
        }
        finally
        {
            _activeKeyCaptureDialog = null;
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
        if (_selectedActionId is null)
        {
            return;
        }

        if (!TryResolvePhysicalAssignment(deviceKind, controlId, out var target, out var rejectionMessage))
        {
            _assignHint.Text = rejectionMessage;
            return;
        }

        var actionId = _selectedActionId;
        _pendingAssign = false;
        if (TryMutateDocument(document => WorkspaceDocumentEditor.SetBinding(
                document,
                actionId,
                target.DeviceKind,
                target.ControlId,
                target.LayerId)))
        {
            Render();
        }
    }

    private bool TryResolvePhysicalAssignment(
        string deviceKind,
        string controlId,
        out PhysicalAssignmentTarget target,
        out string rejectionMessage)
    {
        target = default;
        if (deviceKind is not ("G13" or "G600"))
        {
            rejectionMessage = $"未対応のデバイス '{deviceKind}' からの入力は割り当てられません。";
            return false;
        }

        // マウスの左右クリックはこの画面の操作にも使われるため、押下では割り当てない。
        if (deviceKind == "G600" && controlId is "G1" or "G2")
        {
            rejectionMessage = "左/右クリックは画面操作と区別できないため、絵の G1/G2 をクリックして割り当ててください。";
            return false;
        }

        var layout = _document.Devices.FirstOrDefault(device => device.DeviceKind == deviceKind);
        if (layout is not null &&
            layout.LatchSelectors.Concat(layout.HoldSelectors).Any(selector => selector.ControlId == controlId))
        {
            rejectionMessage = $"{controlId} は層切替キーなので割り当てられません。別のボタンを押してください。";
            return false;
        }

        target = new PhysicalAssignmentTarget(deviceKind, controlId, CurrentLayerFor(deviceKind));
        rejectionMessage = string.Empty;
        return true;
    }

    private readonly record struct PhysicalAssignmentTarget(string DeviceKind, string ControlId, string LayerId);

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
            _windowPlacementMemory?.Attach(_diagnosticsWindow, "diagnostics");
            _diagnosticsWindow.Show();
        }
        else
        {
            _diagnosticsWindow.Activate();
        }
    }

    private void OpenGameOperator(bool openMacroTab = false)
    {
        if (_webResearchIntent is null)
        {
            return;
        }

        if (_gameOperatorWindow is null || !_gameOperatorWindow.IsVisible)
        {
            _gameOperatorWindow = new GameOperatorWindow(
                _webResearchIntent,
                _explorerIntents,
                _learningRouteIntents,
                _supervisedMacroIntents,
                _supervisedUnavailableReason,
                _macroAutomationIntents,
                openMacroTab,
                _demonstrationRecordingIntents,
                _botScriptIntents) { Owner = this };
            _windowPlacementMemory?.Attach(_gameOperatorWindow, GameOperatorPlacementKey);
            _gameOperatorWindow.Show();
        }
        else
        {
            if (openMacroTab) _gameOperatorWindow.SelectMacroTab();
            _gameOperatorWindow.Activate();
        }
    }

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    public Window ControlPanel(string panel, bool activate)
    {
        if (panel == "input-studio")
        {
            if (activate) { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Show(); Activate(); }
            return this;
        }
        if (_gameOperatorWindow is null || !_gameOperatorWindow.IsVisible)
        {
            if (_webResearchIntent is null) throw new InvalidOperationException("Game Operatorは利用できません。");
            _gameOperatorWindow = new GameOperatorWindow(_webResearchIntent, _explorerIntents, _learningRouteIntents,
                _supervisedMacroIntents, _supervisedUnavailableReason, _macroAutomationIntents, false,
                _demonstrationRecordingIntents, _botScriptIntents) { Owner = this, ShowActivated = activate };
            _windowPlacementMemory?.Attach(_gameOperatorWindow, GameOperatorPlacementKey);
            _gameOperatorWindow.Show();
        }
        _gameOperatorWindow.SelectControlPanel(panel);
        if (activate) _gameOperatorWindow.Activate();
        return _gameOperatorWindow;
    }

    private void OnMacroStateChanged(MacroRunSnapshot snapshot)
    {
        if (snapshot.Phase != MacroRunPhase.AwaitingConfirmation) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_macroAutomationIntents?.CurrentRun()?.PendingConfirmation?.ConfirmationId
                != snapshot.PendingConfirmation?.ConfirmationId) return;
            if (_gameOperatorWindow is null || !_gameOperatorWindow.IsVisible)
                OpenGameOperator(openMacroTab: true);
            else
            {
                if (_gameOperatorWindow.WindowState == WindowState.Minimized)
                    _gameOperatorWindow.WindowState = WindowState.Normal;
                _gameOperatorWindow.SelectMacroTab();
            }
        });
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
        _lcdMenuButton.Click += (_, _) =>
        {
            _lcdPopup.PlacementTarget = _lcdMenuButton;
            _lcdPopup.IsOpen = true;
        };
        _onboardMenuButton.Click += (_, _) =>
        {
            _onboardPopup.PlacementTarget = _onboardMenuButton;
            _onboardPopup.IsOpen = true;
        };

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
        _onboardWriteButton.Click += (_, _) => { _onboardPopup.IsOpen = false; RunOnboardOperation(isApply: true); };
        _onboardRestoreButton.Click += (_, _) => { _onboardPopup.IsOpen = false; RunOnboardOperation(isApply: false); };
        _outputSettingsButton.Click += (_, _) => OpenOutputSettings();

        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    /// <summary>
    /// G600 本体書き込み／解除。device write は数秒かかる（settle 待ちを含む）ため UI thread の外で
    /// 実行し、完了時に状態と結果を表示する。実行中は再操作を受け付けない。
    /// </summary>
    private void RunOnboardOperation(bool isApply)
    {
        if (_onboardIntent is null || _onboardBusy)
        {
            return;
        }

        if (isApply)
        {
            var confirmed = MessageBox.Show(
                this,
                "この設定の G600 割当を G600 本体のメモリに書き込みます。\n"
                + "書き込み前の状態は記録され、「本体の書き込みを解除」でいつでも戻せます。\n\n続けますか？",
                "G600 本体に書き込む",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (confirmed != MessageBoxResult.OK)
            {
                return;
            }
        }

        _onboardBusy = true;
        _onboardStatusText.Text = isApply ? "G600 本体: 書き込み中…" : "G600 本体: 解除中…";
        UpdateOnboardButtons();

        var intent = _onboardIntent;
        var document = _document;
        System.Threading.Tasks.Task.Run(() => isApply ? intent.Apply(document) : intent.Restore())
            .ContinueWith(task => Dispatcher.Invoke(() =>
            {
                _onboardBusy = false;
                var result = task.IsFaulted
                    ? new G600OnboardUiResult(false, task.Exception!.GetBaseException().Message)
                    : task.Result;
                RefreshOnboardState();
                MessageBox.Show(
                    this,
                    result.Message,
                    result.Success ? "G600 本体" : "書き込めませんでした",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }));
    }

    private void RefreshOnboardState()
    {
        if (_onboardIntent is null)
        {
            return;
        }

        var state = _onboardIntent.QueryState();
        _onboardStatusText.Text = state.StatusLine;
        _onboardRestoreButton.Visibility = state.Active ? Visibility.Visible : Visibility.Collapsed;
        UpdateOnboardButtons();
    }

    private void OpenOutputSettings()
    {
        if (_serialHidSettingsIntent is null)
        {
            return;
        }

        var window = new SerialHidSettingsWindow(_serialHidSettingsIntent) { Owner = this };
        window.ShowDialog();
        RefreshOutputStatus();
    }

    private void RefreshOutputStatus()
    {
        if (_serialHidSettingsIntent is null)
        {
            return;
        }

        var snapshot = _serialHidSettingsIntent.Load();
        _outputStatusText.Text = snapshot.StatusLine;
        _outputStatusText.ToolTip = snapshot.StatusLine;
    }

    private void UpdateOnboardButtons()
    {
        // 保存済みの内容だけを本体へ書く（未保存の編集と本体がずれた状態を作らない）
        _onboardWriteButton.IsEnabled = !_onboardBusy && _compileOutcome.IsValid && !_hasUnsavedChanges;
        _onboardRestoreButton.IsEnabled = !_onboardBusy;
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

    // ─────────────────────────── 上の帯 ───────────────────────────

    private UIElement BuildHeader()
    {
        var bar = new Border { Background = Theme.BarFill, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 0, 16, 0) };
        var dock = new DockPanel { LastChildFill = false };

        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        brand.Children.Add(new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 9, 0),
            Background = new LinearGradientBrush(
                [new GradientStop(Theme.G13Color, 0), new GradientStop(Theme.AccentColor, 0.5), new GradientStop(Theme.G600Color, 1)],
                new Point(0, 0),
                new Point(1, 1)),
        });
        brand.Children.Add(new TextBlock
        {
            Text = "INPUT STUDIO",
            FontFamily = Theme.Display,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(brand, Dock.Left);
        dock.Children.Add(brand);

        AutomationProperties.SetName(_appPillButton, "設定中のアプリ");
        _appPillButton.Style = Theme.CreateFlatButtonStyle(10);
        _appPillButton.Height = 40;
        _appPillButton.Padding = new Thickness(10, 0, 12, 0);
        _appPillButton.VerticalAlignment = VerticalAlignment.Center;
        var pillContent = new StackPanel { Orientation = Orientation.Horizontal };
        pillContent.Children.Add(new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = Theme.Sunken,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _appPillIcon,
        });
        var pillLabel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        pillLabel.Children.Add(new TextBlock { Text = "設定中のアプリ", Foreground = Theme.Muted, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 3) });
        pillLabel.Children.Add(_appPillLabel);
        pillContent.Children.Add(pillLabel);
        pillContent.Children.Add(new TextBlock { Text = "▾", Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        _appPillButton.Content = pillContent;
        DockPanel.SetDock(_appPillButton, Dock.Left);
        dock.Children.Add(_appPillButton);

        _appPickerPopup.Child = new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.Line2,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(3),
            Margin = new Thickness(0, 0, 12, 12),
            Child = _appPickerList,
        };

        var liveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        liveRow.Children.Add(BuildLiveDot());
        liveRow.Children.Add(new TextBlock { Text = "いまゲームに届いている割当", Foreground = Theme.Muted, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
        liveRow.Children.Add(_liveAssignmentValueText);
        DockPanel.SetDock(liveRow, Dock.Left);
        dock.Children.Add(liveRow);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (_serialHidSettingsIntent is not null)
        {
            right.Children.Add(_outputStatusText);
        }

        if (_macroAutomationIntents is not null)
        {
            var macroButton = TopBarButton("マクロを作る", "マクロ作成画面を開く");
            macroButton.Click += (_, _) => OpenGameOperator(openMacroTab: true);
            right.Children.Add(macroButton);
        }

        if (_webResearchIntent is not null)
        {
            var gameOperatorButton = TopBarButton("Game Operator", "Game Operator画面を開く");
            gameOperatorButton.Click += (_, _) => OpenGameOperator();
            right.Children.Add(gameOperatorButton);
        }

        if (_serialHidSettingsIntent is not null)
        {
            right.Children.Add(_outputSettingsButton);
        }

        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);

        bar.Child = dock;
        return bar;
    }

    private static Button TopBarButton(string label, string automationName)
    {
        var button = Theme.Quiet(new Button { Content = label, Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0) });
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    /// <summary>稼働中の印（緑の丸から輪が広がり続ける）。</summary>
    private static UIElement BuildLiveDot()
    {
        var scale = new ScaleTransform(1, 1);
        var ring = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            BorderBrush = Theme.Ok,
            BorderThickness = new Thickness(1.5),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };
        var grid = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(ring);
        grid.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Theme.Ok });
        if (SystemParameters.ClientAreaAnimation)
        {
            var duration = TimeSpan.FromSeconds(2.2);
            var grow = new System.Windows.Media.Animation.DoubleAnimation(1, 2.3, duration) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.7, 0, duration) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        }
        else
        {
            ring.Visibility = Visibility.Collapsed;
        }

        return grid;
    }

    // ─────────────────────────── 左: 操作 ───────────────────────────

    private UIElement BuildActionListPane()
    {
        var pane = new Border { Background = Theme.SideFill, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0) };
        var dock = new DockPanel();

        var header = PaneHeader("操作", "このアプリでやりたいこと");
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        // 追加の入口は破線の枠（まだ無いものを足す場所）。
        var addFace = new Grid();
        addFace.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Stroke = Theme.Line2,
            StrokeThickness = 1,
            StrokeDashArray = [4, 3],
            RadiusX = 10,
            RadiusY = 10,
        });
        addFace.Children.Add(new TextBlock
        {
            Text = "＋ キーを録って操作を追加",
            Foreground = Theme.Muted,
            FontSize = 12.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _recordAddButton.Style = Theme.CreateFlatButtonStyle(10);
        _recordAddButton.Content = addFace;
        _recordAddButton.Height = 36;
        _recordAddButton.Margin = new Thickness(12, 0, 12, 8);
        _recordAddButton.Padding = new Thickness(0);
        _recordAddButton.Background = Brushes.Transparent;
        _recordAddButton.BorderBrush = Brushes.Transparent;
        _recordAddButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _recordAddButton.VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(_recordAddButton, "キーを録って新しい操作を追加する");
        _recordAddButton.ToolTip = "キーを録画して、新しい操作として追加します";
        _recordAddButton.Click += (_, _) => RecordNewAction();
        DockPanel.SetDock(_recordAddButton, Dock.Top);
        dock.Children.Add(_recordAddButton);

        AutomationProperties.SetName(_actionList, "操作一覧");
        _actionList.Margin = new Thickness(5, 0, 5, 10);
        dock.Children.Add(_actionList);

        pane.Child = dock;
        return pane;
    }

    private static UIElement PaneHeader(string title, string caption)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 14, 16, 10) };
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Bottom });
        header.Children.Add(new TextBlock { Text = caption, Foreground = Theme.Muted, FontSize = 11.5, Margin = new Thickness(10, 0, 0, 1), VerticalAlignment = VerticalAlignment.Bottom });
        return header;
    }

    // ─────────────────────────── 中央: 図 ───────────────────────────

    private UIElement BuildFigurePane()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new DockPanel { Margin = new Thickness(18, 12, 18, 0), LastChildFill = false };
        var deviceButtons = new StackPanel { Orientation = Orientation.Horizontal };
        deviceButtons.Children.Add(_g13TabButton);
        deviceButtons.Children.Add(_g600TabButton);
        var deviceSegment = Segment(deviceButtons);
        DockPanel.SetDock(deviceSegment, Dock.Left);
        head.Children.Add(deviceSegment);

        var layerCaption = new TextBlock { Text = "いま見ている配置", Foreground = Theme.Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 8, 0) };
        DockPanel.SetDock(layerCaption, Dock.Left);
        head.Children.Add(layerCaption);
        var layerSegment = Segment(_layerChipRow);
        DockPanel.SetDock(layerSegment, Dock.Left);
        head.Children.Add(layerSegment);

        // 図ごとの補助の操作は、切替の下の段へ右寄せで置く（G600 は配置名が長く、1段に収まらない）。
        var headRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 6, 18, 0) };
        _shiftSwitchButton.Background = Brushes.Transparent;
        _shiftSwitchButton.BorderBrush = Brushes.Transparent;
        _shiftSwitchButton.Padding = new Thickness(6, 4, 6, 4);
        _shiftSwitchButton.Click += (_, _) => ToggleG600Shift();
        headRight.Children.Add(_shiftSwitchButton);
        AutomationProperties.SetName(_onboardMenuButton, "G600 本体への書き込み");
        headRight.Children.Add(_onboardMenuButton);
        AutomationProperties.SetName(_lcdMenuButton, "G13 の LCD 表示と明かりの色");
        headRight.Children.Add(_lcdMenuButton);
        var headStack = new StackPanel();
        headStack.Children.Add(head);
        headStack.Children.Add(headRight);
        grid.Children.Add(headStack);

        BuildG13LcdSettingsPanel();
        _lcdPopup.Child = new Border { Child = _g13LcdSettingsPanel, Margin = new Thickness(0, 0, 12, 12) };

        var onboardStack = new StackPanel();
        onboardStack.Children.Add(_onboardStatusText);
        onboardStack.Children.Add(_onboardWriteButton);
        onboardStack.Children.Add(_onboardRestoreButton);
        _onboardPopup.Child = new Border
        {
            Background = Theme.Raised,
            BorderBrush = Theme.Line2,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 12, 12),
            Child = onboardStack,
        };

        Grid.SetRow(_figureHost, 1);
        grid.Children.Add(_figureHost);
        Grid.SetRow(_figureNoteText, 2);
        grid.Children.Add(_figureNoteText);
        return grid;
    }

    /// <summary>切替の入れ物（暗い溝の中に選択肢が並ぶ）。</summary>
    private static Border Segment(UIElement content) => new()
    {
        Background = Theme.Sunken,
        BorderBrush = Theme.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(11),
        Padding = new Thickness(3),
        VerticalAlignment = VerticalAlignment.Center,
        Child = content,
    };

    private static void StyleSegmentButton(Button button, bool isOn)
    {
        button.Height = 30;
        button.Padding = new Thickness(14, 0, 14, 0);
        button.FontWeight = FontWeights.Bold;
        button.FontSize = 12.5;
        button.Background = isOn ? Theme.Raised : Brushes.Transparent;
        button.BorderBrush = isOn ? Theme.Line2 : Brushes.Transparent;
        button.Foreground = isOn ? Theme.Text : Theme.Muted;
    }

    private void ToggleG600Shift()
    {
        var layout = _document.Devices.FirstOrDefault(device => device.DeviceKind == "G600");
        if (layout is null)
        {
            return;
        }

        var shiftIsButton = layout.HoldSelectors.Count == 0;
        if (TryMutateDocument(shiftIsButton
                ? WorkspaceDocumentEditor.SetG600ShiftAsSelector
                : WorkspaceDocumentEditor.SetG600ShiftAsButton))
        {
            Render();
        }
    }

    private void BuildG13LcdSettingsPanel()
    {
        var stack = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = "LCD表示",
            Foreground = Theme.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = "このプリセットが前面の時",
            Foreground = Theme.Muted,
            FontSize = 11.5,
            Margin = new Thickness(8, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(titleRow);
        _g13LcdSummary.Margin = new Thickness(0, 4, 0, 9);
        stack.Children.Add(_g13LcdSummary);

        var editRow = new Grid();
        editRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        editRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        editRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        editRow.Children.Add(_g13LcdImageButton);
        _g13LcdTextBox.Margin = new Thickness(8, 0, 0, 0);
        AutomationProperties.SetName(_g13LcdTextBox, "G13 LCDへ表示するテキスト");
        Grid.SetColumn(_g13LcdTextBox, 1);
        editRow.Children.Add(_g13LcdTextBox);
        Grid.SetColumn(_g13LcdTextButton, 2);
        editRow.Children.Add(_g13LcdTextButton);
        Grid.SetColumn(_g13LcdClearButton, 3);
        editRow.Children.Add(_g13LcdClearButton);
        stack.Children.Add(editRow);

        stack.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 14, 0, 11) });
        var lightTitleRow = new StackPanel { Orientation = Orientation.Horizontal };
        lightTitleRow.Children.Add(new TextBlock
        {
            Text = "明かりの色",
            Foreground = Theme.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        lightTitleRow.Children.Add(new TextBlock
        {
            Text = "このプリセットが前面の時",
            Foreground = Theme.Muted,
            FontSize = 11.5,
            Margin = new Thickness(8, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(lightTitleRow);
        _g13BacklightAudioButton.Margin = new Thickness(0, 5, 0, 0);
        _g13BacklightAudioButton.Click += (_, _) =>
        {
            if (TryMutateDocument(document =>
                    WorkspaceDocumentEditor.SetG13BacklightFollowsAudio(document, !document.G13BacklightFollowsAudio)))
            {
                Render();
            }
        };
        stack.Children.Add(_g13BacklightAudioButton);
        stack.Children.Add(new TextBlock
        {
            Text = "前面のアプリの音だけを聞いて、キーとLCDの明かりを虹の7色で変えます。低い音は赤、高い音は紫です。明るさは音の大きさに合わせます。",
            Foreground = Theme.Muted,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        });

        _g13LcdImageButton.Click += (_, _) => SelectG13LcdImage();
        _g13LcdTextButton.Click += (_, _) => SetG13LcdText();
        _g13LcdClearButton.Click += (_, _) =>
        {
            if (TryMutateDocument(WorkspaceDocumentEditor.ClearG13Lcd))
            {
                Render();
            }
        };
        _g13LcdSettingsPanel.Child = stack;
    }
    private void SelectG13LcdImage()
    {
        if (_g13LcdSettingsIntent is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "G13 LCDへ表示する画像を選ぶ",
            Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif|すべてのファイル|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var setting = _g13LcdSettingsIntent.FromImageFile(dialog.FileName);
            if (TryMutateDocument(document => WorkspaceDocumentEditor.SetG13Lcd(document, setting)))
            {
                Render();
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            _saveErrorMessage = $"LCD画像を設定できませんでした: {error.Message}";
            Render();
        }
    }

    private void SetG13LcdText()
    {
        if (_g13LcdSettingsIntent is null)
        {
            return;
        }

        try
        {
            var setting = _g13LcdSettingsIntent.FromText(_g13LcdTextBox.Text);
            if (TryMutateDocument(document => WorkspaceDocumentEditor.SetG13Lcd(document, setting)))
            {
                Render();
            }
        }
        catch (ArgumentException error)
        {
            _saveErrorMessage = $"LCDテキストを設定できませんでした: {error.Message}";
            Render();
        }
    }

    // ─────────────────────────── 右: 割当 ───────────────────────────

    private UIElement BuildBindingPane()
    {
        var pane = new Border { Background = Theme.SideFill, BorderBrush = Theme.Line, BorderThickness = new Thickness(1, 0, 0, 0) };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        layout.Children.Add(PaneHeader("割当", "選んだ操作をボタンへ"));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel { Margin = new Thickness(16, 0, 16, 12) };
        stack.Children.Add(_conflictNotePanel);
        stack.Children.Add(_inspectorEmptyPanel);
        stack.Children.Add(_inspectorBody);

        _inspectorTitleButton.Margin = new Thickness(0, 0, 0, 10);
        _inspectorBody.Children.Add(_inspectorTitleButton);
        _inspectorNameBox.Margin = new Thickness(0, 0, 0, 10);
        _inspectorBody.Children.Add(_inspectorNameBox);

        var sendStack = new StackPanel();
        sendStack.Children.Add(CardCaption("ゲームに送るキー"));
        var sendRow = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        sendRow.Children.Add(_sendKeyRow);
        AutomationProperties.SetName(_recordUpdateButton, "選んだ操作のキーを録り直す");
        _recordUpdateButton.ToolTip = "選んでいる操作のキーを、新しく録ったキーで上書きします";
        _recordUpdateButton.Click += (_, _) => RecordUpdateSelected();
        sendRow.Children.Add(_recordUpdateButton);
        sendStack.Children.Add(sendRow);
        _selectMacroButton.Click += (_, _) => SelectMacroForCurrentAction();
        _selectMacroButton.ToolTip = "保存済みマクロを、この操作の機能として使います";
        AutomationProperties.SetName(_selectMacroButton, "選んだ操作へマクロを設定する");
        sendStack.Children.Add(_selectMacroHostInCard);
        _inspectorBody.Children.Add(Theme.Card(sendStack));

        _assignHint.Margin = new Thickness(2, 10, 2, 0);
        _inspectorBody.Children.Add(_assignHint);

        _inspectorBody.Children.Add(DeviceCard("G13 キーパッド", Theme.G13Color, _g13BindingsPanel));
        _inspectorBody.Children.Add(DeviceCard("G600 マウス", Theme.G600Color, _g600BindingsPanel));

        _actionNotesPanel.Margin = new Thickness(2, 10, 2, 0);
        _inspectorBody.Children.Add(_actionNotesPanel);
        _deleteActionButton.Margin = new Thickness(0, 12, 0, 0);
        _inspectorBody.Children.Add(_deleteActionButton);

        scroll.Content = stack;
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);

        var saveArea = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 12, 16, 12) };
        var saveStack = new StackPanel();
        var stateRow = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        DockPanel.SetDock(_saveDot, Dock.Left);
        stateRow.Children.Add(_saveDot);
        stateRow.Children.Add(_saveChipText);
        saveStack.Children.Add(stateRow);

        var saveRow = new Grid();
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _saveButton.Margin = new Thickness(0, 0, 4, 0);
        AutomationProperties.SetName(_saveButton, "編集内容を保存");
        saveRow.Children.Add(_saveButton);
        _revertButton.Margin = new Thickness(4, 0, 0, 0);
        AutomationProperties.SetName(_revertButton, "未保存の変更を元に戻す");
        Grid.SetColumn(_revertButton, 1);
        saveRow.Children.Add(_revertButton);
        saveStack.Children.Add(saveRow);
        saveArea.Child = saveStack;
        Grid.SetRow(saveArea, 2);
        layout.Children.Add(saveArea);

        pane.Child = layout;
        return pane;
    }

    private static TextBlock CardCaption(string text) => new() { Text = text, Foreground = Theme.Muted, FontWeight = FontWeights.Bold, FontSize = 11.5 };

    private static Border DeviceCard(string label, Color deviceColor, StackPanel rows)
    {
        var stack = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
        title.Children.Add(Theme.Dot(deviceColor));
        title.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Bold, FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0) });
        stack.Children.Add(title);
        stack.Children.Add(rows);
        var card = Theme.Card(stack, new Thickness(8, 11, 8, 7));
        title.Margin = new Thickness(4, 0, 0, 5);
        card.Margin = new Thickness(0, 12, 0, 0);
        return card;
    }

    // ─────────────────────────── 下の帯 ───────────────────────────

    private UIElement BuildFooter()
    {
        var bar = new Border { Background = Theme.BarFill, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 0, 16, 0) };
        var dock = new DockPanel();

        var title = new TextBlock { Text = "動作チェック", FontWeight = FontWeights.Bold, FontSize = 12.5, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(title, Dock.Left);
        dock.Children.Add(title);

        var diagnosticsButton = Theme.Quiet(new Button { Content = "診断", Height = 30, Padding = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetName(diagnosticsButton, "診断画面を開く");
        diagnosticsButton.Click += (_, _) => OpenDiagnostics();
        DockPanel.SetDock(diagnosticsButton, Dock.Right);
        dock.Children.Add(diagnosticsButton);

        _testFieldHint.Text = "デバイスのボタンを押すと、ここに結果が流れます";
        _testFieldHint.TextTrimming = TextTrimming.CharacterEllipsis;
        var flow = new Grid { ClipToBounds = true, Margin = new Thickness(0, 0, 12, 0) };
        flow.Children.Add(_testFieldHint);
        flow.Children.Add(_tickerPanel);
        dock.Children.Add(flow);

        bar.Child = dock;
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
        var layerId = CurrentLayerFor(deviceKind);
        var occupant = _document.Bindings.FirstOrDefault(binding =>
            binding.DeviceKind == deviceKind && binding.LayerId == layerId && binding.ControlId == controlId);
        if (occupant is not null)
        {
            // 光っているキーを押したら、そのキーに載っている操作を選ぶ（別の操作を重ねて衝突させない）。
            if (occupant.ActionId != _selectedActionId)
            {
                _selectedActionId = occupant.ActionId;
                _isRenamingAction = false;
                Render();
            }

            _figureView?.Pulse(controlId);
            return;
        }

        if (_selectedActionId is null)
        {
            ShowToast("先に左の一覧から操作を選んでください");
            return;
        }

        var actionId = _selectedActionId;
        if (TryMutateDocument(document => WorkspaceDocumentEditor.SetBinding(document, actionId, deviceKind, controlId, layerId)))
        {
            Render();
            _figureView?.Pulse(controlId);
            var name = _document.Actions.First(action => action.ActionId == actionId).Name;
            ShowToast($"「{name}」を {deviceKind} の {ControlLabel(deviceKind, controlId)} に載せました");
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
            ShowToast(_residentApply is not null ? "保存しました。ゲームへ反映済みです" : "保存しました");
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
        _appPillIcon.Text = view.Chrome.EditingLabel.Length > 0 ? view.Chrome.EditingLabel[..1] : string.Empty;
        _liveAssignmentValueText.Text = view.Chrome.LiveAssignmentLabel;
        _liveAssignmentValueText.ToolTip = view.Chrome.LiveAssignmentLabel;

        // Items の Clear/Add は SelectionChanged を発火させるため、再構築中はハンドラを外す。
        _appPickerList.SelectionChanged -= OnAppPickerSelectionChanged;
        _appPickerList.Items.Clear();
        AddAppPickerGroup("設定を持っているアプリ", view.RailRows.Where(row => row.IsAssociated || row.IsSelected));
        AddAppPickerGroup("いま動いているアプリから選ぶ", view.RailRows.Where(row => !row.IsAssociated && !row.IsSelected));
        _appPickerList.SelectionChanged += OnAppPickerSelectionChanged;

        var (stateText, stateColor) = !_compileOutcome.IsValid
            ? ("保存できません", Theme.DangerColor)
            : _hasUnsavedChanges
                ? ("未保存の変更あり", Theme.WarnColor)
                : _justSaved
                    ? (_residentApply is not null ? "保存しました。ゲームへ反映済みです" : "保存しました。ゲーム側への反映は再起動後", Theme.OkColor)
                    : ("変更はありません", Theme.FaintColor);
        _saveChipText.Text = stateText;
        _saveChipText.Foreground = stateColor == Theme.FaintColor ? Theme.Muted : Theme.Freeze(stateColor);
        _saveDot.Background = Theme.Freeze(stateColor);
        _saveDot.Effect = _hasUnsavedChanges ? Theme.Glow(stateColor, 10, 0.9) : null;

        _saveButton.IsEnabled = _compileOutcome.IsValid && _hasUnsavedChanges;
        UpdateOnboardButtons();
        _revertButton.IsEnabled = _hasUnsavedChanges;
        // 無効時の見た目は共通 style の不透明度 trigger が担う（Opacity の手動設定は trigger を打ち消すため触らない）。

        // 保存できる変更がある間、保存ボタンの光をゆっくり明滅させる。
        var shouldBreathe = _saveButton.IsEnabled && SystemParameters.ClientAreaAnimation;
        if (shouldBreathe != _saveGlowBreathing && _saveButton.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
        {
            _saveGlowBreathing = shouldBreathe;
            glow.BeginAnimation(
                System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
                shouldBreathe
                    ? new System.Windows.Media.Animation.DoubleAnimation(0.35, 0.95, TimeSpan.FromSeconds(0.9))
                    {
                        AutoReverse = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    }
                    : null);
        }
    }

    private void AddAppPickerGroup(string caption, IEnumerable<ApplicationRailRowView> rows)
    {
        var materialized = rows.ToArray();
        if (materialized.Length == 0)
        {
            return;
        }

        _appPickerList.Items.Add(new ListBoxItem
        {
            Content = new TextBlock { Text = caption, Foreground = Theme.Faint, FontSize = 11 },
            IsEnabled = false,
            IsHitTestVisible = false,
            Padding = new Thickness(10, 6, 10, 2),
        });
        foreach (var row in materialized)
        {
            var line = new DockPanel();
            if (row.IsRunning)
            {
                var running = new TextBlock { Text = "実行中", Foreground = Theme.Ok, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                DockPanel.SetDock(running, Dock.Right);
                line.Children.Add(running);
            }

            line.Children.Add(new TextBlock { Text = row.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var item = new ListBoxItem { Content = line, Tag = row.ApplicationFullPath, IsSelected = row.IsSelected };
            AutomationProperties.SetName(item, row.DisplayName);
            _appPickerList.Items.Add(item);
        }
    }

    private void RenderActionList(ActionBoardView boardView)
    {
        var tintedItemStyle = (Style)FindResource("TintedListItem");
        var defaultLayerByDevice = _document.Devices.ToDictionary(device => device.DeviceKind, device => device.DefaultLayerId, StringComparer.Ordinal);

        // Items の Clear/Add は ListBox の SelectionChanged を都度発火させる。ここで Render() へ
        // 再入すると（Clear 直後の SelectedItem=null による偽の選択変更など）無限再帰になるため、
        // 再構築中はハンドラを外す（選択状態そのものは IsSelected で個別に張り直すので機能は変わらない）。
        _actionList.SelectionChanged -= OnActionListSelectionChanged;
        _actionList.Items.Clear();
        for (var index = 0; index < _document.Actions.Count; index++)
        {
            var action = _document.Actions[index];
            var row = boardView.Rows.First(candidate => candidate.ActionId == action.ActionId);
            var color = Theme.ActionColorValueAt(index);

            var line = new DockPanel();
            var keycap = Theme.Keycap(KeyLegend(action.Outputs), color);
            keycap.Margin = new Thickness(0, 0, 11, 0);
            DockPanel.SetDock(keycap, Dock.Left);
            line.Children.Add(keycap);

            var where = _document.Bindings
                .Where(binding => binding.ActionId == action.ActionId)
                .OrderBy(binding => binding.DeviceKind, StringComparer.Ordinal)
                .ThenBy(binding => defaultLayerByDevice.TryGetValue(binding.DeviceKind, out var defaultLayer) && binding.LayerId == defaultLayer ? 0 : 1)
                .Select(binding =>
                {
                    var place = $"{binding.DeviceKind} {ControlLabel(binding.DeviceKind, binding.ControlId)}";
                    return defaultLayerByDevice.TryGetValue(binding.DeviceKind, out var defaultLayer) && binding.LayerId == defaultLayer
                        ? place
                        : $"{place}（{LayerLabel(binding.DeviceKind, binding.LayerId)}）";
                })
                .ToArray();

            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = row.Name, FontWeight = FontWeights.Bold, FontSize = 13.5, Foreground = Theme.Text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 4) });
            labels.Children.Add(new TextBlock
            {
                Text = where.Length == 0 ? "まだどのボタンにも載せていません" : string.Join("　", where),
                Foreground = where.Length == 0 ? Theme.Warn : Theme.Muted,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            line.Children.Add(labels);

            var item = new ListBoxItem
            {
                Style = tintedItemStyle,
                Content = line,
                Tag = row.ActionId,
                IsSelected = row.IsSelected,
                Background = row.IsSelected ? Theme.Freeze(Theme.WithAlpha(color, 0.13)) : Brushes.Transparent,
                BorderBrush = row.IsSelected ? Theme.Freeze(Theme.WithAlpha(color, 0.55)) : Brushes.Transparent,
                ToolTip = action.Outputs.Count == 0 ? "送るキーが未設定です" : $"{WorkspaceEditorProjection.OutputsDisplayName(action.Outputs)} を送る",
            };
            AutomationProperties.SetName(item, $"操作 {row.Name}");
            _actionList.Items.Add(item);
        }

        _actionList.SelectionChanged += OnActionListSelectionChanged;
    }

    private void RenderFigurePane()
    {
        var isG13 = _selectedFigureDeviceKind == "G13";
        RenderDeviceTab(_g13TabButton, "G13 キーパッド", Theme.G13Color, _snapshot.G13ConnectedCount > 0, isG13);
        RenderDeviceTab(_g600TabButton, "G600 マウス", Theme.G600Color, _snapshot.G600ConnectedCount > 0, !isG13);
        RenderG13LcdSettings(isG13);
        _onboardMenuButton.Visibility = !isG13 && _onboardIntent is not null ? Visibility.Visible : Visibility.Collapsed;
        _shiftSwitchButton.Visibility = isG13 ? Visibility.Collapsed : Visibility.Visible;

        var layout = _document.Devices.FirstOrDefault(device => device.DeviceKind == _selectedFigureDeviceKind);
        _layerChipRow.Children.Clear();
        _figureView = null;
        _figureHost.Child = null;
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
                var chip = new Button { Content = LayerLabel(_selectedFigureDeviceKind, layerId) };
                StyleSegmentButton(chip, layerId == currentLayerId);
                AutomationProperties.SetName(chip, $"配置 {LayerLabel(_selectedFigureDeviceKind, layerId)}");
                var capturedLayerId = layerId;
                chip.Click += (_, _) => ShowFigureLayer(_selectedFigureDeviceKind, capturedLayerId);
                _layerChipRow.Children.Add(chip);
            }

            var shiftIsButton = !isG13 && layout.HoldSelectors.Count == 0;
            if (!isG13)
            {
                RenderShiftSwitch(shiftIsButton);
            }

            var request = new InputStudioFigures.FigureRequest(
                BuildFigureBindingLookup(_selectedFigureDeviceKind, currentLayerId),
                _selectedActionId,
                controlId => OnFigureKeyClicked(_selectedFigureDeviceKind, controlId),
                ShowToast);
            _figureView = isG13
                ? InputStudioFigures.BuildG13(
                    request,
                    layout.LatchSelectors.ToDictionary(selector => selector.ControlId, selector => selector.LayerId, StringComparer.Ordinal),
                    currentLayerId,
                    layerId => ShowFigureLayer("G13", layerId))
                : InputStudioFigures.BuildG600(request, shiftIsButton);
            _figureHost.Child = _figureView.Root;
        }

        var selectedName = _selectedActionId is null
            ? null
            : _document.Actions.FirstOrDefault(action => action.ActionId == _selectedActionId)?.Name;
        var target = selectedName is null ? "左で選んだ操作" : $"「{selectedName}」";
        _figureNoteText.Text = _pendingAssign
            ? $"空のキーをクリックすると{target}を載せます。デバイスのボタンを押しても載せられます。"
            : $"空のキーをクリックすると{target}を載せます。光っているキーを押すと、その操作を選びます。";
    }

    private void ShowFigureLayer(string deviceKind, string layerId)
    {
        _selectedFigureDeviceKind = deviceKind;
        _figureLayerByDevice[deviceKind] = layerId;
        Render();
    }

    private static void RenderDeviceTab(Button button, string label, Color deviceColor, bool connected, bool isOn)
    {
        StyleSegmentButton(button, isOn);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = Theme.Dot(connected ? deviceColor : Theme.FaintColor, 7);
        if (!connected)
        {
            dot.Effect = null;
        }

        content.Children.Add(dot);
        content.Children.Add(new TextBlock { Text = label, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        if (!connected)
        {
            content.Children.Add(new TextBlock { Text = "未接続", Foreground = Theme.Faint, FontWeight = FontWeights.Normal, FontSize = 11, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        button.Content = content;
        button.ToolTip = connected ? $"{label}：接続中" : $"{label}：未接続（編集はできます）";
        AutomationProperties.SetName(button, $"{label}（{(connected ? "接続中" : "未接続")}）");
    }

    private void RenderShiftSwitch(bool shiftIsButton)
    {
        var track = new Border
        {
            Width = 30,
            Height = 17,
            CornerRadius = new CornerRadius(8.5),
            Background = shiftIsButton ? Theme.Freeze(Theme.Mix(Theme.SunkenColor, Theme.G600Color, 0.35)) : Theme.Sunken,
            BorderBrush = Theme.Line2,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new Border
            {
                Width = 11,
                Height = 11,
                CornerRadius = new CornerRadius(5.5),
                Background = shiftIsButton ? Theme.G600 : Theme.Muted,
                HorizontalAlignment = shiftIsButton ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 2, 0),
            },
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(track);
        content.Children.Add(new TextBlock { Text = "G-Shift をボタンにする", Foreground = Theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        _shiftSwitchButton.Content = content;
        _shiftSwitchButton.ToolTip = shiftIsButton
            ? "G6 を層切替に戻します（G6 の割当が残っている間は戻せません）"
            : "G6 を通常のボタンとして割当可能にします（「G-Shift を押している間」の配置は無くなります。割当が残っている間は変えられません）";
        AutomationProperties.SetName(_shiftSwitchButton, shiftIsButton ? "G-Shift をボタンにする（入）" : "G-Shift をボタンにする（切）");
    }

    private void RenderG13LcdSettings(bool isG13)
    {
        var available = isG13 && _g13LcdSettingsIntent is not null;
        _lcdMenuButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (!available)
        {
            _lcdPopup.IsOpen = false;
            return;
        }

        _g13LcdSummary.Text = _document.G13Lcd switch
        {
            { Kind: WorkspaceG13LcdContentKind.Image } image => $"画像: {image.SourceName}",
            { Kind: WorkspaceG13LcdContentKind.Text } text => $"テキスト: {text.Text}",
            _ => "未設定：共通のWindows表示を使います",
        };
        if (!_g13LcdTextBox.IsKeyboardFocusWithin)
        {
            _g13LcdTextBox.Text = _document.G13Lcd?.Kind == WorkspaceG13LcdContentKind.Text
                ? _document.G13Lcd.Text ?? string.Empty
                : string.Empty;
        }

        _g13LcdClearButton.IsEnabled = _document.G13Lcd is not null;
        RenderG13BacklightAudioSwitch(_document.G13BacklightFollowsAudio);
    }

    /// <summary>「LCDと明かり」の設定の中身を、窓を出さずに描く見本用の窓へ移す（移した後は元の窓から開けない）。</summary>
    public Window DetachLcdAndLightPanelForSnapshot()
    {
        var content = _lcdPopup.Child;
        _lcdPopup.Child = null;
        var preview = new Window { Width = _g13LcdSettingsPanel.Width + 24, Content = content };
        Theme.Apply(preview);
        return preview;
    }

    private void RenderG13BacklightAudioSwitch(bool followsAudio)
    {
        var track = new Border
        {
            Width = 30,
            Height = 17,
            CornerRadius = new CornerRadius(8.5),
            Background = followsAudio ? Theme.Freeze(Theme.Mix(Theme.SunkenColor, Theme.G13Color, 0.35)) : Theme.Sunken,
            BorderBrush = Theme.Line2,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new Border
            {
                Width = 11,
                Height = 11,
                CornerRadius = new CornerRadius(5.5),
                Background = followsAudio ? Theme.G13 : Theme.Muted,
                HorizontalAlignment = followsAudio ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 2, 0),
            },
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(track);
        content.Children.Add(new TextBlock { Text = "アプリの音に合わせて色を変える", Foreground = Theme.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        _g13BacklightAudioButton.Content = content;
        AutomationProperties.SetName(_g13BacklightAudioButton, followsAudio ? "アプリの音に合わせて色を変える（入）" : "アプリの音に合わせて色を変える（切）");
    }

    private Dictionary<string, InputStudioFigures.FigureBinding> BuildFigureBindingLookup(string deviceKind, string layerId)
    {
        var result = new Dictionary<string, InputStudioFigures.FigureBinding>(StringComparer.Ordinal);
        foreach (var binding in _document.Bindings)
        {
            if (binding.DeviceKind != deviceKind || binding.LayerId != layerId)
            {
                continue;
            }

            var actionIndex = _document.Actions.ToList().FindIndex(candidate => candidate.ActionId == binding.ActionId);
            if (actionIndex < 0)
            {
                continue;
            }

            var action = _document.Actions[actionIndex];
            result[binding.ControlId] = new InputStudioFigures.FigureBinding(
                action.ActionId,
                action.Name,
                KeyLegend(action.Outputs),
                Theme.ActionColorValueAt(actionIndex));
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
        _sendKeyRow.Children.Clear();

        _selectMacroButton.IsEnabled = _macroAutomationIntents is not null;
        if (_pendingAssign && inspector is not null)
        {
            // 待機中の注意文（層切替キー・左右クリック等）は Render で上書きしない
            if (_assignHint.Text.Length == 0)
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

        // 「保存済みマクロを選ぶ」は操作を選んでいなくても使える（新しい操作として追加する）ため、置き場所を移す。
        var macroHost = inspector is null ? _selectMacroHostWhenEmpty : _selectMacroHostInCard;
        if (!ReferenceEquals(macroHost.Child, _selectMacroButton))
        {
            _selectMacroHostWhenEmpty.Child = null;
            _selectMacroHostInCard.Child = null;
            macroHost.Child = _selectMacroButton;
        }

        _inspectorBody.Visibility = inspector is null ? Visibility.Collapsed : Visibility.Visible;
        if (inspector is null)
        {
            _inspectorEmptyPanel.Children.Add(new TextBlock
            {
                Text = "左の一覧から操作を選ぶと、ここに割当を出します。",
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
            });
            _inspectorEmptyPanel.Children.Add(_selectMacroHostWhenEmpty);
            return;
        }

        var actionIndex = Math.Max(_document.Actions.ToList().FindIndex(action => action.ActionId == inspector.ActionId), 0);
        var color = Theme.ActionColorValueAt(actionIndex);
        var selectedAction = _document.Actions[actionIndex];

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
            var swatch = Theme.Dot(color, 12);
            swatch.CornerRadius = new CornerRadius(4);
            swatch.Margin = new Thickness(0, 0, 10, 0);
            titleContent.Children.Add(swatch);
            titleContent.Children.Add(new TextBlock { Text = inspector.Name, FontSize = 17, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 250 });
            _inspectorTitleButton.Content = titleContent;
            AutomationProperties.SetName(_inspectorTitleButton, $"操作名: {inspector.Name}（クリックで変更）");
        }

        if (selectedAction.Outputs.Count == 0)
        {
            _sendKeyRow.Children.Add(new TextBlock { Text = "（未設定）", Foreground = Theme.Faint, VerticalAlignment = VerticalAlignment.Center });
        }
        else if (selectedAction.Outputs.Count == 1 && MacroInvocationTokens.IsMacro(selectedAction.Outputs[0]))
        {
            _sendKeyRow.Children.Add(Theme.Keycap(WorkspaceEditorProjection.OutputsDisplayName(selectedAction.Outputs), color, height: 40, minWidth: 46, fontSize: 12));
        }
        else
        {
            for (var index = 0; index < selectedAction.Outputs.Count; index++)
            {
                if (index > 0)
                {
                    _sendKeyRow.Children.Add(new TextBlock { Text = "+", Foreground = Theme.Muted, FontFamily = Theme.Mono, FontWeight = FontWeights.Bold, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                }

                _sendKeyRow.Children.Add(Theme.Keycap(KeyLegend([selectedAction.Outputs[index]]), color, height: 40, minWidth: 46, fontSize: 14));
            }
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
            foreach (var layerId in deviceOptions.LayerIds)
            {
                var binding = inspector.Bindings.FirstOrDefault(candidate =>
                    candidate.DeviceKind == deviceOptions.DeviceKind && candidate.LayerId == layerId);
                panel.Children.Add(BuildBindingRow(inspector.ActionId, deviceOptions.DeviceKind, layerId, binding?.ControlId));
            }

            // 割当先の指定は「実機のボタンを押す」または絵のボタンをクリック（pulldown は撤去・オーナー指示 2026-08-22）。
        }
    }

    /// <summary>配置ひとつ分の行。行を押すと、その配置を図で見る。割当があれば「外す」を出す。</summary>
    private Button BuildBindingRow(string actionId, string deviceKind, string layerId, string? controlId)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = LayerLabel(deviceKind, layerId), Foreground = Theme.Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 3) });
        labels.Children.Add(new TextBlock
        {
            Text = controlId is null ? "空き" : ControlLabel(deviceKind, controlId),
            Foreground = controlId is null ? Theme.Faint : Theme.Text,
            FontWeight = controlId is null ? FontWeights.Normal : FontWeights.Bold,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        grid.Children.Add(labels);

        if (controlId is not null)
        {
            var removeButton = Theme.Quiet(new Button { Content = "外す", FontSize = 11, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) });
            AutomationProperties.SetName(removeButton, $"{deviceKind} {LayerLabel(deviceKind, layerId)} の割当を外す");
            removeButton.Click += (_, _) =>
            {
                if (TryMutateDocument(document => WorkspaceDocumentEditor.RemoveBinding(document, actionId, deviceKind, layerId)))
                {
                    Render();
                }
            };
            removeButton.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(removeButton, 1);
            grid.Children.Add(removeButton);
        }

        var row = new Button
        {
            Content = grid,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Padding = new Thickness(8, 5, 8, 5),
            MinHeight = 32,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = "この配置を図で見る",
        };
        AutomationProperties.SetName(row, $"{deviceKind} {LayerLabel(deviceKind, layerId)}：{(controlId is null ? "空き" : ControlLabel(deviceKind, controlId))}");
        row.Click += (_, _) => ShowFigureLayer(deviceKind, layerId);
        return row;
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
        if (deviceKind == "G13" && InputStudioFigures.G13ControlName(controlId) is { } stickName)
        {
            return stickName;
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
