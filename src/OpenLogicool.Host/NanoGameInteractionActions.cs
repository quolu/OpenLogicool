using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

public interface INanoGameInputDevice
{
    string Hover(SerialHidCursorPoint target);

    string Click(SerialHidCursorPoint target);

    string KeyTap(IReadOnlyList<string> keys);

    string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps);

    string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination);
}

public interface IGameInteractionCoordinateMapper
{
    SerialHidCursorPoint MapTargetCenter(GameInteractionTargetBinding target);

    SerialHidCursorPoint MapNormalized(IReadOnlyList<double> normalizedPoint);
}

/// <summary>Observation束縛と一回dispatchだけを所有するgame非依存action port。</summary>
public sealed class NanoGameInteractionActions(
    INanoGameInputDevice device,
    IGameInteractionCoordinateMapper coordinates,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public GameInteractionDispatchReceipt Hover(
        GameInteractionTargetBinding target,
        ObservationResult current) =>
        TargetAction(GameInteractionOperations.Hover, target, current, () =>
            device.Hover(coordinates.MapTargetCenter(target)));

    public GameInteractionDispatchReceipt Click(
        GameInteractionTargetBinding target,
        ObservationResult current) =>
        TargetAction(GameInteractionOperations.Click, target, current, () =>
            device.Click(coordinates.MapTargetCenter(target)));

    public GameInteractionDispatchReceipt KeyTap(
        GameInteractionKeyTapRequest request,
        ObservationResult current)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateObservationBinding(
            request.ObservationId,
            request.FrameSequence,
            request.TransformRevision,
            request.TargetWindowSourceId,
            current);
        if (request.Keys.Count == 0
            || request.Keys.Any(string.IsNullOrWhiteSpace)
            || request.Keys.Distinct(StringComparer.Ordinal).Count() != request.Keys.Count)
        {
            throw new ArgumentException("KeyTapは重複のない有限key列を必要とします。", nameof(request));
        }
        return Dispatch(
            GameInteractionOperations.KeyTap,
            request.ObservationId,
            request.TargetWindowSourceId,
            candidateId: null,
            () => device.KeyTap(request.Keys));
    }

    public GameInteractionDispatchReceipt Scroll(
        GameInteractionScrollRequest request,
        ObservationResult current)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTarget(request.Target, current, GameInteractionOperations.Scroll, requirePrimitive: false);
        if (request.VerticalSteps == 0 && request.HorizontalSteps == 0)
        {
            throw new ArgumentException("Scrollは0以外のstepを必要とします。", nameof(request));
        }
        return Dispatch(
            GameInteractionOperations.Scroll,
            request.Target.ObservationId,
            request.Target.TargetWindowSourceId,
            request.Target.CandidateId,
            () => device.Scroll(
                coordinates.MapTargetCenter(request.Target),
                request.VerticalSteps,
                request.HorizontalSteps));
    }

    public GameInteractionDispatchReceipt Drag(
        GameInteractionDragRequest request,
        ObservationResult current)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTarget(request.Start, current, GameInteractionOperations.Drag, requirePrimitive: false);
        if (request.DestinationNormalized.Count != 2
            || request.DestinationNormalized.Any(value => !double.IsFinite(value) || value is < 0 or > 1))
        {
            throw new ArgumentException("Drag destinationはframe内normalized pointでなければなりません。", nameof(request));
        }
        return Dispatch(
            GameInteractionOperations.Drag,
            request.Start.ObservationId,
            request.Start.TargetWindowSourceId,
            request.Start.CandidateId,
            () => device.Drag(
                coordinates.MapTargetCenter(request.Start),
                coordinates.MapNormalized(request.DestinationNormalized)));
    }

    private GameInteractionDispatchReceipt TargetAction(
        string operation,
        GameInteractionTargetBinding target,
        ObservationResult current,
        Func<string> dispatch)
    {
        ValidateTarget(target, current, operation, requirePrimitive: true);
        return Dispatch(
            operation,
            target.ObservationId,
            target.TargetWindowSourceId,
            target.CandidateId,
            dispatch);
    }

    private GameInteractionDispatchReceipt Dispatch(
        string operation,
        string observationId,
        string windowSourceId,
        string? candidateId,
        Func<string> dispatch)
    {
        var started = time.GetUtcNow();
        try
        {
            var transportReceipt = dispatch();
            return new GameInteractionDispatchReceipt(
                ContractSchemaVersions.Revision03,
                operation,
                GameInteractionDispatchStatus.Dispatched,
                observationId,
                windowSourceId,
                "NanoSerialHid",
                1,
                started,
                time.GetUtcNow(),
                candidateId,
                transportReceipt,
                null);
        }
        catch (Exception exception)
        {
            return new GameInteractionDispatchReceipt(
                ContractSchemaVersions.Revision03,
                operation,
                GameInteractionDispatchStatus.DispatchFailed,
                observationId,
                windowSourceId,
                "NanoSerialHid",
                1,
                started,
                time.GetUtcNow(),
                candidateId,
                null,
                exception.Message);
        }
    }

    private static void ValidateTarget(
        GameInteractionTargetBinding target,
        ObservationResult current,
        string operation,
        bool requirePrimitive)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateObservationBinding(
            target.ObservationId,
            target.FrameSequence,
            target.TransformRevision,
            target.TargetWindowSourceId,
            current);
        if (target.NormalizedBounds.Count != 4
            || target.NormalizedBounds.Any(value => !double.IsFinite(value) || value is < 0 or > 1)
            || target.NormalizedBounds[2] <= 0
            || target.NormalizedBounds[3] <= 0
            || target.NormalizedBounds[0] + target.NormalizedBounds[2] > 1
            || target.NormalizedBounds[1] + target.NormalizedBounds[3] > 1
            || string.IsNullOrWhiteSpace(target.CandidateId)
            || string.IsNullOrWhiteSpace(target.LocatorRevision))
        {
            throw new ArgumentException("target bindingがframe内locator contractを満たしません。", nameof(target));
        }
        if (requirePrimitive && operation is not (GameInteractionOperations.Hover or GameInteractionOperations.Click))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static void ValidateObservationBinding(
        string observationId,
        long frameSequence,
        long transformRevision,
        string windowSourceId,
        ObservationResult current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!string.Equals(observationId, current.ObservationId, StringComparison.Ordinal)
            || frameSequence != current.Frame.Sequence
            || transformRevision != current.Frame.TransformRevision
            || !string.Equals(windowSourceId, current.Frame.SourceId, StringComparison.Ordinal)
            || current.CaptureAvailability != CaptureAvailability.Available)
        {
            throw new InvalidOperationException(
                "input targetは現在のAvailable Observation／frame／transform／windowへ束縛されていません。");
        }
    }
}
