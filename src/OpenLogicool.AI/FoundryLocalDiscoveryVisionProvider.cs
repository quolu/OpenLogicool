using System.Globalization;
using System.Security.Cryptography;
using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.AI;

public sealed record LocalVisionTextRegion(
    string Text,
    EvidenceRegion EvidenceRegion);

public sealed record LocalVisionSceneRequest(
    string SchemaVersion,
    string SceneId,
    string ObservationId,
    CapturedFrameReference Frame,
    string TargetWindowSourceId,
    string CropId,
    int CropWidth,
    int CropHeight,
    string LocatorRevision,
    IReadOnlyList<LocalVisionTextRegion> TextRegions,
    IReadOnlyList<StateCandidate> SameSceneCandidates,
    IReadOnlyList<string> AllowedPrimitives,
    string StructureRevisionId);

public sealed record LocalVisionProviderTelemetry(
    string ProviderId,
    string Endpoint,
    string ModelId,
    string PromptRevision,
    string PromptSha256,
    string CropId,
    int CropWidth,
    int CropHeight,
    string CropSha256,
    int GroundedAffordanceCount,
    long ElapsedMilliseconds,
    int RequestBytes,
    int? InputTokens,
    int? OutputTokens,
    int ExternalAiTransmissionCount,
    decimal ExternalAiApiCostUsd);

public sealed record LocalVisionDiscoveryResult(
    ObservedScene Scene,
    IReadOnlyList<ExplorationProposal> Proposals,
    FoundryVisionResult ProviderResult,
    LocalVisionProviderTelemetry Telemetry);

/// <summary>
/// t05で採用したFoundry Local label→同一frame OCR regionの一経路だけを製品contractへ接続する。
/// provider座標、生screen座標、cloud、別providerへのfallbackは持たない。
/// </summary>
public sealed class FoundryLocalDiscoveryVisionProvider
{
    private const double MinimumFuzzySimilarity = 0.85;
    private const double MinimumFuzzyMargin = 0.15;
    private readonly FoundryLocalVisionClient client;

    public FoundryLocalDiscoveryVisionProvider(FoundryLocalVisionClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
    }

    public async Task<LocalVisionDiscoveryResult> ObserveAsync(
        LocalVisionSceneRequest request,
        ReadOnlyMemory<byte> pngBytes,
        CancellationToken cancellationToken = default)
    {
        Validate(request, pngBytes);

        var providerResult = await client
            .ProposeLabelsAsync(pngBytes, cancellationToken)
            .ConfigureAwait(false);
        var affordances = providerResult.Status == FoundryVisionStatus.Completed
            ? Ground(request, providerResult.Labels)
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
            $"foundry-local:{client.ModelId}:{client.PromptSha256}");
        var proposals = request.AllowedPrimitives.Contains("click", StringComparer.Ordinal)
            ? affordances.Select(affordance => Proposal(request, affordance)).ToArray()
            : [];
        var telemetry = new LocalVisionProviderTelemetry(
            "microsoft-foundry-local",
            client.Endpoint.ToString(),
            client.ModelId,
            FoundryLocalVisionClient.PromptRevision,
            client.PromptSha256,
            request.CropId,
            request.CropWidth,
            request.CropHeight,
            Convert.ToHexString(SHA256.HashData(pngBytes.Span)),
            affordances.Count,
            providerResult.ElapsedMs,
            providerResult.RequestBytes,
            providerResult.InputTokens,
            providerResult.OutputTokens,
            ExternalAiTransmissionCount: 0,
            ExternalAiApiCostUsd: 0m);

        return new LocalVisionDiscoveryResult(scene, proposals, providerResult, telemetry);
    }

    private static IReadOnlyList<AffordanceCandidate> Ground(
        LocalVisionSceneRequest request,
        IReadOnlyList<string> labels)
    {
        var grounded = new List<(string Label, LocalVisionTextRegion Region)>();
        var usedRegions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in labels)
        {
            var region = UniqueRegion(label, request.TextRegions);
            if (region is null)
            {
                continue;
            }

            var regionKey = string.Join(",", region.EvidenceRegion.NormalizedBounds.Select(
                value => value.ToString("R", CultureInfo.InvariantCulture)));
            if (usedRegions.Add(regionKey))
            {
                grounded.Add((label, region));
            }
        }

        return grounded.Select((item, index) => new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            $"affordance:{request.ObservationId}:{index + 1}",
            request.ObservationId,
            request.Frame.Sequence,
            request.Frame.TransformRevision,
            request.TargetWindowSourceId,
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "ocr-text-region",
                item.Region.EvidenceRegion.NormalizedBounds,
                request.LocatorRevision),
            [item.Region.EvidenceRegion],
            Confidence(label: item.Label, observed: item.Region.Text),
            request.AllowedPrimitives.Contains("click", StringComparer.Ordinal) ? ["click"] : []))
            .ToArray();
    }

    private static LocalVisionTextRegion? UniqueRegion(
        string label,
        IReadOnlyList<LocalVisionTextRegion> regions)
    {
        var exact = regions.Where(region => FrameBoundLabelMatcher.Equals(region.Text, label)).ToArray();
        if (exact.Length == 1)
        {
            return exact[0];
        }
        if (exact.Length > 1)
        {
            return null;
        }

        var ranked = regions
            .Select(region => (Region: region, Similarity: FrameBoundLabelMatcher.Similarity(region.Text, label)))
            .OrderByDescending(item => item.Similarity)
            .ToArray();
        if (ranked.Length == 0
            || ranked[0].Similarity < MinimumFuzzySimilarity
            || ranked.Length > 1 && ranked[0].Similarity - ranked[1].Similarity < MinimumFuzzyMargin)
        {
            return null;
        }

        return ranked[0].Region;
    }

    private static double Confidence(string label, string observed) =>
        FrameBoundLabelMatcher.Equals(observed, label)
            ? 1
            : FrameBoundLabelMatcher.Similarity(observed, label);

    private static ExplorationProposal Proposal(
        LocalVisionSceneRequest request,
        AffordanceCandidate affordance) =>
        new(
            ContractSchemaVersions.Revision03,
            $"proposal:{affordance.CandidateId}",
            request.ObservationId,
            request.StructureRevisionId,
            affordance.CandidateId,
            "click",
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
            new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 3, 300, 5_000),
            ["capture-unavailable", "stale-transform", "budget-exhausted", "recovery-lost"]);

    private static void Validate(LocalVisionSceneRequest request, ReadOnlyMemory<byte> pngBytes)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal))
        {
            throw new ArgumentException("Local vision scene requestはschema 0.3.0だけを受理します。", nameof(request));
        }
        if (pngBytes.IsEmpty
            || string.IsNullOrWhiteSpace(request.SceneId)
            || string.IsNullOrWhiteSpace(request.ObservationId)
            || string.IsNullOrWhiteSpace(request.TargetWindowSourceId)
            || string.IsNullOrWhiteSpace(request.CropId)
            || string.IsNullOrWhiteSpace(request.LocatorRevision)
            || string.IsNullOrWhiteSpace(request.StructureRevisionId)
            || request.CropWidth <= 0
            || request.CropHeight <= 0)
        {
            throw new ArgumentException("Local vision scene requestのframe／crop／revision fieldが不正です。", nameof(request));
        }
        ArgumentNullException.ThrowIfNull(request.Frame);
        ArgumentNullException.ThrowIfNull(request.TextRegions);
        ArgumentNullException.ThrowIfNull(request.SameSceneCandidates);
        ArgumentNullException.ThrowIfNull(request.AllowedPrimitives);
        if (!string.Equals(request.Frame.SourceId, request.TargetWindowSourceId, StringComparison.Ordinal)
            || !string.Equals(request.Frame.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || request.Frame.Sequence < 0
            || request.Frame.TransformRevision < 0
            || request.AllowedPrimitives.Count == 0
            || request.AllowedPrimitives.Any(string.IsNullOrWhiteSpace)
            || request.AllowedPrimitives.Distinct(StringComparer.Ordinal).Count() != request.AllowedPrimitives.Count)
        {
            throw new ArgumentException("target windowまたは許可primitiveがframe-bound contractを満たしません。", nameof(request));
        }

        foreach (var region in request.TextRegions)
        {
            if (region is null
                || region.EvidenceRegion is null
                || string.IsNullOrWhiteSpace(region.Text)
                || region.EvidenceRegion.NormalizedBounds.Count != 4
                || region.EvidenceRegion.NormalizedBounds.Any(value => value is < 0 or > 1)
                || region.EvidenceRegion.NormalizedBounds[2] <= 0
                || region.EvidenceRegion.NormalizedBounds[3] <= 0
                || region.EvidenceRegion.NormalizedBounds[0] + region.EvidenceRegion.NormalizedBounds[2] > 1
                || region.EvidenceRegion.NormalizedBounds[1] + region.EvidenceRegion.NormalizedBounds[3] > 1)
            {
                throw new ArgumentException("OCR regionがnormalized frame boundsを満たしません。", nameof(request));
            }
        }

        foreach (var candidate in request.SameSceneCandidates)
        {
            if (candidate is null
                || string.IsNullOrWhiteSpace(candidate.StateId)
                || candidate.Confidence is < 0 or > 1
                || candidate.EvidenceRegions is null
                || candidate.EvidenceRegions.Count == 0)
            {
                throw new ArgumentException("同一scene候補がstate evidence contractを満たしません。", nameof(request));
            }
        }
    }
}
