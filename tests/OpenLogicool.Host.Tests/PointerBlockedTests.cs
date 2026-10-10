using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>クリックの前にpointerを動かせなかった失敗を、ほかの失敗と見分ける。</summary>
public sealed class PointerBlockedTests
{
    [Fact]
    public void pointerを動かせなかったクリックだけに印が付きボタンは押さない()
    {
        var device = new Device { ClickFailure = new SerialHidPointerMoveException("cursorが変化しませんでした") };
        var actions = new NanoGameInteractionActions(device, new Mapper());
        var sequence = new VisualProgressInputSequence(actions);
        var click = new VisualProgressChoice(VisualProgressAction.Click, "exit", "exit", Point: [0.4, 0.9], AfterClickKey: "Key:Esc");

        Assert.Equal(GameInteractionDispatchStatus.DispatchFailed, sequence.Dispatch(click, Observation(1)).Status);
        Assert.True(actions.LastDispatchPointerUnmoved);
        // クリックが送れていないので、クリック後のキーも予約しない。
        Assert.Null(sequence.Pending);
        Assert.Equal(["click"], device.Calls);

        // 次の観測でやり直して送れたら、印は消える。
        device.ClickFailure = null;
        Assert.Equal(GameInteractionDispatchStatus.Dispatched, sequence.Dispatch(click, Observation(2)).Status);
        Assert.False(actions.LastDispatchPointerUnmoved);
        Assert.NotNull(sequence.Pending);
    }

    [Fact]
    public void pointer以外の失敗には印を付けない()
    {
        var device = new Device { ClickFailure = new InvalidOperationException("Serial HID sessionは再利用できません。") };
        var actions = new NanoGameInteractionActions(device, new Mapper());
        var click = new VisualProgressChoice(VisualProgressAction.Click, "exit", "exit", Point: [0.4, 0.9]);

        var receipt = new VisualProgressInputSequence(actions).Dispatch(click, Observation(1));
        Assert.Equal(GameInteractionDispatchStatus.DispatchFailed, receipt.Status);
        Assert.False(actions.LastDispatchPointerUnmoved);
    }

    private static ObservationResult Observation(long sequence) => new(ContractSchemaVersions.Revision03,
        $"observation-{sequence}", new CapturedFrameReference(ContractSchemaVersions.Revision03, "window:game",
            CaptureBackend.WindowsGraphicsCapture, sequence, 0, DateTimeOffset.UnixEpoch, 1, 0, 0),
        CaptureAvailability.Available, StateIdentityStatus.Novel, [], "ocr", 0, null);

    private sealed class Mapper : IGameInteractionCoordinateMapper
    {
        public SerialHidCursorPoint MapTargetCenter(GameInteractionTargetBinding target) => new(925, 42);
        public SerialHidCursorPoint MapNormalized(IReadOnlyList<double> point) => new(925, 42);
    }

    private sealed class Device : INanoGameInputDevice
    {
        public List<string> Calls { get; } = [];
        public Exception? ClickFailure { get; set; }
        public string Click(SerialHidCursorPoint target)
        {
            Calls.Add("click");
            if (ClickFailure is not null) throw ClickFailure;
            return "click-receipt";
        }
        public string KeyTap(IReadOnlyList<string> keys) { Calls.AddRange(keys); return "key-receipt"; }
        public string Hover(SerialHidCursorPoint target) => throw new NotSupportedException();
        public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps) => throw new NotSupportedException();
        public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination) => throw new NotSupportedException();
    }
}
