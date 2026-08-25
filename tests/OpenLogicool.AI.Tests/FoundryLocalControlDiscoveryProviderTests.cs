using System.Net;
using System.Text;
using OpenLogicool.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.AI.Tests;

public sealed class FoundryLocalControlDiscoveryProviderTests
{
    [Fact]
    public async Task Text_and_icon_controls_become_frame_bound_affordances()
    {
        using var client = ClientReturning(
            "{\"controls\":[{\"kind\":\"text\",\"label\":\"部隊\",\"x\":0.1,\"y\":0.2,\"width\":0.2,\"height\":0.1},{\"kind\":\"icon\",\"label\":\"歯車\",\"x\":0.9,\"y\":0.02,\"width\":0.05,\"height\":0.05}]}");
        var provider = new FoundryLocalControlDiscoveryProvider(client);

        var result = await provider.ObserveAsync(Request(), new byte[] { 1, 2, 3 });

        Assert.Equal(StateIdentityStatus.Novel, result.Scene.StateIdentity);
        Assert.Collection(
            result.Scene.Affordances,
            text =>
            {
                Assert.Equal("foundry-local-text-region", text.Locator.LocatorType);
                Assert.Equal([0.1, 0.2, 0.2, 0.1], text.Locator.NormalizedBounds);
                Assert.Equal([GameInteractionOperations.Hover, GameInteractionOperations.Click], text.AllowedPrimitives);
            },
            icon =>
            {
                Assert.Equal("foundry-local-icon-region", icon.Locator.LocatorType);
                Assert.Equal([0.9, 0.02, 0.05, 0.05], icon.Locator.NormalizedBounds);
            });
        Assert.Equal(2, result.Proposals.Count);
        Assert.Equal("Completed", result.Scene.DiscoveryEvidence!.Status);
        Assert.Equal("clickable-controls-v3", result.Scene.DiscoveryEvidence.PromptRevision);
        Assert.All(result.Proposals, proposal => Assert.Equal(GameInteractionOperations.Click, proposal.Primitive));
        Assert.Equal(0, result.Telemetry.ExternalAiTransmissionCount);
    }

    [Fact]
    public async Task Provider_failure_returns_no_affordance_without_fallback()
    {
        using var client = ClientReturning("{\"labels\":[\"部隊\"]}");
        var provider = new FoundryLocalControlDiscoveryProvider(client);

        var result = await provider.ObserveAsync(Request(), new byte[] { 1 });

        Assert.Equal(FoundryVisionStatus.Unknown, result.ProviderResult.Status);
        Assert.Empty(result.Scene.Affordances);
        Assert.Empty(result.Proposals);
    }

    [Fact]
    public async Task Target_intent_limits_visual_discovery_to_one_goal_control()
    {
        using var client = ClientReturning(
            "{\"controls\":[{\"kind\":\"icon\",\"label\":\"ホーム\",\"x\":0.05,\"y\":0.9,\"width\":0.05,\"height\":0.05},{\"kind\":\"icon\",\"label\":\"設定\",\"x\":0.9,\"y\":0.02,\"width\":0.05,\"height\":0.05}]}");
        var provider = new FoundryLocalControlDiscoveryProvider(client);

        var result = await provider.ObserveAsync(Request("ホームへ戻る"), new byte[] { 1 });

        var affordance = Assert.Single(result.Scene.Affordances);
        Assert.Equal("ホーム", affordance.SemanticLabel);
        Assert.True(result.ProviderResult.Normalization.HasFlag(FoundryVisionNormalization.OutputLimitApplied));
        Assert.Equal(client.ControlsPromptSha256For("ホームへ戻る"), result.Scene.DiscoveryEvidence!.PromptSha256);
    }

    [Fact]
    public async Task Goal_mismatched_visual_control_is_kept_until_transition_result()
    {
        using var client = ClientReturning(
            "{\"controls\":[{\"kind\":\"icon\",\"label\":\"アーツ\",\"x\":0.2,\"y\":0.8,\"width\":0.1,\"height\":0.1}]}");
        var provider = new FoundryLocalControlDiscoveryProvider(client);

        var result = await provider.ObserveAsync(Request("ホームへ戻る"), new byte[] { 1 });

        Assert.Single(result.Scene.Affordances);
        Assert.Single(result.Proposals);
        Assert.Equal(FoundryVisionNormalization.None, result.ProviderResult.Normalization);
    }

    private static LocalVisionSceneRequest Request(string? targetIntent = null) => new(
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
        [],
        [],
        [GameInteractionOperations.Hover, GameInteractionOperations.Click],
        "structure-0",
        targetIntent);

    private static FoundryLocalVisionClient ClientReturning(string output) => new(
        new Uri("http://127.0.0.1:5000"),
        "model",
        TimeSpan.FromSeconds(1),
        new StubHandler((_, _) => Task.FromResult(EventStream(output))));

    private static HttpResponseMessage EventStream(string output)
    {
        var events = new[]
        {
            $"{{\"type\":\"response.output_text.delta\",\"delta\":{System.Text.Json.JsonSerializer.Serialize(output)}}}",
            "{\"type\":\"response.completed\",\"response\":{\"usage\":{}}}",
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                string.Join("\n\n", events.Select(value => $"data: {value}")) + "\n\ndata: [DONE]\n\n",
                Encoding.UTF8,
                "text/event-stream"),
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
