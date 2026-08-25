using System.Security.Cryptography;
using System.Text;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Exploration;

public sealed record KnownScreenActionReference(
    string PageStateId,
    string ActionId,
    string? DestinationStateId);

public interface IIncrementalKnownScreenIndex
{
    KnownScreenActionReference RememberControl(
        ObservedScene page,
        AffordanceCandidate control,
        string evidenceId);

    KnownScreenActionReference RememberControl(
        IReadOnlyList<ObservedScene> pageSamples,
        AffordanceCandidate control,
        string evidenceId);

    KnownScreenActionReference RememberDestination(
        ObservedScene before,
        AffordanceCandidate control,
        IReadOnlyList<ObservedScene> destinationSamples,
        string evidenceId);

    KnownScreenActionReference RememberDestination(
        IReadOnlyList<ObservedScene> beforeSamples,
        AffordanceCandidate control,
        IReadOnlyList<ObservedScene> destinationSamples,
        string evidenceId);
}

/// <summary>目的達成中に見つけた一つのcontrolだけを、AIなし再照合用profileへ逐次追記する。</summary>
public sealed class IncrementalKnownScreenIndex(
    ILearnedSceneProfileStore store,
    string gameId,
    string environmentScope,
    string processName,
    string? windowTitle = null) : IIncrementalKnownScreenIndex
{
    public KnownScreenActionReference RememberControl(
        ObservedScene page,
        AffordanceCandidate control,
        string evidenceId) => RememberControl([page], control, evidenceId);

    public KnownScreenActionReference RememberControl(
        IReadOnlyList<ObservedScene> pageSamples,
        AffordanceCandidate control,
        string evidenceId)
    {
        ArgumentNullException.ThrowIfNull(pageSamples);
        if (pageSamples.Count == 0)
        {
            throw new ArgumentException("source索引には1件以上の観測が必要です。", nameof(pageSamples));
        }
        var page = pageSamples[0];
        Validate(page, control, evidenceId);
        var document = store.Load(gameId, environmentScope) ?? Empty(evidenceId);
        var (state, states) = EnsureState(document.States, pageSamples, evidenceId, control);
        var primitive = control.AllowedPrimitives.Contains(GameInteractionOperations.Click, StringComparer.Ordinal)
            ? GameInteractionOperations.Click
            : control.AllowedPrimitives.First();
        var existingAction = state.Affordances.SingleOrDefault(action =>
            action.AllowedPrimitives.Contains(primitive, StringComparer.Ordinal)
            && OcrTextMatcher.IsSimilar(action.Text, control.SemanticLabel ?? string.Empty)
            && PositionMatches(action.NormalizedBounds, control.Locator.NormalizedBounds));
        var action = existingAction is null
            ? Action(state.StateId, control, evidenceId, destinationStateId: null)
            : existingAction with
            {
                Text = OcrTextMatcher.PreferObserved(existingAction.Text, control.SemanticLabel!)
                    ? control.SemanticLabel!
                    : existingAction.Text,
                EvidenceIds = existingAction.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
                PreviousTexts = OcrTextMatcher.PreferObserved(existingAction.Text, control.SemanticLabel!)
                    ? (existingAction.PreviousTexts ?? []).Append(existingAction.Text).Distinct(StringComparer.Ordinal).ToArray()
                    : existingAction.PreviousTexts,
            };
        var merged = state with
        {
            Affordances = state.Affordances
                .Where(existing => !string.Equals(existing.CandidateId, action.CandidateId, StringComparison.Ordinal))
                .Append(action)
                .ToArray(),
            EvidenceIds = state.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
        };
        store.Upsert(document with
        {
            States = Replace(states, merged),
            EvidenceIds = document.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
        });
        return new KnownScreenActionReference(state.StateId, action.CandidateId, null);
    }

    public KnownScreenActionReference RememberDestination(
        ObservedScene before,
        AffordanceCandidate control,
        IReadOnlyList<ObservedScene> destinationSamples,
        string evidenceId) => RememberDestination([before], control, destinationSamples, evidenceId);

    public KnownScreenActionReference RememberDestination(
        IReadOnlyList<ObservedScene> beforeSamples,
        AffordanceCandidate control,
        IReadOnlyList<ObservedScene> destinationSamples,
        string evidenceId)
    {
        ArgumentNullException.ThrowIfNull(beforeSamples);
        ArgumentNullException.ThrowIfNull(destinationSamples);
        if (beforeSamples.Count == 0 || destinationSamples.Count == 0)
        {
            throw new ArgumentException("sourceと行き先索引には1件以上の安定観測が必要です。");
        }
        var source = RememberControl(beforeSamples, control, evidenceId);
        var document = store.Load(gameId, environmentScope)
            ?? throw new InvalidOperationException("保存直後のknown screen indexを読み出せません。");
        var (rawDestination, states) = EnsureState(document.States, destinationSamples, evidenceId);
        var sourceState = states.Single(state => state.StateId == source.PageStateId);
        var destination = rawDestination.StateId == sourceState.StateId
            ? rawDestination
            : rawDestination with
            {
                SupersedesStateIds = (rawDestination.SupersedesStateIds ?? [])
                    .Append(sourceState.StateId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
            };
        var action = sourceState.Affordances.Single(item => item.CandidateId == source.ActionId) with
        {
            DestinationStateId = destination.StateId,
            EvidenceIds = sourceState.Affordances.Single(item => item.CandidateId == source.ActionId)
                .EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
        };
        var updatedSource = sourceState with
        {
            Affordances = sourceState.Affordances
                .Where(item => item.CandidateId != source.ActionId)
                .Append(action)
                .ToArray(),
        };
        store.Upsert(document with
        {
            States = Replace(Replace(states, destination), updatedSource),
            EvidenceIds = document.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
        });
        return source with { DestinationStateId = destination.StateId };
    }

    private (LearnedStateSceneSignature State, IReadOnlyList<LearnedStateSceneSignature> States) EnsureState(
        IReadOnlyList<LearnedStateSceneSignature> states,
        ObservedScene scene,
        string evidenceId) => EnsureState(states, [scene], evidenceId, null);

    private (LearnedStateSceneSignature State, IReadOnlyList<LearnedStateSceneSignature> States) EnsureState(
        IReadOnlyList<LearnedStateSceneSignature> states,
        IReadOnlyList<ObservedScene> scenes,
        string evidenceId,
        AffordanceCandidate? visualStateControl = null)
    {
        var anchors = Anchors(scenes, evidenceId, visualStateControl?.VisualPatch is not null);
        var stateId = anchors.Count > 0
            ? StateId(anchors)
            : VisualStateId(visualStateControl!);
        var existing = states.SingleOrDefault(state => state.StateId == stateId)
            ?? states.SingleOrDefault(state => StateMatches(
                state,
                anchors,
                visualStateControl));
        if (existing is not null)
        {
            var refined = RefineExistingState(existing, anchors, evidenceId);
            return (refined, Replace(states, refined));
        }
        var created = new LearnedStateSceneSignature(
            stateId,
            "known-screen-index-v1",
            anchors,
            [],
            [evidenceId]);
        return (created, states.Append(created).ToArray());
    }

    private static bool StateMatches(
        LearnedStateSceneSignature state,
        IReadOnlyList<LearnedSceneAnchor> anchors,
        AffordanceCandidate? visualControl)
    {
        if (anchors.Count > 0 && state.Anchors.Count == anchors.Count)
        {
            return state.Anchors.All(saved => anchors.Any(observed =>
                OcrTextMatcher.IsSimilar(saved.Text, observed.Text)
                && PositionMatches(saved.NormalizedBounds, observed.NormalizedBounds)));
        }
        if (anchors.Count == 0 && visualControl is not null)
        {
            return state.Affordances.Any(action =>
                OcrTextMatcher.IsSimilar(action.Text, visualControl.SemanticLabel ?? string.Empty)
                && PositionMatches(action.NormalizedBounds, visualControl.Locator.NormalizedBounds));
        }
        return false;
    }

    private static LearnedStateSceneSignature RefineExistingState(
        LearnedStateSceneSignature state,
        IReadOnlyList<LearnedSceneAnchor> observed,
        string evidenceId)
    {
        var changed = false;
        var matches = state.Anchors.Select(saved => new
        {
            Saved = saved,
            Current = observed.FirstOrDefault(candidate =>
                OcrTextMatcher.IsSimilar(saved.Text, candidate.Text)
                && PositionMatches(saved.NormalizedBounds, candidate.NormalizedBounds)),
        }).ToArray();
        var proposedTexts = matches.Select(item =>
            item.Current is not null && OcrTextMatcher.PreferObserved(item.Saved.Text, item.Current.Text)
                ? item.Current.Text
                : item.Saved.Text).ToArray();
        var collided = proposedTexts
            .Select(OcrTextMatcher.Normalize)
            .GroupBy(text => text, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var anchors = matches.Select((item, index) =>
        {
            if (item.Current is null
                || !OcrTextMatcher.PreferObserved(item.Saved.Text, item.Current.Text)
                || collided.Contains(OcrTextMatcher.Normalize(proposedTexts[index])))
            {
                return item.Saved;
            }
            changed = true;
            return item.Saved with
            {
                Text = item.Current.Text,
                EvidenceId = evidenceId,
                PreviousTexts = (item.Saved.PreviousTexts ?? []).Append(item.Saved.Text).Distinct(StringComparer.Ordinal).ToArray(),
            };
        }).ToArray();
        return changed
            ? state with
            {
                Anchors = anchors,
                EvidenceIds = state.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
            }
            : state;
    }

    private static IReadOnlyList<LearnedSceneAnchor> Anchors(
        IReadOnlyList<ObservedScene> scenes,
        string evidenceId,
        bool allowVisualFallback)
    {
        var regions = scenes[0].DiscoveryEvidence?.LocalGroundingRegions
            ?? throw new InvalidOperationException("ページ索引には同一frameのlocal OCR regionが必要です。");
        var anchors = regions
            .Where(region => Normalize(region.Text).Length >= 2)
            .Where(region => Normalize(region.Text).Length <= 16)
            .Where(region => !region.Text.Any(char.IsDigit))
            .Where(region => !MixesAsciiAndNonAsciiLetters(region.Text))
            .Where(region => region.EvidenceRegion.NormalizedBounds[1] >= 0.03)
            .GroupBy(region => Normalize(region.Text), StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(region => region.EvidenceRegion.NormalizedBounds[3])
                .ThenByDescending(region => Area(region.EvidenceRegion.NormalizedBounds))
                .First())
            .Where(region => scenes.Skip(1).All(scene =>
                scene.DiscoveryEvidence?.LocalGroundingRegions?.Any(other =>
                    OcrTextMatcher.IsSimilar(other.Text, region.Text)
                    && PositionMatches(
                        region.EvidenceRegion.NormalizedBounds,
                        other.EvidenceRegion.NormalizedBounds)) == true))
            .OrderByDescending(region => ContainsNonAsciiLetter(region.Text))
            .ThenByDescending(region => region.EvidenceRegion.NormalizedBounds[3])
            .ThenByDescending(region => Area(region.EvidenceRegion.NormalizedBounds))
            .ToArray();
        var selected = SelectSpatiallyDistinct(anchors, 2)
            .Select(region => new LearnedSceneAnchor(
                region.Text,
                region.EvidenceRegion.NormalizedBounds.ToArray(),
                evidenceId))
            .ToArray();
        if (selected.Length == 0)
        {
            if (allowVisualFallback)
            {
                return [];
            }
            throw new InvalidOperationException("ページ索引に使えるOCR anchorを取得できません。");
        }
        return selected;
    }

    private static IReadOnlyList<SceneGroundingRegion> SelectSpatiallyDistinct(
        IReadOnlyList<SceneGroundingRegion> ranked,
        int count)
    {
        var selected = new List<SceneGroundingRegion>(count);
        foreach (var candidate in ranked)
        {
            if (selected.Any(existing => SameVisualTextRegion(existing, candidate)))
            {
                continue;
            }
            selected.Add(candidate);
            if (selected.Count == count)
            {
                break;
            }
        }
        return selected;
    }

    private static bool SameVisualTextRegion(SceneGroundingRegion left, SceneGroundingRegion right)
    {
        var leftText = Normalize(left.Text);
        var rightText = Normalize(right.Text);
        var textOverlaps = leftText.Contains(rightText, StringComparison.Ordinal)
            || rightText.Contains(leftText, StringComparison.Ordinal);
        return textOverlaps
            && (PositionMatches(
                    left.EvidenceRegion.NormalizedBounds,
                    right.EvidenceRegion.NormalizedBounds)
                || ContainsBounds(left.EvidenceRegion.NormalizedBounds, right.EvidenceRegion.NormalizedBounds)
                || ContainsBounds(right.EvidenceRegion.NormalizedBounds, left.EvidenceRegion.NormalizedBounds));
    }

    private static bool ContainsBounds(IReadOnlyList<double> outer, IReadOnlyList<double> inner) =>
        inner[0] >= outer[0]
        && inner[1] >= outer[1]
        && inner[0] + inner[2] <= outer[0] + outer[2]
        && inner[1] + inner[3] <= outer[1] + outer[3];

    private static bool PositionMatches(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var leftX = left[0] + left[2] / 2;
        var leftY = left[1] + left[3] / 2;
        var rightX = right[0] + right[2] / 2;
        var rightY = right[1] + right[3] / 2;
        return Math.Abs(leftX - rightX) <= 0.04 && Math.Abs(leftY - rightY) <= 0.04;
    }

    private static LearnedAffordanceSignature Action(
        string stateId,
        AffordanceCandidate control,
        string evidenceId,
        string? destinationStateId)
    {
        var label = control.SemanticLabel
            ?? throw new InvalidOperationException("既知操作索引は文字labelを持つcontrolだけを保存します。");
        var primitive = control.AllowedPrimitives.Contains(GameInteractionOperations.Click, StringComparer.Ordinal)
            ? GameInteractionOperations.Click
            : control.AllowedPrimitives.First();
        var actionId = CreateActionId(stateId, label, primitive);
        return new LearnedAffordanceSignature(
            actionId,
            control.Locator.LocatorRevision,
            label,
            control.Locator.NormalizedBounds.ToArray(),
            [primitive],
            [evidenceId],
            destinationStateId,
            control.VisualPatch,
            control.KeyTokens,
            control.VerticalScrollSteps,
            control.HorizontalScrollSteps,
            control.DragDestinationNormalized);
    }

    private LearnedSceneProfileDocument Empty(string evidenceId) =>
        new(
            ContractSchemaVersions.Revision03,
            Id("known-profile", $"{gameId}\n{environmentScope}"),
            "known-screen-index-v1",
            gameId,
            environmentScope,
            processName,
            windowTitle,
            1_000,
            0.04,
            [],
            [evidenceId]);

    private static IReadOnlyList<LearnedStateSceneSignature> Replace(
        IReadOnlyList<LearnedStateSceneSignature> states,
        LearnedStateSceneSignature replacement) =>
        states.Where(state => state.StateId != replacement.StateId).Append(replacement).ToArray();

    private static string StateId(IReadOnlyList<LearnedSceneAnchor> anchors) =>
        Id("known-screen", string.Join('\n', anchors
            .OrderBy(anchor => Normalize(anchor.Text), StringComparer.Ordinal)
            .Select(anchor => $"{Normalize(anchor.Text)}|{Band(anchor.NormalizedBounds[0] + anchor.NormalizedBounds[2] / 2)}|{Band(anchor.NormalizedBounds[1] + anchor.NormalizedBounds[3] / 2)}")));

    private static string VisualStateId(AffordanceCandidate control) =>
        control.VisualPatch is null
            ? throw new InvalidOperationException("visual state IDにはtarget patchが必要です。")
            : Id("known-screen", string.Join(
                '\n',
                "visual",
                control.VisualPatch.Sha256,
                Band(control.Locator.NormalizedBounds[0] + control.Locator.NormalizedBounds[2] / 2),
                Band(control.Locator.NormalizedBounds[1] + control.Locator.NormalizedBounds[3] / 2)));

    private static string Id(string prefix, string value) =>
        $"{prefix}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    public static string CreateActionId(string stateId, string label, string primitive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(primitive);
        return Id("known-action", $"{stateId}\n{Normalize(label)}\n{primitive}");
    }

    private static int Band(double value) => Math.Clamp((int)Math.Floor(value * 8), 0, 7);
    private static double Area(IReadOnlyList<double> bounds) => bounds[2] * bounds[3];
    private static bool ContainsNonAsciiLetter(string value) =>
        value.Any(character => character > 0x7F && char.IsLetter(character));
    private static bool MixesAsciiAndNonAsciiLetters(string value) =>
        value.Any(character => character <= 0x7F && char.IsLetter(character))
        && ContainsNonAsciiLetter(value);
    private static string Normalize(string value) =>
        string.Concat(value.Normalize(NormalizationForm.FormKC)
            .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character)))
            .ToUpperInvariant();

    private static void Validate(ObservedScene page, AffordanceCandidate control, string evidenceId)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceId);
        if (control.ObservationId != page.ObservationId
            || control.FrameSequence != page.Frame.Sequence
            || control.TransformRevision != page.Frame.TransformRevision)
        {
            throw new InvalidOperationException("controlは保存対象pageの同一Observationへ束縛されていません。");
        }
    }
}
