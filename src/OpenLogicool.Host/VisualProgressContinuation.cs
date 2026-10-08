namespace OpenLogicool.Host;

/// <summary>確認通知と操作規則の実行を分離する。</summary>
internal static class VisualProgressContinuation
{
    public static VisualProgressChoice AfterReview(VisualProgressChoice candidate, bool continueRules) =>
        continueRules && candidate.Action == VisualProgressAction.Normal
            ? new(VisualProgressAction.Normal)
            : new(VisualProgressAction.Wait);

    public static bool RequiresUnchangedReview(bool continueRules, bool hudVisible) =>
        !continueRules || !hudVisible;
}
