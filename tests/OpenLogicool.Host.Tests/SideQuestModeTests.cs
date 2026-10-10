using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>サイドクエストモードの受注の流れを、実機で取得したクエスト一覧の画面で確かめる。</summary>
public sealed class SideQuestModeTests
{
    private const string Stage = "side-quest";

    private static string Fixture(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008", name);
    }

    private static VisualProgressProfile Profile() => VisualProgressProfile.Load(Fixture("progress.json"));

    [Fact]
    public async Task 通常の画面ではクエストの帯を押す()
    {
        var choice = await Recognize("side-quest-hud.png", Stage);
        Assert.Equal("side-quest-open", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.InRange(choice.Point![0], 0.955, 0.98);
        Assert.InRange(choice.Point[1], 0.19, 0.21);
        Assert.True(choice.AllowWhileInhibited);
    }

    [Fact]
    public async Task サイドが選ばれていなければサイドを押す()
    {
        var choice = await Recognize("side-quest-list-all.png", Stage);
        Assert.Equal("side-quest-tab", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.InRange(choice.Point![0], 0.775, 0.8);
        Assert.InRange(choice.Point[1], 0.16, 0.18);
    }

    [Fact]
    public async Task 一覧の途中では上へ払い一番上では払わない()
    {
        var middle = await Recognize("side-quest-list-middle.png", Stage);
        Assert.Equal("side-quest-to-top", middle.RuleId);
        Assert.Equal(VisualProgressAction.Flick, middle.Action);
        // 描画領域の縦49%で押し、64%まで下へ進む（一覧は上へ戻る）。
        Assert.InRange(middle.Point![0], 0.79, 0.81);
        Assert.InRange(middle.Point[1], 0.49, 0.515);
        Assert.InRange(middle.FlickTo![0], 0.79, 0.81);
        Assert.InRange(middle.FlickTo[1], 0.64, 0.66);
        Assert.False(middle.EndStage);
        // 一覧の文字が変われば、次の払う操作として扱う。
        Assert.StartsWith("side-quest-to-top:", middle.Signature);
        Assert.True(middle.Signature!.Length > "side-quest-to-top:".Length + 8);

        foreach (var top in new[] { "side-quest-list-top.png", "side-quest-list-top-progress.png" })
            Assert.NotEqual(VisualProgressAction.Flick, (await Recognize(top, Stage)).Action);
    }

    [Theory]
    [InlineData("side-quest-list-top.png", 0.448, "料理入門", "料理を学びたいけど")]
    [InlineData("side-quest-list-top-progress.png", 0.518, "料理入門", "食料品店で砂糖を購入")]
    public async Task 一番上では先頭のクエストの内容を覚えて進行を押し段階を終える(string image, double y, string title, string body)
    {
        var choice = await Recognize(image, Stage);
        Assert.Equal("side-quest-accept", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.InRange(choice.Point![0], 0.9, 0.93);
        Assert.InRange(choice.Point[1], y - 0.015, y + 0.015);
        Assert.True(choice.EndStage);
        var remembered = choice.Remembered!;
        Assert.Equal("side-quest", remembered.Name);
        Assert.Contains(remembered.Lines, line => line.Contains(title, StringComparison.Ordinal));
        Assert.Contains(remembered.Lines, line => line.Contains(body, StringComparison.Ordinal));
        // 覚えるのは押すカードまで。次のカードの名前は入れない。
        Assert.DoesNotContain(remembered.Lines, line => line.Contains("肝試し", StringComparison.Ordinal));
        Assert.InRange(remembered.Bounds[1] + remembered.Bounds[3], choice.Point[1] + 0.03, choice.Point[1] + 0.06);
    }

    [Theory]
    [InlineData("side-quest-hud.png")]
    [InlineData("side-quest-list-all.png")]
    [InlineData("side-quest-list-middle.png")]
    [InlineData("side-quest-list-top.png")]
    [InlineData("side-quest-list-top-progress.png")]
    public async Task モードの段階の外ではクエストの一覧を操作しない(string image)
    {
        var choice = await Recognize(image, null);
        Assert.False(choice.RuleId?.StartsWith("side-quest", StringComparison.Ordinal) == true, choice.RuleId);
        Assert.NotEqual(VisualProgressAction.Flick, choice.Action);
    }

    [Fact]
    public void モードは通常の画面が続いた時に段階を始め受注の操作で段階を終える()
    {
        var profile = Profile();
        var mode = profile.Modes!.Single();
        var schedule = new VisualProgressSchedule(profile);
        var normal = new VisualProgressChoice(VisualProgressAction.Normal);
        schedule.Decide(0, normal, false, true, true);
        Assert.Null(schedule.Stage);

        schedule.SetMode(mode);
        Assert.Equal("side-quest", schedule.ModeId);
        // 通常の画面でない間（会話など）は始めない。
        schedule.Decide(1000, normal, false, false, true);
        schedule.Decide(4000, normal, false, false, true);
        Assert.Null(schedule.Stage);
        // 通常の画面が2秒続いたら始める。停止表示中でも始める。
        schedule.Decide(5000, normal, true, true, true);
        schedule.Decide(6900, normal, true, true, true);
        Assert.Null(schedule.Stage);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(7000, normal, true, true, true).Action);
        Assert.Equal(Stage, schedule.Stage);

        schedule.RecordInput(7500, new(VisualProgressAction.Click, "side-quest-open", "side-quest-open:", Point: [0.97, 0.2]));
        Assert.Equal(Stage, schedule.Stage);
        schedule.RecordInput(9000, new(VisualProgressAction.Click, "side-quest-accept", "side-quest-accept:進行", Point: [0.91, 0.45], EndStage: true));
        Assert.Null(schedule.Stage);
        // 受注した後は、通常の画面が続いても始め直さない。
        schedule.Decide(10000, normal, false, true, true);
        schedule.Decide(20000, normal, false, true, true);
        Assert.Null(schedule.Stage);
    }

    [Fact]
    public void クエストクリアの操作の後は通常の画面へ戻った時に次の受注を始める()
    {
        var profile = Profile();
        var schedule = new VisualProgressSchedule(profile);
        var normal = new VisualProgressChoice(VisualProgressAction.Normal);
        schedule.SetMode(profile.Modes!.Single());
        schedule.Decide(0, normal, false, true, true);
        schedule.Decide(2000, normal, false, true, true);
        schedule.RecordInput(3000, new(VisualProgressAction.Click, "side-quest-accept", "side-quest-accept:進行", Point: [0.91, 0.45], EndStage: true));
        Assert.Null(schedule.Stage);

        schedule.RecordInput(60000, new(VisualProgressAction.Key, "quest-reward-prompt", "quest-reward-prompt", "Key:Space", Immediate: true));
        schedule.Decide(61000, normal, false, true, true);
        Assert.Null(schedule.Stage);
        schedule.Decide(63000, normal, false, true, true);
        Assert.Equal(Stage, schedule.Stage);
    }

    [Fact]
    public void モードに入っていなければクエストクリアの後も始めない()
    {
        var schedule = new VisualProgressSchedule(Profile());
        var normal = new VisualProgressChoice(VisualProgressAction.Normal);
        schedule.RecordInput(0, new(VisualProgressAction.Key, "quest-reward-prompt", "quest-reward-prompt", "Key:Space", Immediate: true));
        schedule.Decide(1000, normal, false, true, true);
        schedule.Decide(9000, normal, false, true, true);
        Assert.Null(schedule.Stage);
    }

    [Fact]
    public void 解除するとモードが始めた段階をその場で終え始め直さない()
    {
        var profile = Profile();
        var schedule = new VisualProgressSchedule(profile);
        var normal = new VisualProgressChoice(VisualProgressAction.Normal);
        schedule.SetMode(profile.Modes!.Single());
        schedule.Decide(0, normal, false, true, true);
        schedule.Decide(2000, normal, false, true, true);
        schedule.RecordInput(2500, new(VisualProgressAction.Click, "side-quest-open", "side-quest-open:", Point: [0.97, 0.2]));
        Assert.Equal(Stage, schedule.Stage);

        schedule.SetMode(null);
        Assert.Null(schedule.Stage);
        Assert.Null(schedule.ModeId);
        schedule.Decide(3000, normal, false, true, true);
        schedule.Decide(9000, normal, false, true, true);
        Assert.Null(schedule.Stage);
    }

    [Fact]
    public void モードの段階が何も送らずに終わった時は黙って終えずに知らせる()
    {
        var profile = Profile();
        var schedule = new VisualProgressSchedule(profile);
        var normal = new VisualProgressChoice(VisualProgressAction.Normal);
        schedule.SetMode(profile.Modes!.Single());
        schedule.Decide(0, normal, false, true, true);
        schedule.Decide(2000, normal, false, true, true);
        Assert.Equal(Stage, schedule.Stage);
        // クエストの帯を見つけられないまま、通常の画面が続いた。
        schedule.Decide(2250, normal, false, true, true);
        var ended = schedule.Decide(5250, normal, false, true, true);
        Assert.Null(schedule.Stage);
        Assert.Equal(VisualProgressAction.Review, ended.Action);
        Assert.Contains("サイドクエストモード", ended.Detail);
        // 見つけられなかった後は、同じ操作を繰り返し始めない。
        schedule.Decide(6000, normal, false, true, true);
        schedule.Decide(20000, normal, false, true, true);
        Assert.Null(schedule.Stage);
    }

    [Fact]
    public void 払う操作は始点と終点をNanoへ一回で送る()
    {
        var device = new Device();
        var sequence = new VisualProgressInputSequence(new NanoGameInteractionActions(device, new Mapper()));
        var receipt = sequence.Dispatch(new(VisualProgressAction.Flick, "side-quest-to-top", "side-quest-to-top:x",
            Point: [0.8, 0.5], FlickTo: [0.8, 0.65]), Observation(1));
        Assert.Equal(GameInteractionDispatchStatus.Dispatched, receipt.Status);
        Assert.Equal(GameInteractionOperations.Flick, receipt.Operation);
        Assert.Equal([(new SerialHidCursorPoint(800, 500), new SerialHidCursorPoint(800, 650))], device.Flicks);
        Assert.Null(sequence.Pending);
    }

    [Theory]
    [InlineData("""{ "Id": "x", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Flick": { "From": [0.5, 0.5], "To": [0.5] } }""")]
    [InlineData("""{ "Id": "x", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Flick": { "From": [0.5, 0.5], "To": [0.5, 1.2] } }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Flick": { "From": [0.5, 0.5], "To": [0.5, 0.6] } }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Areas": [] }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Areas": [{ "Bounds": [0, 0, 1, 1] }] }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Areas": [{ "Bounds": [0, 0, 1, 1], "Rgb": [0, 0] }] }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Areas": [{ "Bounds": [0, 0, 2, 1], "Flat": true }] }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Remember": { "Name": "a", "Bounds": [0, 0, 1, 1] } }""")]
    [InlineData("""{ "Id": "x", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Click": { "Text": "文字", "Bounds": [0, 0, 1, 1] }, "Remember": { "Name": "a b", "Bounds": [0, 0, 1, 1] } }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "EndStage": true }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "Stage": "a", "NextStage": "b", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "EndStage": true }""")]
    public void 払う操作と画素の条件と覚える指定の不正な設定は読込みで拒否する(string rule) =>
        Assert.ThrowsAny<Exception>(() => Load($$"""{ "SchemaVersion": 1, "ReviewWhen": [], "Rules": [{{rule}}] }"""));

    [Theory]
    [InlineData("""{ "Id": "m", "Name": "モード", "Stage": "無い段階" }""")]
    [InlineData("""{ "Id": "m", "Name": "", "Stage": "a" }""")]
    [InlineData("""{ "Id": "m", "Name": "モード", "Stage": "a", "RestartAfter": ["無い規則"] }""")]
    [InlineData("""{ "Id": "m", "Name": "モード", "Stage": "a" }, { "Id": "m", "Name": "別", "Stage": "a" }""")]
    public void モードの不正な設定は読込みで拒否する(string modes) =>
        Assert.ThrowsAny<Exception>(() => Load($$"""
            { "SchemaVersion": 1, "ReviewWhen": [], "Modes": [{{modes}}],
              "Rules": [{ "Id": "x", "Key": "Key:I", "Stage": "a", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }] }] }
            """));

    [Fact]
    public void モードは解除するまでBotの操作の入口を作り直しても残る()
    {
        var root = Path.Combine(Path.GetTempPath(), "openlogicool-mode-" + Guid.NewGuid().ToString("N"));
        try
        {
            BotScriptMode[] modes = [new("test", "side-quest", "サイドクエストモード", false)];
            using (var intents = Intents(root, modes))
            {
                Assert.Null(intents.Current().Mode);
                Assert.Null(intents.ActiveMode("test"));
                Assert.Throws<ArgumentException>(() => intents.SetMode("無いモード"));
                Assert.Equal("サイドクエストモード", intents.SetMode("side-quest").Mode);
                Assert.Equal("side-quest", intents.ActiveMode("test"));
                Assert.True(Assert.Single(intents.ListModes()).Active);
            }
            using (var intents = Intents(root, modes))
            {
                Assert.Equal("side-quest", intents.ActiveMode("test"));
                Assert.Equal("サイドクエストモード", intents.Current().Mode);
                Assert.Null(intents.ClearMode().Mode);
                Assert.Null(intents.ActiveMode("test"));
            }
            using (var intents = Intents(root, modes))
                Assert.False(Assert.Single(intents.ListModes()).Active);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static HostBotScriptIntents Intents(string root, BotScriptMode[] modes) =>
        new([new("test", "試験", "説明")], root, new(), (_, _, _, _) => Task.FromResult(new BotScriptResult(false, "終了")), modes: modes);

    private static VisualProgressProfile Load(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            return VisualProgressProfile.Load(path);
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
        return new(ContractSchemaVersions.Revision03, "side-quest-test", CaptureBackend.WindowsGraphicsCapture,
            1, 0, DateTimeOffset.UnixEpoch, bitmap.PixelWidth, bitmap.PixelHeight, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(bytes, bitmap.PixelWidth * 4));
    }

    private static ObservationResult Observation(long sequence) => new(ContractSchemaVersions.Revision03,
        $"observation-{sequence}", new CapturedFrameReference(ContractSchemaVersions.Revision03, "window:game",
            CaptureBackend.WindowsGraphicsCapture, sequence, 0, DateTimeOffset.UnixEpoch, 1, 0, 0),
        CaptureAvailability.Available, StateIdentityStatus.Novel, [], "ocr", 0, null);

    private sealed class Mapper : IGameInteractionCoordinateMapper
    {
        public SerialHidCursorPoint MapTargetCenter(GameInteractionTargetBinding target) => new(
            (int)Math.Round((target.NormalizedBounds[0] + target.NormalizedBounds[2] / 2) * 1000),
            (int)Math.Round((target.NormalizedBounds[1] + target.NormalizedBounds[3] / 2) * 1000));
        public SerialHidCursorPoint MapNormalized(IReadOnlyList<double> point) =>
            new((int)Math.Round(point[0] * 1000), (int)Math.Round(point[1] * 1000));
    }

    private sealed class Device : INanoGameInputDevice
    {
        public List<(SerialHidCursorPoint Start, SerialHidCursorPoint Destination)> Flicks { get; } = [];
        public string Click(SerialHidCursorPoint target) => throw new NotSupportedException();
        public string KeyTap(IReadOnlyList<string> keys) => throw new NotSupportedException();
        public string Hover(SerialHidCursorPoint target) => throw new NotSupportedException();
        public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps) => throw new NotSupportedException();
        public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination) => throw new NotSupportedException();
        public string Flick(SerialHidCursorPoint start, SerialHidCursorPoint destination) { Flicks.Add((start, destination)); return "flick"; }
    }
}
