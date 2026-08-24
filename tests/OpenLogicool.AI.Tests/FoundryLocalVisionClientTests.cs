using System.Net;
using System.Text;
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
        Assert.Equal(12, result.InputTokens);
        Assert.Equal(6, result.OutputTokens);
        Assert.Equal("http://127.0.0.1:5000/v1/responses", observedUri!.AbsoluteUri);
        Assert.Contains("\"stream\":true", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"message\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"input_image\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"image_data\":\"AQID\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"media_type\":\"image/png\"", observedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("image_url", observedBody, StringComparison.Ordinal);
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
