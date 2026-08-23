using OpenLogicool.Contracts.Research;

namespace OpenLogicool.Desktop;

public sealed record WebResearchPreview(
    WebReferenceAcquisitionPlan Plan,
    string PolicyLabel,
    string SavedContentLabel,
    string QuoteLabel,
    string LocalAiLabel,
    string ExternalTransmissionLabel,
    string ExternalApiCostLabel,
    string ExpiryLabel);

public sealed record WebResearchOperationResult(
    bool Succeeded,
    string StatusLabel,
    string? SourceId,
    string? DocumentId);

public sealed record WebResearchDocumentItem(
    string SourceId,
    string DocumentId,
    string Title,
    SourcePolicy Policy,
    long Revision)
{
    public string DisplayLabel => $"{Title}　[{PolicyLabel}]　版 {Revision}";

    private string PolicyLabel => Policy switch
    {
        SourcePolicy.FullTextAllowed => "本文保存",
        SourcePolicy.SummaryOnly => "要約カード",
        SourcePolicy.LinkOnly => "リンクのみ",
        SourcePolicy.Blocked => "取得しない",
        _ => "不明",
    };
}

/// <summary>STEP 0 UIからHostの取得・store境界を一つのpublic intentとして呼ぶ。</summary>
public interface IWebResearchIntent
{
    WebResearchPreview Preview(
        Uri url,
        SourceTermsDisposition terms,
        RobotsDisposition robots,
        DateTimeOffset? expiresUtc);

    Task<WebResearchOperationResult> StartAsync(
        WebReferenceAcquisitionPlan plan,
        CancellationToken cancellationToken = default);

    void Exclude(Uri url, string reason);

    Task<WebResearchOperationResult> ReacquireAsync(
        string sourceId,
        CancellationToken cancellationToken = default);

    IReadOnlyList<WebResearchDocumentItem> ListDocuments();

    string GetMarkdown(string documentId);

    WebReferenceDeletionPreview PreviewDelete(string sourceId);

    void Delete(string sourceId, string reason);
}

/// <summary>WPFから副作用を分離して、previewから削除まで同じintent経路を通す。</summary>
public sealed class WebResearchWorkspace(IWebResearchIntent intent)
{
    public WebResearchPreview? CurrentPreview { get; private set; }

    public WebResearchPreview Preview(
        Uri url,
        SourceTermsDisposition terms,
        RobotsDisposition robots,
        DateTimeOffset? expiresUtc = null)
    {
        CurrentPreview = intent.Preview(url, terms, robots, expiresUtc);
        return CurrentPreview;
    }

    public Task<WebResearchOperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentPreview is null)
        {
            throw new InvalidOperationException("先に取得内容を確認してください。");
        }

        return intent.StartAsync(CurrentPreview.Plan, cancellationToken);
    }

    public void Exclude(Uri url, string reason) => intent.Exclude(url, reason);

    public Task<WebResearchOperationResult> ReacquireAsync(
        string sourceId,
        CancellationToken cancellationToken = default) =>
        intent.ReacquireAsync(sourceId, cancellationToken);

    public IReadOnlyList<WebResearchDocumentItem> ListDocuments() => intent.ListDocuments();

    public string GetMarkdown(string documentId) => intent.GetMarkdown(documentId);

    public WebReferenceDeletionPreview PreviewDelete(string sourceId) => intent.PreviewDelete(sourceId);

    public void Delete(string sourceId, string reason) => intent.Delete(sourceId, reason);
}
