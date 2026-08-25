using OpenLogicool.Contracts.Capture;
using System.Text.Json.Serialization;

namespace OpenLogicool.Contracts.Perception;

public enum CaptureAvailability
{
    Available,
    Unavailable,
    Stale,
}

public enum StateIdentityStatus
{
    Known,
    Novel,
    Ambiguous,
    InsufficientEvidence,
}

public sealed record CapturedFrameReference(
    string SchemaVersion,
    [property: JsonPropertyName("source")]
    string SourceId,
    CaptureBackend Backend,
    long Sequence,
    [property: JsonPropertyName("monotonicTimeMs")]
    double MonotonicMs,
    DateTimeOffset WallClockUtc,
    long TransformRevision,
    long FreshnessMs,
    long LastChangeMs,
    CapturedFrameArtifact? Artifact = null);

public sealed record CapturedFrameArtifact(
    string ArtifactId,
    string MediaType,
    string Sha256,
    int Width,
    int Height,
    string LocalPath);

public sealed record EvidenceRegion(
    string SchemaVersion,
    string Shape,
    [property: JsonPropertyName("normalized")]
    IReadOnlyList<double> NormalizedBounds,
    [property: JsonPropertyName("recognizer")]
    string RecognizerId);

public sealed record StateCandidate(
    string SchemaVersion,
    string StateId,
    double Confidence,
    IReadOnlyList<EvidenceRegion> EvidenceRegions);

public sealed record ObservationResult(
    string SchemaVersion,
    string ObservationId,
    CapturedFrameReference Frame,
    CaptureAvailability CaptureAvailability,
    StateIdentityStatus StateIdentity,
    IReadOnlyList<StateCandidate> StateCandidates,
    string RecognizerVersion,
    long FreshnessMs,
    string? CaptureFailureReason);

public sealed record AffordanceLocator(
    string SchemaVersion,
    string LocatorType,
    IReadOnlyList<double> NormalizedBounds,
    string LocatorRevision);

public sealed record VisualPatchSignature(
    string SchemaVersion,
    int SampleWidth,
    int SampleHeight,
    string LumaBase64,
    string Sha256);

public sealed record AffordanceCandidate(
    string SchemaVersion,
    string CandidateId,
    string ObservationId,
    long FrameSequence,
    long TransformRevision,
    string TargetWindowSourceId,
    AffordanceLocator Locator,
    IReadOnlyList<EvidenceRegion> EvidenceRegions,
    double Confidence,
    IReadOnlyList<string> AllowedPrimitives,
    string? SemanticKind = null,
    string? SemanticLabel = null,
    VisualPatchSignature? VisualPatch = null,
    IReadOnlyList<string>? KeyTokens = null,
    int? VerticalScrollSteps = null,
    int? HorizontalScrollSteps = null,
    IReadOnlyList<double>? DragDestinationNormalized = null,
    IReadOnlyList<string>? ContextTexts = null);

public sealed record ObservedScene(
    string SchemaVersion,
    string SceneId,
    string ObservationId,
    CapturedFrameReference Frame,
    CaptureAvailability CaptureAvailability,
    StateIdentityStatus StateIdentity,
    string? StateHypothesisId,
    IReadOnlyList<StateCandidate> StateCandidates,
    IReadOnlyList<AffordanceCandidate> Affordances,
    string PerceptionVersion,
    SceneDiscoveryEvidence? DiscoveryEvidence = null,
    VisualPatchSignature? SceneVisualPatch = null);

public sealed record SceneGroundingRegion(
    string Text,
    EvidenceRegion EvidenceRegion);

public sealed record SceneDiscoveryEvidence(
    string ProviderId,
    string ModelId,
    string PromptRevision,
    string PromptSha256,
    string Status,
    string Failure,
    string? FailureDetail,
    string RawResponse,
    long ElapsedMilliseconds,
    int ExternalAiTransmissionCount,
    decimal ExternalAiApiCostUsd,
    IReadOnlyList<string>? LocalGroundingTexts = null,
    IReadOnlyList<string>? ProposedLabels = null,
    IReadOnlyList<string>? GroundingDiagnostics = null,
    IReadOnlyList<SceneGroundingRegion>? LocalGroundingRegions = null);

public interface IObservationSource
{
    ObservationResult Observe(CapturedFrame frame);
}

public enum KnowledgePackTrust
{
    Untrusted,
}

public sealed record KnowledgePackGame(
    string SchemaVersion,
    string Name,
    string Build,
    string Locale);

public sealed record SupportedEnvironment(
    string SchemaVersion,
    string Build,
    string Locale,
    double UiScale,
    string Resolution,
    string DisplayMode,
    double Dpi,
    bool Hdr,
    CaptureBackend CaptureBackend,
    string InputRoute,
    string ScreenGraphVersion,
    string RecognizerVersion);

public sealed record KnowledgePackSection(
    string SchemaVersion,
    string Path,
    string Sha256);

public sealed record KnowledgePackProvenance(
    string SchemaVersion,
    string Author,
    DateTimeOffset CreatedUtc,
    string Source,
    string License);

public sealed record KnowledgePackMigration(
    string SchemaVersion,
    string FromPackVersion,
    string ToPackVersion,
    IReadOnlyDictionary<string, string> FieldRenames);

public sealed record KnowledgePackState(
    string SchemaVersion,
    string StateId,
    IReadOnlyList<string> AnchorRefs,
    IReadOnlyList<string> SuccessConditionRefs,
    IReadOnlyList<string> ActionRefs);

public sealed record KnowledgePackManifest(
    string SchemaVersion,
    string PackId,
    string PackVersion,
    KnowledgePackGame Game,
    IReadOnlyList<SupportedEnvironment> SupportedEnvironments,
    IReadOnlyDictionary<string, KnowledgePackSection> Sections,
    KnowledgePackProvenance Provenance,
    KnowledgePackTrust Trust,
    IReadOnlyList<KnowledgePackMigration> Migrations);

public enum ScreenGraphVerificationState
{
    Candidate,
    Verified,
}

public sealed record ScreenGraphNode(
    string SchemaVersion,
    string StateId,
    ScreenGraphVerificationState VerificationState);

public sealed record ScreenGraphEdge(
    string SchemaVersion,
    string FromStateId,
    string ToStateId,
    string? VisualTargetRef,
    string? AttributedActionRef,
    ScreenGraphVerificationState VerificationState);

public sealed record ScreenGraph(
    string SchemaVersion,
    string GraphVersionId,
    IReadOnlyList<ScreenGraphNode> Nodes,
    IReadOnlyList<ScreenGraphEdge> Edges,
    string EnvironmentScope);

public sealed record KnowledgePackDocument(
    KnowledgePackManifest Manifest,
    IReadOnlyList<KnowledgePackState> States,
    ScreenGraph ScreenGraph);
