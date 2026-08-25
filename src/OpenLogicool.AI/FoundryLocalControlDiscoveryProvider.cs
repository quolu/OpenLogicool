using System.Security.Cryptography;
using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.AI;

public sealed record LocalControlDiscoveryResult(
    ObservedScene Scene,
    IReadOnlyList<ExplorationProposal> Proposals,
    FoundryVisionControlsResult ProviderResult,
    LocalVisionProviderTelemetry Telemetry);

public interface ILocalControlDiscoveryProvider
{
    Task<LocalControlDiscoveryResult> ObserveAsync(
        LocalVisionSceneRequest request,
        ReadOnlyMemory<byte> pngBytes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 文字付きとicon-onlyのclickable controlを、同じframeのnormalized boundsへ直接束縛する。
/// provider failureをOCR、既知label、別providerへfallbackしない。
/// </summary>
public sealed class FoundryLocalControlDiscoveryProvider(FoundryLocalVisionClient client)
    : ILocalControlDiscoveryProvider
{
    public async Task<LocalControlDiscoveryResult> ObserveAsync(
        LocalVisionSceneRequest request,
        ReadOnlyMemory<byte> pngBytes,
        CancellationToken cancellationToken = default)
    {
        Validate(request, pngBytes);
        var promptSha256 = client.ControlsPromptSha256For(request.TargetIntent);
        var providerResult = await client
            .ProposeControlsAsync(pngBytes, request.TargetIntent, cancellationToken)
            .ConfigureAwait(false);
        var affordances = providerResult.Status == FoundryVisionStatus.Completed
            ? providerResult.Controls.Select((control, index) => Affordance(request, control, index)).ToArray()
            : [];
        var identity = request.SameSceneCandidates.Count switch
        {
            0 => StateIdentityStatus.Novel,
            1 => StateIdentityStatus.Known,
            _ => StateIdentityStatus.Ambiguous,
        };
        var scene = new ObservedScene(
            ContractSchemaVersions.Revision03,
            request.SceneId,
            request.ObservationId,
            request.Frame,
            CaptureAvailability.Available,
            identity,
            identity == StateIdentityStatus.Novel
                ? $"hypothesis:{request.SceneId}"
                : identity == StateIdentityStatus.Known
                    ? request.SameSceneCandidates[0].StateId
                    : null,
            request.SameSceneCandidates,
            affordances,
            $"foundry-local-controls:{client.ModelId}:{promptSha256}",
            new SceneDiscoveryEvidence(
                "microsoft-foundry-local",
                client.ModelId,
                FoundryLocalVisionClient.ControlsPromptRevision,
                promptSha256,
                providerResult.Status.ToString(),
                providerResult.Failure.ToString(),
                providerResult.FailureDetail,
                providerResult.RawOutput,
                providerResult.ElapsedMs,
                0,
                0m));
        var proposals = request.AllowedPrimitives.Contains(GameInteractionOperations.Click, StringComparer.Ordinal)
            ? affordances.Select(affordance => Proposal(request, affordance)).ToArray()
            : [];
        var telemetry = new LocalVisionProviderTelemetry(
            "microsoft-foundry-local",
            client.Endpoint.ToString(),
            client.ModelId,
            FoundryLocalVisionClient.ControlsPromptRevision,
            promptSha256,
            request.CropId,
            request.CropWidth,
            request.CropHeight,
            Convert.ToHexString(SHA256.HashData(pngBytes.Span)),
            affordances.Length,
            providerResult.ElapsedMs,
            providerResult.RequestBytes,
            providerResult.InputTokens,
            providerResult.OutputTokens,
            ExternalAiTransmissionCount: 0,
            ExternalAiApiCostUsd: 0m);
        return new LocalControlDiscoveryResult(scene, proposals, providerResult, telemetry);
    }

    private static AffordanceCandidate Affordance(
        LocalVisionSceneRequest request,
        FoundryVisionControl control,
        int index)
    {
        var bounds = new[] { control.X, control.Y, control.Width, control.Height };
        var evidence = new EvidenceRegion(
            ContractSchemaVersions.Revision03,
            "rect",
            bounds,
            $"foundry-local-control:{control.Kind}:{control.Label}");
        var allowed = request.AllowedPrimitives
            .Where(operation => GameInteractionOperations.InputOperations.Contains(operation, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            $"affordance:{request.ObservationId}:{index + 1}",
            request.ObservationId,
            request.Frame.Sequence,
            request.Frame.TransformRevision,
            request.TargetWindowSourceId,
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                $"foundry-local-{control.Kind}-region",
                bounds,
                request.LocatorRevision),
            [evidence],
            0.5,
            allowed,
            control.Kind,
            control.Label);
    }

    private static ExplorationProposal Proposal(
        LocalVisionSceneRequest request,
        AffordanceCandidate affordance) => new(
            ContractSchemaVersions.Revision03,
            $"proposal:{affordance.CandidateId}",
            request.ObservationId,
            request.StructureRevisionId,
            affordance.CandidateId,
            GameInteractionOperations.Click,
            $"probe:{affordance.CandidateId}",
            [
                ExplorationOutcomeKind.Destination,
                ExplorationOutcomeKind.Novel,
                ExplorationOutcomeKind.NoChange,
                ExplorationOutcomeKind.Ambiguous,
                ExplorationOutcomeKind.Unavailable,
                ExplorationOutcomeKind.Fault,
                ExplorationOutcomeKind.OutcomeUnknown,
            ],
            new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 3, 300, 10_000),
            ["capture-unavailable", "stale-transform", "budget-exhausted", "recovery-lost"]);

    private static void Validate(LocalVisionSceneRequest request, ReadOnlyMemory<byte> pngBytes)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (pngBytes.IsEmpty
            || !string.Equals(request.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !string.Equals(request.Frame.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || !string.Equals(request.Frame.SourceId, request.TargetWindowSourceId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(request.SceneId)
            || string.IsNullOrWhiteSpace(request.ObservationId)
            || string.IsNullOrWhiteSpace(request.CropId)
            || string.IsNullOrWhiteSpace(request.LocatorRevision)
            || string.IsNullOrWhiteSpace(request.StructureRevisionId)
            || request.CropWidth <= 0
            || request.CropHeight <= 0
            || request.AllowedPrimitives.Count == 0
            || request.AllowedPrimitives.Any(string.IsNullOrWhiteSpace)
            || request.AllowedPrimitives.Distinct(StringComparer.Ordinal).Count() != request.AllowedPrimitives.Count)
        {
            throw new ArgumentException("control discovery requestがframe-bound contractを満たしません。", nameof(request));
        }
    }
}
