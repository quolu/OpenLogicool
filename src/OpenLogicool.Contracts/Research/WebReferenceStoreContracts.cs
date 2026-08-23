namespace OpenLogicool.Contracts.Research;

/// <summary>ResearchRunはwire上にrevisionを持たないため、store採番を外側へ保持する。</summary>
public sealed record ResearchRunRevisionRecord(long RevisionNumber, ResearchRun Run);

/// <summary>利用者が保存内容を確認・移送できる、削除済みpayloadを含まないexport。</summary>
public sealed record WebReferenceExportBundle(
    string SchemaVersion,
    DateTimeOffset ExportedUtc,
    IReadOnlyList<WebReferenceSource> Sources,
    IReadOnlyList<ReferenceDocument> Documents,
    IReadOnlyList<WebReferenceFact> Facts,
    IReadOnlyList<WebReferenceContradiction> Contradictions,
    IReadOnlyList<ResearchRunRevisionRecord> ResearchRuns,
    IReadOnlyList<WebReferenceDeletionTombstone> Tombstones,
    IReadOnlyList<WebReferenceSourceExclusion> Exclusions,
    IReadOnlyList<WebReferenceReacquisitionRequest> ReacquisitionRequests);

/// <summary>
/// STEP 0参照のappend-only port。通常データにupdateを公開せず、物理削除はsource単位の
/// preview→delete+tombstone transactionだけに閉じる。
/// </summary>
public interface IWebReferenceStore
{
    void AppendSource(WebReferenceSource source);

    void AppendDocument(ReferenceDocument document);

    void AppendFact(WebReferenceFact fact);

    void AppendContradiction(WebReferenceContradiction contradiction);

    long AppendResearchRun(ResearchRun run);

    void AppendExclusion(WebReferenceSourceExclusion exclusion);

    void AppendReacquisitionRequest(WebReferenceReacquisitionRequest request);

    IReadOnlyList<WebReferenceSource> ListSources();

    IReadOnlyList<ReferenceDocument> ListDocuments(string? sourceId = null);

    IReadOnlyList<WebReferenceFact> ListFacts();

    IReadOnlyList<WebReferenceContradiction> ListContradictions();

    IReadOnlyList<ResearchRunRevisionRecord> ListResearchRuns(string? runId = null);

    IReadOnlyList<WebReferenceDeletionTombstone> ListTombstones();

    IReadOnlyList<WebReferenceSourceExclusion> ListExclusions();

    IReadOnlyList<WebReferenceReacquisitionRequest> ListReacquisitionRequests();

    WebReferenceDeletionPreview PreviewDeleteSource(string sourceId);

    WebReferenceDeletionTombstone DeleteSource(
        string sourceId,
        string tombstoneId,
        DateTimeOffset deletedUtc,
        string reason);

    WebReferenceExportBundle Export(DateTimeOffset exportedUtc);
}
