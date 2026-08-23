using OpenLogicool.Contracts.Research;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;

namespace OpenLogicool.Host.Research;

/// <summary>STEP 0画面を既存acquisitionとappend-only storeへ配線するHost adapter。</summary>
public sealed class HostWebResearchIntent(
    IWebReferenceStore store,
    WebReferenceAcquisitionService acquisition,
    string? localProviderId = null,
    string? localModelId = null,
    TimeProvider? timeProvider = null) : IWebResearchIntent
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly GameWithReferenceAdapter _gameWith = new(acquisition);

    public WebResearchPreview Preview(
        Uri url,
        SourceTermsDisposition terms,
        RobotsDisposition robots,
        DateTimeOffset? expiresUtc)
    {
        ArgumentNullException.ThrowIfNull(url);
        var evidence = new SourcePolicyEvidence(ContractSchemaVersions.Revision01, terms, robots);
        var decision = SourcePolicyEvaluator.Evaluate(url, url, evidence);
        var needsSummary = decision.Policy == SourcePolicy.SummaryOnly;
        var plan = new WebReferenceAcquisitionPlan(
            ContractSchemaVersions.Revision01,
            NewId("plan"),
            url,
            url,
            WebReferenceAcquisitionMethod.DirectHttp,
            evidence,
            decision,
            needsSummary ? localProviderId : null,
            needsSummary ? localModelId : null,
            AiExecutionLocation.LocalDevice,
            ExternalApiCostUsd: 0m,
            expiresUtc);
        WebReferenceContractSchema.Validate(plan);

        return new WebResearchPreview(
            plan,
            decision.Policy switch
            {
                SourcePolicy.FullTextAllowed => "本文を取得して保存",
                SourcePolicy.SummaryOnly => "要約カードだけ保存",
                SourcePolicy.LinkOnly => "リンクと判定だけ保存",
                SourcePolicy.Blocked => "取得せず拒否理由だけ保存",
                _ => throw new ArgumentOutOfRangeException(nameof(decision)),
            },
            decision.Policy switch
            {
                SourcePolicy.FullTextAllowed => "正規化Markdown本文",
                SourcePolicy.SummaryOnly => "出典付き要約カード（raw HTML／全文Markdownは保存しない）",
                SourcePolicy.LinkOnly => "URLと取得判定だけ",
                SourcePolicy.Blocked => "拒否理由だけ",
                _ => throw new ArgumentOutOfRangeException(nameof(decision)),
            },
            $"最大{decision.QuoteScope.MaxExcerptCharacters}文字×{decision.QuoteScope.MaxExcerptCount}件",
            needsSummary
                ? localProviderId is null ? "このPC内（ローカルmodel未選定）" : $"このPC内（{localProviderId} / {localModelId}）"
                : "不要",
            "なし",
            "0円",
            expiresUtc?.ToString("u") ?? "期限なし");
    }

    public async Task<WebResearchOperationResult> StartAsync(
        WebReferenceAcquisitionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var command = NewCommand(plan, WebReferenceSourceKind.Other);
        var result = IsGameWith(plan.Url)
            ? await _gameWith.AcquireAsync(command, cancellationToken).ConfigureAwait(false)
            : await acquisition.AcquireAsync(command, cancellationToken).ConfigureAwait(false);
        Persist(result);
        return Present(result);
    }

    public void Exclude(Uri url, string reason)
    {
        ArgumentNullException.ThrowIfNull(url);
        RequireText(reason, nameof(reason));
        store.AppendExclusion(new WebReferenceSourceExclusion(
            ContractSchemaVersions.Revision01,
            NewId("exclude"),
            url,
            _timeProvider.GetUtcNow(),
            reason));
    }

    public async Task<WebResearchOperationResult> ReacquireAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        RequireText(sourceId, nameof(sourceId));
        var source = store.ListSources().LastOrDefault(item => item.SourceId == sourceId)
                     ?? throw new InvalidOperationException("再取得元のsourceが見つかりません。");
        store.AppendReacquisitionRequest(new WebReferenceReacquisitionRequest(
            ContractSchemaVersions.Revision01,
            NewId("reacquire"),
            sourceId,
            _timeProvider.GetUtcNow(),
            "利用者がSTEP 0画面から再取得"));

        var needsSummary = source.PolicyDecision.Policy == SourcePolicy.SummaryOnly;
        var plan = new WebReferenceAcquisitionPlan(
            ContractSchemaVersions.Revision01,
            NewId("plan"),
            source.Url,
            source.CanonicalUrl,
            WebReferenceAcquisitionMethod.DirectHttp,
            source.PolicyEvidence,
            source.PolicyDecision,
            needsSummary ? localProviderId : null,
            needsSummary ? localModelId : null,
            AiExecutionLocation.LocalDevice,
            ExternalApiCostUsd: 0m,
            source is AcquiredWebReferenceSource acquired ? acquired.Provenance.ExpiresUtc : null);
        return await StartAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<WebResearchDocumentItem> ListDocuments()
    {
        var sources = store.ListSources().ToDictionary(item => item.SourceId, StringComparer.Ordinal);
        return store.ListDocuments()
            .OrderByDescending(item => item.CreatedUtc)
            .Select(document => new WebResearchDocumentItem(
                document.SourceId,
                document.DocumentId,
                sources.TryGetValue(document.SourceId, out var source) && source is AcquiredWebReferenceSource acquired
                    ? acquired.Title
                    : source?.CanonicalUrl.ToString() ?? document.SourceId,
                document.Policy,
                document.Revision))
            .ToArray();
    }

    public string GetMarkdown(string documentId)
    {
        RequireText(documentId, nameof(documentId));
        var document = store.ListDocuments().SingleOrDefault(item => item.DocumentId == documentId)
                       ?? throw new InvalidOperationException("Reference documentが見つかりません。");
        return document.Body switch
        {
            FullTextReferenceBody full => full.NormalizedMarkdown,
            SummaryReferenceBody summary => summary.StructuredSummaryMarkdown
                                              + "\n\n## 根拠\n"
                                              + string.Join("\n", summary.EvidenceExcerpts.Select(item => $"- {item}"))
                                              + "\n\n## 用語\n"
                                              + string.Join("\n", summary.Terms.Select(item => $"- {item}")),
            LinkOnlyReferenceBody link => $"# Link Only\n\n{link.Reason}",
            BlockedReferenceBody blocked => $"# Blocked\n\n{blocked.Reason}",
            _ => throw new ArgumentOutOfRangeException(nameof(document)),
        };
    }

    public WebReferenceDeletionPreview PreviewDelete(string sourceId)
    {
        RequireText(sourceId, nameof(sourceId));
        return store.PreviewDeleteSource(sourceId);
    }

    public void Delete(string sourceId, string reason)
    {
        RequireText(sourceId, nameof(sourceId));
        RequireText(reason, nameof(reason));
        store.DeleteSource(sourceId, NewId("tombstone"), _timeProvider.GetUtcNow(), reason);
    }

    private WebReferenceAcquisitionCommand NewCommand(
        WebReferenceAcquisitionPlan plan,
        WebReferenceSourceKind sourceKind) => new(
            NewId("attempt"),
            NewId("source"),
            NewId("document"),
            plan,
            PublisherFallback: null,
            LocaleFallback: "ja-JP",
            sourceKind,
            Timeout: TimeSpan.FromSeconds(30));

    private void Persist(WebReferenceAcquisitionResult result)
    {
        if (result.Source is not null)
        {
            store.AppendSource(result.Source);
        }

        if (result.Document is not null)
        {
            store.AppendDocument(result.Document);
        }

        var status = result.Attempt.Status switch
        {
            WebReferenceAcquisitionStatus.Succeeded or WebReferenceAcquisitionStatus.ReusedExisting
                or WebReferenceAcquisitionStatus.PolicyLimited => ResearchRunStatus.Completed,
            WebReferenceAcquisitionStatus.Cancelled => ResearchRunStatus.Cancelled,
            _ => ResearchRunStatus.Failed,
        };
        store.AppendResearchRun(new ResearchRun(
            ContractSchemaVersions.Revision01,
            NewId("research-run"),
            "step0",
            "Web Referenceを取得する",
            status,
            result.Attempt.StartedUtc,
            result.Attempt.CompletedUtc,
            [result.Attempt]));
    }

    private static WebResearchOperationResult Present(WebReferenceAcquisitionResult result) => new(
        result.Attempt.Status == WebReferenceAcquisitionStatus.Succeeded,
        result.Attempt.Status switch
        {
            WebReferenceAcquisitionStatus.Succeeded => "取得してMarkdown Referenceへ保存しました。",
            WebReferenceAcquisitionStatus.ProviderUnselected => "ローカルAI modelが未選定のため、要約を開始できません。外部APIへは送信していません。",
            WebReferenceAcquisitionStatus.PolicyLimited => "利用条件に従い、本文を取得せず参照判定だけ保存しました。",
            _ => $"取得は完了しませんでした: {result.Attempt.Status} — {result.Attempt.Detail}",
        },
        result.Source?.SourceId,
        result.Document?.DocumentId);

    private static bool IsGameWith(Uri url)
    {
        var host = url.IdnHost.TrimEnd('.');
        return string.Equals(host, "gamewith.jp", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".gamewith.jp", StringComparison.OrdinalIgnoreCase);
    }

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static void RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("値が空です。", parameterName);
        }
    }
}
