using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Perception;

public sealed record OcrWordBox(string Text, double X, double Y, double Width, double Height);

public sealed record OcrFrameSnapshot(
    string RecognizerVersion,
    string RecognizerLanguage,
    IReadOnlyList<OcrWordBox> Words);

/// <summary>学習済みanchorをfresh OCRへ照合するpure recognizer。状態名やgame固有文字列はdataから受け取る。</summary>
public static class LearnedSceneMatcher
{
    public static LearnedSceneProfileDocument RefineText(
        LearnedSceneProfileDocument profile,
        CapturedFrame frame,
        OcrFrameSnapshot ocr)
    {
        LearnedSceneProfileValidator.Validate(profile);
        var spans = BuildSpans(ocr.Words);
        var evidenceId = $"ocr-refine:{frame.SourceId}:{frame.Sequence}";
        var changed = false;
        var states = profile.States.Select(state =>
        {
            var stateChanged = false;
            var anchorMatches = state.Anchors.Select(anchor => new
            {
                Anchor = anchor,
                Observed = UniqueAt(anchor.Text, anchor.NormalizedBounds, spans, frame,
                    profile.NormalizedPositionTolerance),
            }).ToArray();
            var proposedTexts = anchorMatches.Select(item =>
                item.Observed is not null && OcrTextMatcher.PreferObserved(item.Anchor.Text, item.Observed.Text)
                    ? item.Observed.Text
                    : item.Anchor.Text).ToArray();
            var collided = proposedTexts
                .Select(OcrTextMatcher.Normalize)
                .GroupBy(text => text, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            var anchors = anchorMatches.Select((item, index) =>
            {
                if (item.Observed is null
                    || !OcrTextMatcher.PreferObserved(item.Anchor.Text, item.Observed.Text)
                    || collided.Contains(OcrTextMatcher.Normalize(proposedTexts[index])))
                {
                    return item.Anchor;
                }
                stateChanged = true;
                return item.Anchor with
                {
                    Text = item.Observed.Text,
                    EvidenceId = evidenceId,
                    PreviousTexts = AppendPrevious(item.Anchor.PreviousTexts, item.Anchor.Text),
                };
            }).ToArray();
            var affordances = state.Affordances.Select(affordance =>
            {
                var observed = UniqueAt(affordance.Text, affordance.NormalizedBounds, spans, frame,
                    profile.NormalizedPositionTolerance);
                if (observed is null || !OcrTextMatcher.PreferObserved(affordance.Text, observed.Text))
                {
                    return affordance;
                }
                stateChanged = true;
                return affordance with
                {
                    Text = observed.Text,
                    EvidenceIds = affordance.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
                    PreviousTexts = AppendPrevious(affordance.PreviousTexts, affordance.Text),
                };
            }).ToArray();
            if (!stateChanged)
            {
                return state;
            }
            changed = true;
            return state with
            {
                Anchors = anchors,
                Affordances = affordances,
                EvidenceIds = state.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
            };
        }).ToArray();
        return changed
            ? profile with
            {
                States = states,
                EvidenceIds = profile.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
            }
            : profile;
    }

    private static IReadOnlyList<string> AppendPrevious(IReadOnlyList<string>? previous, string value) =>
        (previous ?? []).Append(value).Distinct(StringComparer.Ordinal).ToArray();

    public static ObservedScene Match(
        LearnedSceneProfileDocument profile,
        CapturedFrame frame,
        OcrFrameSnapshot ocr)
    {
        LearnedSceneProfileValidator.Validate(profile);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(ocr);
        if (string.IsNullOrWhiteSpace(ocr.RecognizerVersion) || ocr.Words is null)
        {
            throw new ArgumentException("OCR snapshotが不正です。", nameof(ocr));
        }

        var spans = BuildSpans(ocr.Words);
        var matched = profile.States
            .Select(state => MatchState(state, spans, frame, profile.NormalizedPositionTolerance))
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();
        var effective = matched
            .Where(candidate => !matched.Any(other =>
                other.State.SupersedesStateIds?.Contains(candidate.State.StateId, StringComparer.Ordinal) == true))
            .ToArray();
        var identity = effective.Length switch
        {
            0 => StateIdentityStatus.Novel,
            1 => StateIdentityStatus.Known,
            _ => StateIdentityStatus.Ambiguous,
        };
        var availability = frame.FreshnessMs > profile.MaximumFrameFreshnessMilliseconds
            ? CaptureAvailability.Stale
            : CaptureAvailability.Available;
        var observationId = $"observation:{frame.SourceId}:{frame.Sequence}:{Guid.NewGuid():N}";
        var candidates = matched.Select(result => new StateCandidate(
            ContractSchemaVersions.Revision03,
            result.State.StateId,
            1,
            result.AnchorMatches.Select(match => Region(match, frame, ocr.RecognizerVersion)).ToArray())).ToArray();
        var affordances = effective.Length == 1
            ? MatchAffordances(effective[0].State, spans, frame, observationId, ocr.RecognizerVersion,
                profile.NormalizedPositionTolerance)
            : [];
        return new ObservedScene(
            ContractSchemaVersions.Revision03,
            $"scene:{observationId}",
            observationId,
            Reference(frame),
            availability,
            identity,
            identity == StateIdentityStatus.Known ? effective[0].State.StateId : null,
            candidates,
            affordances,
            $"{profile.ProfileVersion}/{ocr.RecognizerVersion}",
            SceneVisualPatch: frame.Pixels is null
                ? null
                : VisualPatchMatcher.Capture(frame, [0d, 0d, 1d, 1d]));
    }

    private static StateMatch? MatchState(
        LearnedStateSceneSignature state,
        IReadOnlyList<OcrWordBox> spans,
        CapturedFrame frame,
        double tolerance)
    {
        if (state.Anchors.Count == 0)
        {
            return MatchStateByVisualAffordances(state, frame);
        }
        var matches = new List<OcrWordBox>();
        foreach (var anchor in state.Anchors)
        {
            var match = UniqueAt(anchor.Text, anchor.NormalizedBounds, spans, frame, tolerance);
            if (match is null)
            {
                return MatchStateByVisualAffordances(state, frame);
            }
            matches.Add(match);
        }
        return new StateMatch(state, matches);
    }

    private static StateMatch? MatchStateByVisualAffordances(
        LearnedStateSceneSignature state,
        CapturedFrame frame)
    {
        if (state.VisualPatch is not null
            && VisualPatchMatcher.Matches(state.VisualPatch, frame, [0d, 0d, 1d, 1d]))
        {
            return new StateMatch(state, []);
        }
        var visual = state.Affordances
            .Where(affordance => affordance.VisualPatch is not null)
            .ToArray();
        if (visual.Length == 0
            || visual.Any(affordance => !VisualPatchMatcher.Matches(
                affordance.VisualPatch!,
                frame,
                affordance.NormalizedBounds)))
        {
            return null;
        }
        return new StateMatch(
            state,
            visual.Select(affordance => Box(affordance.NormalizedBounds, frame, affordance.Text)).ToArray());
    }

    private static IReadOnlyList<AffordanceCandidate> MatchAffordances(
        LearnedStateSceneSignature state,
        IReadOnlyList<OcrWordBox> spans,
        CapturedFrame frame,
        string observationId,
        string recognizerVersion,
        double tolerance) => state.Affordances
        .Select(signature => new
        {
            Signature = signature,
            Match = signature.VisualPatch is null
                ? UniqueAt(signature.Text, signature.NormalizedBounds, spans, frame, tolerance)
                : VisualPatchMatcher.Matches(signature.VisualPatch, frame, signature.NormalizedBounds)
                    ? Box(signature.NormalizedBounds, frame, signature.Text)
                    : null,
        })
        .Where(item => item.Match is not null)
        .Select(item => new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            item.Signature.CandidateId,
            observationId,
            frame.Sequence,
            frame.TransformRevision,
            frame.SourceId,
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "ocr-normalized-rect",
                NormalizeBounds(item.Match!, frame),
                item.Signature.LocatorRevision),
            [Region(item.Match!, frame, recognizerVersion)],
            1,
            item.Signature.AllowedPrimitives.ToArray(),
            item.Signature.VisualPatch is null ? "text" : "visual",
            item.Signature.Text))
        .ToArray();

    private static OcrWordBox Box(IReadOnlyList<double> bounds, CapturedFrame frame, string text) =>
        new(
            text,
            bounds[0] * frame.Width,
            bounds[1] * frame.Height,
            bounds[2] * frame.Width,
            bounds[3] * frame.Height);

    private static OcrWordBox? UniqueAt(
        string text,
        IReadOnlyList<double> expected,
        IReadOnlyList<OcrWordBox> spans,
        CapturedFrame frame,
        double tolerance)
    {
        var ranked = spans
            .Where(span => PositionMatches(expected, span, frame, tolerance))
            .Select(span => new
            {
                Span = span,
                Similarity = OcrTextMatcher.Similarity(text, span.Text),
                Distance = PositionDistance(expected, span, frame),
            })
            .Where(item => item.Similarity >= OcrTextMatcher.DefaultMinimumSimilarity)
            .OrderByDescending(item => item.Similarity)
            .ThenBy(item => item.Distance)
            .ToArray();
        if (ranked.Length == 0)
        {
            return null;
        }
        if (ranked.Length > 1
            && ranked[0].Similarity - ranked[1].Similarity < 0.08
            && Math.Abs(ranked[0].Distance - ranked[1].Distance) < 0.01)
        {
            return null;
        }
        return ranked[0].Span;
    }

    private static bool PositionMatches(
        IReadOnlyList<double> expected,
        OcrWordBox actual,
        CapturedFrame frame,
        double tolerance)
    {
        var bounds = NormalizeBounds(actual, frame);
        var expectedCenterX = expected[0] + expected[2] / 2;
        var expectedCenterY = expected[1] + expected[3] / 2;
        var actualCenterX = bounds[0] + bounds[2] / 2;
        var actualCenterY = bounds[1] + bounds[3] / 2;
        return Math.Abs(expectedCenterX - actualCenterX) <= tolerance
            && Math.Abs(expectedCenterY - actualCenterY) <= tolerance;
    }

    private static double PositionDistance(
        IReadOnlyList<double> expected,
        OcrWordBox actual,
        CapturedFrame frame)
    {
        var bounds = NormalizeBounds(actual, frame);
        var x = expected[0] + expected[2] / 2 - (bounds[0] + bounds[2] / 2);
        var y = expected[1] + expected[3] / 2 - (bounds[1] + bounds[3] / 2);
        return Math.Sqrt(x * x + y * y);
    }

    private static IReadOnlyList<OcrWordBox> BuildSpans(IReadOnlyList<OcrWordBox> words)
    {
        var results = new List<OcrWordBox>();
        var lines = new List<List<OcrWordBox>>();
        foreach (var word in words.OrderBy(word => word.Y).ThenBy(word => word.X))
        {
            var line = lines.FirstOrDefault(existing => existing.Any(item =>
                item.Y < word.Y + word.Height && word.Y < item.Y + item.Height));
            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }
        foreach (var line in lines)
        {
            var ordered = line.OrderBy(word => word.X).ToArray();
            for (var start = 0; start < ordered.Length; start++)
            {
                for (var end = start; end < ordered.Length; end++)
                {
                    if (end > start)
                    {
                        var previous = ordered[end - 1];
                        var gap = ordered[end].X - (previous.X + previous.Width);
                        if (gap > Math.Max(12, Math.Max(previous.Height, ordered[end].Height) * 2))
                        {
                            break;
                        }
                    }
                    var span = ordered[start..(end + 1)];
                    var left = span.Min(word => word.X);
                    var top = span.Min(word => word.Y);
                    var right = span.Max(word => word.X + word.Width);
                    var bottom = span.Max(word => word.Y + word.Height);
                    results.Add(new OcrWordBox(string.Concat(span.Select(word => word.Text)), left, top, right - left, bottom - top));
                }
            }
        }
        return results;
    }

    private static double[] NormalizeBounds(OcrWordBox box, CapturedFrame frame) =>
        [box.X / frame.Width, box.Y / frame.Height, box.Width / frame.Width, box.Height / frame.Height];

    private static EvidenceRegion Region(OcrWordBox box, CapturedFrame frame, string recognizerVersion) =>
        new(ContractSchemaVersions.Revision03, "rect", NormalizeBounds(box, frame), recognizerVersion);

    private static CapturedFrameReference Reference(CapturedFrame frame) => new(
        ContractSchemaVersions.Revision03,
        frame.SourceId,
        frame.Backend,
        frame.Sequence,
        frame.MonotonicMs,
        frame.WallClockUtc,
        frame.TransformRevision,
        frame.FreshnessMs,
        frame.LastChangeMs);

    private sealed record StateMatch(
        LearnedStateSceneSignature State,
        IReadOnlyList<OcrWordBox> AnchorMatches);
}
