using OpenLogicool.Desktop;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>
/// t10（Phase 3 Exit 条件5）: UI test scenario を fake（in-memory FakeWorkspaceEditorIntents）で
/// 常設 focused test として回す。real（実 SQLite・実 device 列挙）との突き合わせは
/// `OpenLogicool.Host ui-test-scenario` CLI で probe-output へ証跡化する（CI 常設は不要）。
/// </summary>
public sealed class UiTestScenarioTests
{
    [Fact]
    public void App_selection_moves_from_default_to_the_target_application()
    {
        var result = UiTestScenario.Run(new FakeWorkspaceEditorIntents(), g13ConnectedCount: 1, g600ConnectedCount: 1);

        Assert.Equal("共通設定（どのアプリでもない時）", result.DefaultEditingLabel);
        Assert.Equal("t10 シナリオアプリ", result.SelectedEditingLabelAfterAppSelect);
        Assert.Equal(UiTestScenario.TargetApplicationFullPath, result.SelectedApplicationFullPath);
    }

    [Fact]
    public void Action_creation_and_both_device_bindings_land_in_the_document()
    {
        var result = UiTestScenario.Run(new FakeWorkspaceEditorIntents(), g13ConnectedCount: 1, g600ConnectedCount: 1);

        Assert.Equal(1, result.ActionCount);
        Assert.Equal(UiTestScenario.TargetActionId, result.ActionId);
        Assert.Equal(UiTestScenario.TargetActionName, result.ActionName);
        Assert.Equal(UiTestScenario.TargetOutputs, result.ActionOutputs);

        Assert.Equal(UiTestScenario.G13ControlId, result.G13BindingControlId);
        Assert.Equal(UiTestScenario.BaseLayerId, result.G13BindingLayerId);
        Assert.Equal(UiTestScenario.G600ControlId, result.G600BindingControlId);
        Assert.Equal(UiTestScenario.BaseLayerId, result.G600BindingLayerId);
    }

    [Fact]
    public void Compile_is_valid_and_the_bound_controls_raise_no_unconfirmed_control_warning()
    {
        var result = UiTestScenario.Run(new FakeWorkspaceEditorIntents(), g13ConnectedCount: 1, g600ConnectedCount: 1);

        Assert.True(result.CompileIsValid);
        Assert.Null(result.CompileErrorMessage);
        Assert.Equal(2, result.CompileProfileCount); // G13 + G600

        // 既定 draft layout（WorkspaceDocumentEditor.CreateDraft）は G13 の M1/M2/M3 を layer selector
        // として持ち、これらは実測根拠が「強い推定」（G13Controls.ConfirmedButtons に非収録）のため
        // 未確認 control 警告が出る——シナリオが選んだ binding 先（G1・G9）はどちらも確認済みなので
        // この3件以外の警告は出ない。
        Assert.Equal(3, result.CompileWarnings.Count);
        Assert.All(result.CompileWarnings, warning => Assert.Contains("未確認 control: G13", warning));
        Assert.DoesNotContain(result.CompileWarnings, warning => warning.Contains("'G1'") || warning.Contains("'G9'"));
    }

    [Fact]
    public void Save_succeeds_and_apply_status_stages_reflect_the_saved_revision()
    {
        var result = UiTestScenario.Run(new FakeWorkspaceEditorIntents(), g13ConnectedCount: 1, g600ConnectedCount: 1);

        Assert.Equal(1, result.SaveRevisionNumber); // 新規 workspace への最初の保存
        Assert.Equal("revision 1", result.AppliedRevisionLabelAfterSave);
        Assert.NotEmpty(result.SaveStageCells);
        // 段階セルは WorkspaceApplyReport（Profiles）の語彙をそのまま写す——保存段階は「成立」で
        // revision 番号を含み、runtime 適用段階は「保存していない」を意味する「未実施」ではない
        // （dry-run ではなく実際に保存済みであることの確認）。
        var saveStage = Assert.Single(result.SaveStageCells, stage => stage.Stage == "保存（revision）");
        Assert.Equal("成立", saveStage.State);
        Assert.Contains("revision 1", saveStage.Detail);

        var runtimeStage = Assert.Single(result.SaveStageCells, stage => stage.Stage == "runtime 適用");
        Assert.NotEqual("未実施", runtimeStage.State);
    }

    [Fact]
    public void Device_connection_labels_reflect_the_given_connected_counts()
    {
        var connected = UiTestScenario.Run(new FakeWorkspaceEditorIntents(), g13ConnectedCount: 1, g600ConnectedCount: 0);

        Assert.Contains("G13: 接続中（1 台）", connected.DeviceConnectionLabels);
        Assert.Contains("G600: 未接続（編集は可）", connected.DeviceConnectionLabels);
    }

    [Fact]
    public void Running_the_scenario_twice_on_independent_fakes_produces_identical_comparable_results()
    {
        // fake 同士（どちらも in-memory・まっさらな状態）は完全一致するはず——
        // これは comparer が「本当に一致する2つ」を誤検出しないことの回帰確認。
        var first = UiTestScenario.Run(new FakeWorkspaceEditorIntents(), g13ConnectedCount: 1, g600ConnectedCount: 1);
        var second = UiTestScenario.Run(new FakeWorkspaceEditorIntents(), g13ConnectedCount: 1, g600ConnectedCount: 1);

        var comparison = UiTestScenarioComparer.Compare(first, second);

        Assert.True(comparison.IsMatch);
        Assert.Empty(comparison.Mismatches);
    }
}

/// <summary>UiTestScenarioComparer 自体の focused test（除外 field を尊重しつつ、それ以外の不一致は見逃さない）。</summary>
public sealed class UiTestScenarioComparerTests
{
    private static UiTestScenarioResult BaseResult() => UiTestScenario.Run(
        new FakeWorkspaceEditorIntents(), g13ConnectedCount: 1, g600ConnectedCount: 1);

    [Fact]
    public void Identical_results_match_with_no_mismatches()
    {
        var result = BaseResult();

        var comparison = UiTestScenarioComparer.Compare(result, result);

        Assert.True(comparison.IsMatch);
        Assert.Empty(comparison.Mismatches);
    }

    [Fact]
    public void Different_device_connection_labels_are_excluded_and_do_not_cause_a_mismatch()
    {
        var fake = BaseResult();
        var real = fake with { DeviceConnectionLabels = ["G13: 接続中（3 台）", "G600: 接続中（2 台）"] };

        var comparison = UiTestScenarioComparer.Compare(fake, real);

        Assert.True(comparison.IsMatch);
        Assert.Empty(comparison.Mismatches);
        Assert.Single(comparison.ExcludedFields);
    }

    [Fact]
    public void A_genuine_difference_in_a_non_excluded_field_is_reported_as_a_mismatch()
    {
        var fake = BaseResult();
        var real = fake with { ActionName = "違う名前" };

        var comparison = UiTestScenarioComparer.Compare(fake, real);

        Assert.False(comparison.IsMatch);
        Assert.Contains(comparison.Mismatches, mismatch => mismatch.StartsWith("ActionName:"));
    }
}
