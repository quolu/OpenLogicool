using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>数値で始める規則・段階・続けて送るキーを、実機で取得した所持品の画面で確かめる。</summary>
public sealed class VisualProgressStageTests
{
    private static string Fixture(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008", name);
    }

    private static VisualProgressProfile Profile() => VisualProgressProfile.Load(Fixture("progress.json"));

    [Theory]
    [InlineData("87/100", 87)]
    [InlineData("8 7 / 100", 87)]
    [InlineData("100/100", 100)]
    [InlineData("0/100", 0)]
    [InlineData("87", null)]
    [InlineData("8/10", null)]
    [InlineData("87/1000", null)]
    [InlineData("187/100", null)]
    [InlineData("1,662,752 87/100", null)]
    [InlineData("", null)]
    public void 上限つきの数値は上限まで読めた時だけ現在値を返す(string text, int? expected) =>
        Assert.Equal(expected, VisualProgressRecognizer.ReadNumber(text, new([0, 0, 1, 1], AtMost: 30, OutOf: 100)));

    [Theory]
    [InlineData("70", 70)]
    [InlineData("7 0", 70)]
    [InlineData("1,000", 1000)]
    [InlineData("70/100", null)]
    [InlineData("最大", null)]
    public void 上限のない数値は数字が一つだけの時に返す(string text, int? expected) =>
        Assert.Equal(expected, VisualProgressRecognizer.ReadNumber(text, new([0, 0, 1, 1], Exactly: 70)));

    [Fact]
    public async Task 通常画面のコインが条件を外れていれば補充を始めず条件を外れたと知らせる()
    {
        var frame = ReadFrame(Fixture("coin-hud.png"));
        var unmet = new List<string>();
        var choice = await new VisualProgressRecognizer(Profile()).RecognizeNumberRuleAsync(frame, Viewport(frame), null,
            _ => true, rule => unmet.Add(rule.Id));
        Assert.Null(choice);
        Assert.Equal(["coin-refill-open"], unmet);
    }

    [Fact]
    public async Task 通常画面のコインが条件以下なら所持品を開くキーを選び段階の中と停止表示中は選ばない()
    {
        var frame = ReadFrame(Fixture("coin-hud.png"));
        // 実録は87/100。境だけを試験用に上げ、読み取りの領域と形式は製品の設定のまま使う。
        var profile = Profile();
        profile = profile with { Rules = profile.Rules.Select(rule => rule.Id == "coin-refill-open"
            ? rule with { Number = rule.Number! with { AtMost = 90 } } : rule).ToArray() };
        var recognizer = new VisualProgressRecognizer(profile);
        var choice = await recognizer.RecognizeNumberRuleAsync(frame, Viewport(frame), null, _ => true, _ => { });
        Assert.NotNull(choice);
        Assert.Equal(VisualProgressAction.Key, choice.Action);
        Assert.Equal("coin-refill-open", choice.RuleId);
        Assert.Equal("coin-refill-open:87", choice.Signature);
        Assert.Equal("Key:I", choice.Key);
        Assert.Equal("coin-refill", choice.NextStage);
        Assert.False(choice.AllowWhileInhibited);
        Assert.Null(await recognizer.RecognizeNumberRuleAsync(frame, Viewport(frame), "coin-refill", _ => true, _ => { }));
        Assert.Null(await recognizer.RecognizeNumberRuleAsync(frame, Viewport(frame), null, _ => true, _ => { }, inhibited: true));
        Assert.Null(await recognizer.RecognizeNumberRuleAsync(frame, Viewport(frame), null, _ => false, _ => { }));
    }

    [Fact]
    public void 数値で始める規則はほかに進める表示が無い時と優先度が高い時だけ選び確認の表示には譲る()
    {
        var recognizer = new VisualProgressRecognizer(Profile());
        var open = new VisualProgressChoice(VisualProgressAction.Key, "coin-refill-open", "coin-refill-open:30", "Key:I");
        Assert.Same(open, recognizer.Prefer(open, new(VisualProgressAction.Normal)));
        Assert.Same(open, recognizer.Prefer(open, new(VisualProgressAction.Key, "compass-space", "compass-space", "Key:Space", Immediate: true)));
        var prompt = new VisualProgressChoice(VisualProgressAction.Key, "screen-prompt", "screen-prompt", "Key:Space", Immediate: true);
        Assert.Same(prompt, recognizer.Prefer(open, prompt));
        var review = new VisualProgressChoice(VisualProgressAction.Review, Detail: "利用者の判断が必要な表示");
        Assert.Same(review, recognizer.Prefer(open, review));
        Assert.Same(prompt, recognizer.Prefer(null, prompt));
    }

    [Theory]
    [InlineData("bag-equipment.png", "coin-refill", "coin-refill-item-tab", 0.7986, 0.94)]
    [InlineData("bag-items.png", "coin-refill", "coin-refill-consumables", 0.7326, 0.162)]
    [InlineData("bag-consumables.png", "coin-refill", "coin-refill-box", 0.864, 0.4745)]
    public async Task 所持品の各画面は実機で進んだ位置をクリックする(string image, string stage, string ruleId, double x, double y)
    {
        var choice = await Recognize(image, stage);
        Assert.Equal(ruleId, choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.InRange(choice.Point![0], x - 0.02, x + 0.02);
        Assert.InRange(choice.Point[1], y - 0.02, y + 0.02);
    }

    [Fact]
    public async Task 箱の説明が1個の獲得の時だけ開封へ進む()
    {
        var choice = await Recognize("coin-box-detail.png", "coin-refill");
        Assert.Equal("coin-refill-detail", choice.RuleId);
        Assert.Equal("Key:Space", choice.Key);
        Assert.Null(choice.NextStage);
        // 同じ名前で5個を獲得する箱では進まない。
        Assert.NotEqual("coin-refill-detail", (await Recognize("coin-box-detail-five.png", "coin-refill")).RuleId);
    }

    [Fact]
    public async Task 数量が70でなければ打ち直し70を読めた時だけ開封して閉じる段階へ進む()
    {
        var typing = await Recognize("coin-box-quantity-one.png", "coin-refill");
        Assert.Equal("coin-refill-quantity", typing.RuleId);
        Assert.Equal("Key:Enter", typing.Key);
        Assert.Equal(["Key:Backspace", "Key:Backspace", "Key:Backspace", "Key:7", "Key:0", "Key:Enter"], typing.ThenKeys!);
        Assert.Null(typing.NextStage);
        var confirm = await Recognize("coin-box-quantity.png", "coin-refill");
        Assert.Equal("coin-refill-confirm", confirm.RuleId);
        Assert.Equal("Key:Space", confirm.Key);
        Assert.Equal("coin-refill-close", confirm.NextStage);
    }

    [Fact]
    public async Task 閉じる段階は開封の結果を確認してから所持品を閉じる()
    {
        var opened = await Recognize("box-opened.png", "coin-refill-close");
        Assert.Equal("coin-refill-opened", opened.RuleId);
        Assert.Equal("Key:Space", opened.Key);
        var close = await Recognize("bag-consumables.png", "coin-refill-close");
        Assert.Equal("coin-refill-close", close.RuleId);
        Assert.Equal("Key:Esc", close.Key);
    }

    [Theory]
    [InlineData("bag-equipment.png")]
    [InlineData("bag-items.png")]
    [InlineData("bag-consumables.png")]
    [InlineData("coin-box-detail.png")]
    [InlineData("coin-box-quantity-one.png")]
    [InlineData("coin-box-quantity.png")]
    public async Task 段階の外では所持品の画面を操作しない(string image)
    {
        var choice = await Recognize(image, null);
        Assert.False(choice.RuleId?.StartsWith("coin-refill", StringComparison.Ordinal) == true, choice.RuleId);
        Assert.NotEqual(VisualProgressAction.Key, choice.Action);
        Assert.NotEqual(VisualProgressAction.Click, choice.Action);
    }

    [Fact]
    public void 段階は始めた操作で入り通常の画面が続いたら終える()
    {
        var profile = Profile();
        var schedule = new VisualProgressSchedule(profile);
        var open = profile.Rules.Single(rule => rule.Id == "coin-refill-open");
        Assert.True(schedule.MayStart(open));
        schedule.RecordInput(0, new(VisualProgressAction.Key, open.Id, open.Id + ":30", open.Key, NextStage: open.NextStage));
        Assert.Equal("coin-refill", schedule.Stage);
        // 始めた規則は、数値が条件を外れたのを見るまで、もう一度は始めない。
        Assert.False(schedule.MayStart(open));
        var normal = new VisualProgressChoice(VisualProgressAction.Normal);
        // 所持品が開くまでの通常の画面では終えない。
        schedule.Decide(250, normal, false, true, true);
        schedule.Decide(2000, normal, false, true, true);
        Assert.Equal("coin-refill", schedule.Stage);
        // 所持品の画面（HUDなし）をはさんだら、通常の画面の時間を数え直す。
        schedule.Decide(2500, normal, false, false, true);
        schedule.Decide(3000, normal, false, true, true);
        schedule.Decide(5900, normal, false, true, true);
        Assert.Equal("coin-refill", schedule.Stage);
        schedule.Decide(6000, normal, false, true, true);
        Assert.Null(schedule.Stage);
        Assert.False(schedule.MayStart(open));
        schedule.ObserveUnmet(open);
        Assert.True(schedule.MayStart(open));
    }

    [Fact]
    public void 続けて送るキーは新しい観測ごとに一つずつ送り最後で予約を消す()
    {
        var device = new Device();
        var sequence = new VisualProgressInputSequence(new NanoGameInteractionActions(device, new Mapper()));
        var choice = new VisualProgressChoice(VisualProgressAction.Key, "quantity", "quantity", "Key:Enter",
            ThenKeys: ["Key:Backspace", "Key:7", "Key:0", "Key:Enter"]);
        Assert.Equal(GameInteractionDispatchStatus.Dispatched, sequence.Dispatch(choice, Observation(1)).Status);
        for (var index = 2; sequence.Pending is { } pending; index++)
        {
            Assert.Equal("quantity", pending.Signature);
            Assert.Equal(GameInteractionDispatchStatus.Dispatched, sequence.Dispatch(pending, Observation(index)).Status);
        }
        Assert.Equal(["Key:Enter", "Key:Backspace", "Key:7", "Key:0", "Key:Enter"], device.Calls);
    }

    [Theory]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Number": { "Bounds": [0, 0, 1, 1] } }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Number": { "Bounds": [0, 0, 1, 1], "AtMost": 1, "Exactly": 1 } }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Number": { "Bounds": [0, 0, 2, 1], "AtMost": 1 } }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "ThenKeys": [] }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "ThenKeys": ["不明"] }""")]
    [InlineData("""{ "Id": "x", "WaitForChange": true, "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "NextStage": "a" }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "", "Bounds": [0, 0, 1, 1], "Exact": true }] }""")]
    public void 数値と段階と続けて送るキーの不正な設定は読込みで拒否する(string rule)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $$"""{ "SchemaVersion": 1, "ReviewWhen": [], "Rules": [{{rule}}] }""");
            Assert.ThrowsAny<Exception>(() => VisualProgressProfile.Load(path));
        }
        finally { File.Delete(path); }
    }

    private static async Task<VisualProgressChoice> Recognize(string image, string? stage)
    {
        var frame = ReadFrame(Fixture(image));
        var recognizer = new VisualProgressRecognizer(Profile());
        return recognizer.Recognize(await recognizer.ReadOcrAsync(frame, Viewport(frame)), frame.Width, frame.Height,
            Viewport(frame), frame, stage: stage);
    }

    // 窓の画像は、枠の左右各1px・上31px・下1pxを除いた部分が描画領域。
    private static FrameRect Viewport(CapturedFrame frame) => new(1, 31, frame.Width - 2, frame.Height - 32);

    private static CapturedFrame ReadFrame(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return new(ContractSchemaVersions.Revision03, "stage-test", CaptureBackend.WindowsGraphicsCapture,
            1, 0, DateTimeOffset.UnixEpoch, bitmap.PixelWidth, bitmap.PixelHeight, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(bytes, bitmap.PixelWidth * 4));
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
        public string Click(SerialHidCursorPoint target) => throw new NotSupportedException();
        public string KeyTap(IReadOnlyList<string> keys) { Calls.AddRange(keys); return "key-receipt"; }
        public string Hover(SerialHidCursorPoint target) => throw new NotSupportedException();
        public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps) => throw new NotSupportedException();
        public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination) => throw new NotSupportedException();
        public string Flick(SerialHidCursorPoint start, SerialHidCursorPoint destination) => throw new NotSupportedException();
    }
}
