using System.Text.Json.Serialization;

namespace OpenLogicool.Contracts.Research;

public enum WebReferenceSourceKind
{
    Official,
    Guide,
    Update,
    News,
    Other,
}

public enum WebReferenceAcquisitionMethod
{
    DirectHttp,
    SearchResult,
    UserProvided,
}

public enum SourceTermsDisposition
{
    FullTextAllowed,
    SummaryAllowed,
    Unknown,
    Unavailable,
    Rejected,
}

public enum RobotsDisposition
{
    Allowed,
    Unknown,
    Unavailable,
    Rejected,
}

public enum SourcePolicy
{
    FullTextAllowed,
    SummaryOnly,
    LinkOnly,
    Blocked,
}

public enum SourcePolicyReason
{
    ExplicitFullTextPermission,
    ExplicitSummaryPermission,
    GameWithSummaryOnly,
    TermsUnknown,
    TermsUnavailable,
    TermsRejected,
    RobotsUnknown,
    RobotsUnavailable,
    RobotsRejected,
}

public sealed record SourcePolicyEvidence(
    string SchemaVersion,
    SourceTermsDisposition Terms,
    RobotsDisposition Robots);

public sealed record ReferenceQuoteScope(
    string SchemaVersion,
    int MaxExcerptCount,
    int MaxExcerptCharacters);

public sealed record SourcePolicyDecision(
    string SchemaVersion,
    SourcePolicy Policy,
    SourcePolicyReason Reason,
    ReferenceQuoteScope QuoteScope);

/// <summary>Game OperatorのAI処理場所。製品契約は利用者端末内だけを表す。</summary>
public enum AiExecutionLocation
{
    LocalDevice,
}

public sealed record AiSummaryProvenance(
    string SchemaVersion,
    string Provider,
    string Model,
    string PromptRevision,
    DateTimeOffset GeneratedUtc,
    AiExecutionLocation ExecutionLocation,
    decimal ExternalApiCostUsd);

public sealed record WebReferenceProvenance(
    string SchemaVersion,
    string ContentDigest,
    WebReferenceAcquisitionMethod AcquisitionMethod,
    DateTimeOffset RetrievedUtc,
    AiSummaryProvenance? AiSummary,
    DateTimeOffset? ExpiresUtc);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "sourceState")]
[JsonDerivedType(typeof(AcquiredWebReferenceSource), "acquired")]
[JsonDerivedType(typeof(RestrictedWebReferenceSource), "restricted")]
public abstract record WebReferenceSource(
    string SchemaVersion,
    string SourceId,
    Uri Url,
    Uri CanonicalUrl,
    SourcePolicyEvidence PolicyEvidence,
    SourcePolicyDecision PolicyDecision);

/// <summary>本文取得が成立したsource。FullTextAllowedまたはSummaryOnlyだけを表す。</summary>
public sealed record AcquiredWebReferenceSource(
    string SchemaVersion,
    string SourceId,
    Uri Url,
    Uri CanonicalUrl,
    string Title,
    string Publisher,
    DateTimeOffset? PublishedUtc,
    DateTimeOffset? UpdatedUtc,
    string Locale,
    WebReferenceSourceKind SourceKind,
    SourcePolicyEvidence PolicyEvidence,
    SourcePolicyDecision PolicyDecision,
    WebReferenceProvenance Provenance)
    : WebReferenceSource(SchemaVersion, SourceId, Url, CanonicalUrl, PolicyEvidence, PolicyDecision);

/// <summary>
/// policy評価で本文取得前に止まったsource。未知のtitle等を偽値で埋めず、LinkOnly／Blockedを記録する。
/// </summary>
public sealed record RestrictedWebReferenceSource(
    string SchemaVersion,
    string SourceId,
    Uri Url,
    Uri CanonicalUrl,
    string? Title,
    string? Publisher,
    string? Locale,
    SourcePolicyEvidence PolicyEvidence,
    SourcePolicyDecision PolicyDecision,
    DateTimeOffset EvaluatedUtc)
    : WebReferenceSource(SchemaVersion, SourceId, Url, CanonicalUrl, PolicyEvidence, PolicyDecision);

/// <summary>取得前に保存内容・引用・ローカルAI処理・外部AI API費用0・期限を表示するpure plan。</summary>
public sealed record WebReferenceAcquisitionPlan(
    string SchemaVersion,
    string PlanId,
    Uri Url,
    Uri CanonicalUrl,
    WebReferenceAcquisitionMethod AcquisitionMethod,
    SourcePolicyEvidence PolicyEvidence,
    SourcePolicyDecision PolicyDecision,
    string? SummaryProvider,
    string? SummaryModel,
    AiExecutionLocation ExecutionLocation,
    decimal ExternalApiCostUsd,
    DateTimeOffset? ExpiresUtc);

public sealed record WebReferenceSourceExclusion(
    string SchemaVersion,
    string ExclusionId,
    Uri Url,
    DateTimeOffset ExcludedUtc,
    string Reason);

public sealed record WebReferenceReacquisitionRequest(
    string SchemaVersion,
    string RequestId,
    string SourceId,
    DateTimeOffset RequestedUtc,
    string Reason);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "bodyKind")]
[JsonDerivedType(typeof(FullTextReferenceBody), "full-text")]
[JsonDerivedType(typeof(SummaryReferenceBody), "summary-card")]
[JsonDerivedType(typeof(LinkOnlyReferenceBody), "link-only")]
[JsonDerivedType(typeof(BlockedReferenceBody), "blocked")]
public abstract record ReferenceDocumentBody(string SchemaVersion);

public sealed record FullTextReferenceBody(
    string SchemaVersion,
    string NormalizedMarkdown)
    : ReferenceDocumentBody(SchemaVersion);

/// <summary>
/// SummaryOnly用の参照カード。raw HTML、画像、変換全文を保持できるfieldを持たない。
/// </summary>
public sealed record SummaryReferenceBody(
    string SchemaVersion,
    string StructuredSummaryMarkdown,
    IReadOnlyList<string> EvidenceExcerpts,
    IReadOnlyList<string> Terms)
    : ReferenceDocumentBody(SchemaVersion);

public sealed record LinkOnlyReferenceBody(
    string SchemaVersion,
    string Reason)
    : ReferenceDocumentBody(SchemaVersion);

public sealed record BlockedReferenceBody(
    string SchemaVersion,
    string Reason)
    : ReferenceDocumentBody(SchemaVersion);

public sealed record ReferenceDocument(
    string SchemaVersion,
    string DocumentId,
    long Revision,
    string? ParentDocumentId,
    string SourceId,
    SourcePolicy Policy,
    DateTimeOffset CreatedUtc,
    ReferenceDocumentBody Body);

public enum WebReferenceFactKind
{
    Mechanic,
    Rule,
    Daily,
    Reset,
    Resource,
    NavigationHint,
    Term,
    Other,
}

/// <summary>Webだけで生成できる状態の閉集合。Verifiedは意図的に存在しない。</summary>
public enum WebReferenceFactValidity
{
    Hypothesis,
    Stale,
    Contradicted,
}

public sealed record WebReferenceFactScope(
    string SchemaVersion,
    string? GameBuild,
    string? Locale,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc);

public sealed record WebReferenceFact(
    string SchemaVersion,
    string FactId,
    long Revision,
    string? ParentFactId,
    WebReferenceFactKind Kind,
    string Claim,
    IReadOnlyList<string> SourceReferenceIds,
    decimal Confidence,
    WebReferenceFactValidity Validity,
    WebReferenceFactScope Scope,
    IReadOnlyList<string> ContradictionIds,
    DateTimeOffset CreatedUtc);

public sealed record WebReferenceContradiction(
    string SchemaVersion,
    string ContradictionId,
    long Revision,
    string? ParentContradictionId,
    string LeftFactId,
    string RightFactId,
    IReadOnlyList<string> SourceReferenceIds,
    DateTimeOffset DetectedUtc,
    string Note);

public enum WebReferenceAcquisitionStatus
{
    Succeeded,
    ReusedExisting,
    PolicyLimited,
    ProviderUnselected,
    NetworkUnavailable,
    TermsRejected,
    RobotsRejected,
    HttpFailed,
    ParseFailed,
    Cancelled,
    TimedOut,
}

public sealed record WebReferenceAcquisitionAttempt(
    string SchemaVersion,
    string AttemptId,
    Uri RequestedUrl,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    WebReferenceAcquisitionStatus Status,
    string? SourceId,
    string? NewDocumentId,
    string? ExistingDocumentId,
    string? Detail);

public enum ResearchRunStatus
{
    Planned,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record ResearchRun(
    string SchemaVersion,
    string RunId,
    string GameId,
    string ResearchGoal,
    ResearchRunStatus Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? CompletedUtc,
    IReadOnlyList<WebReferenceAcquisitionAttempt> Attempts);

/// <summary>削除前に利用者へ示す識別子と量だけを持ち、payload本文は持たない。</summary>
public sealed record WebReferenceDeletionPreview(
    string SchemaVersion,
    string SourceId,
    IReadOnlyList<string> DocumentIds,
    IReadOnlyList<string> FactIds,
    IReadOnlyList<string> ContradictionIds,
    long PayloadBytes);

/// <summary>
/// payloadを物理削除した後にappend-only系列へ追記する墓標。本文、要約、引用、claimは保持しない。
/// </summary>
public sealed record WebReferenceDeletionTombstone(
    string SchemaVersion,
    string TombstoneId,
    string SourceId,
    DateTimeOffset DeletedUtc,
    string Reason,
    IReadOnlyList<string> DeletedDocumentIds,
    IReadOnlyList<string> DeletedFactIds,
    IReadOnlyList<string> DeletedContradictionIds);
