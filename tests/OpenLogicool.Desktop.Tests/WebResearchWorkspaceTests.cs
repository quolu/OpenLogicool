using OpenLogicool.Contracts.Research;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class WebResearchWorkspaceTests
{
    [Fact]
    public async Task Journey_UsesOnePublicIntentForPreviewStartReacquireMarkdownAndDelete()
    {
        var intent = new FakeIntent();
        var workspace = new WebResearchWorkspace(intent);
        var url = new Uri("https://gamewith.jp/nikke/article/show/1");

        var preview = workspace.Preview(
            url,
            SourceTermsDisposition.SummaryAllowed,
            RobotsDisposition.Allowed);

        Assert.Equal(AiExecutionLocation.LocalDevice, preview.Plan.ExecutionLocation);
        Assert.Equal(0m, preview.Plan.ExternalApiCostUsd);
        Assert.Equal("なし", preview.ExternalTransmissionLabel);
        Assert.Equal("0円", preview.ExternalApiCostLabel);

        var started = await workspace.StartAsync();
        workspace.Exclude(url, "除外");
        var reacquired = await workspace.ReacquireAsync(started.SourceId!);
        var item = Assert.Single(workspace.ListDocuments());
        Assert.Equal("# NIKKE", workspace.GetMarkdown(item.DocumentId));
        var deletion = workspace.PreviewDelete(item.SourceId);
        workspace.Delete(item.SourceId, "削除");

        Assert.True(started.Succeeded);
        Assert.True(reacquired.Succeeded);
        Assert.Equal(["preview", "start", "exclude", "reacquire", "list", "markdown", "preview-delete", "delete"], intent.Calls);
        Assert.Equal(item.SourceId, deletion.SourceId);
    }

    [Fact]
    public async Task StartRequiresPreview()
    {
        var workspace = new WebResearchWorkspace(new FakeIntent());
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.StartAsync());
    }

    private sealed class FakeIntent : IWebResearchIntent
    {
        public List<string> Calls { get; } = [];

        public WebResearchPreview Preview(Uri url, SourceTermsDisposition terms, RobotsDisposition robots, DateTimeOffset? expiresUtc)
        {
            Calls.Add("preview");
            var evidence = new SourcePolicyEvidence(ContractSchemaVersions.Revision01, terms, robots);
            return new(
                new WebReferenceAcquisitionPlan(
                    ContractSchemaVersions.Revision01,
                    "plan-1",
                    url,
                    url,
                    WebReferenceAcquisitionMethod.DirectHttp,
                    evidence,
                    SourcePolicyEvaluator.Evaluate(url, url, evidence),
                    "local-runtime",
                    "local-model",
                    AiExecutionLocation.LocalDevice,
                    0m,
                    expiresUtc),
                "SummaryOnly",
                "要約カード",
                "200文字×3件",
                "このPC内",
                "なし",
                "0円",
                "期限なし");
        }

        public Task<WebResearchOperationResult> StartAsync(WebReferenceAcquisitionPlan plan, CancellationToken cancellationToken = default)
        {
            Calls.Add("start");
            return Task.FromResult(new WebResearchOperationResult(true, "完了", "source-1", "document-1"));
        }

        public void Exclude(Uri url, string reason) => Calls.Add("exclude");

        public Task<WebResearchOperationResult> ReacquireAsync(string sourceId, CancellationToken cancellationToken = default)
        {
            Calls.Add("reacquire");
            return Task.FromResult(new WebResearchOperationResult(true, "再取得", sourceId, "document-1"));
        }

        public IReadOnlyList<WebResearchDocumentItem> ListDocuments()
        {
            Calls.Add("list");
            return [new WebResearchDocumentItem("source-1", "document-1", "NIKKE", SourcePolicy.SummaryOnly, 1)];
        }

        public string GetMarkdown(string documentId)
        {
            Calls.Add("markdown");
            return "# NIKKE";
        }

        public WebReferenceDeletionPreview PreviewDelete(string sourceId)
        {
            Calls.Add("preview-delete");
            return new(ContractSchemaVersions.Revision01, sourceId, ["document-1"], [], [], 7);
        }

        public void Delete(string sourceId, string reason) => Calls.Add("delete");
    }
}
