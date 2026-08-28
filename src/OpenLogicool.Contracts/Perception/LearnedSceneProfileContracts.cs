namespace OpenLogicool.Contracts.Perception;

public sealed record LearnedSceneAnchor(
    string Text,
    IReadOnlyList<double> NormalizedBounds,
    string EvidenceId,
    IReadOnlyList<string>? PreviousTexts = null);

public sealed record LearnedAffordanceSignature(
    string CandidateId,
    string LocatorRevision,
    string Text,
    IReadOnlyList<double> NormalizedBounds,
    IReadOnlyList<string> AllowedPrimitives,
    IReadOnlyList<string> EvidenceIds,
    string? DestinationStateId = null,
    VisualPatchSignature? VisualPatch = null,
    IReadOnlyList<string>? KeyTokens = null,
    int? VerticalScrollSteps = null,
    int? HorizontalScrollSteps = null,
    IReadOnlyList<double>? DragDestinationNormalized = null,
    IReadOnlyList<string>? PreviousTexts = null);

public sealed record LearnedStateSceneSignature(
    string StateId,
    string SignatureVersion,
    IReadOnlyList<LearnedSceneAnchor> Anchors,
    IReadOnlyList<LearnedAffordanceSignature> Affordances,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string>? SupersedesStateIds = null,
    VisualPatchSignature? VisualPatch = null);

/// <summary>探索で得た画面同定規則をcodeではなくruntime dataとして保持する。</summary>
public sealed record LearnedSceneProfileDocument(
    string SchemaVersion,
    string ProfileId,
    string ProfileVersion,
    string GameId,
    string EnvironmentScope,
    string ProcessName,
    string? WindowTitle,
    long MaximumFrameFreshnessMilliseconds,
    double NormalizedPositionTolerance,
    IReadOnlyList<LearnedStateSceneSignature> States,
    IReadOnlyList<string> EvidenceIds);

public interface ILearnedSceneProfileStore
{
    void Upsert(LearnedSceneProfileDocument document);

    LearnedSceneProfileDocument? Load(string gameId, string environmentScope);
}

public static class LearnedSceneProfileValidator
{
    public static void Validate(LearnedSceneProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SchemaVersion != "0.3.0"
            || string.IsNullOrWhiteSpace(profile.ProfileId)
            || string.IsNullOrWhiteSpace(profile.ProfileVersion)
            || string.IsNullOrWhiteSpace(profile.GameId)
            || string.IsNullOrWhiteSpace(profile.EnvironmentScope)
            || string.IsNullOrWhiteSpace(profile.ProcessName)
            || profile.MaximumFrameFreshnessMilliseconds < 0
            || profile.NormalizedPositionTolerance is <= 0 or > 0.25
            || profile.States is null
            || profile.States.Count == 0
            || profile.States.Select(state => state.StateId).Distinct(StringComparer.Ordinal).Count() != profile.States.Count
            || profile.States.Any(InvalidState)
            || profile.States.Any(state => state.SupersedesStateIds is not null
                && (state.SupersedesStateIds.Any(string.IsNullOrWhiteSpace)
                    || state.SupersedesStateIds.Contains(state.StateId, StringComparer.Ordinal)
                    || state.SupersedesStateIds.Distinct(StringComparer.Ordinal).Count() != state.SupersedesStateIds.Count
                    || state.SupersedesStateIds.Any(id => profile.States.All(candidate => candidate.StateId != id))))
            || profile.EvidenceIds is null
            || profile.EvidenceIds.Count == 0
            || profile.EvidenceIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("学習済みscene profileの必須fieldまたはschemaが不正です。", nameof(profile));
        }
    }

    private static bool InvalidState(LearnedStateSceneSignature state) =>
        state is null
        || string.IsNullOrWhiteSpace(state.StateId)
        || string.IsNullOrWhiteSpace(state.SignatureVersion)
        || state.Anchors is null
        || state.Anchors.Count == 0
            && state.VisualPatch is null
            && !state.Affordances.Any(affordance => affordance.VisualPatch is not null)
        || state.Anchors.Any(anchor => string.IsNullOrWhiteSpace(anchor.Text)
            || string.IsNullOrWhiteSpace(anchor.EvidenceId)
            || anchor.PreviousTexts is not null && anchor.PreviousTexts.Any(string.IsNullOrWhiteSpace)
            || !ValidBounds(anchor.NormalizedBounds))
        || state.Anchors.Select(anchor => Normalize(anchor.Text)).Distinct(StringComparer.Ordinal).Count() != state.Anchors.Count
        || state.Affordances is null
        || state.Affordances.Any(affordance => string.IsNullOrWhiteSpace(affordance.CandidateId)
            || string.IsNullOrWhiteSpace(affordance.LocatorRevision)
            || string.IsNullOrWhiteSpace(affordance.Text)
            || !ValidBounds(affordance.NormalizedBounds)
            || affordance.AllowedPrimitives is null
            || affordance.AllowedPrimitives.Count == 0
            || affordance.AllowedPrimitives.Any(string.IsNullOrWhiteSpace)
            || affordance.EvidenceIds is null
            || affordance.EvidenceIds.Count == 0
            || affordance.PreviousTexts is not null && affordance.PreviousTexts.Any(string.IsNullOrWhiteSpace)
            || InvalidOperationParameters(affordance))
        || state.EvidenceIds is null
        || state.EvidenceIds.Count == 0;

    private static bool InvalidOperationParameters(LearnedAffordanceSignature affordance)
    {
        var operation = affordance.AllowedPrimitives.Count == 1
            ? affordance.AllowedPrimitives[0]
            : null;
        return operation == "key-tap"
                && (affordance.KeyTokens is null
                    || affordance.KeyTokens.Count == 0
                    || affordance.KeyTokens.Any(string.IsNullOrWhiteSpace))
            || operation == "scroll"
                && affordance.VerticalScrollSteps.GetValueOrDefault() == 0
                && affordance.HorizontalScrollSteps.GetValueOrDefault() == 0
            || operation == "drag"
                && !ValidPoint(affordance.DragDestinationNormalized);
    }

    private static bool ValidPoint(IReadOnlyList<double>? point) =>
        point is { Count: 2 }
        && point.All(double.IsFinite)
        && point.All(value => value is >= 0 and <= 1);

    private static bool ValidBounds(IReadOnlyList<double>? bounds) =>
        bounds is { Count: 4 }
        && bounds.All(double.IsFinite)
        && bounds[0] is >= 0 and <= 1
        && bounds[1] is >= 0 and <= 1
        && bounds[2] is > 0 and <= 1
        && bounds[3] is > 0 and <= 1
        && bounds[0] + bounds[2] <= 1.000001
        && bounds[1] + bounds[3] <= 1.000001;

    private static string Normalize(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
}
