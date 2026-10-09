using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class ItemHandoverTests
{
    [Theory]
    [InlineData("1/1 1/1 2/2", true)]
    [InlineData("3 / 3 １２／１２", true)]
    [InlineData("1/1 0/2", false)]
    [InlineData("1/0", false)]
    [InlineData("1/2", false)]
    [InlineData("読めない", false)]
    public void 必要数は品名に依存せず全件一致で確認する(string text, bool expected)
        => Assert.Equal(expected, VisualProgressRecognizer.QuantitiesFilled(text));

    [Fact]
    public async Task 納品画面では未登録スイッチをクリックし全数充足後だけ渡す()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var bitmap = new FormatConvertedBitmap(BitmapDecoder.Create(new Uri(Path.Combine(fixture, "item-handover.png")),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        var frame = new CapturedFrame(ContractSchemaVersions.Revision03, "納品", CaptureBackend.WindowsGraphicsCapture,
            1, 0, DateTimeOffset.UnixEpoch, bitmap.PixelWidth, bitmap.PixelHeight, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(bytes, bitmap.PixelWidth * 4));
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var recognizer = new VisualProgressRecognizer(profile);
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var register = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("item-handover-register", register.RuleId);
        Assert.Equal(VisualProgressAction.Click, register.Action);
        Assert.InRange(register.Point![0], 0.15, 0.18);
        Assert.InRange(register.Point[1], 0.6, 0.64);
        var waiting = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport);
        Assert.Equal("item-handover-wait", waiting.RuleId);
        Assert.Equal(VisualProgressAction.Wait, waiting.Action);
        var quantities = ocr.Words.Where(word => word.Y < 245 || word.Y > 460 || word.X < 258 || word.X > 857)
            .Concat(new[] { new WindowsGameOcrWord("1/1", 401, 355, 36, 24), new("1/1", 540, 355, 36, 24), new("2/2", 674, 355, 36, 24) }).ToArray();
        var filled = ocr with { Words = quantities };
        Assert.Equal("item-handover-complete", recognizer.Recognize(filled, frame.Width, frame.Height, viewport, frame).RuleId);
        var hidden = ocr with { Words = ocr.Words.Where(word => word.Y > 125).ToArray() };
        Assert.DoesNotContain("item-handover", recognizer.Recognize(hidden, frame.Width, frame.Height, viewport, frame).RuleId ?? "");

        var filledBitmap = new FormatConvertedBitmap(BitmapDecoder.Create(new Uri(Path.Combine(fixture, "item-handover-filled.png")),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var filledBytes = new byte[filledBitmap.PixelWidth * filledBitmap.PixelHeight * 4];
        filledBitmap.CopyPixels(filledBytes, filledBitmap.PixelWidth * 4, 0);
        var filledFrame = frame with { Pixels = new FramePixels(filledBytes, filledBitmap.PixelWidth * 4) };
        var filledOcr = await recognizer.ReadOcrAsync(filledFrame, viewport);
        Assert.Equal("item-handover-complete", recognizer.Recognize(filledOcr, frame.Width, frame.Height, viewport, filledFrame).RuleId);
    }
}
