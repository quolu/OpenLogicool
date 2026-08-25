using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Host;

public enum KnownGoalActionSelectionKind
{
    UseKnown,
    MissingSavedButton,
    PreviousTransitionUnconfirmed,
    AmbiguousSavedButton,
}

public sealed record KnownGoalActionSelection(
    KnownGoalActionSelectionKind Kind,
    LearnedAffordanceSignature? Action,
    string Reason);

public static class KnownGoalActionSelector
{
    public static KnownGoalActionSelection Select(
        LearnedStateSceneSignature state,
        string goal,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var ranked = state.Affordances
            .Where(action => action.AllowedPrimitives.Contains(operation, StringComparer.Ordinal))
            .Select(action => new
            {
                Action = action,
                Score = Similarity(goal, action.Text),
            })
            .Where(item => item.Score >= OcrTextMatcher.DefaultMinimumSimilarity)
            .OrderByDescending(item => item.Score)
            .ToArray();
        if (ranked.Length == 0)
        {
            return new(
                KnownGoalActionSelectionKind.MissingSavedButton,
                null,
                "現在ページに目的へ使える保存済みボタンがありません。");
        }
        if (ranked.Length > 1 && ranked[0].Score - ranked[1].Score < 0.08)
        {
            return new(
                KnownGoalActionSelectionKind.AmbiguousSavedButton,
                null,
                "目的へ使える保存済みボタンを一意に選べません。");
        }
        return new(
            KnownGoalActionSelectionKind.UseKnown,
            ranked[0].Action,
            "保存済みボタンをAIなしで実行します。");
    }

    private static double Similarity(string goal, string actionText)
    {
        var normalizedGoal = GoalCore(goal);
        var normalizedAction = OcrTextMatcher.Normalize(actionText);
        return normalizedGoal.Contains(normalizedAction, StringComparison.Ordinal)
            || normalizedAction.Contains(normalizedGoal, StringComparison.Ordinal)
                ? 1
                : OcrTextMatcher.Similarity(normalizedGoal, normalizedAction);
    }

    private static string GoalCore(string goal)
    {
        var normalized = OcrTextMatcher.Normalize(goal);
        foreach (var suffix in new[] { "を開く", "へ移動する", "へ戻る", "を指す", "を押す", "を選ぶ", "開く", "戻る" })
        {
            var normalizedSuffix = OcrTextMatcher.Normalize(suffix);
            if (normalized.EndsWith(normalizedSuffix, StringComparison.Ordinal))
            {
                return normalized[..^normalizedSuffix.Length];
            }
        }
        return normalized;
    }
}
