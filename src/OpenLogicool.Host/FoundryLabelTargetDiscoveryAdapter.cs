using OpenLogicool.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

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
    string? targetIntent = null) : IProductGameTargetDiscovery
{
    private const int MaximumVisionDimension = 640;

    public async ValueTask<ObservedScene> DiscoverAsync(
        ObservationResult observation,
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        var ocrResult = await ocr.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
        var png = pngEncoder.Encode(frame, MaximumVisionDimension);
        var textRegions = WindowsGameOcrSpanBuilder.Canonicalize(
            WindowsGameOcrSpanBuilder.Build(ocrResult, frame.Width, frame.Height));
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
            [GameInteractionOperations.Click],
            structureRevisionId(),
            targetIntent);
        var discovered = await provider.ObserveAsync(request, png.Bytes, cancellationToken).ConfigureAwait(false);
        return discovered.Scene with
        {
            DiscoveryEvidence = discovered.Scene.DiscoveryEvidence! with
            {
                LocalGroundingTexts = textRegions.Select(region => region.Text).ToArray(),
                LocalGroundingRegions = textRegions
                    .Select(region => new SceneGroundingRegion(region.Text, region.EvidenceRegion))
                    .ToArray(),
            },
        };
    }
}
