using System.Net;
using System.Text;
using System.Text.Json;
using OpenLogicool.AI;
using Xunit;

namespace OpenLogicool.AI.Tests;

public sealed class FoundryLocalVisionClientTests
{
    [Fact]
    public void ConstructorRejectsNonLoopbackEndpoint()
    {
        Assert.Throws<ArgumentException>(() => new FoundryLocalVisionClient(
            new Uri("https://example.com"),
            "model",
            TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task UsesFoundryStreamingResponsesImageSchemaAndParsesFencedJson()
    {
        string? observedBody = null;
        Uri? observedUri = null;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            observedUri = request.RequestUri;
            observedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return EventStream(
                "{\"type\":\"response.output_text.delta\",\"delta\":\"```json\\n{\\\"labels\\\":[\\\"OpenEvent\\\"]}\\n```\"}",
                "{\"type\":\"response.completed\",\"response\":{\"usage\":{\"input_tokens\":12,\"output_tokens\":6}}}");
        });
        using var client = new FoundryLocalVisionClient(
            new Uri("http://127.0.0.1:5000"),
            "qwen3-vl-test:2",
            TimeSpan.FromSeconds(1),
            handler);

        var result = await client.ProposeLabelsAsync(new byte[] { 1, 2, 3 });

        Assert.Equal(FoundryVisionStatus.Completed, result.Status);
        Assert.Equal(["OpenEvent"], result.Labels);
        Assert.Equal(FoundryVisionNormalization.None, result.Normalization);
        Assert.Equal(12, result.InputTokens);
        Assert.Equal(6, result.OutputTokens);
        Assert.Equal("http://127.0.0.1:5000/v1/responses", observedUri!.AbsoluteUri);
        Assert.Contains("\"stream\":true", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"message\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"input_image\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"image_data\":\"AQID\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"media_type\":\"image/png\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("Each label must appear at most once. Never repeat a label.", observedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("image_url", observedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Candidate_constrained_prompt_accepts_only_exact_same_frame_ocr_string()
    {
        string? observedBody = null;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            observedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return EventStream(
                "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"labels\\\":[\\\"アリーナ\\\"]}\"}",
                "{\"type\":\"response.completed\",\"response\":{\"usage\":{}}}");
        });
        using var client = new FoundryLocalVisionClient(
            new Uri("http://127.0.0.1:5000"),
            "model",
            TimeSpan.FromSeconds(1),
            handler);

        var result = await client.ProposeLabelsAsync(
            new byte[] { 1 },
            ["アリーナ", "戻る"]);

        Assert.Equal(FoundryVisionStatus.Completed, result.Status);
        Assert.Equal(["アリーナ"], result.Labels);
        using var request = System.Text.Json.JsonDocument.Parse(observedBody!);
        var prompt = request.RootElement
            .GetProperty("input")[0]
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();
        Assert.Contains("same-frame OCR strings", prompt, StringComparison.Ordinal);
        Assert.Contains("アリーナ", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Candidate_constrained_prompt_rebinds_a_unique_similar_ocr_string()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"labels\\\":[\\\"アーク\\\"]}\"}");

        var result = await client.ProposeLabelsAsync(new byte[] { 1 }, ["0アーク", "戻る"]);

        Assert.Equal(FoundryVisionStatus.Completed, result.Status);
        Assert.Equal(["0アーク"], result.Labels);
        Assert.True(result.Normalization.HasFlag(FoundryVisionNormalization.SimilarCandidateLabelRebound));
    }

    [Fact]
    public async Task Candidate_constrained_prompt_rejects_transliteration_without_fallback()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"labels\\\":[\\\"ТЕТРА\\\"]}\"}");

        var result = await client.ProposeLabelsAsync(new byte[] { 1 }, ["TETRA"]);

        Assert.Equal(FoundryVisionStatus.Unknown, result.Status);
        Assert.Equal(FoundryVisionFailure.InvalidResponse, result.Failure);
        Assert.Empty(result.Labels);
    }

    [Fact]
    public async Task Candidate_constrained_prompt_keeps_exact_labels_and_reports_dropped_hallucinations()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"labels\\\":[\\\"アリーナ\\\",\\\"ТЕТРА\\\"]}\"}");

        var result = await client.ProposeLabelsAsync(new byte[] { 1 }, ["アリーナ", "戻る"]);

        Assert.Equal(FoundryVisionStatus.Completed, result.Status);
        Assert.Equal(["アリーナ"], result.Labels);
        Assert.Equal(FoundryVisionNormalization.OutOfCandidateLabelsDropped, result.Normalization);
    }

    [Fact]
    public async Task InvalidSchemaIsUnknownWithoutFallback()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"controls\\\":[]}\"}");

        var result = await client.ProposeLabelsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Unknown, result.Status);
        Assert.Equal(FoundryVisionFailure.InvalidResponse, result.Failure);
        Assert.Empty(result.Labels);
    }

    [Fact]
    public async Task PromptPlaceholderIsUnknown()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"labels\\\":[\\\"visible text label\\\"]}\"}");

        var result = await client.ProposeLabelsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Unknown, result.Status);
        Assert.Equal(FoundryVisionFailure.InvalidResponse, result.Failure);
        Assert.Empty(result.Labels);
    }

    [Fact]
    public async Task ExactDuplicateLabelsAreCollapsedAsOneSemanticCandidate()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"labels\\\":[\\\"お知らせ\\\",\\\"お知らせ\\\"]}\"}");

        var result = await client.ProposeLabelsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Completed, result.Status);
        Assert.Equal(["お知らせ"], result.Labels);
        Assert.Equal(FoundryVisionNormalization.DuplicateLabelsCollapsed, result.Normalization);
    }

    [Fact]
    public async Task TruncatedExactRepetitionIsRecoveredAndReported()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"```json\\n{\\\"labels\\\":[\\\"NIKKE\\\",\\\"401\\\",\\\"401\\\",\\\"401\\\",\\\"40\"}");

        var result = await client.ProposeLabelsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Completed, result.Status);
        Assert.Equal(["NIKKE", "401"], result.Labels);
        Assert.Equal(
            FoundryVisionNormalization.DuplicateLabelsCollapsed
                | FoundryVisionNormalization.TruncatedRepetitionRecovered,
            result.Normalization);
    }

    [Fact]
    public async Task TruncatedResponseWithoutProvenRepetitionRemainsUnknown()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"labels\\\":[\\\"Open\\\",\\\"Setti\"}");

        var result = await client.ProposeLabelsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Unknown, result.Status);
        Assert.Equal(FoundryVisionFailure.InvalidResponse, result.Failure);
        Assert.Equal(FoundryVisionNormalization.None, result.Normalization);
        Assert.Empty(result.Labels);
    }

    [Fact]
    public async Task WrongSchemaWithRepetitionRemainsUnknown()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"label_list\\\":[\\\"401\\\",\\\"401\\\",\\\"401\\\"\"}");

        var result = await client.ProposeLabelsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Unknown, result.Status);
        Assert.Equal(FoundryVisionFailure.InvalidResponse, result.Failure);
        Assert.Equal(FoundryVisionNormalization.None, result.Normalization);
        Assert.Empty(result.Labels);
    }

    [Fact]
    public async Task ProviderFailureIsUnknownWithoutRetry()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(EventStream(
                "{\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"bad\",\"message\":\"no\"}}}"));
        });
        using var client = new FoundryLocalVisionClient(
            new Uri("http://127.0.0.1:5000"),
            "model",
            TimeSpan.FromSeconds(1),
            handler);

        var result = await client.ProposeLabelsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Unknown, result.Status);
        Assert.Equal(FoundryVisionFailure.Provider, result.Failure);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task StreamWithoutCompletedEventIsUnknown()
    {
        using var client = ClientReturningWithoutCompleted(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"labels\\\":[\\\"OpenEvent\\\"]}\"}");

        var result = await client.ProposeLabelsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Unknown, result.Status);
        Assert.Equal(FoundryVisionFailure.Provider, result.Failure);
        Assert.Empty(result.Labels);
    }

    [Fact]
    public async Task TimeoutIsUnknownWithoutRetry()
    {
        var calls = 0;
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            calls++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        using var client = new FoundryLocalVisionClient(
            new Uri("http://127.0.0.1:5000"),
            "model",
            TimeSpan.FromMilliseconds(20),
            handler);

        var result = await client.ProposeLabelsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Unknown, result.Status);
        Assert.Equal(FoundryVisionFailure.Timeout, result.Failure);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Controls_include_text_and_icon_bounds_from_one_strict_response()
    {
        string? observedBody = null;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            observedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return EventStream(
                "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"controls\\\":[{\\\"kind\\\":\\\"text\\\",\\\"label\\\":\\\"部隊\\\",\\\"x\\\":0.1,\\\"y\\\":0.2,\\\"width\\\":0.15,\\\"height\\\":0.08},{\\\"kind\\\":\\\"icon\\\",\\\"label\\\":\\\"歯車\\\",\\\"x\\\":0.9,\\\"y\\\":0.02,\\\"width\\\":0.05,\\\"height\\\":0.05}]}\"}",
                "{\"type\":\"response.completed\",\"response\":{\"usage\":{}}}");
        });
        using var client = new FoundryLocalVisionClient(
            new Uri("http://127.0.0.1:5000"),
            "model",
            TimeSpan.FromSeconds(1),
            handler);

        var result = await client.ProposeControlsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Completed, result.Status);
        Assert.Collection(
            result.Controls,
            control =>
            {
                Assert.Equal("text", control.Kind);
                Assert.Equal("部隊", control.Label);
                Assert.Equal(0.1, control.X);
            },
            control =>
            {
                Assert.Equal("icon", control.Kind);
                Assert.Equal("歯車", control.Label);
                Assert.Equal(0.05, control.Width);
            });
        Assert.Contains("icon-only controls", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"max_output_tokens\":1500", observedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Goal_specific_controls_prompt_accepts_icon_and_returns_only_one_control()
    {
        string? observedBody = null;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            observedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return EventStream(
                "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"controls\\\":[{\\\"kind\\\":\\\"icon\\\",\\\"label\\\":\\\"ホーム\\\",\\\"x\\\":0.05,\\\"y\\\":0.9,\\\"width\\\":0.05,\\\"height\\\":0.05},{\\\"kind\\\":\\\"icon\\\",\\\"label\\\":\\\"設定\\\",\\\"x\\\":0.9,\\\"y\\\":0.02,\\\"width\\\":0.05,\\\"height\\\":0.05}]}\"}",
                "{\"type\":\"response.completed\",\"response\":{\"usage\":{}}}");
        });
        using var client = new FoundryLocalVisionClient(
            new Uri("http://127.0.0.1:5000"),
            "model",
            TimeSpan.FromSeconds(1),
            handler);

        var result = await client.ProposeControlsAsync(new byte[] { 1 }, "ホームへ戻る");

        var control = Assert.Single(result.Controls);
        Assert.Equal("icon", control.Kind);
        Assert.Equal("ホーム", control.Label);
        Assert.True(result.Normalization.HasFlag(FoundryVisionNormalization.TargetIntentMismatchDropped));
        using var request = JsonDocument.Parse(observedBody!);
        var prompt = request.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("ホームへ戻る", prompt, StringComparison.Ordinal);
        Assert.Contains("return exactly one visible clickable control", prompt, StringComparison.Ordinal);
        Assert.Contains("icon-only, or be an image button", prompt, StringComparison.Ordinal);
        Assert.Contains("shortest exact substring copied from the current goal", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Goal_specific_controls_drop_a_control_unrelated_to_the_requested_goal()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(EventStream(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"controls\\\":[{\\\"kind\\\":\\\"icon\\\",\\\"label\\\":\\\"出撃\\\",\\\"x\\\":0.5,\\\"y\\\":0.7,\\\"width\\\":0.2,\\\"height\\\":0.1}]}\"}",
            "{\"type\":\"response.completed\",\"response\":{\"usage\":{}}}")));
        using var client = new FoundryLocalVisionClient(
            new Uri("http://127.0.0.1:5000"), "model", TimeSpan.FromSeconds(1), handler);

        var result = await client.ProposeControlsAsync(new byte[] { 1 }, "部隊編成を開く");

        Assert.Empty(result.Controls);
        Assert.True(result.Normalization.HasFlag(FoundryVisionNormalization.TargetIntentMismatchDropped));
    }

    [Fact]
    public async Task Control_bounds_outside_the_frame_are_unknown_without_normalization()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"controls\\\":[{\\\"kind\\\":\\\"icon\\\",\\\"label\\\":\\\"歯車\\\",\\\"x\\\":0.98,\\\"y\\\":0.1,\\\"width\\\":0.1,\\\"height\\\":0.1}]}\"}");

        var result = await client.ProposeControlsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Unknown, result.Status);
        Assert.Equal(FoundryVisionFailure.InvalidResponse, result.Failure);
        Assert.Empty(result.Controls);
    }

    [Fact]
    public async Task Control_schema_rejects_extra_properties()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"controls\\\":[{\\\"kind\\\":\\\"text\\\",\\\"label\\\":\\\"部隊\\\",\\\"x\\\":0.1,\\\"y\\\":0.2,\\\"width\\\":0.1,\\\"height\\\":0.1,\\\"action\\\":\\\"click\\\"}]}\"}");

        var result = await client.ProposeControlsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Unknown, result.Status);
        Assert.Equal(FoundryVisionFailure.InvalidResponse, result.Failure);
    }

    [Fact]
    public async Task Exact_duplicate_controls_are_collapsed_after_complete_json()
    {
        using var client = ClientReturning(
            "{\"type\":\"response.output_text.delta\",\"delta\":\"{\\\"controls\\\":[{\\\"kind\\\":\\\"icon\\\",\\\"label\\\":\\\"前哨基地\\\",\\\"x\\\":0.3,\\\"y\\\":0.5,\\\"width\\\":0.1,\\\"height\\\":0.1},{\\\"kind\\\":\\\"icon\\\",\\\"label\\\":\\\"前哨基地\\\",\\\"x\\\":0.3,\\\"y\\\":0.5,\\\"width\\\":0.1,\\\"height\\\":0.1}]}\"}");

        var result = await client.ProposeControlsAsync(new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Completed, result.Status);
        Assert.Single(result.Controls);
        Assert.Equal(FoundryVisionNormalization.DuplicateLabelsCollapsed, result.Normalization);
    }

    private static FoundryLocalVisionClient ClientReturning(params string[] events) => new(
        new Uri("http://127.0.0.1:5000"),
        "model",
        TimeSpan.FromSeconds(1),
        new StubHandler((_, _) => Task.FromResult(EventStream([
            .. events,
            "{\"type\":\"response.completed\",\"response\":{\"usage\":{}}}",
        ]))));

    private static FoundryLocalVisionClient ClientReturningWithoutCompleted(params string[] events) => new(
        new Uri("http://127.0.0.1:5000"),
        "model",
        TimeSpan.FromSeconds(1),
        new StubHandler((_, _) => Task.FromResult(EventStream(events))));

    private static HttpResponseMessage EventStream(params string[] events)
    {
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
