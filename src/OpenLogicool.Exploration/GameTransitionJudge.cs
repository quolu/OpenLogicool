using System.Security.Cryptography;
using System.Text;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Exploration;

public sealed record GameSceneSemanticSignature(
    StateIdentityStatus StateIdentity,
    IReadOnlyList<string> StateIds,
    IReadOnlyList<string> AffordanceKeys)
{
    public bool HasEvidence => StateIds.Count > 0 || AffordanceKeys.Count > 0;
}

/// <summary>raw pixelを使わず、認識済みstateとactionable structureだけを比較する。</summary>
public static class GameSceneSemanticComparer
{
    public static GameSceneSemanticSignature Signature(ObservedScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var stateIds = scene.StateCandidates
            .Select(candidate => candidate.StateId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var affordances = scene.Affordances
            .Where(candidate => !string.Equals(candidate.SemanticKind, "probe-target", StringComparison.Ordinal))
            .Select(TargetKey)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new GameSceneSemanticSignature(scene.StateIdentity, stateIds, affordances);
    }

    public static bool Equivalent(
        GameSceneSemanticSignature left,
        GameSceneSemanticSignature right) =>
        left.StateIdentity == right.StateIdentity
        && left.StateIds.SequenceEqual(right.StateIds, StringComparer.Ordinal)
        && left.AffordanceKeys.SequenceEqual(right.AffordanceKeys, StringComparer.Ordinal);

    public static bool StableEquivalent(
        GameSceneSemanticSignature left,
        GameSceneSemanticSignature right) =>
        left.StateIdentity == right.StateIdentity
        && left.StateIds.SequenceEqual(right.StateIds, StringComparer.Ordinal)
        && (left.StateIdentity == StateIdentityStatus.Known && left.StateIds.Count > 0
            || AffordancesStableEquivalent(left.AffordanceKeys, right.AffordanceKeys));

    private static bool AffordancesStableEquivalent(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        if (AffordanceSetsEquivalent(left, right))
        {
            return true;
        }
        var smallerCount = Math.Min(left.Count, right.Count);
        if (smallerCount < 3)
        {
            return false;
        }
        var common = CommonAffordanceCount(left, right);
        return common / (double)smallerCount >= 0.8 && common >= 2;
    }

    private static bool AffordanceSetsEquivalent(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Count == right.Count && CommonAffordanceCount(left, right) == left.Count;

    private static int CommonAffordanceCount(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        var remaining = right.ToList();
        var common = 0;
        foreach (var leftKey in left)
        {
            var index = remaining.FindIndex(rightKey => AffordanceKeySimilar(leftKey, rightKey));
            if (index < 0)
            {
                continue;
            }
            common++;
            remaining.RemoveAt(index);
        }
        return common;
    }

    public static bool AffordanceKeySimilar(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }
        var leftParts = left.Split('|');
        var rightParts = right.Split('|');
        return leftParts.Length == 4
            && rightParts.Length == 4
            && string.Equals(leftParts[0], "ocr-text", StringComparison.Ordinal)
            && string.Equals(rightParts[0], "ocr-text", StringComparison.Ordinal)
            && string.Equals(leftParts[2], rightParts[2], StringComparison.Ordinal)
            && string.Equals(leftParts[3], rightParts[3], StringComparison.Ordinal)
            && OcrTextMatcher.IsSimilar(leftParts[1], rightParts[1]);
    }

    public static string SignatureId(ObservedScene scene)
    {
        var signature = Signature(scene);
        var material = string.Join(
            '\n',
            signature.StateIdentity.ToString(),
            string.Join('\u001f', signature.StateIds),
            string.Join('\u001f', signature.AffordanceKeys));
        return $"scene-signature:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()}";
    }

    public static string TargetKey(AffordanceCandidate candidate)
    {
        var bounds = candidate.Locator.NormalizedBounds;
        var centerXBand = Band(bounds[0] + bounds[2] / 2);
        var centerYBand = Band(bounds[1] + bounds[3] / 2);
        return string.Join(
            '|',
            candidate.SemanticKind ?? candidate.Locator.LocatorType,
            NormalizeSemanticLabel(candidate.SemanticLabel),
            centerXBand,
            centerYBand);
    }

    private static string NormalizeSemanticLabel(string? value) =>
        value is null
            ? "(unlabelled)"
            : string.Concat(value
                .Normalize(NormalizationForm.FormKC)
                .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character)))
                .ToUpperInvariant();

    private static int Band(double normalized) =>
        Math.Clamp((int)Math.Floor(normalized * 4), 0, 3);
}

/// <summary>意味構造が必要frame数と時間の両方で連続したかを判定するpure window。</summary>
public sealed class GameSceneStabilityWindow(ExplorationWaitCondition condition)
{
    private GameSceneSemanticSignature? current;
    private long stableStartedMilliseconds;
    private int stableFrames;

    public bool Observe(ObservedScene scene, long elapsedMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (elapsedMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
        }
        if (scene.CaptureAvailability != CaptureAvailability.Available)
        {
            Reset();
            return false;
        }
        var signature = GameSceneSemanticComparer.Signature(scene);
        if (!signature.HasEvidence)
        {
            return false;
        }
        if (current is null || !GameSceneSemanticComparer.StableEquivalent(current, signature))
        {
            current = signature;
            stableStartedMilliseconds = elapsedMilliseconds;
            stableFrames = 1;
            return condition.StableFrames <= 1 && condition.MinimumStableMilliseconds <= 0;
        }
        stableFrames++;
        return stableFrames >= condition.StableFrames
            && elapsedMilliseconds - stableStartedMilliseconds >= condition.MinimumStableMilliseconds;
    }

    public int StableFramesObserved => stableFrames;

    public long StableMillisecondsObserved(long elapsedMilliseconds) =>
        current is null ? 0 : Math.Max(0, elapsedMilliseconds - stableStartedMilliseconds);

    private void Reset()
    {
        current = null;
        stableStartedMilliseconds = 0;
        stableFrames = 0;
    }
}

public sealed class GameTransitionJudge
{
    public GameTransitionComparison Compare(
        ObservedScene before,
        GameInteractionStabilityResult after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (after.Status != GameInteractionStabilityStatus.Stable || after.StableScene is null)
        {
            return Undetermined(before, after.StableScene, $"stability:{after.Status}");
        }
        var stable = after.StableScene;
        if (!string.Equals(before.Frame.SourceId, stable.Frame.SourceId, StringComparison.Ordinal)
            || before.Frame.Backend != stable.Frame.Backend
            || before.Frame.TransformRevision != stable.Frame.TransformRevision)
        {
            return Undetermined(before, stable, "capture binding changed");
        }
        if (before.StateIdentity == StateIdentityStatus.Ambiguous
            || stable.StateIdentity == StateIdentityStatus.Ambiguous
            || before.StateIdentity == StateIdentityStatus.InsufficientEvidence
            || stable.StateIdentity == StateIdentityStatus.InsufficientEvidence)
        {
            return Undetermined(before, stable, "state identity is ambiguous or insufficient");
        }
        var beforeSignature = GameSceneSemanticComparer.Signature(before);
        var afterSignature = GameSceneSemanticComparer.Signature(stable);
        if (!beforeSignature.HasEvidence || !afterSignature.HasEvidence)
        {
            return Undetermined(before, stable, "semantic evidence is empty");
        }
        if (GameSceneSemanticComparer.Equivalent(beforeSignature, afterSignature))
        {
            return new GameTransitionComparison(
                ContractSchemaVersions.Revision03,
                before.ObservationId,
                stable.ObservationId,
                GameTransitionJudgement.Stayed,
                [],
                ["state候補とactionable structureが同一"]);
        }
        if (before.SceneVisualPatch is not null
            && stable.SceneVisualPatch is not null
            && VisualPatchSignatureComparer.MeanAbsoluteDifference(before.SceneVisualPatch, stable.SceneVisualPatch)
                < MinimumVisualDifference(before))
        {
            return new GameTransitionComparison(
                ContractSchemaVersions.Revision03,
                before.ObservationId,
                stable.ObservationId,
                GameTransitionJudgement.Stayed,
                [],
                ["OCR構造差に対して全画面visual差が小さい"]);
        }
        var changedKeyCount = ChangedAffordanceCount(beforeSignature.AffordanceKeys, afterSignature.AffordanceKeys);
        if (beforeSignature.StateIds.SequenceEqual(afterSignature.StateIds, StringComparer.Ordinal)
            && Math.Min(beforeSignature.AffordanceKeys.Count, afterSignature.AffordanceKeys.Count) >= 3
            && changedKeyCount < 4)
        {
            return new GameTransitionComparison(
                ContractSchemaVersions.Revision03,
                before.ObservationId,
                stable.ObservationId,
                GameTransitionJudgement.Stayed,
                [],
                ["単一のOCR構造差はページ遷移に使わない"]);
        }
        var changed = ChangedRegions(before, stable);
        return new GameTransitionComparison(
            ContractSchemaVersions.Revision03,
            before.ObservationId,
            stable.ObservationId,
            GameTransitionJudgement.Moved,
            changed,
            ["state候補またはactionable structureが変化"]);
    }

    private static double MinimumVisualDifference(ObservedScene before)
    {
        var operation = before.Affordances
            .SelectMany(candidate => candidate.AllowedPrimitives)
            .FirstOrDefault(GameInteractionOperations.InputOperations.Contains);
        return operation is GameInteractionOperations.Hover or GameInteractionOperations.Scroll or GameInteractionOperations.Drag
            ? 1
            : 6;
    }

    private static int ChangedAffordanceCount(
        IReadOnlyList<string> before,
        IReadOnlyList<string> after) =>
        before.Count(key => !after.Any(other => GameSceneSemanticComparer.AffordanceKeySimilar(key, other)))
        + after.Count(key => !before.Any(other => GameSceneSemanticComparer.AffordanceKeySimilar(key, other)));

    private static GameTransitionComparison Undetermined(
        ObservedScene before,
        ObservedScene? after,
        string reason) =>
        new(
            ContractSchemaVersions.Revision03,
            before.ObservationId,
            after?.ObservationId,
            GameTransitionJudgement.Undetermined,
            [],
            [reason]);

    private static IReadOnlyList<EvidenceRegion> ChangedRegions(
        ObservedScene before,
        ObservedScene after)
    {
        var beforeKeys = before.Affordances.Select(GameSceneSemanticComparer.TargetKey).ToArray();
        var afterKeys = after.Affordances.Select(GameSceneSemanticComparer.TargetKey).ToArray();
        return before.Affordances
            .Where(candidate => !afterKeys.Any(key => GameSceneSemanticComparer.AffordanceKeySimilar(
                GameSceneSemanticComparer.TargetKey(candidate), key)))
            .SelectMany(candidate => candidate.EvidenceRegions)
            .Concat(after.Affordances
                .Where(candidate => !beforeKeys.Any(key => GameSceneSemanticComparer.AffordanceKeySimilar(
                    GameSceneSemanticComparer.TargetKey(candidate), key)))
                .SelectMany(candidate => candidate.EvidenceRegions))
            .ToArray();
    }
}
