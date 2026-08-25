using System.Net;
using System.Security.Cryptography;
using System.Text;
using OpenLogicool.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.AI.Tests;

public sealed class FoundryLocalDiscoveryVisionProviderTests
{
    [Fact]
    public async Task Completed_local_labels_become_frame_bound_affordance_and_structured_proposal()
    {
        using var client = ClientReturning("{\"labels\":[\"OpenEvent\"]}", inputTokens: 12, outputTokens: 6);
        var provider = new FoundryLocalDiscoveryVisionProvider(client);
        var png = new byte[] { 1, 2, 3 };

        var result = await provider.ObserveAsync(
            Request([Region("OpenEvent", 0.2, 0.3, 0.1, 0.05)]),
            png);

        Assert.Equal(CaptureAvailability.Available, result.Scene.CaptureAvailability);
        Assert.Equal(StateIdentityStatus.Novel, result.Scene.StateIdentity);
        var affordance = Assert.Single(result.Scene.Affordances);
        Assert.Equal("observation-1", affordance.ObservationId);
        Assert.Equal(7, affordance.FrameSequence);
        Assert.Equal(3, affordance.TransformRevision);
        Assert.Equal("window:game", affordance.TargetWindowSourceId);
        Assert.Equal([0.2, 0.3, 0.1, 0.05], affordance.Locator.NormalizedBounds);
        Assert.Equal(["click"], affordance.AllowedPrimitives);
        Assert.Equal("text", affordance.SemanticKind);
        Assert.Equal("OpenEvent", affordance.SemanticLabel);
        var proposal = Assert.Single(result.Proposals);
        Assert.Equal(affordance.CandidateId, proposal.AffordanceCandidateId);
        Assert.Equal("structure-0", proposal.SourceStructureRevisionId);
        Assert.Equal("click", proposal.Primitive);
        Assert.Contains(ExplorationOutcomeKind.OutcomeUnknown, proposal.AllowedOutcomes);
        Assert.Equal("microsoft-foundry-local", result.Telemetry.ProviderId);
        Assert.Equal("qwen3-vl-test:2", result.Telemetry.ModelId);
        Assert.Equal(FoundryLocalVisionClient.PromptRevision, result.Telemetry.PromptRevision);
        Assert.Equal(64, result.Telemetry.PromptSha256.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(png)), result.Telemetry.CropSha256);
        Assert.Equal(12, result.Telemetry.InputTokens);
        Assert.Equal(6, result.Telemetry.OutputTokens);
        Assert.Equal(0, result.Telemetry.ExternalAiTransmissionCount);
        Assert.Equal(0m, result.Telemetry.ExternalAiApiCostUsd);
    }

    [Fact]
    public async Task Same_scene_candidates_control_identity_without_game_specific_seed()
    {
        using var client = ClientReturning("{\"labels\":[]}");
        var provider = new FoundryLocalDiscoveryVisionProvider(client);

        var known = await provider.ObserveAsync(Request([], [State("state-local-1")]), new byte[] { 1 });
        var ambiguous = await provider.ObserveAsync(
            Request([], [State("state-local-1"), State("state-local-2")]),
            new byte[] { 2 });

        Assert.Equal(StateIdentityStatus.Known, known.Scene.StateIdentity);
        Assert.Equal("state-local-1", known.Scene.StateHypothesisId);
        Assert.Equal(StateIdentityStatus.Ambiguous, ambiguous.Scene.StateIdentity);
        Assert.Null(ambiguous.Scene.StateHypothesisId);
    }

    [Fact]
    public async Task Candidate_constraint_rebinds_unique_similar_ocr_but_keeps_duplicate_regions_ambiguous()
    {
        using var client = ClientReturning("{\"labels\":[\"タップして受けける\"]}");
        var provider = new FoundryLocalDiscoveryVisionProvider(client);

        var unique = await provider.ObserveAsync(
            Request([Region("タップして受け取る", 0.1, 0.1, 0.2, 0.1)]),
            new byte[] { 1 });
        var ambiguous = await provider.ObserveAsync(
            Request([
                Region("タップして受け取る", 0.1, 0.1, 0.2, 0.1),
                Region("タップして受け取る", 0.6, 0.1, 0.2, 0.1),
            ]),
            new byte[] { 2 });

        Assert.Equal(FoundryVisionStatus.Completed, unique.ProviderResult.Status);
        Assert.Equal("タップして受け取る", Assert.Single(unique.Scene.Affordances).SemanticLabel);
        Assert.True(unique.ProviderResult.Normalization.HasFlag(FoundryVisionNormalization.SimilarCandidateLabelRebound));
        Assert.Empty(ambiguous.Scene.Affordances);
        Assert.Empty(ambiguous.Proposals);
    }

    [Fact]
    public async Task Nested_exact_ocr_spans_choose_the_smallest_region_but_separate_labels_remain_ambiguous()
    {
        using var client = ClientReturning("{\"labels\":[\"アーク\"]}");
        var provider = new FoundryLocalDiscoveryVisionProvider(client);
        var nested = await provider.ObserveAsync(
            Request([
                Region("①アーク", 0.60, 0.70, 0.12, 0.05),
                Region("アーク", 0.62, 0.70, 0.08, 0.05),
            ]),
            new byte[] { 1 });
        var separate = await provider.ObserveAsync(
            Request([
                Region("アーク", 0.10, 0.70, 0.08, 0.05),
                Region("アーク", 0.62, 0.70, 0.08, 0.05),
            ]),
            new byte[] { 2 });

        Assert.Equal([0.62, 0.70, 0.08, 0.05], Assert.Single(nested.Scene.Affordances).Locator.NormalizedBounds);
        Assert.Empty(separate.Scene.Affordances);
    }

    [Fact]
    public async Task Provider_failure_returns_scene_without_affordance_and_never_falls_back()
    {
        using var client = ClientReturning("{\"controls\":[]}");
        var provider = new FoundryLocalDiscoveryVisionProvider(client);

        var result = await provider.ObserveAsync(
            Request([Region("OpenEvent", 0.2, 0.3, 0.1, 0.05)]),
            new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Unknown, result.ProviderResult.Status);
        Assert.Equal(FoundryVisionFailure.InvalidResponse, result.ProviderResult.Failure);
        Assert.Empty(result.Scene.Affordances);
        Assert.Empty(result.Proposals);
        Assert.Equal(0, result.Telemetry.ExternalAiTransmissionCount);
    }

    [Fact]
    public async Task Request_rejects_window_mismatch_and_out_of_frame_region_before_provider_call()
    {
        var calls = 0;
        using var client = ClientReturning("{\"labels\":[]}", onCall: () => calls++);
        var provider = new FoundryLocalDiscoveryVisionProvider(client);
        var mismatch = Request([]) with { TargetWindowSourceId = "window:other" };
        var invalidRegion = Request([Region("OpenEvent", 0.95, 0.3, 0.1, 0.05)]);

        await Assert.ThrowsAsync<ArgumentException>(() => provider.ObserveAsync(mismatch, new byte[] { 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.ObserveAsync(invalidRegion, new byte[] { 1 }));
        Assert.Equal(0, calls);
    }

    private static LocalVisionSceneRequest Request(
        IReadOnlyList<LocalVisionTextRegion> regions,
        IReadOnlyList<StateCandidate>? sameSceneCandidates = null) =>
        new(
            ContractSchemaVersions.Revision03,
            "scene-1",
            "observation-1",
            new CapturedFrameReference(
                ContractSchemaVersions.Revision03,
                "window:game",
                CaptureBackend.WindowsGraphicsCapture,
                7,
                700,
                DateTimeOffset.UnixEpoch,
                3,
                10,
                250),
            "window:game",
            "crop:full-frame",
            1920,
            1080,
            "locator-1",
            regions,
            sameSceneCandidates ?? [],
            ["click", "back"],
            "structure-0");

    private static LocalVisionTextRegion Region(
        string text,
        double x,
        double y,
        double width,
        double height) =>
        new(
            text,
            new EvidenceRegion(
                ContractSchemaVersions.Revision03,
                "rect",
                [x, y, width, height],
                "windows-ocr"));

    private static StateCandidate State(string stateId) =>
        new(
            ContractSchemaVersions.Revision03,
            stateId,
            0.9,
            [new EvidenceRegion(
                ContractSchemaVersions.Revision03,
                "rect",
                [0, 0, 1, 1],
                "scene-signature")]);

    private static FoundryLocalVisionClient ClientReturning(
        string output,
        int? inputTokens = null,
        int? outputTokens = null,
        Action? onCall = null) =>
        new(
            new Uri("http://127.0.0.1:5000"),
            "qwen3-vl-test:2",
            TimeSpan.FromSeconds(1),
            new StubHandler((_, _) =>
            {
                onCall?.Invoke();
                return Task.FromResult(EventStream(output, inputTokens, outputTokens));
            }));

    private static HttpResponseMessage EventStream(
        string output,
        int? inputTokens,
        int? outputTokens)
    {
        var usage = inputTokens is null || outputTokens is null
            ? "{}"
            : $"{{\"input_tokens\":{inputTokens},\"output_tokens\":{outputTokens}}}";
        var events = new[]
        {
            $"{{\"type\":\"response.output_text.delta\",\"delta\":{System.Text.Json.JsonSerializer.Serialize(output)}}}",
            $"{{\"type\":\"response.completed\",\"response\":{{\"usage\":{usage}}}}}",
        };
        var content = string.Join("\n\n", events.Select(value => $"data: {value}"))
            + "\n\ndata: [DONE]\n\n";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "text/event-stream"),
        };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
