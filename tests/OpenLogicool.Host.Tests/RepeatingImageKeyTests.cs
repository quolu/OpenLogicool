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
        Assert.Null(new VisualProgressRecognizer(Profile()).RecognizeRepeatingImage(frame, Viewport(frame), rule => rule.Id == "feather-t"));
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

    [Fact]
    public void 停止表示が出ている自動移動中の実画面でも羽根を検出しTを押してよい規則として選ぶ()
    {
        // 実測: 羽根は自動移動中（右下に停止ボタンがある間）にだけ出る。
        var frame = Read("feather-auto-move.png");
        var choice = new VisualProgressRecognizer(Profile()).RecognizeRepeatingImage(frame, Viewport(frame), rule => rule.Id == "feather-t");
        Assert.NotNull(choice);
        Assert.Equal("Key:T", choice.Key);
        Assert.True(choice.AllowWhileInhibited);
    }

    [Fact]
    public void 停止表示が止めるのはSpaceを押す規則だけでSpaceを送らない規則は停止表示中も評価する()
    {
        var rules = Profile().Rules.ToDictionary(rule => rule.Id);
        foreach (var id in new[] { "feather-t", "top-left-b", "pointer-guide", "level-guide-recommended", "merchant-greeting-skip" })
            Assert.True(rules[id].AllowWhileInhibited, id);
        foreach (var id in new[] { "compass-space", "space-confirm", "screen-prompt", "npc-dialogue-cue", "quest-reward-prompt", "dungeon-clear-prompt" })
            Assert.False(rules[id].AllowWhileInhibited, id);
        // Spaceを送る規則でも、停止表示より優先すると明示した休憩の退出だけは評価する。
        Assert.True(rules["rest-exit"].AllowWhileInhibited);

        // 停止表示中の同じ画面で、Spaceの規則は選ばず、Spaceを送らない規則は選ぶ。
        var recognizer = new VisualProgressRecognizer(Profile());
        var frame = Read("pointer-guide-screen.png");
        var inhibited = recognizer.Recognize(new("", "ja", 0, [new("画面を押してください", 700, 1040, 240, 24)]),
            frame.Width, frame.Height, Viewport(frame), frame, inhibited: true);
        Assert.Equal("pointer-guide", inhibited.RuleId);
        var spaceOnly = recognizer.Recognize(new("", "ja", 0, [new("画面を押してください", 700, 1040, 240, 24)]),
            1000, 600, new(0, 0, 1000, 600), inhibited: true);
        Assert.NotEqual("screen-prompt", spaceOnly.RuleId);
    }

    [Theory]
    [InlineData("top-left-b-screen.png")]
    [InlineData("before.png")] // 窓の大きさが違う別の日の実画面。
    public void 左上にBの印が付いた利用者のアイコンがある実画面ではBを選ぶ(string name)
    {
        var frame = Read(name);
        var recognizer = new VisualProgressRecognizer(Profile());
        var choice = recognizer.RecognizeRepeatingImage(frame, Viewport(frame), rule => rule.Id == "top-left-b");
        Assert.NotNull(choice);
        Assert.Equal("top-left-b", choice.RuleId);
        Assert.Equal("Key:B", choice.Key);
        Assert.Equal(1000, choice.RepeatIntervalMs);
        Assert.NotEqual("top-left-b", recognizer.RecognizeImmediateImage(frame, Viewport(frame))?.RuleId);
    }

    [Theory]
    [InlineData("top-left-b-no-key.png")] // 同じアイコンはあるがBの印が出ていない。
    [InlineData("feather-screen.png")]
    [InlineData("combat.png")]
    [InlineData("compass-screen.png")]
    [InlineData("bonus-three.png")]
    [InlineData("quest-reward-prompt.png")]
    public void Bの印がない実画面やアイコンのない実画面ではBを選ばない(string name)
    {
        var frame = Read(name);
        Assert.Null(new VisualProgressRecognizer(Profile()).RecognizeRepeatingImage(frame, Viewport(frame), rule => rule.Id == "top-left-b"));
    }

    [Fact]
    public void 指差しの案内がある実画面では手の中心ではなく指先をクリック先に選ぶ()
    {
        var frame = Read("pointer-guide-screen.png");
        var choice = new VisualProgressRecognizer(Profile()).Recognize(new("", "ja", 0, []), frame.Width, frame.Height, Viewport(frame), frame);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.Equal("pointer-guide", choice.RuleId);
        // 実測の指先は(1499, 74)。手の中心(1507, 108)はメニューボタンの外になる。
        Assert.InRange(choice.Point![0] * frame.Width, 1495, 1503);
        Assert.InRange(choice.Point[1] * frame.Height, 70, 78);
        Assert.StartsWith("pointer-guide:@", choice.Signature);
    }

    // 画面中央に説明文だけが出るページ。文字認識は試験で使わないため、中央の文の位置へ文字を置く。
    private static WindowsGameOcrResult CenterText(CapturedFrame frame) => new("", "ja", 0,
        [new("宝石やルーンなどを錬金術で昇級させると", frame.Width * 0.36, frame.Height * 0.4, frame.Width * 0.28, 28)]);
    private static VisualProgressProfile NarrationOnly()
    {
        var profile = Profile();
        return profile with { Rules = profile.Rules.Where(rule => rule.Id == "narration-cue").ToArray() };
    }

    [Fact]
    public void 中央に説明文と送りの印だけが出る実画面ではSpaceを選ぶ()
    {
        var frame = Read("narration-cue-screen.png");
        var choice = new VisualProgressRecognizer(Profile()).Recognize(CenterText(frame), frame.Width, frame.Height, Viewport(frame), frame);
        Assert.Equal(VisualProgressAction.Key, choice.Action);
        Assert.Equal("narration-cue", choice.RuleId);
        Assert.Equal("Key:Space", choice.Key);
    }

    [Fact]
    public void 中央の送りの印の規則はほかの実画面の白い形に一致しない()
    {
        var recognizer = new VisualProgressRecognizer(NarrationOnly());
        var screens = Directory.GetFiles(Fixture, "*.png").Select(Path.GetFileName).Where(name => name != "narration-cue-screen.png")
            .Select(name => (Name: name!, Frame: Read(name!))).Where(screen => screen.Frame.Width >= 1000).ToArray();
        Assert.True(screens.Length >= 20, "実画面の見本が足りません: " + screens.Length);
        var matched = screens.Where(screen => recognizer.Recognize(CenterText(screen.Frame), screen.Frame.Width, screen.Frame.Height,
            Viewport(screen.Frame), screen.Frame).RuleId == "narration-cue").Select(screen => screen.Name).ToArray();
        Assert.Empty(matched);
    }

    [Fact]
    public void 手の下側が描画領域の下端で切れた実画面でも指先をクリック先に選ぶ()
    {
        // 実測: 右下のバッグを指す手は、手首が窓の下端の外へ出る。指先は(1500, 1047)。
        var frame = Read("pointer-guide-clipped-screen.png");
        var choice = new VisualProgressRecognizer(Profile()).Recognize(new("", "ja", 0, []), frame.Width, frame.Height, Viewport(frame), frame);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.Equal("pointer-guide", choice.RuleId);
        Assert.InRange(choice.Point![0] * frame.Width, 1492, 1508);
        Assert.InRange(choice.Point[1] * frame.Height, 1040, 1056);
    }

    [Theory]
    [InlineData("before.png")]
    [InlineData("combat.png")]
    [InlineData("compass-screen.png")]
    [InlineData("bonus-three.png")]
    [InlineData("space-confirm.png")]
    [InlineData("quest-reward-prompt.png")]
    [InlineData("top-left-b-screen.png")]
    [InlineData("top-left-b-no-key.png")]
    [InlineData("feather-screen.png")]
    public void 指差しの案内がない実画面では指先のクリックを選ばない(string name)
    {
        var frame = Read(name);
        var choice = new VisualProgressRecognizer(Profile()).Recognize(new("", "ja", 0, []), frame.Width, frame.Height, Viewport(frame), frame);
        Assert.NotEqual("pointer-guide", choice.RuleId);
    }

    [Fact]
    public void 指差しの先が別の場所へ移ったら結果待ちにせず次のクリックとして扱う()
    {
        var schedule = new VisualProgressSchedule(Profile());
        var first = new VisualProgressChoice(VisualProgressAction.Click, "pointer-guide", "pointer-guide:@0.90,0.05", Point: [0.875, 0.066]);
        var moved = first with { Signature = "pointer-guide:@0.45,0.60", Point = [0.45, 0.6] };
        schedule.RecordInput(0, first);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(1000, first, false, false, due: true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(2000, moved, false, false, due: true).Action);
        var next = schedule.Decide(2600, moved, false, false, due: true);
        Assert.Equal(VisualProgressAction.Click, next.Action);
        Assert.Equal(moved.Point, next.Point);
    }

    [Fact]
    public void 画像内のクリック位置は画像のクリック規則にだけ指定できる()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new VisualProgressProfile(1,
                [new("point", [], "Key:Space", Image: "unused.png", ImageBounds: [0, 0, 1, 1],
                    ImageClientWidth: 1506, ClickImagePoint: [0.5, 0.1])], [])));
            Assert.Throws<InvalidDataException>(() => VisualProgressProfile.Load(path));
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new VisualProgressProfile(1,
                [new("point", [], Image: "unused.png", ImageBounds: [0, 0, 1, 1],
                    ImageClientWidth: 1506, ClickImage: true, ClickImagePoint: [0.5, 1.5])], [])));
            Assert.Throws<InvalidDataException>(() => VisualProgressProfile.Load(path));
        }
        finally { File.Delete(path); }
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
        // VERY HARD が選ばれていて、印も VERY HARD に付いている画面。
        var frame = Read("dungeon-entry-screen.png");
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var recognizer = new VisualProgressRecognizer(Profile());
        // 印の付いた難易度は、既に選ばれていても一度押してから入場する。
        var difficulty = recognizer.Recognize(ocr, frame.Width, frame.Height, Viewport(frame), frame);
        Assert.Equal("dungeon-difficulty", difficulty.RuleId);
        Assert.Equal(VisualProgressAction.Click, difficulty.Action);
        Assert.InRange(difficulty.Point![0] * frame.Width, 1150, 1195);
        Assert.InRange(difficulty.Point[1] * frame.Height, 124, 142);
        Assert.Equal("dungeon-enter", difficulty.NextStage);
        var enter = recognizer.Recognize(ocr, frame.Width, frame.Height, Viewport(frame), frame, stage: "dungeon-enter");
        Assert.Equal("dungeon-enter-recommended", enter.RuleId);
        Assert.Equal("Key:Space", enter.Key);
        // 印を見分ける画像が無い時（印の無い入場の画面）は、今までどおりSpaceだけを送る。
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, Viewport(frame));
        Assert.Equal("dungeon-entry", choice.RuleId);
        Assert.Equal("Key:Space", choice.Key);
        var withoutSpace = ocr with { Words = ocr.Words.Where(word => !word.Text.Contains("Space", StringComparison.OrdinalIgnoreCase)).ToArray() };
        Assert.Null(recognizer.Recognize(withoutSpace, frame.Width, frame.Height, Viewport(frame), frame).RuleId);
        Assert.Null(recognizer.Recognize(withoutSpace, frame.Width, frame.Height, Viewport(frame)).RuleId);
        var withoutEntry = ocr with { Words = ocr.Words.Where(word => word.Y < frame.Height * 0.9).ToArray() };
        Assert.Null(recognizer.Recognize(withoutEntry, frame.Width, frame.Height, Viewport(frame), frame).RuleId);
        Assert.Null(recognizer.Recognize(withoutEntry, frame.Width, frame.Height, Viewport(frame), stage: "dungeon-enter").RuleId);
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
