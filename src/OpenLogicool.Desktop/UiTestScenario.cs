namespace OpenLogicool.Desktop;

/// <summary>
/// t10（Phase 3 Exit 条件5）: 「アプリ選択（共通設定→特定アプリ）→ 操作作成 → G13/G600 両 device
/// binding → 保存 → 適用状態表示の確認」を、<see cref="InputStudioWindow"/> のイベントハンドラが
/// 呼ぶのと同じ public 経路（<see cref="IWorkspaceEditorIntents"/>・<see cref="WorkspaceDocumentEditor"/>・
/// <see cref="WorkspaceScreenProjection"/>）だけで駆動する pure runner。
/// Window（WPF）自体は介さないが、テスト専用の別経路は作らない——各段は
/// InputStudioWindow.OnAppPickerSelectionChanged／LoadSelectedWorkspace（LoadDocument）、
/// OnAddAction（WorkspaceDocumentEditor.AddAction）、OnFigureKeyClicked（SetBinding）、
/// OnInspectorOutputsCommitted（SetActionOutputs）、SaveCurrentDocument（intents.Save）、
/// Render（WorkspaceScreenProjection.Project）が呼ぶのと同一の呼び出しである。
/// </summary>
public static class UiTestScenario
{
    /// <summary>編集対象 app（実行中でも関連付け済みでもない、シナリオ専用の合成 path）。</summary>
    public const string TargetApplicationFullPath = @"c:\game\t10-scenario-app.exe";

    public const string TargetActionId = "action-t10";
    public const string TargetActionName = "t10 テスト操作";
    public static readonly IReadOnlyList<string> TargetOutputs = ["Key:A"];

    // 確認済み control（根拠4値・G13Controls/G600Controls.ConfirmedButtons）だけを使い、
    // 「未確認 control」警告が compile 結果に紛れ込まないようにする。
    public const string G13ControlId = "G1";
    public const string G600ControlId = "G9";
    public const string BaseLayerId = "base";

    /// <summary>rail は fake/real 双方が同一の literal を使う（実行中 app 一覧の実測値は使わない
    /// ——rail 構築そのものは表示専用の関心であり、この scenario が検証する fake/real 契約の対象外）。</summary>
    public static IReadOnlyList<ApplicationRailEntryInput> BuildRailEntries() =>
    [
        new("*", "共通設定（どのアプリでもない時）", IsRunning: false, IsAssociated: true),
        new(TargetApplicationFullPath, "t10 シナリオアプリ", IsRunning: false, IsAssociated: false),
    ];

    public static UiTestScenarioResult Run(IWorkspaceEditorIntents intents, int g13ConnectedCount, int g600ConnectedCount)
    {
        var railEntries = BuildRailEntries();
        const string foregroundStateLabel = "既定 app（identity 識別済み・関連付けなし）";

        // 段階1a: 共通設定を選ぶ（InputStudioWindow の初期選択・OnAppPickerSelectionChanged と同じ LoadDocument）。
        var defaultLoad = intents.LoadDocument("*");
        var defaultSnapshot = new WorkspaceScreenSnapshot(
            foregroundStateLabel, null, defaultLoad.RevisionNumber, defaultLoad.Stages,
            g13ConnectedCount, g600ConnectedCount, railEntries);
        var defaultView = WorkspaceScreenProjection.Project(defaultSnapshot, "*");

        // 段階1b: 特定アプリへ切り替える（OnAppPickerSelectionChanged → LoadSelectedWorkspace）。
        var appLoad = intents.LoadDocument(TargetApplicationFullPath);
        var afterSelectSnapshot = defaultSnapshot with
        {
            SelectedWorkspaceRevisionNumber = appLoad.RevisionNumber,
            Stages = appLoad.Stages,
        };
        var afterSelectView = WorkspaceScreenProjection.Project(afterSelectSnapshot, TargetApplicationFullPath);

        // 段階2: 操作を作成する（OnAddAction と同じ WorkspaceDocumentEditor.AddAction）。
        var document = WorkspaceDocumentEditor.AddAction(appLoad.Document, TargetActionId, TargetActionName, []);

        // 段階3: G13/G600 両方へ binding する（OnFigureKeyClicked と同じ SetBinding）＋出力設定
        // （OnInspectorOutputsCommitted／OnRecordKeyClicked と同じ SetActionOutputs）。
        document = WorkspaceDocumentEditor.SetBinding(document, TargetActionId, "G13", G13ControlId, BaseLayerId);
        document = WorkspaceDocumentEditor.SetBinding(document, TargetActionId, "G600", G600ControlId, BaseLayerId);
        document = WorkspaceDocumentEditor.SetActionOutputs(document, TargetActionId, TargetOutputs);

        // TryMutateDocument は変更の都度 Compile を取り直す。ここでは最終形に対して1回で足りる。
        var compileOutcome = intents.Compile(document);

        // 段階4: 保存する（SaveCurrentDocument と同じ intents.Save）。
        var saveOutcome = intents.Save(document);
        var afterSaveSnapshot = afterSelectSnapshot with
        {
            SelectedWorkspaceRevisionNumber = saveOutcome.RevisionNumber,
            Stages = saveOutcome.Stages,
        };

        // 段階5: 適用状態表示を確認する（Render と同じ WorkspaceScreenProjection.Project）。
        var afterSaveView = WorkspaceScreenProjection.Project(afterSaveSnapshot, TargetApplicationFullPath);

        var g13Binding = document.Bindings.SingleOrDefault(binding => binding.DeviceKind == "G13");
        var g600Binding = document.Bindings.SingleOrDefault(binding => binding.DeviceKind == "G600");
        var savedAction = document.Actions.Single(action => action.ActionId == TargetActionId);

        return new UiTestScenarioResult(
            DefaultEditingLabel: defaultView.Chrome.EditingLabel,
            SelectedEditingLabelAfterAppSelect: afterSelectView.Chrome.EditingLabel,
            SelectedApplicationFullPath: TargetApplicationFullPath,
            ActionCount: document.Actions.Count,
            ActionId: TargetActionId,
            ActionName: savedAction.Name,
            ActionOutputs: savedAction.Outputs,
            G13BindingControlId: g13Binding?.ControlId,
            G13BindingLayerId: g13Binding?.LayerId,
            G600BindingControlId: g600Binding?.ControlId,
            G600BindingLayerId: g600Binding?.LayerId,
            CompileIsValid: compileOutcome.IsValid,
            CompileProfileCount: compileOutcome.ProfileCount,
            CompileWarnings: compileOutcome.Warnings,
            CompileErrorMessage: compileOutcome.ErrorMessage,
            SaveRevisionNumber: saveOutcome.RevisionNumber,
            SaveStageCells: saveOutcome.Stages,
            AppliedRevisionLabelAfterSave: afterSaveView.Chrome.AppliedRevisionLabel,
            EditingLabelAfterSave: afterSaveView.Chrome.EditingLabel,
            DeviceConnectionLabels: afterSaveView.DeviceConnections
                .Select(connection => $"{connection.DeviceKind}: {connection.ConnectionLabel}")
                .ToArray());
    }
}

/// <summary>
/// UI test scenario 1回分の構造化結果。fake/real どちらの <see cref="IWorkspaceEditorIntents"/> で
/// 駆動しても、<see cref="DeviceConnectionLabels"/>（実機接続台数という環境依存値）を除く全 field が
/// 一致することを Host の UiTestScenarioComparer が機械判定する。
/// </summary>
public sealed record UiTestScenarioResult(
    string DefaultEditingLabel,
    string SelectedEditingLabelAfterAppSelect,
    string SelectedApplicationFullPath,
    int ActionCount,
    string ActionId,
    string ActionName,
    IReadOnlyList<string> ActionOutputs,
    string? G13BindingControlId,
    string? G13BindingLayerId,
    string? G600BindingControlId,
    string? G600BindingLayerId,
    bool CompileIsValid,
    int CompileProfileCount,
    IReadOnlyList<string> CompileWarnings,
    string? CompileErrorMessage,
    long SaveRevisionNumber,
    IReadOnlyList<WorkspaceStageCell> SaveStageCells,
    string AppliedRevisionLabelAfterSave,
    string EditingLabelAfterSave,
    IReadOnlyList<string> DeviceConnectionLabels);
