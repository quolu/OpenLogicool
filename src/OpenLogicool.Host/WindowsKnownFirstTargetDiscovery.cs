using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Perception;

namespace OpenLogicool.Host;

/// <summary>
/// 保存済みページとactionをWindows OCRで先に解決し、使える保存情報が無い時か、
/// その保存actionで10秒間の遷移を確認できなかった時だけAI discoveryへ進む。
/// </summary>
public sealed class WindowsKnownFirstTargetDiscovery(
    IProductGameTargetDiscovery aiDiscovery,
    IWindowsGameOcrRecognizer ocr,
    ILearnedSceneProfileStore profiles,
    string gameId,
    string environmentScope,
    string? goal,
    string operation) : IProductGameTargetDiscovery, IProductGameRediscoveryTrigger, ILocalAiCallCounter
{
    private readonly HashSet<string> transitionUnconfirmed = new(StringComparer.Ordinal);
    public int AiCallCount => (aiDiscovery as ILocalAiCallCounter)?.AiCallCount ?? 0;

    public async ValueTask<ObservedScene> DiscoverAsync(
        ObservationResult observation,
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        var profile = profiles.Load(gameId, environmentScope);
        if (profile is null)
        {
            return await aiDiscovery.DiscoverAsync(observation, frame, cancellationToken).ConfigureAwait(false);
        }

        var recognized = await ocr.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
        var snapshot = new OcrFrameSnapshot(
            $"windows-ocr:{recognized.RecognizerLanguage}",
            recognized.RecognizerLanguage,
            recognized.Words.Select(word => new OcrWordBox(
                word.Text,
                word.X,
                word.Y,
                word.Width,
                word.Height)).ToArray());
        var scene = LearnedSceneMatcher.Match(profile, frame, snapshot);
        scene = scene with
        {
            ObservationId = scene.ObservationId,
            Frame = observation.Frame,
            Affordances = scene.Affordances.Select(candidate => candidate with
            {
                ObservationId = observation.ObservationId,
                FrameSequence = observation.Frame.Sequence,
                TransformRevision = observation.Frame.TransformRevision,
                TargetWindowSourceId = observation.Frame.SourceId,
            }).ToArray(),
        };
        scene = scene with { ObservationId = observation.ObservationId };
        var bindingObservation = observation with
        {
            CaptureAvailability = scene.CaptureAvailability,
            StateIdentity = scene.StateIdentity,
            StateCandidates = scene.StateCandidates,
            RecognizerVersion = scene.PerceptionVersion,
        };
        var textRegions = WindowsGameOcrSpanBuilder.Build(recognized, frame.Width, frame.Height);
        scene = scene with
        {
            Affordances =
            [
                .. scene.Affordances,
                .. LocalTargetTrackingSceneBuilder.StructuralText(
                    bindingObservation,
                    frame,
                    textRegions,
                    scene.Affordances),
            ],
        };
        var refined = LearnedSceneMatcher.RefineText(profile, frame, snapshot);
        if (!ReferenceEquals(refined, profile))
        {
            profiles.Upsert(refined);
            profile = refined;
        }

        if (scene.StateIdentity == StateIdentityStatus.Known && scene.StateHypothesisId is not null)
        {
            var state = profile.States.Single(item => item.StateId == scene.StateHypothesisId);
            var saved = SelectSaved(state, scene);
            if (saved is not null)
            {
                var key = Key(state.StateId, saved.CandidateId);
                if (!transitionUnconfirmed.Remove(key))
                {
                    return scene with
                    {
                        Affordances = scene.Affordances
                            .Where(candidate => candidate.CandidateId == saved.CandidateId)
                            .ToArray(),
                    };
                }
            }
        }

        return await aiDiscovery.DiscoverAsync(observation, frame, cancellationToken).ConfigureAwait(false);
    }

    public void MarkTransitionUnconfirmed(ObservedScene before, AffordanceCandidate target)
    {
        if (before.StateIdentity == StateIdentityStatus.Known && before.StateHypothesisId is not null)
        {
            transitionUnconfirmed.Add(Key(before.StateHypothesisId, target.CandidateId));
        }
    }

    public void MarkTransitionConfirmed(ObservedScene before, AffordanceCandidate target)
    {
        if (before.StateIdentity == StateIdentityStatus.Known && before.StateHypothesisId is not null)
        {
            transitionUnconfirmed.Remove(Key(before.StateHypothesisId, target.CandidateId));
        }
    }

    private AffordanceCandidate? SelectSaved(
        LearnedStateSceneSignature state,
        ObservedScene scene)
    {
        if (!string.IsNullOrWhiteSpace(goal))
        {
            var selection = KnownGoalActionSelector.Select(state, goal, operation);
            return selection.Kind == KnownGoalActionSelectionKind.UseKnown
                ? scene.Affordances.SingleOrDefault(candidate => candidate.CandidateId == selection.Action!.CandidateId)
                : null;
        }

        var usable = state.Affordances
            .Where(action => action.DestinationStateId is not null)
            .Where(action => action.AllowedPrimitives.Contains(operation, StringComparer.Ordinal))
            .Select(action => action.CandidateId)
            .ToHashSet(StringComparer.Ordinal);
        return scene.Affordances.FirstOrDefault(candidate => usable.Contains(candidate.CandidateId));
    }

    private static string Key(string stateId, string candidateId) => $"{stateId}\n{candidateId}";
}
