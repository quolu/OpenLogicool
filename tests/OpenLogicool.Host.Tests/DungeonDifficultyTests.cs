using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>入場の画面では、サムズアップの印が付いた難易度を選んでから入場する。実機で取得した画面で確かめる。</summary>
public sealed class DungeonDifficultyTests
{
    private static string Fixture(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008", name);
    }

    private static VisualProgressProfile Profile() => VisualProgressProfile.Load(Fixture("progress.json"));

    [Fact]
    public async Task 入場の画面では印の付いた難易度を先に押す()
    {
        // NORMAL が選ばれていて、VERY HARD の右上に印が付いている画面。
        var choice = await Recognize("dungeon-entry-difficulty.png", null);
        Assert.Equal("dungeon-difficulty", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        // 印（窓の横1194・縦114）の左下、VERY HARD のボタンの中（横1058〜1203・縦116〜150）を押す。
        Assert.InRange(choice.Point![0] * 1713, 1150, 1190);
        Assert.InRange(choice.Point[1] * 1117, 124, 142);
        Assert.Equal("dungeon-enter", choice.NextStage);
        Assert.Null(choice.Key);
    }

    [Fact]
    public async Task 難易度を押した後は入場のSpaceを送って段階を終える()
    {
        var choice = await Recognize("dungeon-entry-difficulty.png", "dungeon-enter");
        Assert.Equal("dungeon-enter-recommended", choice.RuleId);
        Assert.Equal(VisualProgressAction.Key, choice.Action);
        Assert.Equal("Key:Space", choice.Key);
        Assert.True(choice.EndStage);
    }

    [Fact]
    public void 段階は難易度を押して入り入場のSpaceで終える()
    {
        var schedule = new VisualProgressSchedule(Profile());
        schedule.RecordInput(0, new(VisualProgressAction.Click, "dungeon-difficulty", "dungeon-difficulty:", Point: [0.68, 0.12], NextStage: "dungeon-enter"));
        Assert.Equal("dungeon-enter", schedule.Stage);
        schedule.RecordInput(1500, new(VisualProgressAction.Key, "dungeon-enter-recommended", "dungeon-enter-recommended:", "Key:Space", EndStage: true));
        Assert.Null(schedule.Stage);
    }

    [Theory]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "ClickImageOffset": [0.1, 0.1] }""")]
    [InlineData("""{ "Id": "x", "ClickImage": true, "When": [], "Image": "stop.png", "ImageBounds": [0, 0, 1, 1], "ImageClientWidth": 1711, "ClickImageOffset": [0.1] }""")]
    [InlineData("""{ "Id": "x", "ClickImage": true, "When": [], "Image": "stop.png", "ImageBounds": [0, 0, 1, 1], "ImageClientWidth": 1711, "ClickImageOffset": [0.1, 2] }""")]
    public void 画像からずらすクリック位置の不正な設定は読込みで拒否する(string rule)
    {
        var path = Path.Combine(Path.GetDirectoryName(Fixture("progress.json"))!, "temp-" + Guid.NewGuid().ToString("N") + ".json");
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
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var recognizer = new VisualProgressRecognizer(Profile());
        return recognizer.Recognize(await recognizer.ReadOcrAsync(frame, viewport, stage: stage), frame.Width, frame.Height,
            viewport, frame, stage: stage);
    }

    private static CapturedFrame ReadFrame(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return new(ContractSchemaVersions.Revision03, "dungeon-difficulty-test", CaptureBackend.WindowsGraphicsCapture,
            1, 0, DateTimeOffset.UnixEpoch, bitmap.PixelWidth, bitmap.PixelHeight, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(bytes, bitmap.PixelWidth * 4));
    }
}
