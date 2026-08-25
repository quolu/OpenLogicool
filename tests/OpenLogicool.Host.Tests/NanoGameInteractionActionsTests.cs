using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class NanoGameInteractionActionsTests
{
    [Fact]
    public void Five_input_operations_each_dispatch_once()
    {
        var device = new RecordingDevice();
        var actions = new NanoGameInteractionActions(device, new Mapper());
        var current = Observation();
        var target = Target();

        var receipts = new[]
        {
            actions.Hover(target, current),
            actions.Click(target, current),
            actions.KeyTap(new GameInteractionKeyTapRequest(
                ContractSchemaVersions.Revision03,
                current.ObservationId,
                current.Frame.Sequence,
                current.Frame.TransformRevision,
                current.Frame.SourceId,
                ["Key:Esc"]), current),
            actions.Scroll(new GameInteractionScrollRequest(
                ContractSchemaVersions.Revision03,
                target,
                1,
                0), current),
            actions.Drag(new GameInteractionDragRequest(
                ContractSchemaVersions.Revision03,
                target,
                [0.8, 0.7]), current),
        };

        Assert.All(receipts, receipt =>
        {
            Assert.Equal(GameInteractionDispatchStatus.Dispatched, receipt.Status);
            Assert.Equal(1, receipt.DispatchCount);
            Assert.Equal("NanoSerialHid", receipt.Route);
        });
        Assert.Equal(["hover", "click", "key-tap", "scroll", "drag"], device.Calls);
        Assert.Equal(2, device.ClickTarget.X);
        Assert.Equal(4, device.ClickTarget.Y);
        Assert.Equal(new SerialHidCursorPoint(8, 7), device.DragDestination);
    }

    [Fact]
    public void Stale_target_stops_before_device_call()
    {
        var device = new RecordingDevice();
        var actions = new NanoGameInteractionActions(device, new Mapper());
        var stale = Target() with { FrameSequence = 6 };

        Assert.Throws<InvalidOperationException>(() => actions.Click(stale, Observation()));

        Assert.Empty(device.Calls);
    }

    [Fact]
    public void Device_failure_is_explicit_and_never_retried()
    {
        var device = new RecordingDevice { Failure = new InvalidOperationException("nano fault") };
        var actions = new NanoGameInteractionActions(device, new Mapper());

        var receipt = actions.Click(Target(), Observation());

        Assert.Equal(GameInteractionDispatchStatus.DispatchFailed, receipt.Status);
        Assert.Equal(1, receipt.DispatchCount);
        Assert.Equal("nano fault", receipt.FailureReason);
        Assert.Equal(["click"], device.Calls);
    }

    private static ObservationResult Observation() => new(
        ContractSchemaVersions.Revision03,
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
        CaptureAvailability.Available,
        StateIdentityStatus.Novel,
        [],
        "vision",
        10,
        null);

    private static GameInteractionTargetBinding Target() => new(
        ContractSchemaVersions.Revision03,
        "observation-1",
        7,
        3,
        "window:game",
        "candidate-1",
        "locator-1",
        [0.1, 0.2, 0.2, 0.4]);

    private sealed class Mapper : IGameInteractionCoordinateMapper
    {
        public SerialHidCursorPoint MapTargetCenter(GameInteractionTargetBinding target) =>
            new(2, 4);

        public SerialHidCursorPoint MapNormalized(IReadOnlyList<double> normalizedPoint) =>
            new((int)(normalizedPoint[0] * 10), (int)(normalizedPoint[1] * 10));
    }

    private sealed class RecordingDevice : INanoGameInputDevice
    {
        public List<string> Calls { get; } = [];
        public Exception? Failure { get; set; }
        public SerialHidCursorPoint ClickTarget { get; private set; }
        public SerialHidCursorPoint DragDestination { get; private set; }

        public string Hover(SerialHidCursorPoint target) => Record("hover");

        public string Click(SerialHidCursorPoint target)
        {
            ClickTarget = target;
            return Record("click");
        }

        public string KeyTap(IReadOnlyList<string> keys) => Record("key-tap");

        public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps) =>
            Record("scroll");

        public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination)
        {
            DragDestination = destination;
            return Record("drag");
        }

        private string Record(string operation)
        {
            Calls.Add(operation);
            if (Failure is not null)
            {
                throw Failure;
            }
            return $"receipt:{operation}";
        }
    }
}
