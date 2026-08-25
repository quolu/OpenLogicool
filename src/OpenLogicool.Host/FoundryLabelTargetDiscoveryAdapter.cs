using OpenLogicool.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Perception;

namespace OpenLogicool.Host;

/// <summary>
/// Foundry Localの短いlabel列を同一frameのWindows OCR regionへ束縛する正規adapter。
/// icon-only controlへfallbackしない。
/// </summary>
public sealed class FoundryLabelTargetDiscoveryAdapter(
    ILocalLabelDiscoveryProvider provider,
    IWindowsGameOcrRecognizer ocr,
    IGameFramePngEncoder pngEncoder,
    Func<string> structureRevisionId,
    string? targetIntent = null,
    string interactionOperation = GameInteractionOperations.Click) : IProductGameTargetDiscovery, ILocalAiCallCounter
{
    private const int MaximumVisionDimension = 640;
    private int discoveryCount;
    private int aiCallCount;
    private IReadOnlyList<AffordanceCandidate> initialTargets = [];

    public int AiCallCount => Volatile.Read(ref aiCallCount);

    public async ValueTask<ObservedScene> DiscoverAsync(
        ObservationResult observation,
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        var ocrResult = await ocr.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
        var textRegions = WindowsGameOcrSpanBuilder.Canonicalize(
            WindowsGameOcrSpanBuilder.Build(ocrResult, frame.Width, frame.Height));
        if (Interlocked.Increment(ref discoveryCount) > 1)
        {
            return LocalTargetTrackingSceneBuilder.Build(observation, frame, textRegions, initialTargets);
        }
        var png = pngEncoder.Encode(frame, MaximumVisionDimension);
        Interlocked.Increment(ref aiCallCount);
        var request = new LocalVisionSceneRequest(
            ContractSchemaVersions.Revision03,
            $"scene:{observation.ObservationId}",
            observation.ObservationId,
            observation.Frame,
            observation.Frame.SourceId,
            $"crop:full:{observation.ObservationId}",
            png.Width,
            png.Height,
            $"locator:{observation.ObservationId}",
            textRegions,
            observation.StateCandidates,
            [interactionOperation],
            structureRevisionId(),
            targetIntent);
        var discovered = await provider.ObserveAsync(request, png.Bytes, cancellationToken).ConfigureAwait(false);
        initialTargets = discovered.Scene.Affordances
            .Select(target => target with
            {
                SemanticKind = "probe-target",
                VisualPatch = VisualPatchMatcher.Capture(frame, target.Locator.NormalizedBounds),
                ContextTexts = textRegions.Select(region => region.Text).ToArray(),
            })
            .ToArray();
        return discovered.Scene with
        {
            Affordances = LocalTargetTrackingSceneBuilder.MergeInitial(
                observation,
                frame,
                textRegions,
                initialTargets),
            DiscoveryEvidence = discovered.Scene.DiscoveryEvidence! with
            {
                LocalGroundingTexts = textRegions.Select(region => region.Text).ToArray(),
                LocalGroundingRegions = textRegions
                    .Select(region => new SceneGroundingRegion(region.Text, region.EvidenceRegion))
                    .ToArray(),
            },
            SceneVisualPatch = VisualPatchMatcher.Capture(frame, [0d, 0d, 1d, 1d]),
        };
    }
}
