using OpenLogicool.Desktop;

namespace OpenLogicool.Host;

/// <summary>fake と real、それぞれの <see cref="UiTestScenarioResult"/> を突き合わせた結果。
/// <see cref="ExcludedFields"/> は比較対象から明示的に除外した field 名と、なぜ除外するかを一覧できる
/// ようにするための一覧（黙って丸めない）。</summary>
public sealed record UiTestScenarioComparison(
    bool IsMatch,
    IReadOnlyList<string> Mismatches,
    IReadOnlyList<string> ExcludedFields);

/// <summary>
/// t10: fake（<see cref="FakeWorkspaceEditorIntents"/>）と real（<see cref="HostWorkspaceEditorIntents"/>）
/// で同一 <see cref="UiTestScenario"/> を実行した結果を機械的に突き合わせる。
/// <see cref="UiTestScenarioResult.DeviceConnectionLabels"/> だけは実機接続台数（列挙結果）という
/// 環境依存値のため比較対象から除外し、それ以外の全 field は一致を要求する。
/// </summary>
public static class UiTestScenarioComparer
{
    public static readonly IReadOnlyList<string> ExcludedFields =
        [$"{nameof(UiTestScenarioResult.DeviceConnectionLabels)}（実機接続台数は環境依存のため除外）"];

    public static UiTestScenarioComparison Compare(UiTestScenarioResult fake, UiTestScenarioResult real)
    {
        var mismatches = new List<string>();

        void Check<T>(string field, T fakeValue, T realValue)
        {
            if (!Equals(fakeValue, realValue))
            {
                mismatches.Add($"{field}: fake='{fakeValue}' / real='{realValue}'");
            }
        }

        void CheckSequence(string field, IReadOnlyList<string> fakeValue, IReadOnlyList<string> realValue)
        {
            if (!fakeValue.SequenceEqual(realValue))
            {
                mismatches.Add(
                    $"{field}: fake=[{string.Join(", ", fakeValue)}] / real=[{string.Join(", ", realValue)}]");
            }
        }

        Check(nameof(UiTestScenarioResult.DefaultEditingLabel), fake.DefaultEditingLabel, real.DefaultEditingLabel);
        Check(nameof(UiTestScenarioResult.SelectedEditingLabelAfterAppSelect), fake.SelectedEditingLabelAfterAppSelect, real.SelectedEditingLabelAfterAppSelect);
        Check(nameof(UiTestScenarioResult.SelectedApplicationFullPath), fake.SelectedApplicationFullPath, real.SelectedApplicationFullPath);
        Check(nameof(UiTestScenarioResult.ActionCount), fake.ActionCount, real.ActionCount);
        Check(nameof(UiTestScenarioResult.ActionId), fake.ActionId, real.ActionId);
        Check(nameof(UiTestScenarioResult.ActionName), fake.ActionName, real.ActionName);
        CheckSequence(nameof(UiTestScenarioResult.ActionOutputs), fake.ActionOutputs, real.ActionOutputs);
        Check(nameof(UiTestScenarioResult.G13BindingControlId), fake.G13BindingControlId, real.G13BindingControlId);
        Check(nameof(UiTestScenarioResult.G13BindingLayerId), fake.G13BindingLayerId, real.G13BindingLayerId);
        Check(nameof(UiTestScenarioResult.G600BindingControlId), fake.G600BindingControlId, real.G600BindingControlId);
        Check(nameof(UiTestScenarioResult.G600BindingLayerId), fake.G600BindingLayerId, real.G600BindingLayerId);
        Check(nameof(UiTestScenarioResult.CompileIsValid), fake.CompileIsValid, real.CompileIsValid);
        Check(nameof(UiTestScenarioResult.CompileProfileCount), fake.CompileProfileCount, real.CompileProfileCount);
        CheckSequence(nameof(UiTestScenarioResult.CompileWarnings), fake.CompileWarnings, real.CompileWarnings);
        Check(nameof(UiTestScenarioResult.CompileErrorMessage), fake.CompileErrorMessage, real.CompileErrorMessage);
        Check(nameof(UiTestScenarioResult.SaveRevisionNumber), fake.SaveRevisionNumber, real.SaveRevisionNumber);

        if (!fake.SaveStageCells.SequenceEqual(real.SaveStageCells))
        {
            mismatches.Add(
                $"{nameof(UiTestScenarioResult.SaveStageCells)}: " +
                $"fake=[{string.Join(" | ", fake.SaveStageCells)}] / real=[{string.Join(" | ", real.SaveStageCells)}]");
        }

        Check(nameof(UiTestScenarioResult.AppliedRevisionLabelAfterSave), fake.AppliedRevisionLabelAfterSave, real.AppliedRevisionLabelAfterSave);
        Check(nameof(UiTestScenarioResult.EditingLabelAfterSave), fake.EditingLabelAfterSave, real.EditingLabelAfterSave);

        return new UiTestScenarioComparison(mismatches.Count == 0, mismatches, ExcludedFields);
    }
}
