using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Contracts.Perception;

public enum ObservationStatus
{
    Known,
    Ambiguous,
    Unknown,
    Unavailable,
}

public sealed record CapturedFrameReference(
    string SchemaVersion,
    string SourceId,
    CaptureBackend Backend,
    long Sequence,
    double MonotonicMs,
    DateTimeOffset WallClockUtc,
    long TransformRevision,
    long FreshnessMs,
    long LastChangeMs);

public sealed record EvidenceRegion(
    string SchemaVersion,
    string Shape,
    IReadOnlyList<double> NormalizedBounds,
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
    ObservationStatus Status,
    IReadOnlyList<StateCandidate> StateCandidates,
    string RecognizerVersion,
    long FreshnessMs,
    string? UnavailableReason);

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
