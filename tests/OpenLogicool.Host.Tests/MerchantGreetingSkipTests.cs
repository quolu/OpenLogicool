using System.IO;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class MerchantGreetingSkipTests
{
    private static VisualProgressProfile Profile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        return VisualProgressProfile.Load(Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008/progress.json"));
    }

    private static WindowsGameOcrWord[] Greeting(string name = "ネコ商品") =>
        [new(name, 400, 80, 120, 25), new("こんニャちわ", 400, 130, 200, 25), new("スキップ", 880, 30, 90, 25)];

    private static VisualProgressChoice Recognize(WindowsGameOcrWord[] words) =>
        new VisualProgressRecognizer(Profile()).Recognize(new("", "ja", 0, words), 1000, 600, new(0, 0, 1000, 600));

    [Theory]
    [InlineData("ネコ商品")]
    [InlineData("ネコ商人")]
    public void 三語が揃った時だけ右上のスキップとクリック後のEscを選ぶ(string name)
    {
        var choice = Recognize(Greeting(name));
        Assert.Equal("merchant-greeting-skip", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.InRange(choice.Point![0], 0.88, 0.99);
        Assert.InRange(choice.Point[1], 0, 0.15);
        Assert.Equal("Key:Esc", choice.AfterClickKey);
        Assert.Null(choice.Key);
        var schedule = new VisualProgressSchedule(Profile());
        Assert.Equal(VisualProgressAction.Click, schedule.Decide(0, choice, false, false, true).Action);
        // 停止表示が止めるのはSpaceを押す操作だけ。クリックとEscのこの規則は停止表示中も選ぶ。
        Assert.True(choice.AllowWhileInhibited);
        Assert.Equal(VisualProgressAction.Click, schedule.Decide(0, choice, true, false, true).Action);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void 一語でも欠けた時はスキップしない(int omitted)
    {
        Assert.NotEqual("merchant-greeting-skip", Recognize(Greeting().Where((_, index) => index != omitted).ToArray()).RuleId);
    }

    [Fact]
    public void 右上以外のスキップ文字をクリック先にしない()
    {
        var words = Greeting();
        words[2] = new("スキップ", 300, 500, 90, 25);
        Assert.NotEqual("merchant-greeting-skip", Recognize(words).RuleId);
    }

    [Fact]
    public void クリック成功後に新しい観測でEscを一度送り手入力待ちでも予約を保つ()
    {
        var device = new Device();
        var sequence = new VisualProgressInputSequence(new NanoGameInteractionActions(device, new Mapper()));
        var choice = Recognize(Greeting());
        Assert.Equal(GameInteractionDispatchStatus.Dispatched, sequence.Dispatch(choice, Observation(1)).Status);
        Assert.Equal(["click"], device.Calls);
        var pending = Assert.IsType<VisualProgressChoice>(sequence.Pending);
        Assert.Equal("Key:Esc", pending.Key);
        Assert.Equal(VisualProgressAction.Key, pending.Action);
        Assert.Equal(choice.Signature, pending.Signature);
        // 利用者の入力中はdispatchしないので、予約したEscはそのまま残る。
        Assert.Same(pending, sequence.Pending);
        Assert.Equal(GameInteractionDispatchStatus.Dispatched, sequence.Dispatch(pending, Observation(2)).Status);
        Assert.Equal(["click", "Key:Esc"], device.Calls);
        Assert.Null(sequence.Pending);
    }

    [Fact]
    public void クリック失敗ではEscを予約せず再送もしない()
    {
        var device = new Device { FailClick = true };
        var sequence = new VisualProgressInputSequence(new NanoGameInteractionActions(device, new Mapper()));
        Assert.Equal(GameInteractionDispatchStatus.DispatchFailed, sequence.Dispatch(Recognize(Greeting()), Observation(1)).Status);
        Assert.Null(sequence.Pending);
        Assert.Equal(["click"], device.Calls);
    }

    [Fact]
    public void 停止表示で許可された別操作はクリック後のEscを消さない()
    {
        var device = new Device();
        var sequence = new VisualProgressInputSequence(new NanoGameInteractionActions(device, new Mapper()));
        sequence.Dispatch(Recognize(Greeting()), Observation(1));
        var pending = sequence.Pending;
        sequence.Dispatch(new(VisualProgressAction.Key, "rest-exit", "休憩退出", "Key:Space"), Observation(2));
        Assert.Same(pending, sequence.Pending);
        sequence.Dispatch(pending!, Observation(3));
        Assert.Equal(["click", "Key:Space", "Key:Esc"], device.Calls);
    }

    [Fact]
    public void クリックのない規則にクリック後のキーを指定したら読込みで拒否する()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new VisualProgressProfile(1,
                [new("invalid", [new("文字", [0, 0, 1, 1])], Key: "Key:Space", AfterClickKey: "Key:Esc")], [])));
            Assert.Throws<InvalidDataException>(() => VisualProgressProfile.Load(path));
        }
        finally { File.Delete(path); }
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
        public bool FailClick { get; init; }
        public string Click(SerialHidCursorPoint target)
        {
            Calls.Add("click");
            if (FailClick) throw new InvalidOperationException("クリック失敗");
            return "click-receipt";
        }
        public string KeyTap(IReadOnlyList<string> keys) { Calls.AddRange(keys); return "key-receipt"; }
        public string Hover(SerialHidCursorPoint target) => throw new NotSupportedException();
        public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps) => throw new NotSupportedException();
        public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination) => throw new NotSupportedException();
    }
}
