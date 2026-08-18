namespace OpenLogicool.Profiles;

/// <summary>workspace 適用の1段階の状態（APP-007）。State は「成立」以外を成功と読ませない語だけを使う。</summary>
public sealed record WorkspaceStageStatus(
    string Stage,
    string State,
    string Detail);

/// <summary>
/// workspace 適用の段階別報告（pure・APP-007 の機能中核）。
/// 編集（compile）・保存（revision）・runtime 適用・device 反映を別状態で表し、
/// 保存成功を「適用完了」と表示しない（部分成功を一括成功と表示しない）。
/// </summary>
public static class WorkspaceApplyReport
{
    /// <param name="savedRevisionNumber">保存した revision 番号。dry-run 等で保存していなければ null。</param>
    /// <param name="hostResident">常駐 host が起動中か（named mutex の観測結果）。</param>
    public static IReadOnlyList<WorkspaceStageStatus> Build(long? savedRevisionNumber, bool hostResident)
    {
        var stages = new List<WorkspaceStageStatus>
        {
            new("編集（compile）", "成立", "workspace 文書は検証済み（構造エラーなし）"),
        };

        if (savedRevisionNumber is null)
        {
            stages.Add(new("保存（revision）", "未実施", "書き込みなし（dry-run）"));
            stages.Add(new("runtime 適用", "未実施", "保存していないため対象外"));
        }
        else
        {
            stages.Add(new("保存（revision）", "成立", $"revision {savedRevisionNumber} として保存済み"));
            stages.Add(hostResident
                ? new("runtime 適用", "未反映", "常駐 host は起動時の構成のまま——反映には host の再起動が必要")
                : new("runtime 適用", "未適用", "host 非常駐——次回起動時に保存済み構成を読み込む"));
        }

        stages.Add(new(
            "device 反映",
            "対象外",
            "保存では書かない（MAP-010）。常駐開始時に本体の出荷割当を無効化する"));
        return stages;
    }
}
