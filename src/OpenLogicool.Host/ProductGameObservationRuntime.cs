using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Host;

public interface IProductGameFrameSource
{
    ValueTask<CapturedFrame> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface IProductGameTargetDiscovery
{
    ValueTask<ObservedScene> DiscoverAsync(
        ObservationResult observation,
        CapturedFrame frame,
        CancellationToken cancellationToken = default);
}

public interface IProductGameRediscoveryTrigger
{
    void MarkTransitionUnconfirmed(ObservedScene before, AffordanceCandidate target);
    void MarkTransitionConfirmed(ObservedScene before, AffordanceCandidate target);
}

public interface IProductGameRouteControl
{
    void SetRouteTarget(StructureScreenEdge? edge);
    void BeginComparison();
    void EndComparison();
}

public interface IProductGameFrameEvidenceSink
{
    ValueTask<CapturedFrameArtifact> SaveAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Captureとtarget discoveryを同じfresh frameへ束縛するgame非依存の製品runtime。
/// OS、OCR、vision providerの詳細はadapterへ置く。
/// </summary>
public sealed class ProductGameObservationRuntime(
    IProductGameFrameSource frameSource,
    IObservationSource observationSource,
    IProductGameTargetDiscovery targetDiscovery,
    IProductGameFrameEvidenceSink evidenceSink) :
    IGameObservationRuntime,
    IProductGameRediscoveryTrigger,
    IProductGameRouteControl
{
    private readonly object gate = new();
    private CapturedFrame? currentFrame;
    private ObservationResult? currentObservation;

    public async ValueTask<ObservationResult> ObserveAsync(
        CancellationToken cancellationToken = default)
    {
        var frame = await frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("frame sourceがframeを返しませんでした。");
        var artifact = await evidenceSink.SaveAsync(frame, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("frame evidence sinkがartifactを返しませんでした。");
        var observation = observationSource.Observe(frame)
            ?? throw new InvalidOperationException("observation sourceが結果を返しませんでした。");
        observation = observation with { Frame = observation.Frame with { Artifact = artifact } };
        ValidateObservation(frame, observation);
        lock (gate)
        {
            currentFrame = frame;
            currentObservation = observation;
        }
        return observation;
    }

    public async ValueTask<ObservedScene> DiscoverTargetsAsync(
        ObservationResult observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        CapturedFrame frame;
        lock (gate)
        {
            if (currentFrame is null
                || currentObservation is null
                || !ObservationBindingMatches(observation, currentObservation))
            {
                throw new InvalidOperationException(
                    "target discoveryは直前にこのruntimeが返したObservationだけを受理します。");
            }
            frame = currentFrame;
        }

        var scene = await targetDiscovery
            .DiscoverAsync(observation, frame, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("target discoveryがsceneを返しませんでした。");
        ValidateScene(observation, scene);
        return scene;
    }

    public void MarkTransitionUnconfirmed(ObservedScene before, AffordanceCandidate target)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(target);
        if (targetDiscovery is IProductGameRediscoveryTrigger rediscovery)
        {
            rediscovery.MarkTransitionUnconfirmed(before, target);
        }
    }

    public void MarkTransitionConfirmed(ObservedScene before, AffordanceCandidate target)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(target);
        if (targetDiscovery is IProductGameRediscoveryTrigger rediscovery)
        {
            rediscovery.MarkTransitionConfirmed(before, target);
        }
    }

    public void SetRouteTarget(StructureScreenEdge? edge)
    {
        if (targetDiscovery is IProductGameRouteControl routeControl)
        {
            routeControl.SetRouteTarget(edge);
        }
    }

    public void BeginComparison()
    {
        if (targetDiscovery is IProductGameRouteControl routeControl)
        {
            routeControl.BeginComparison();
        }
    }

    public void EndComparison()
    {
        if (targetDiscovery is IProductGameRouteControl routeControl)
        {
            routeControl.EndComparison();
        }
    }

    private static void ValidateObservation(CapturedFrame frame, ObservationResult observation)
    {
        if (!string.Equals(frame.SourceId, observation.Frame.SourceId, StringComparison.Ordinal)
            || frame.Backend != observation.Frame.Backend
            || frame.Sequence != observation.Frame.Sequence
            || frame.TransformRevision != observation.Frame.TransformRevision
            || frame.FreshnessMs != observation.Frame.FreshnessMs
            || observation.Frame.Artifact is null)
        {
            throw new InvalidOperationException(
                "Observationがcapture frameのsource／sequence／transform／freshnessへ束縛されていません。");
        }
    }

    private static bool ObservationBindingMatches(
        ObservationResult supplied,
        ObservationResult current) =>
        string.Equals(supplied.ObservationId, current.ObservationId, StringComparison.Ordinal)
        && string.Equals(supplied.Frame.SourceId, current.Frame.SourceId, StringComparison.Ordinal)
        && supplied.Frame.Backend == current.Frame.Backend
        && supplied.Frame.Sequence == current.Frame.Sequence
        && supplied.Frame.TransformRevision == current.Frame.TransformRevision
        && supplied.Frame.FreshnessMs == current.Frame.FreshnessMs;

    private static void ValidateScene(ObservationResult observation, ObservedScene scene)
    {
        if (!string.Equals(scene.ObservationId, observation.ObservationId, StringComparison.Ordinal)
            || !string.Equals(scene.Frame.SourceId, observation.Frame.SourceId, StringComparison.Ordinal)
            || scene.Frame.Backend != observation.Frame.Backend
            || scene.Frame.Sequence != observation.Frame.Sequence
            || scene.Frame.TransformRevision != observation.Frame.TransformRevision
            || scene.CaptureAvailability != observation.CaptureAvailability)
        {
            throw new InvalidOperationException(
                "ObservedSceneが入力Observationのframe／windowへ束縛されていません。");
        }
        foreach (var candidate in scene.Affordances)
        {
            if (!string.Equals(candidate.ObservationId, observation.ObservationId, StringComparison.Ordinal)
                || candidate.FrameSequence != observation.Frame.Sequence
                || candidate.TransformRevision != observation.Frame.TransformRevision
                || !string.Equals(candidate.TargetWindowSourceId, observation.Frame.SourceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "AffordanceCandidateが入力Observationのframe／transform／windowへ束縛されていません。");
            }
        }
    }
}
