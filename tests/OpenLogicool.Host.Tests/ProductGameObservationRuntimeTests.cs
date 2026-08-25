using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class ProductGameObservationRuntimeTests
{
    [Fact]
    public async Task Observe_then_discover_uses_the_exact_same_frame_binding()
    {
        var frame = Frame(sequence: 7);
        var runtime = new ProductGameObservationRuntime(
            new FrameSource(frame),
            new ObservationSource(),
            new Discovery(),
            new EvidenceSink());

        var observation = await runtime.ObserveAsync();
        var scene = await runtime.DiscoverTargetsAsync(observation);

        Assert.Equal("artifact-7", observation.Frame.Artifact!.ArtifactId);
        Assert.Equal("image/png", observation.Frame.Artifact.MediaType);
        Assert.Equal("observation-7", scene.ObservationId);
        var candidate = Assert.Single(scene.Affordances);
        Assert.Equal(7, candidate.FrameSequence);
        Assert.Equal(3, candidate.TransformRevision);
        Assert.Equal("window:game", candidate.TargetWindowSourceId);
    }

    [Fact]
    public async Task Older_observation_cannot_discover_against_a_new_frame()
    {
        var frames = new Queue<CapturedFrame>([Frame(7), Frame(8)]);
        var runtime = new ProductGameObservationRuntime(
            new QueueFrameSource(frames),
            new ObservationSource(),
            new Discovery(),
            new EvidenceSink());
        var old = await runtime.ObserveAsync();
        _ = await runtime.ObserveAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runtime.DiscoverTargetsAsync(old));

        Assert.Contains("直前", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discovery_cannot_return_candidate_for_another_window()
    {
        var runtime = new ProductGameObservationRuntime(
            new FrameSource(Frame(7)),
            new ObservationSource(),
            new MismatchedDiscovery(),
            new EvidenceSink());
        var observation = await runtime.ObserveAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runtime.DiscoverTargetsAsync(observation));

        Assert.Contains("AffordanceCandidate", exception.Message, StringComparison.Ordinal);
    }

    private static CapturedFrame Frame(long sequence) => new(
        ContractSchemaVersions.Revision03,
        "window:game",
        CaptureBackend.WindowsGraphicsCapture,
        sequence,
        sequence * 100,
        DateTimeOffset.UnixEpoch.AddMilliseconds(sequence * 100),
        1920,
        1080,
        "BGRA8",
        96,
        96,
        3,
        10,
        250,
        Pixels: new FramePixels(new byte[1920 * 4], 1920 * 4));

    private sealed class FrameSource(CapturedFrame frame) : IProductGameFrameSource
    {
        public ValueTask<CapturedFrame> CaptureAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(frame);
    }

    private sealed class QueueFrameSource(Queue<CapturedFrame> frames) : IProductGameFrameSource
    {
        public ValueTask<CapturedFrame> CaptureAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(frames.Dequeue());
    }

    private sealed class ObservationSource : IObservationSource
    {
        public ObservationResult Observe(CapturedFrame frame) => new(
            ContractSchemaVersions.Revision03,
            $"observation-{frame.Sequence}",
            new CapturedFrameReference(
                ContractSchemaVersions.Revision03,
                frame.SourceId,
                frame.Backend,
                frame.Sequence,
                frame.MonotonicMs,
                frame.WallClockUtc,
                frame.TransformRevision,
                frame.FreshnessMs,
                frame.LastChangeMs),
            CaptureAvailability.Available,
            StateIdentityStatus.InsufficientEvidence,
            [],
            "test-recognizer",
            frame.FreshnessMs,
            null);
    }

    private sealed class EvidenceSink : IProductGameFrameEvidenceSink
    {
        public ValueTask<CapturedFrameArtifact> SaveAsync(
            CapturedFrame frame,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CapturedFrameArtifact(
                $"artifact-{frame.Sequence}",
                "image/png",
                new string('a', 64),
                frame.Width,
                frame.Height,
                $"C:\\evidence\\frame-{frame.Sequence}.png"));
    }

    private sealed class Discovery : IProductGameTargetDiscovery
    {
        public ValueTask<ObservedScene> DiscoverAsync(
            ObservationResult observation,
            CapturedFrame frame,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Scene(observation, observation.Frame.SourceId));
    }

    private sealed class MismatchedDiscovery : IProductGameTargetDiscovery
    {
        public ValueTask<ObservedScene> DiscoverAsync(
            ObservationResult observation,
            CapturedFrame frame,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Scene(observation, "window:other"));
    }

    private static ObservedScene Scene(ObservationResult observation, string candidateWindow) => new(
        ContractSchemaVersions.Revision03,
        $"scene-{observation.Frame.Sequence}",
        observation.ObservationId,
        observation.Frame,
        observation.CaptureAvailability,
        StateIdentityStatus.Novel,
        $"hypothesis:scene-{observation.Frame.Sequence}",
        [],
        [new AffordanceCandidate(
            ContractSchemaVersions.Revision03,
            "candidate-1",
            observation.ObservationId,
            observation.Frame.Sequence,
            observation.Frame.TransformRevision,
            candidateWindow,
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "vision-region",
                [0.1, 0.2, 0.3, 0.1],
                "locator-1"),
            [new EvidenceRegion(
                ContractSchemaVersions.Revision03,
                "rect",
                [0.1, 0.2, 0.3, 0.1],
                "test-vision")],
            0.9,
            [GameInteractionOperations.Click])],
        "test-discovery");
}
