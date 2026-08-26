using System.IO;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Perception;

namespace OpenLogicool.Host;

public sealed class CodexSuppliedTargetDiscovery(
    IWindowsGameOcrRecognizer ocr,
    ILearnedSceneProfileStore profiles,
    string gameId,
    string environmentScope) : IProductGameTargetDiscovery, IProductGameRouteControl
{
    private StructureScreenEdge? routeTarget;
    private bool comparisonOnly;

    public async ValueTask<ObservedScene> DiscoverAsync(
        ObservationResult observation,
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        var recognized = await ocr.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
        var textRegions = WindowsGameOcrSpanBuilder.Build(recognized, frame.Width, frame.Height);
        var profile = profiles.Load(gameId, environmentScope);
        ObservedScene scene;
        if (profile is null)
        {
            scene = LocalTargetTrackingSceneBuilder.Build(observation, frame, textRegions, []);
        }
        else
        {
            var snapshot = new OcrFrameSnapshot(
                $"windows-ocr:{recognized.RecognizerLanguage}",
                recognized.RecognizerLanguage,
                recognized.Words.Select(word => new OcrWordBox(
                    word.Text, word.X, word.Y, word.Width, word.Height)).ToArray());
            scene = LearnedSceneMatcher.Match(profile, frame, snapshot);
            var binding = observation with
            {
                CaptureAvailability = scene.CaptureAvailability,
                StateIdentity = scene.StateIdentity,
                StateCandidates = scene.StateCandidates,
                RecognizerVersion = scene.PerceptionVersion,
            };
            var local = LocalTargetTrackingSceneBuilder.Build(binding, frame, textRegions, []);
            scene = scene with
            {
                ObservationId = observation.ObservationId,
                Frame = observation.Frame,
                Affordances =
                [
                    .. scene.Affordances.Select(candidate => candidate with
                    {
                        ObservationId = observation.ObservationId,
                        FrameSequence = observation.Frame.Sequence,
                        TransformRevision = observation.Frame.TransformRevision,
                        TargetWindowSourceId = observation.Frame.SourceId,
                    }),
                    .. LocalTargetTrackingSceneBuilder.StructuralText(binding, frame, textRegions, scene.Affordances),
                ],
                DiscoveryEvidence = local.DiscoveryEvidence,
                SceneVisualPatch = local.SceneVisualPatch,
            };
        }
        if (!comparisonOnly && RouteCandidate(observation) is { } routeCandidate)
            scene = scene with { Affordances = [routeCandidate] };
        return scene;
    }

    public void SetRouteTarget(StructureScreenEdge? edge) => routeTarget = edge;
    public void BeginComparison() => comparisonOnly = true;
    public void EndComparison() => comparisonOnly = false;

    private AffordanceCandidate? RouteCandidate(ObservationResult observation)
    {
        if (routeTarget?.TargetNormalizedBounds is not { Count: 4 } bounds) return null;
        return new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            $"codex-route:{routeTarget.EdgeId}:{observation.ObservationId}",
            observation.ObservationId,
            observation.Frame.Sequence,
            observation.Frame.TransformRevision,
            observation.Frame.SourceId,
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "codex-supplied-route",
                bounds.ToArray(),
                routeTarget.LocatorRevision),
            [],
            1,
            [routeTarget.Primitive],
            "codex-supplied",
            routeTarget.TargetSemanticKey ?? routeTarget.AffordanceCandidateId,
            KeyTokens: routeTarget.KeyTokens,
            VerticalScrollSteps: routeTarget.VerticalScrollSteps,
            HorizontalScrollSteps: routeTarget.HorizontalScrollSteps,
            DragDestinationNormalized: routeTarget.DragDestinationNormalized);
    }
}

public sealed class CodexProductGameToolRuntime(ProductGameExplorerRuntime product) : ICodexGameToolRuntime
{
    public async ValueTask<CodexGameObservation> ObserveAsync(CancellationToken cancellationToken = default)
    {
        product.SetRouteTarget(null, repairing: false);
        var observation = await product.ObserveAsync(cancellationToken).ConfigureAwait(false);
        var scene = await product.DiscoverTargetsAsync(observation, cancellationToken).ConfigureAwait(false);
        var artifact = observation.Frame.Artifact
            ?? throw new InvalidOperationException("Codex observeにframe artifactがありません。");
        var path = artifact.LocalPath
            ?? throw new InvalidOperationException("Codex observeのframe artifactがlocal pathを持ちません。");
        var imageData = "data:image/png;base64," + Convert.ToBase64String(await File.ReadAllBytesAsync(path, cancellationToken));
        var texts = scene.DiscoveryEvidence?.LocalGroundingTexts?
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(text => OcrTextMatcher.Normalize(text).Length)
            .Take(160)
            .ToArray() ?? [];
        var actions = scene.Affordances
            .Where(candidate => candidate.AllowedPrimitives.Count > 0)
            .Select(candidate => new CodexGameObservedAction(
                candidate.CandidateId,
                candidate.SemanticLabel ?? candidate.CandidateId,
                candidate.AllowedPrimitives[0],
                candidate.Locator.NormalizedBounds))
            .ToArray();
        return new CodexGameObservation(observation.ObservationId, imageData, texts, actions);
    }

    public async ValueTask<CodexGameActionOutcome> ExecuteAsync(
        CodexGameActionCommand command,
        bool repairing,
        CancellationToken cancellationToken = default)
    {
        var edge = command.SavedEdge ?? DraftEdge(command);
        product.SetRouteTarget(edge, repairing: command.SavedEdge is null || repairing);
        var result = await product.ExecuteNextAsync(cancellationToken).ConfigureAwait(false);
        return new CodexGameActionOutcome(
            result.Status.ToString(),
            result.Comparison?.Judgement,
            result.CommittedEdgeId,
            result.Detail);
    }

    private static StructureScreenEdge DraftEdge(CodexGameActionCommand command)
    {
        if (!GameInteractionOperations.InputOperations.Contains(command.Operation, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(command));
        if (command.Operation == GameInteractionOperations.KeyTap && command.KeyTokens is not { Count: > 0 }
            || command.Operation == GameInteractionOperations.Scroll && command.VerticalScrollSteps.GetValueOrDefault() == 0
            || command.Operation is not (GameInteractionOperations.KeyTap or GameInteractionOperations.Scroll)
                && command.NormalizedBounds is not { Count: 4 })
            throw new ArgumentException("Codex action parameterが操作種別と一致しません。", nameof(command));
        return new StructureScreenEdge(
            ContractSchemaVersions.Revision03,
            $"codex-draft:{Guid.NewGuid():N}",
            "codex-current",
            null,
            null,
            "codex-candidate",
            $"codex-locator:{Guid.NewGuid():N}",
            command.Operation,
            "explicit-user-goal",
            [],
            false,
            "codex-before",
            null,
            new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
            [],
            [],
            StructureVerificationState.Candidate,
            TargetSemanticKey: $"codex|{command.Label}",
            TargetNormalizedBounds: command.NormalizedBounds,
            KeyTokens: command.KeyTokens,
            VerticalScrollSteps: command.VerticalScrollSteps,
            HorizontalScrollSteps: command.Operation == GameInteractionOperations.Scroll ? 0 : null);
    }
}

public sealed class CodexLearningRouteRecorder(
    string gameId,
    string environmentScope,
    string goal,
    IGameStructureStore structures,
    ILearningRouteStore routes,
    TimeProvider? timeProvider = null) : ICodexRouteRecorder
{
    private readonly string routeId = PurposeLearningRouteIds.Create(gameId, environmentScope, goal);
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private LearningRouteRevision? route = routes.LoadLatest(PurposeLearningRouteIds.Create(gameId, environmentScope, goal));
    private int stepIndex;

    public int StepNumber => stepIndex;
    public long RevisionNumber => route?.RevisionNumber ?? 0;
    public bool Repairing { get; private set; }
    public StructureScreenEdge? NextSavedEdge
    {
        get
        {
            if (route is null || stepIndex >= route.EdgeIds.Count) return null;
            var id = route.EdgeIds[stepIndex];
            return structures.LoadRevision(gameId, environmentScope).ScreenGraph.Edges
                .Single(edge => edge.EdgeId == id && !edge.Retired);
        }
    }

    public void Record(CodexGameActionOutcome outcome, bool usedSavedEdge)
    {
        if (outcome.Judgement != GameTransitionJudgement.Moved)
        {
            Repairing = true;
            return;
        }
        if (usedSavedEdge)
        {
            stepIndex++;
            Repairing = false;
            return;
        }
        var committedEdgeId = outcome.CommittedEdgeId
            ?? throw new InvalidOperationException("Moved Codex actionにcommit済みedgeがありません。");
        var edgeIds = route?.EdgeIds.ToList() ?? [];
        if (Repairing && stepIndex < edgeIds.Count) edgeIds[stepIndex] = committedEdgeId;
        else if (stepIndex == edgeIds.Count) edgeIds.Add(committedEdgeId);
        else throw new InvalidOperationException("Codex route step indexがedge列と一致しません。");
        var current = structures.LoadRevision(gameId, environmentScope);
        route = routes.Append(new LearningRouteDraft(
            ContractSchemaVersions.Revision03,
            routeId,
            route?.VersionId,
            gameId,
            environmentScope,
            current.RevisionId,
            goal,
            edgeIds,
            LearningRouteAuthor.Ai,
            null,
            Repairing ? $"step {stepIndex + 1}だけをCodex修復" : $"step {stepIndex + 1}をCodex逐次追記",
            LearningRouteStatus.Draft,
            time.GetUtcNow()));
        stepIndex++;
        Repairing = false;
    }

    public void Complete(IReadOnlyList<string> facts)
    {
        if (route is null || route.Status != LearningRouteStatus.Draft) return;
        var current = structures.LoadRevision(gameId, environmentScope);
        route = routes.Append(new LearningRouteDraft(
            ContractSchemaVersions.Revision03,
            routeId,
            route.VersionId,
            gameId,
            environmentScope,
            current.RevisionId,
            goal,
            route.EdgeIds,
            LearningRouteAuthor.Ai,
            null,
            $"Codexがgoal完了を確認（facts {facts.Count}件）",
            LearningRouteStatus.Compiled,
            time.GetUtcNow()));
    }
}
