using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class RepeatingImageKeyTests
{
    private static string Fixture
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
            return Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        }
    }

    private static VisualProgressProfile Profile() => VisualProgressProfile.Load(Path.Combine(Fixture, "progress.json"));
    private static FrameRect Viewport(CapturedFrame frame) => new(1, 31, frame.Width - 2, frame.Height - 32);

    [Fact]
    public void 利用者の羽根画像を実画面の右下から検出してTを選ぶ()
    {
        var frame = Read("feather-screen.png");
        var recognizer = new VisualProgressRecognizer(Profile());
        var choice = recognizer.RecognizeRepeatingImage(frame, Viewport(frame), _ => true);
        Assert.NotNull(choice);
        Assert.Equal("feather-t", choice.RuleId);
        Assert.Equal("Key:T", choice.Key);
        Assert.Equal(1000, choice.RepeatIntervalMs);
        Assert.Null(recognizer.RecognizeRepeatingImage(frame, Viewport(frame), _ => false));
        Assert.NotEqual("feather-t", recognizer.RecognizeImmediateImage(frame, Viewport(frame))?.RuleId);
    }

    [Theory]
    [InlineData("before.png")]
    [InlineData("combat.png")]
    [InlineData("compass-screen.png")]
    [InlineData("bonus-three.png")]
    [InlineData("class-level.png")]
    [InlineData("space-confirm.png")]
    public void 羽根のない実画面ではTを選ばない(string name)
    {
        var frame = Read(name);
        Assert.Null(new VisualProgressRecognizer(Profile()).RecognizeRepeatingImage(frame, Viewport(frame), _ => true));
    }

    [Fact]
    public void 羽根が消えた画面や暗い使用待ち表示ではTを選ばない()
    {
        var frame = Read("feather-screen.png");
        var bytes = frame.Pixels!.Bgra8.ToArray();
        for (var y = (int)(frame.Height * 0.8); y < frame.Height; y++)
            for (var x = (int)(frame.Width * 0.86); x < frame.Width; x++)
                for (var channel = 0; channel < 3; channel++) bytes[y * frame.Pixels.Stride + x * 4 + channel] /= 3;
        var dark = frame with { Pixels = new FramePixels(bytes, frame.Pixels.Stride) };
        Assert.Null(new VisualProgressRecognizer(Profile()).RecognizeRepeatingImage(dark, Viewport(dark), _ => true));
    }

    [Fact]
    public void 同じ羽根の表示でも一秒ごとに押せるが間隔前には押さない()
    {
        var profile = Profile();
        var rule = profile.Rules.Single(rule => rule.Id == "feather-t");
        var schedule = new VisualProgressSchedule(profile);
        var frame = Read("feather-screen.png");
        var recognizer = new VisualProgressRecognizer(profile);
        VisualProgressChoice? At(long now) => recognizer.RecognizeRepeatingImage(frame, Viewport(frame),
            candidate => schedule.RepeatIsDue(now, candidate));
        var first = At(0);
        Assert.NotNull(first);
        schedule.RecordInput(0, first);
        Assert.Null(At(999));
        var next = At(1000);
        Assert.NotNull(next);
        schedule.RecordInput(1000, next);
        Assert.False(schedule.RepeatIsDue(1999, rule));
        Assert.True(schedule.RepeatIsDue(2000, rule));
    }

    [Fact]
    public void Tを押しても会話の結果待ちやコンパスの一回送出をリセットしない()
    {
        var profile = Profile();
        var schedule = new VisualProgressSchedule(profile);
        var dialogue = new VisualProgressChoice(VisualProgressAction.Key, "npc-dialogue", "同じ台詞", "Key:Space");
        var feather = new VisualProgressChoice(VisualProgressAction.Key, "feather-t", "feather-t", "Key:T",
            Immediate: true, RepeatIntervalMs: 1000);
        schedule.RecordInput(0, dialogue);
        schedule.RecordInput(500, feather);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(1000, dialogue, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(2000, dialogue, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Review, schedule.Decide(5000, dialogue, false, false, true).Action);
        var compass = new VisualProgressChoice(VisualProgressAction.Key, "compass-space", "compass-space", "Key:Space", Immediate: true);
        schedule.RecordInput(6000, compass);
        schedule.RecordInput(6500, feather);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(7000, compass, false, true, true).Action);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1000, false)]
    public void 不正な反復設定を読込み時に拒否する(int interval, bool immediate)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new VisualProgressProfile(1,
                [new("repeat", [], "Key:T", Image: "unused.png", ImageBounds: [0, 0, 1, 1],
                    ImageClientWidth: 1506, Immediate: immediate, RepeatIntervalMs: interval)], [])));
            Assert.Throws<InvalidDataException>(() => VisualProgressProfile.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task 選択済みダンジョンの入場画面は報酬一覧とSpaceと入場の組合せで進める()
    {
        var frame = Read("dungeon-entry-screen.png");
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var recognizer = new VisualProgressRecognizer(Profile());
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, Viewport(frame), frame);
        Assert.Equal("dungeon-entry", choice.RuleId);
        Assert.Equal("Key:Space", choice.Key);
        var withoutSpace = ocr with { Words = ocr.Words.Where(word => !word.Text.Contains("Space", StringComparison.OrdinalIgnoreCase)).ToArray() };
        Assert.NotEqual("dungeon-entry", recognizer.Recognize(withoutSpace, frame.Width, frame.Height, Viewport(frame), frame).RuleId);
        var withoutEntry = ocr with { Words = ocr.Words.Where(word => word.Y < frame.Height * 0.9).ToArray() };
        Assert.NotEqual("dungeon-entry", recognizer.Recognize(withoutEntry, frame.Width, frame.Height, Viewport(frame), frame).RuleId);
    }

    private static CapturedFrame Read(string name)
    {
        using var file = File.OpenRead(Path.Combine(Fixture, name));
        var bitmap = new FormatConvertedBitmap(BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return new(ContractSchemaVersions.Revision03, "repeat-test", CaptureBackend.WindowsGraphicsCapture, 1, 0,
            DateTimeOffset.UnixEpoch, bitmap.PixelWidth, bitmap.PixelHeight, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(bytes, bitmap.PixelWidth * 4));
    }
}
