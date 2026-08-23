using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OpenLogicool.Contracts.Research;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host.Research;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class WebReferenceAcquisitionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExplicitFullTextPermission_NormalizesRedirectedHtmlAndHashesRawBytes()
    {
        var html = """
            <html lang="ja"><head>
              <title>公式ルール</title>
              <meta property="og:site_name" content="公式サイト">
              <link rel="canonical" href="/rules">
            </head><body><nav>捨てる</nav><main><h1>遊び方</h1><p>毎日確認する。</p></main></body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        var transport = new FakeTransport(new(
            new Uri("https://example.test/start"),
            new Uri("https://EXAMPLE.test/redirected#fragment"),
            HttpStatusCode.OK,
            "text/html",
            bytes));
        var service = Service(transport);

        var result = await service.AcquireAsync(Command(FullTextPlan(), timeout: TimeSpan.FromSeconds(1)));

        var source = Assert.IsType<AcquiredWebReferenceSource>(result.Source);
        Assert.Equal("https://example.test/rules", source.CanonicalUrl.AbsoluteUri);
        Assert.Equal("公式ルール", source.Title);
        Assert.Equal("公式サイト", source.Publisher);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), source.Provenance.ContentDigest);
        var body = Assert.IsType<FullTextReferenceBody>(result.Document!.Body);
        Assert.Contains("# 遊び方", body.NormalizedMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("捨てる", body.NormalizedMarkdown, StringComparison.Ordinal);
        Assert.Equal(WebReferenceAcquisitionStatus.Succeeded, result.Attempt.Status);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task GameWith_StoresOnlySummaryCardAndNeverReturnsRawPageText()
    {
        const string rawSecret = "FULL_PAGE_MUST_NOT_PERSIST";
        var html = $"<html lang='ja'><head><title>NIKKE日課</title></head><body><article><p>{rawSecret}</p></article></body></html>";
        var transport = new FakeTransport(new(
            new Uri("https://gamewith.jp/nikke/article/show/1"),
            new Uri("https://gamewith.jp/nikke/article/show/1"),
            HttpStatusCode.OK,
            "text/html",
            Encoding.UTF8.GetBytes(html)));
        var summary = new FakeSummaryProvider(new SummaryReferenceBody(
            ContractSchemaVersions.Revision01,
            "# 日課候補\n\n- デイリーを確認する",
            ["デイリーを確認"],
            ["デイリー"]));
        var service = Service(transport, summary);
        var adapter = new GameWithReferenceAdapter(service);

        var result = await adapter.AcquireAsync(Command(GameWithPlan(), timeout: TimeSpan.FromSeconds(1)));

        Assert.IsType<SummaryReferenceBody>(result.Document!.Body);
        Assert.Equal(SourcePolicy.SummaryOnly, result.Document.Policy);
        Assert.DoesNotContain(rawSecret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Contains(rawSecret, summary.LastRequest!.Markdown.Replace("\\_", "_", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(WebReferenceAcquisitionStatus.Succeeded, result.Attempt.Status);
    }

    [Fact]
    public async Task SummaryOnlyWithoutProvider_IsExplicitFailureAndDoesNotFallback()
    {
        var transport = new FakeTransport(HtmlPayload("https://gamewith.jp/nikke/article/show/1"));
        var service = Service(transport);

        var result = await service.AcquireAsync(Command(GameWithPlan(withProvider: false), timeout: TimeSpan.FromSeconds(1)));

        Assert.Equal(WebReferenceAcquisitionStatus.ProviderUnselected, result.Attempt.Status);
        Assert.Null(result.Source);
        Assert.Null(result.Document);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task LinkOnlyPolicy_DoesNotFetchPage()
    {
        var transport = new FakeTransport(HtmlPayload("https://unknown.test/guide"));
        var plan = Plan(
            "https://unknown.test/guide",
            SourceTermsDisposition.Unknown,
            RobotsDisposition.Allowed);
        var service = Service(transport);

        var result = await service.AcquireAsync(Command(plan, timeout: TimeSpan.FromSeconds(1)));

        Assert.Equal(WebReferenceAcquisitionStatus.PolicyLimited, result.Attempt.Status);
        Assert.IsType<RestrictedWebReferenceSource>(result.Source);
        Assert.IsType<LinkOnlyReferenceBody>(result.Document!.Body);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task HttpFailure_IsNotReplacedByCachedOrAlternateSource()
    {
        var transport = new FakeTransport(new(
            new Uri("https://example.test/start"),
            new Uri("https://example.test/start"),
            HttpStatusCode.ServiceUnavailable,
            "text/html",
            []));
        var service = Service(transport);

        var result = await service.AcquireAsync(Command(FullTextPlan(), timeout: TimeSpan.FromSeconds(1)));

        Assert.Equal(WebReferenceAcquisitionStatus.HttpFailed, result.Attempt.Status);
        Assert.Null(result.Source);
        Assert.Null(result.Document);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task TimeoutAndCallerCancellation_AreDistinct()
    {
        var transport = new BlockingTransport();
        var service = Service(transport);

        var timedOut = await service.AcquireAsync(Command(FullTextPlan(), timeout: TimeSpan.FromMilliseconds(10)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelledResult = await service.AcquireAsync(
            Command(FullTextPlan(), timeout: TimeSpan.FromSeconds(1)),
            cancelled.Token);

        Assert.Equal(WebReferenceAcquisitionStatus.TimedOut, timedOut.Attempt.Status);
        Assert.Equal(WebReferenceAcquisitionStatus.Cancelled, cancelledResult.Attempt.Status);
    }

    [Fact]
    public async Task GameWithAdapter_RejectsDifferentSourceInsteadOfFallingBack()
    {
        var adapter = new GameWithReferenceAdapter(Service(new FakeTransport(HtmlPayload("https://example.test/start"))));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            adapter.AcquireAsync(Command(FullTextPlan(), timeout: TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public async Task HttpClientBoundary_ReportsFinalUrlAndRejectsOversizedPayload()
    {
        var final = new Uri("https://example.test/final");
        using var client = new HttpClient(new StubHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, final),
                Content = new ByteArrayContent([1, 2, 3]),
            };
            response.Content.Headers.ContentType = new("text/html");
            return response;
        }));
        var transport = new HttpClientWebReferenceTransport(client, maxPayloadBytes: 2);

        await Assert.ThrowsAsync<WebReferencePayloadTooLargeException>(() =>
            transport.FetchAsync(new Uri("https://example.test/start"), CancellationToken.None));

        var accepted = new HttpClientWebReferenceTransport(client, maxPayloadBytes: 3);
        var payload = await accepted.FetchAsync(new Uri("https://example.test/start"), CancellationToken.None);
        Assert.Equal(final, payload.FinalUrl);
        Assert.Equal([1, 2, 3], payload.Content);
    }

    private static WebReferenceAcquisitionService Service(
        IWebReferenceHttpTransport transport,
        IWebReferenceSummaryProvider? summary = null) =>
        new(transport, new WebReferenceHtmlNormalizer(), summary, new FixedTimeProvider(Now));

    private static WebReferenceAcquisitionCommand Command(
        WebReferenceAcquisitionPlan plan,
        TimeSpan timeout) =>
        new(
            "attempt-1",
            "source-1",
            "document-1",
            plan,
            null,
            "ja-JP",
            WebReferenceSourceKind.Guide,
            timeout);

    private static WebReferenceAcquisitionPlan FullTextPlan() =>
        Plan(
            "https://example.test/start",
            SourceTermsDisposition.FullTextAllowed,
            RobotsDisposition.Allowed);

    private static WebReferenceAcquisitionPlan GameWithPlan(bool withProvider = true) =>
        Plan(
            "https://gamewith.jp/nikke/article/show/1",
            SourceTermsDisposition.SummaryAllowed,
            RobotsDisposition.Allowed,
            withProvider ? "openai" : null,
            withProvider ? "summary-model" : null,
            withProvider ? "OpenAI API" : null);

    private static WebReferenceAcquisitionPlan Plan(
        string url,
        SourceTermsDisposition terms,
        RobotsDisposition robots,
        string? provider = null,
        string? model = null,
        string? destination = null)
    {
        var uri = new Uri(url);
        var evidence = new SourcePolicyEvidence(ContractSchemaVersions.Revision01, terms, robots);
        return new WebReferenceAcquisitionPlan(
            ContractSchemaVersions.Revision01,
            "plan-1",
            uri,
            uri,
            WebReferenceAcquisitionMethod.DirectHttp,
            evidence,
            SourcePolicyEvaluator.Evaluate(uri, uri, evidence),
            provider,
            model,
            destination,
            provider is null ? null : 0.01m,
            Now.AddDays(7));
    }

    private static WebReferenceHttpPayload HtmlPayload(string url) =>
        new(
            new Uri(url),
            new Uri(url),
            HttpStatusCode.OK,
            "text/html",
            Encoding.UTF8.GetBytes("<html lang='ja'><head><title>Guide</title></head><body><main>Body</main></body></html>"));

    private sealed class FakeTransport(WebReferenceHttpPayload payload) : IWebReferenceHttpTransport
    {
        public int CallCount { get; private set; }

        public Task<WebReferenceHttpPayload> FetchAsync(Uri url, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(payload);
        }
    }

    private sealed class BlockingTransport : IWebReferenceHttpTransport
    {
        public async Task<WebReferenceHttpPayload> FetchAsync(Uri url, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("到達不能");
        }
    }

    private sealed class FakeSummaryProvider(SummaryReferenceBody response) : IWebReferenceSummaryProvider
    {
        public WebReferenceSummaryRequest? LastRequest { get; private set; }

        public Task<SummaryReferenceBody> SummarizeAsync(
            WebReferenceSummaryRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
