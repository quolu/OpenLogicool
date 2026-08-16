using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Profiles;

/// <summary>
/// undo の対象 revision 選択（pure・MAP-009）。
/// undo は履歴を巻き戻さず、選んだ過去 revision を新 revision として再適用する（append-only）。
/// 番号未指定なら最新の一つ前。連続で遡る場合は番号を明示する（append-only のため、
/// 無指定の連続 undo は直前の undo 自体を打ち消す）。
/// </summary>
public static class WorkspaceUndo
{
    public static WorkspaceRevisionRecord SelectTarget(
        IReadOnlyList<WorkspaceRevisionRecord> revisions,
        long? requestedRevisionNumber)
    {
        if (revisions.Count == 0)
        {
            throw new InvalidOperationException("workspace に保存済み revision がありません。");
        }

        if (requestedRevisionNumber is { } number)
        {
            return revisions.SingleOrDefault(revision => revision.RevisionNumber == number)
                ?? throw new InvalidOperationException(
                    $"revision {number} は存在しません（保存済み: {revisions[0].RevisionNumber}〜{revisions[^1].RevisionNumber}）。");
        }

        if (revisions.Count < 2)
        {
            throw new InvalidOperationException(
                "revision が1件だけのため、戻る先がありません（undo は最新の一つ前へ戻します）。");
        }

        return revisions[^2];
    }
}
