using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Research;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host.Research;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostWebResearchIntentTests
{
    [Fact]
    public async Task LocalSummaryJourneyPersistsMarkdownReacquiresAndDeletesWithoutExternalApiCost()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        var store = new SqliteWebReferenceStore(connection);
        var url = new Uri("https://gamewith.jp/nikke/article/show/1");
        var service = new WebReferenceAcquisitionService(
            new FakeTransport(new WebReferenceHttpPayload(
                url,
                url,
                HttpStatusCode.OK,
                "text/html",
                Encoding.UTF8.GetBytes("<html><head><title>NIKKE日課</title></head><body>デイリー</body></html>"))),
            new WebReferenceHtmlNormalizer(),
            new FakeLocalSummaryProvider());
        var intent = new HostWebResearchIntent(store, service, "local-runtime", "local-model");

        var preview = intent.Preview(
            url,
            SourceTermsDisposition.SummaryAllowed,
            RobotsDisposition.Allowed,
            DateTimeOffset.UtcNow.AddDays(30));
        var started = await intent.StartAsync(preview.Plan);
        var first = Assert.Single(intent.ListDocuments());
        var markdown = intent.GetMarkdown(first.DocumentId);
        var reacquired = await intent.ReacquireAsync(first.SourceId);
        var deletion = intent.PreviewDelete(first.SourceId);
        intent.Delete(first.SourceId, "test cleanup");

        Assert.Equal(AiExecutionLocation.LocalDevice, preview.Plan.ExecutionLocation);
        Assert.Equal(0m, preview.Plan.ExternalApiCostUsd);
        Assert.True(started.Succeeded);
        Assert.True(reacquired.Succeeded);
        Assert.Contains("# 日課候補", markdown);
        Assert.Single(deletion.DocumentIds);
        Assert.DoesNotContain(intent.ListDocuments(), item => item.SourceId == first.SourceId);
        Assert.Single(store.ListTombstones());
    }

    private sealed class FakeTransport(WebReferenceHttpPayload payload) : IWebReferenceHttpTransport
    {
        public Task<WebReferenceHttpPayload> FetchAsync(Uri url, CancellationToken cancellationToken) =>
            Task.FromResult(payload);
    }

    private sealed class FakeLocalSummaryProvider : IWebReferenceSummaryProvider
    {
        public Task<SummaryReferenceBody> SummarizeAsync(
            WebReferenceSummaryRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SummaryReferenceBody(
                ContractSchemaVersions.Revision01,
                "# 日課候補\n\n- デイリー",
                ["デイリー"],
                ["日課"]));
    }
}
