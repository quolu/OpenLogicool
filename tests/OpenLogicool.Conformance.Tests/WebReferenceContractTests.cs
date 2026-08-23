using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Research;
using OpenLogicool.Contracts.Shared;
using System.Text.Json;
using Xunit;

namespace OpenLogicool.Conformance.Tests;

public sealed class WebReferenceContractTests
{
    private const string Schema = ContractSchemaVersions.Revision01;
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GameWithCanonicalHost_IsAlwaysSummaryOnlyWhenRetrievalIsAllowed()
    {
        var evidence = new SourcePolicyEvidence(
            Schema,
            SourceTermsDisposition.FullTextAllowed,
            RobotsDisposition.Allowed);

        var firstUrl = new Uri("https://gamewith.jp/nikke/article/show/1");
        var secondUrl = new Uri("https://sub.gamewith.jp/path");
        var first = SourcePolicyEvaluator.Evaluate(firstUrl, firstUrl, evidence);
        var second = SourcePolicyEvaluator.Evaluate(secondUrl, secondUrl, evidence);

        Assert.Equal(SourcePolicy.SummaryOnly, first.Policy);
        Assert.Equal(SourcePolicyReason.GameWithSummaryOnly, first.Reason);
        Assert.Equal(first, SourcePolicyEvaluator.Evaluate(firstUrl, firstUrl, evidence));
        Assert.Equal(SourcePolicy.SummaryOnly, second.Policy);
        Assert.Equal(3, first.QuoteScope.MaxExcerptCount);
        Assert.Equal(200, first.QuoteScope.MaxExcerptCharacters);
    }

    [Theory]
    [InlineData(SourceTermsDisposition.Unknown, RobotsDisposition.Allowed, SourcePolicy.LinkOnly)]
    [InlineData(SourceTermsDisposition.Unavailable, RobotsDisposition.Allowed, SourcePolicy.LinkOnly)]
    [InlineData(SourceTermsDisposition.FullTextAllowed, RobotsDisposition.Unknown, SourcePolicy.LinkOnly)]
    [InlineData(SourceTermsDisposition.FullTextAllowed, RobotsDisposition.Unavailable, SourcePolicy.LinkOnly)]
    [InlineData(SourceTermsDisposition.Rejected, RobotsDisposition.Allowed, SourcePolicy.Blocked)]
    [InlineData(SourceTermsDisposition.FullTextAllowed, RobotsDisposition.Rejected, SourcePolicy.Blocked)]
    public void UnknownUnavailableOrRejectedPolicy_NeverAllowsContent(
        SourceTermsDisposition terms,
        RobotsDisposition robots,
        SourcePolicy expected)
    {
        var actual = SourcePolicyEvaluator.Evaluate(
            new Uri("https://example.test/guide"),
            new Uri("https://example.test/guide"),
            new SourcePolicyEvidence(Schema, terms, robots));

        Assert.Equal(expected, actual.Policy);
        Assert.Equal(0, actual.QuoteScope.MaxExcerptCount);
        Assert.Equal(0, actual.QuoteScope.MaxExcerptCharacters);
    }

    [Theory]
    [InlineData("https://gamewith.jp/nikke/1", "https://example.test/canonical")]
    [InlineData("https://example.test/link", "https://gamewith.jp/nikke/1")]
    [InlineData("https://gamewith.jp./nikke/1", "https://example.test/canonical")]
    public void GameWithOriginOrCanonical_CannotEscapeSummaryOnly(string url, string canonicalUrl)
    {
        var evidence = new SourcePolicyEvidence(
            Schema,
            SourceTermsDisposition.FullTextAllowed,
            RobotsDisposition.Allowed);

        var decision = SourcePolicyEvaluator.Evaluate(new Uri(url), new Uri(canonicalUrl), evidence);

        Assert.Equal(SourcePolicy.SummaryOnly, decision.Policy);
        Assert.Equal(SourcePolicyReason.GameWithSummaryOnly, decision.Reason);
    }

    [Fact]
    public void SourceDecision_MustMatchCanonicalUrlDecisionTable()
    {
        var source = Source(
            new Uri("https://gamewith.jp/nikke/article/show/1"),
            new SourcePolicyDecision(
                Schema,
                SourcePolicy.FullTextAllowed,
                SourcePolicyReason.ExplicitFullTextPermission,
                new ReferenceQuoteScope(Schema, 3, 200)));

        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(source));
    }

    [Fact]
    public void GameWithSource_WithComputedSummaryDecision_IsValid()
    {
        var canonicalUrl = new Uri("https://gamewith.jp/nikke/article/show/1");
        var evidence = new SourcePolicyEvidence(
            Schema,
            SourceTermsDisposition.FullTextAllowed,
            RobotsDisposition.Allowed);
        var source = Source(canonicalUrl, SourcePolicyEvaluator.Evaluate(canonicalUrl, canonicalUrl, evidence));

        WebReferenceContractSchema.Validate(source);
    }

    [Fact]
    public void RestrictedSource_RecordsLinkOnlyWithoutInventedMetadata()
    {
        var url = new Uri("https://example.test/guide");
        var evidence = new SourcePolicyEvidence(Schema, SourceTermsDisposition.Unknown, RobotsDisposition.Allowed);
        var source = new RestrictedWebReferenceSource(
            Schema,
            "source-link",
            url,
            url,
            null,
            null,
            null,
            evidence,
            SourcePolicyEvaluator.Evaluate(url, url, evidence),
            Now);
        var document = new ReferenceDocument(
            Schema,
            "document-link",
            1,
            null,
            "source-link",
            SourcePolicy.LinkOnly,
            Now,
            new LinkOnlyReferenceBody(Schema, "利用条件が不明"));

        WebReferenceContractSchema.Validate(document, source);
    }

    [Fact]
    public void AcquisitionPlan_ExposesPreflightPolicyTransmissionCostAndExpiry()
    {
        var url = new Uri("https://gamewith.jp/nikke/article/show/1");
        var evidence = new SourcePolicyEvidence(Schema, SourceTermsDisposition.SummaryAllowed, RobotsDisposition.Allowed);
        var plan = new WebReferenceAcquisitionPlan(
            Schema,
            "plan-1",
            url,
            url,
            WebReferenceAcquisitionMethod.DirectHttp,
            evidence,
            SourcePolicyEvaluator.Evaluate(url, url, evidence),
            "provider-1",
            "model-1",
            "provider.example",
            0.01m,
            Now.AddDays(30));

        WebReferenceContractSchema.Validate(plan);
        Assert.Equal(SourcePolicy.SummaryOnly, plan.PolicyDecision.Policy);
        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(
            plan with { SummaryModel = null }));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebReferenceContractSchema.Validate(
            plan with { EstimatedCostUsd = -0.01m }));
    }

    [Fact]
    public void ExclusionAndReacquisition_AreExplicitUserIntents()
    {
        var exclusion = new WebReferenceSourceExclusion(
            Schema,
            "exclude-1",
            new Uri("https://example.test/guide"),
            Now,
            "このsourceを調査対象から外す");
        var reacquisition = new WebReferenceReacquisitionRequest(
            Schema,
            "reacquire-1",
            "source-1",
            Now,
            "更新情報を取り直す");

        WebReferenceContractSchema.Validate(exclusion);
        WebReferenceContractSchema.Validate(reacquisition);
    }

    [Fact]
    public void BlockedReference_RemainsTraceableWithoutAcquiredPayload()
    {
        var url = new Uri("https://example.test/blocked");
        var evidence = new SourcePolicyEvidence(Schema, SourceTermsDisposition.Rejected, RobotsDisposition.Allowed);
        var source = new RestrictedWebReferenceSource(
            Schema,
            "source-blocked",
            url,
            url,
            null,
            null,
            null,
            evidence,
            SourcePolicyEvaluator.Evaluate(url, url, evidence),
            Now);
        var document = new ReferenceDocument(
            Schema,
            "document-blocked",
            1,
            null,
            source.SourceId,
            SourcePolicy.Blocked,
            Now,
            new BlockedReferenceBody(Schema, "利用条件が拒否された"));

        WebReferenceContractSchema.Validate(document, source);
        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(
            document with { Policy = SourcePolicy.LinkOnly }, source));
    }

    [Fact]
    public void ReferenceDocument_RequiresParentForLaterRevisionAndRoundTripsBodyKind()
    {
        var first = SummaryDocument(["短い根拠"], "構造化した要約");
        var second = first with
        {
            DocumentId = "document-2",
            Revision = 2,
            ParentDocumentId = first.DocumentId,
        };

        WebReferenceContractSchema.Validate(second);
        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(
            second with { ParentDocumentId = null }));

        var json = JsonSerializer.Serialize(second);
        var restored = JsonSerializer.Deserialize<ReferenceDocument>(json);
        Assert.NotNull(restored);
        Assert.IsType<SummaryReferenceBody>(restored.Body);
        WebReferenceContractSchema.Validate(restored);
    }

    [Fact]
    public void SummaryOnlyDocument_AcceptsOnlyBoundedSummaryCard()
    {
        var document = SummaryDocument(["短い根拠"], "構造化した要約");

        WebReferenceContractSchema.Validate(document);

        var tooMany = document with
        {
            Body = ((SummaryReferenceBody)document.Body) with { EvidenceExcerpts = ["a", "b", "c", "d"] },
        };
        var tooLong = document with
        {
            Body = ((SummaryReferenceBody)document.Body) with { EvidenceExcerpts = [new string('x', 201)] },
        };
        var fullText = document with { Body = new FullTextReferenceBody(Schema, "全文") };

        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(tooMany));
        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(tooLong));
        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(fullText));
    }

    [Fact]
    public void SummaryCardAndTombstone_HaveNoRawPayloadSurface()
    {
        var summaryProperties = typeof(SummaryReferenceBody).GetProperties().Select(property => property.Name).ToArray();
        var tombstoneProperties = typeof(WebReferenceDeletionTombstone).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("RawHtml", summaryProperties);
        Assert.DoesNotContain("FullText", summaryProperties);
        Assert.DoesNotContain("Image", summaryProperties);
        Assert.DoesNotContain("NormalizedMarkdown", summaryProperties);
        Assert.DoesNotContain("Body", tombstoneProperties);
        Assert.DoesNotContain("Claim", tombstoneProperties);
        Assert.DoesNotContain("EvidenceExcerpts", tombstoneProperties);
        Assert.DoesNotContain("StructuredSummaryMarkdown", tombstoneProperties);
    }

    [Fact]
    public void WebFact_RequiresSourcesAndCannotRepresentVerifiedState()
    {
        var fact = Fact(["source-1"]);
        WebReferenceContractSchema.Validate(fact);

        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(fact with { SourceReferenceIds = [] }));
        Assert.DoesNotContain("Verified", Enum.GetNames<WebReferenceFactValidity>());
    }

    [Fact]
    public void ContradictedFact_RequiresAppendOnlyContradictionReference()
    {
        var contradicted = Fact(["source-1"]) with
        {
            Validity = WebReferenceFactValidity.Contradicted,
            ContradictionIds = ["contradiction-1"],
        };
        var contradiction = new WebReferenceContradiction(
            Schema,
            "contradiction-1",
            1,
            null,
            "fact-1",
            "fact-2",
            ["source-1", "source-2"],
            Now,
            "公式情報と攻略情報でreset時刻が異なる");

        WebReferenceContractSchema.Validate(contradicted);
        WebReferenceContractSchema.Validate(contradiction);
        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(
            contradicted with { ContradictionIds = [] }));
    }

    [Fact]
    public void AcquisitionStatuses_KeepNewSuccessCacheReuseAndFailuresDistinct()
    {
        var success = Attempt(WebReferenceAcquisitionStatus.Succeeded) with
        {
            SourceId = "source-1",
            NewDocumentId = "document-1",
        };
        var reused = Attempt(WebReferenceAcquisitionStatus.ReusedExisting) with
        {
            SourceId = "source-1",
            ExistingDocumentId = "document-old",
        };
        var failures = Enum.GetValues<WebReferenceAcquisitionStatus>()
            .Except([
                WebReferenceAcquisitionStatus.Succeeded,
                WebReferenceAcquisitionStatus.ReusedExisting,
                WebReferenceAcquisitionStatus.PolicyLimited,
                WebReferenceAcquisitionStatus.TermsRejected,
                WebReferenceAcquisitionStatus.RobotsRejected,
            ])
            .Select(status => Attempt(status) with { Detail = status.ToString() })
            .ToArray();
        var policyLimited = Attempt(WebReferenceAcquisitionStatus.PolicyLimited) with
        {
            SourceId = "source-link",
            NewDocumentId = "document-link",
            Detail = "terms unknown",
        };
        var blocked = Attempt(WebReferenceAcquisitionStatus.TermsRejected) with
        {
            SourceId = "source-blocked",
            NewDocumentId = "document-blocked",
            Detail = "terms rejected",
        };

        WebReferenceContractSchema.Validate(success);
        WebReferenceContractSchema.Validate(reused);
        WebReferenceContractSchema.Validate(policyLimited);
        WebReferenceContractSchema.Validate(blocked);
        Assert.All(failures, WebReferenceContractSchema.Validate);
        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(
            Attempt(WebReferenceAcquisitionStatus.Succeeded)));
        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(
            reused with { NewDocumentId = "new-document" }));
    }

    [Fact]
    public void ResearchRun_RequiresGameGoalAndKeepsFailedAttempt()
    {
        var attempt = Attempt(WebReferenceAcquisitionStatus.NetworkUnavailable) with
        {
            Detail = "network unavailable",
        };
        var run = new ResearchRun(
            Schema,
            "run-1",
            "nikke",
            "日課候補とreset条件を調べる",
            ResearchRunStatus.Failed,
            Now,
            Now.AddSeconds(1),
            [attempt]);

        WebReferenceContractSchema.Validate(run);
        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(run with { ResearchGoal = "" }));
        Assert.Throws<ArgumentException>(() => WebReferenceContractSchema.Validate(run with { CompletedUtc = null }));
    }

    [Fact]
    public void WebPayloadContracts_HaveNoExecutionAuthorityFieldsOrTypes()
    {
        var payloadTypes = new[]
        {
            typeof(ReferenceDocument),
            typeof(ReferenceDocumentBody),
            typeof(FullTextReferenceBody),
            typeof(SummaryReferenceBody),
            typeof(WebReferenceFact),
        };
        var forbiddenTypes = new[]
        {
            typeof(GamePolicyRecord),
            typeof(PlannerBudget),
            typeof(ProposalAction),
        };
        var forbiddenNames = new[]
        {
            "StateId",
            "TargetCoordinate",
            "AllowedAction",
            "AllowedPrimitive",
            "ExpectedTransition",
            "RiskClass",
            "Approval",
            "Budget",
        };

        foreach (var type in payloadTypes)
        {
            var properties = type.GetProperties();
            Assert.All(properties, property => Assert.DoesNotContain(property.PropertyType, forbiddenTypes));
            Assert.All(forbiddenNames, name => Assert.DoesNotContain(properties, property =>
                property.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void DeletionTombstone_RecordsDeletionWithoutPayload()
    {
        var preview = new WebReferenceDeletionPreview(
            Schema,
            "source-1",
            ["document-1"],
            ["fact-1", "fact-2"],
            8192);
        var tombstone = new WebReferenceDeletionTombstone(
            Schema,
            "tombstone-1",
            "source-1",
            Now,
            "利用者がsourceを削除",
            ["document-1"],
            ["fact-1", "fact-2"]);

        WebReferenceContractSchema.Validate(preview);
        WebReferenceContractSchema.Validate(tombstone);
    }

    private static AcquiredWebReferenceSource Source(Uri canonicalUrl, SourcePolicyDecision decision)
    {
        var evidence = new SourcePolicyEvidence(
            Schema,
            SourceTermsDisposition.FullTextAllowed,
            RobotsDisposition.Allowed);
        return new AcquiredWebReferenceSource(
            Schema,
            "source-1",
            canonicalUrl,
            canonicalUrl,
            "NIKKE攻略",
            "GameWith",
            Now.AddDays(-1),
            Now,
            "ja-JP",
            WebReferenceSourceKind.Guide,
            evidence,
            decision,
            new WebReferenceProvenance(
                Schema,
                "sha256:abc",
                WebReferenceAcquisitionMethod.DirectHttp,
                Now,
                null,
                Now.AddDays(30)));
    }

    private static ReferenceDocument SummaryDocument(IReadOnlyList<string> excerpts, string summary) => new(
        Schema,
        "document-1",
        1,
        null,
        "source-1",
        SourcePolicy.SummaryOnly,
        Now,
        new SummaryReferenceBody(Schema, summary, excerpts, ["SummaryOnly"]));

    private static WebReferenceFact Fact(IReadOnlyList<string> sources) => new(
        Schema,
        "fact-1",
        1,
        null,
        WebReferenceFactKind.Daily,
        "日課は毎日更新される",
        sources,
        0.5m,
        WebReferenceFactValidity.Hypothesis,
        new WebReferenceFactScope(Schema, "2026.08", "ja-JP", Now, null),
        [],
        Now);

    private static WebReferenceAcquisitionAttempt Attempt(WebReferenceAcquisitionStatus status) => new(
        Schema,
        $"attempt-{status}",
        new Uri("https://example.test/guide"),
        Now,
        Now.AddSeconds(1),
        status,
        null,
        null,
        null,
        null);
}
