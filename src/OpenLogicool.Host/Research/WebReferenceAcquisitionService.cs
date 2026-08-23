using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using AngleSharp;
using AngleSharp.Dom;
using OpenLogicool.Contracts.Research;
using OpenLogicool.Contracts.Shared;
using ReverseMarkdown;

namespace OpenLogicool.Host.Research;

public sealed record WebReferenceHttpPayload(
    Uri RequestedUrl,
    Uri FinalUrl,
    HttpStatusCode StatusCode,
    string? MediaType,
    byte[] Content);

public interface IWebReferenceHttpTransport
{
    Task<WebReferenceHttpPayload> FetchAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>HttpClientを唯一のnetwork境界に閉じ、redirect後URLと取得bytesを返す。</summary>
public sealed class HttpClientWebReferenceTransport(HttpClient client, int maxPayloadBytes = 4 * 1024 * 1024)
    : IWebReferenceHttpTransport
{
    public async Task<WebReferenceHttpPayload> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (maxPayloadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maxPayloadBytes)
        {
            throw new WebReferencePayloadTooLargeException(maxPayloadBytes);
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (content.Length > maxPayloadBytes)
        {
            throw new WebReferencePayloadTooLargeException(maxPayloadBytes);
        }

        return new WebReferenceHttpPayload(
            url,
            response.RequestMessage?.RequestUri ?? url,
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            content);
    }
}

public sealed class WebReferencePayloadTooLargeException(int maxPayloadBytes)
    : Exception($"Web参照本文が上限 {maxPayloadBytes} bytes を超えました。")
{
}

public sealed record NormalizedWebReferencePage(
    Uri CanonicalUrl,
    string Title,
    string Publisher,
    string Locale,
    DateTimeOffset? PublishedUtc,
    DateTimeOffset? UpdatedUtc,
    string Markdown);

public interface IWebReferenceHtmlNormalizer
{
    Task<NormalizedWebReferencePage> NormalizeAsync(
        byte[] htmlBytes,
        Uri finalUrl,
        string? publisherFallback,
        string localeFallback,
        CancellationToken cancellationToken);
}

/// <summary>HTML5 parserでmetadataを読み、本文だけをMarkdownへ正規化する。</summary>
public sealed class WebReferenceHtmlNormalizer : IWebReferenceHtmlNormalizer
{
    public async Task<NormalizedWebReferencePage> NormalizeAsync(
        byte[] htmlBytes,
        Uri finalUrl,
        string? publisherFallback,
        string localeFallback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(htmlBytes);
        ArgumentNullException.ThrowIfNull(finalUrl);
        if (string.IsNullOrWhiteSpace(localeFallback))
        {
            throw new ArgumentException("locale fallbackが空です。", nameof(localeFallback));
        }

        var html = DecodeUtf8(htmlBytes);
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(
            request => request.Address(finalUrl).Content(html),
            cancellationToken).ConfigureAwait(false);

        var canonical = ResolveCanonical(document, finalUrl);
        var title = document.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new FormatException("HTML titleを取得できません。");
        }

        var publisher = Meta(document, "property", "og:site_name")
            ?? Meta(document, "name", "author")
            ?? publisherFallback
            ?? canonical.IdnHost;
        var locale = document.DocumentElement?.GetAttribute("lang")?.Trim();
        if (string.IsNullOrWhiteSpace(locale))
        {
            locale = localeFallback;
        }

        var content = document.QuerySelector("article")
            ?? document.QuerySelector("main")
            ?? document.Body
            ?? throw new FormatException("HTML bodyを取得できません。");
        // CommonMark/GitHub flavorは先頭のHTML blockを仕様どおりrawで通すため、
        // Reference本文はHTML tagを残さないDefault writerを使う。
        var config = new Config { Flavor = Config.MarkdownFlavor.Default };
        config.Formatting.RemoveComments = true;
        config.Links.SmartHref = true;
        config.Preprocess
            .RemoveScripts()
            .RemoveStyles()
            .Remove("nav, footer, aside, .advertisement")
            .Unwrap("span, font");
        var markdown = new Converter(config).Convert(content.InnerHtml).Trim();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new FormatException("HTML本文をMarkdownへ変換できません。");
        }

        return new NormalizedWebReferencePage(
            canonical,
            title,
            publisher.Trim(),
            locale,
            ParseUtc(Meta(document, "property", "article:published_time")),
            ParseUtc(Meta(document, "property", "article:modified_time")),
            markdown);
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(
            stream,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static Uri ResolveCanonical(IDocument document, Uri finalUrl)
    {
        var href = document.QuerySelector("link[rel~='canonical']")?.GetAttribute("href");
        var candidate = string.IsNullOrWhiteSpace(href) ? finalUrl : new Uri(finalUrl, href);
        if (!candidate.IsAbsoluteUri || candidate.Scheme is not ("http" or "https"))
        {
            throw new FormatException("canonical URLがHTTP(S)ではありません。");
        }

        var builder = new UriBuilder(candidate)
        {
            Fragment = string.Empty,
            Host = candidate.IdnHost.ToLowerInvariant(),
        };
        if (candidate.IsDefaultPort)
        {
            builder.Port = -1;
        }

        return builder.Uri;
    }

    private static string? Meta(IDocument document, string attribute, string value) =>
        document.QuerySelector($"meta[{attribute}='{value}']")?.GetAttribute("content")?.Trim() is { Length: > 0 } content
            ? content
            : null;

    private static DateTimeOffset? ParseUtc(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
                ? parsed
                : null;
}

public sealed record WebReferenceSummaryRequest(
    string Provider,
    string Model,
    Uri CanonicalUrl,
    string Title,
    string Markdown,
    ReferenceQuoteScope QuoteScope);

public interface IWebReferenceSummaryProvider
{
    Task<SummaryReferenceBody> SummarizeAsync(
        WebReferenceSummaryRequest request,
        CancellationToken cancellationToken);
}

public sealed record WebReferenceAcquisitionCommand(
    string AttemptId,
    string SourceId,
    string DocumentId,
    WebReferenceAcquisitionPlan Plan,
    string? PublisherFallback,
    string LocaleFallback,
    WebReferenceSourceKind SourceKind,
    TimeSpan Timeout);

public sealed record WebReferenceAcquisitionResult(
    WebReferenceSource? Source,
    ReferenceDocument? Document,
    WebReferenceAcquisitionAttempt Attempt);

/// <summary>
/// STEP 0の一取得を、明示された一つのsourceとproviderだけで実行する。
/// cache・別source・別providerへのfallbackは持たない。
/// </summary>
public sealed class WebReferenceAcquisitionService(
    IWebReferenceHttpTransport transport,
    IWebReferenceHtmlNormalizer normalizer,
    IWebReferenceSummaryProvider? summaryProvider = null,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<WebReferenceAcquisitionResult> AcquireAsync(
        WebReferenceAcquisitionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        WebReferenceContractSchema.Validate(command.Plan);
        RequireText(command.AttemptId, nameof(command.AttemptId));
        RequireText(command.SourceId, nameof(command.SourceId));
        RequireText(command.DocumentId, nameof(command.DocumentId));
        RequireText(command.LocaleFallback, nameof(command.LocaleFallback));
        if (command.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "timeoutは正でなければなりません。");
        }

        var started = _timeProvider.GetUtcNow();
        var plannedDecision = SourcePolicyEvaluator.Evaluate(
            command.Plan.Url,
            command.Plan.CanonicalUrl,
            command.Plan.PolicyEvidence);
        if (plannedDecision.Policy is SourcePolicy.LinkOnly or SourcePolicy.Blocked)
        {
            return PolicyLimited(command, command.Plan.CanonicalUrl, plannedDecision, started);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(command.Timeout);
        WebReferenceHttpPayload payload;
        try
        {
            payload = await transport.FetchAsync(command.Plan.Url, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure(
                command,
                cancellationToken.IsCancellationRequested
                    ? WebReferenceAcquisitionStatus.Cancelled
                    : WebReferenceAcquisitionStatus.TimedOut,
                cancellationToken.IsCancellationRequested ? "利用者により取消されました。" : "取得がtimeoutしました。",
                started);
        }
        catch (HttpRequestException exception)
        {
            return Failure(command, WebReferenceAcquisitionStatus.NetworkUnavailable, exception.Message, started);
        }
        catch (WebReferencePayloadTooLargeException exception)
        {
            return Failure(command, WebReferenceAcquisitionStatus.ParseFailed, exception.Message, started);
        }
        catch (IOException exception)
        {
            return Failure(command, WebReferenceAcquisitionStatus.NetworkUnavailable, exception.Message, started);
        }

        if ((int)payload.StatusCode is < 200 or >= 300)
        {
            return Failure(
                command,
                WebReferenceAcquisitionStatus.HttpFailed,
                $"HTTP {(int)payload.StatusCode} ({payload.StatusCode})",
                started);
        }

        if (payload.MediaType is not null
            && !string.Equals(payload.MediaType, "text/html", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(payload.MediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                command,
                WebReferenceAcquisitionStatus.ParseFailed,
                $"未対応content type: {payload.MediaType}",
                started);
        }

        NormalizedWebReferencePage page;
        try
        {
            page = await normalizer.NormalizeAsync(
                payload.Content,
                payload.FinalUrl,
                command.PublisherFallback,
                command.LocaleFallback,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure(
                command,
                cancellationToken.IsCancellationRequested
                    ? WebReferenceAcquisitionStatus.Cancelled
                    : WebReferenceAcquisitionStatus.TimedOut,
                cancellationToken.IsCancellationRequested ? "利用者により取消されました。" : "変換がtimeoutしました。",
                started);
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException)
        {
            return Failure(command, WebReferenceAcquisitionStatus.ParseFailed, exception.Message, started);
        }

        var actualDecision = SourcePolicyEvaluator.Evaluate(
            command.Plan.Url,
            page.CanonicalUrl,
            command.Plan.PolicyEvidence);
        if (actualDecision.Policy is SourcePolicy.LinkOnly or SourcePolicy.Blocked)
        {
            return PolicyLimited(command, page.CanonicalUrl, actualDecision, started);
        }

        ReferenceDocumentBody body;
        if (actualDecision.Policy == SourcePolicy.SummaryOnly)
        {
            if (summaryProvider is null
                || string.IsNullOrWhiteSpace(command.Plan.SummaryProvider)
                || string.IsNullOrWhiteSpace(command.Plan.SummaryModel))
            {
                return Failure(
                    command,
                    WebReferenceAcquisitionStatus.ProviderUnselected,
                    "SummaryOnly sourceの要約providerが選択されていません。",
                    started);
            }

            try
            {
                body = await summaryProvider.SummarizeAsync(
                    new WebReferenceSummaryRequest(
                        command.Plan.SummaryProvider,
                        command.Plan.SummaryModel,
                        page.CanonicalUrl,
                        page.Title,
                        page.Markdown,
                        actualDecision.QuoteScope),
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Failure(
                    command,
                    cancellationToken.IsCancellationRequested
                        ? WebReferenceAcquisitionStatus.Cancelled
                        : WebReferenceAcquisitionStatus.TimedOut,
                    cancellationToken.IsCancellationRequested ? "利用者により取消されました。" : "要約がtimeoutしました。",
                    started);
            }
        }
        else
        {
            body = new FullTextReferenceBody(ContractSchemaVersions.Revision01, page.Markdown);
        }

        var now = _timeProvider.GetUtcNow();
        var source = new AcquiredWebReferenceSource(
            ContractSchemaVersions.Revision01,
            command.SourceId,
            command.Plan.Url,
            page.CanonicalUrl,
            page.Title,
            page.Publisher,
            page.PublishedUtc,
            page.UpdatedUtc,
            page.Locale,
            command.SourceKind,
            command.Plan.PolicyEvidence,
            actualDecision,
            new WebReferenceProvenance(
                ContractSchemaVersions.Revision01,
                Convert.ToHexString(SHA256.HashData(payload.Content)).ToLowerInvariant(),
                command.Plan.AcquisitionMethod,
                now,
                actualDecision.Policy == SourcePolicy.SummaryOnly
                    ? new AiSummaryProvenance(
                        ContractSchemaVersions.Revision01,
                        command.Plan.SummaryProvider!,
                        command.Plan.SummaryModel!,
                        "step0-summary-v1",
                        now,
                        command.Plan.ExternalDestination!,
                        command.Plan.EstimatedCostUsd)
                    : null,
                command.Plan.ExpiresUtc));
        var document = new ReferenceDocument(
            ContractSchemaVersions.Revision01,
            command.DocumentId,
            1,
            null,
            command.SourceId,
            actualDecision.Policy,
            now,
            body);
        WebReferenceContractSchema.Validate(source);
        WebReferenceContractSchema.Validate(document, source);
        var attempt = Attempt(
            command,
            WebReferenceAcquisitionStatus.Succeeded,
            started,
            source.SourceId,
            document.DocumentId,
            null,
            null);
        return new WebReferenceAcquisitionResult(source, document, attempt);
    }

    private WebReferenceAcquisitionResult PolicyLimited(
        WebReferenceAcquisitionCommand command,
        Uri canonicalUrl,
        SourcePolicyDecision decision,
        DateTimeOffset started)
    {
        var now = _timeProvider.GetUtcNow();
        var source = new RestrictedWebReferenceSource(
            ContractSchemaVersions.Revision01,
            command.SourceId,
            command.Plan.Url,
            canonicalUrl,
            null,
            command.PublisherFallback,
            command.LocaleFallback,
            command.Plan.PolicyEvidence,
            decision,
            now);
        ReferenceDocumentBody body = decision.Policy == SourcePolicy.Blocked
            ? new BlockedReferenceBody(ContractSchemaVersions.Revision01, decision.Reason.ToString())
            : new LinkOnlyReferenceBody(ContractSchemaVersions.Revision01, decision.Reason.ToString());
        var document = new ReferenceDocument(
            ContractSchemaVersions.Revision01,
            command.DocumentId,
            1,
            null,
            command.SourceId,
            decision.Policy,
            now,
            body);
        var status = decision.Reason switch
        {
            SourcePolicyReason.TermsRejected => WebReferenceAcquisitionStatus.TermsRejected,
            SourcePolicyReason.RobotsRejected => WebReferenceAcquisitionStatus.RobotsRejected,
            _ => WebReferenceAcquisitionStatus.PolicyLimited,
        };
        WebReferenceContractSchema.Validate(source);
        WebReferenceContractSchema.Validate(document, source);
        return new WebReferenceAcquisitionResult(
            source,
            document,
            Attempt(command, status, started, source.SourceId, document.DocumentId, null, decision.Reason.ToString()));
    }

    private WebReferenceAcquisitionResult Failure(
        WebReferenceAcquisitionCommand command,
        WebReferenceAcquisitionStatus status,
        string detail,
        DateTimeOffset started) =>
        new(
            null,
            null,
            Attempt(command, status, started, null, null, null, detail));

    private WebReferenceAcquisitionAttempt Attempt(
        WebReferenceAcquisitionCommand command,
        WebReferenceAcquisitionStatus status,
        DateTimeOffset started,
        string? sourceId,
        string? newDocumentId,
        string? existingDocumentId,
        string? detail)
    {
        var attempt = new WebReferenceAcquisitionAttempt(
            ContractSchemaVersions.Revision01,
            command.AttemptId,
            command.Plan.Url,
            started,
            _timeProvider.GetUtcNow(),
            status,
            sourceId,
            newDocumentId,
            existingDocumentId,
            detail);
        WebReferenceContractSchema.Validate(attempt);
        return attempt;
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name}が空です。", name);
        }
    }
}

/// <summary>GameWith取得成功がSummaryOnly参照カード以外を返さないことをadapter境界でも固定する。</summary>
public sealed class GameWithReferenceAdapter(WebReferenceAcquisitionService acquisition)
{
    public async Task<WebReferenceAcquisitionResult> AcquireAsync(
        WebReferenceAcquisitionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!IsGameWith(command.Plan.Url) && !IsGameWith(command.Plan.CanonicalUrl))
        {
            throw new ArgumentException("GameWith adapterへGameWith以外のURLは渡せません。", nameof(command));
        }

        var result = await acquisition.AcquireAsync(command, cancellationToken).ConfigureAwait(false);
        if (result.Attempt.Status == WebReferenceAcquisitionStatus.Succeeded
            && result.Document?.Body is not SummaryReferenceBody)
        {
            throw new InvalidOperationException("GameWith取得成功はSummaryOnly参照カードでなければなりません。");
        }

        return result;
    }

    private static bool IsGameWith(Uri url) =>
        string.Equals(url.IdnHost.TrimEnd('.'), "gamewith.jp", StringComparison.OrdinalIgnoreCase)
        || url.IdnHost.TrimEnd('.').EndsWith(".gamewith.jp", StringComparison.OrdinalIgnoreCase);
}
