using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
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
    string operation,
    bool allowAiDiscovery = true,
    bool startWithAiRepair = false) :
    IProductGameTargetDiscovery,
    IProductGameRediscoveryTrigger,
    IProductGameRouteControl,
    ILocalAiCallCounter
{
    private readonly HashSet<string> transitionUnconfirmed = new(StringComparer.Ordinal);
    private StructureScreenEdge? routeTarget;
    private string? selectedSavedKey;
    private bool comparisonOnly;
    private bool forceAiRepair = startWithAiRepair;
    public int AiCallCount => (aiDiscovery as ILocalAiCallCounter)?.AiCallCount ?? 0;

    public async ValueTask<ObservedScene> DiscoverAsync(
        ObservationResult observation,
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        var recognized = await ocr.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
        if (!comparisonOnly && !forceAiRepair && RouteCandidate(observation) is { } routeCandidate)
        {
            var local = LocalScene(observation, frame, recognized);
            return local with { Affordances = [routeCandidate] };
        }
        var profile = profiles.Load(gameId, environmentScope);
        if (profile is null)
        {
            if (comparisonOnly || !allowAiDiscovery)
            {
                return LocalScene(observation, frame, recognized);
            }
            return await aiDiscovery.DiscoverAsync(observation, frame, cancellationToken).ConfigureAwait(false);
        }
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
        var localScene = LocalTargetTrackingSceneBuilder.Build(bindingObservation, frame, textRegions, []);
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
            DiscoveryEvidence = localScene.DiscoveryEvidence,
            SceneVisualPatch = localScene.SceneVisualPatch,
        };
        var refined = LearnedSceneMatcher.RefineText(profile, frame, snapshot);
        if (!ReferenceEquals(refined, profile))
        {
            profiles.Upsert(refined);
            profile = refined;
        }

        if (comparisonOnly)
        {
            return scene;
        }

        if (forceAiRepair)
        {
            if (!allowAiDiscovery) return scene with { Affordances = [] };
            return await aiDiscovery.DiscoverAsync(observation, frame, cancellationToken).ConfigureAwait(false);
        }

        selectedSavedKey = null;
        if (scene.StateIdentity == StateIdentityStatus.Known && scene.StateHypothesisId is not null)
        {
            var state = profile.States.Single(item => item.StateId == scene.StateHypothesisId);
            var saved = SelectSaved(state, scene);
            if (saved is not null)
            {
                var key = Key(state.StateId, saved.CandidateId);
                selectedSavedKey = key;
                if (!transitionUnconfirmed.Contains(key))
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

        return allowAiDiscovery
            ? await aiDiscovery.DiscoverAsync(observation, frame, cancellationToken).ConfigureAwait(false)
            : scene with { Affordances = [] };
    }

    public void MarkTransitionUnconfirmed(ObservedScene before, AffordanceCandidate target)
    {
        forceAiRepair = true;
        if (selectedSavedKey is not null)
        {
            transitionUnconfirmed.Add(selectedSavedKey);
            return;
        }
        if (before.StateIdentity == StateIdentityStatus.Known && before.StateHypothesisId is not null)
        {
            transitionUnconfirmed.Add(Key(before.StateHypothesisId, target.CandidateId));
        }
    }

    public void MarkTransitionConfirmed(ObservedScene before, AffordanceCandidate target)
    {
        forceAiRepair = false;
        if (selectedSavedKey is not null)
        {
            transitionUnconfirmed.Remove(selectedSavedKey);
            selectedSavedKey = null;
        }
        if (before.StateIdentity == StateIdentityStatus.Known && before.StateHypothesisId is not null)
        {
            transitionUnconfirmed.Remove(Key(before.StateHypothesisId, target.CandidateId));
        }
    }

    private AffordanceCandidate? SelectSaved(
        LearnedStateSceneSignature state,
        ObservedScene scene)
    {
        if (routeTarget is not null)
        {
            var action = state.Affordances
                .Where(action => action.AllowedPrimitives.Contains(routeTarget.Primitive, StringComparer.Ordinal))
                .FirstOrDefault(action => routeTarget.TargetSemanticKey is not null
                    && GameSceneSemanticComparer.AffordanceKeySimilar(
                        routeTarget.TargetSemanticKey,
                        GameSceneSemanticComparer.TargetKey(
                            action.VisualPatch is null ? "text" : "visual",
                            action.Text,
                            action.NormalizedBounds)));
            return action is null
                ? null
                : scene.Affordances.SingleOrDefault(candidate => candidate.CandidateId == action.CandidateId);
        }
        if (!string.IsNullOrWhiteSpace(goal))
        {
            var selection = KnownGoalActionSelector.Select(state, goal, operation);
            return selection.Kind == KnownGoalActionSelectionKind.UseKnown
                ? scene.Affordances.SingleOrDefault(candidate => candidate.CandidateId == selection.Action!.CandidateId)
                : null;
        }

        var usable = state.Affordances
            .Where(action => action.AllowedPrimitives.Contains(operation, StringComparer.Ordinal))
            .Select(action => action.CandidateId)
            .ToHashSet(StringComparer.Ordinal);
        return scene.Affordances.FirstOrDefault(candidate => usable.Contains(candidate.CandidateId));
    }

    public void SetRouteTarget(StructureScreenEdge? edge)
    {
        routeTarget = edge;
        if (aiDiscovery is IProductGameOperationControl operationControl)
            operationControl.SetInteractionOperation(edge?.Primitive ?? operation);
    }

    public void BeginComparison() => comparisonOnly = true;

    public void EndComparison() => comparisonOnly = false;

    private AffordanceCandidate? RouteCandidate(ObservationResult observation)
    {
        if (routeTarget?.TargetNormalizedBounds is not { Count: 4 } bounds) return null;
        var semantic = routeTarget.TargetSemanticKey?.Split('|') ?? [];
        var kind = semantic.Length > 0 && semantic[0].Length > 0 ? semantic[0] : "saved-route";
        var label = semantic.Length > 1 && semantic[1].Length > 0 ? semantic[1] : routeTarget.AffordanceCandidateId;
        return new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            $"route:{routeTarget.EdgeId}:{observation.ObservationId}",
            observation.ObservationId,
            observation.Frame.Sequence,
            observation.Frame.TransformRevision,
            observation.Frame.SourceId,
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "saved-route",
                bounds.ToArray(),
                routeTarget.LocatorRevision),
            [],
            1,
            [routeTarget.Primitive],
            kind,
            label,
            KeyTokens: routeTarget.KeyTokens,
            VerticalScrollSteps: routeTarget.VerticalScrollSteps,
            HorizontalScrollSteps: routeTarget.HorizontalScrollSteps,
            DragDestinationNormalized: routeTarget.DragDestinationNormalized);
    }

    private static ObservedScene LocalScene(
        ObservationResult observation,
        CapturedFrame frame,
        WindowsGameOcrResult recognized)
    {
        return LocalTargetTrackingSceneBuilder.Build(
            observation,
            frame,
            WindowsGameOcrSpanBuilder.Build(recognized, frame.Width, frame.Height),
            []);
    }

    private static string Key(string stateId, string candidateId) => $"{stateId}\n{candidateId}";
}
