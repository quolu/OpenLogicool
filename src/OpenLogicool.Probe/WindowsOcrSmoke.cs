using System.Text.Json;
using OpenLogicool.Contracts.Capture;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using Windows.Storage;

namespace OpenLogicool.Probe;

internal static class WindowsOcrSmoke
{
    public static int Run(string[] arguments)
    {
        if (arguments.Length != 1)
        {
            Console.Error.WriteLine("usage: windows-ocr-smoke <image-path>");
            return 1;
        }

        return RunAsync(Path.GetFullPath(arguments[0])).GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(string imagePath)
    {
        var file = await StorageFile.GetFileFromPathAsync(imagePath);
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            Console.Error.WriteLine("Windows OCR engine is unavailable.");
            return 2;
        }

        var snapshot = await RecognizeAsync(engine, bitmap);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Probe = "windows-ocr-smoke",
            Image = imagePath,
            Width = bitmap.PixelWidth,
            Height = bitmap.PixelHeight,
            snapshot.RecognizerLanguage,
            snapshot.MaxImageDimension,
            snapshot.ElapsedMs,
            snapshot.Text,
            snapshot.VisualLines,
            snapshot.Words,
        }));
        return 0;
    }

    internal static async Task<WindowsOcrSnapshot> RecognizeFrameAsync(CapturedFrame frame)
    {
        var pixels = frame.Pixels
            ?? throw new InvalidOperationException("OCRにはBGRA8 frame pixelsが必要です。");
        var packedStride = checked(frame.Width * 4);
        if (pixels.Stride < packedStride || pixels.Bgra8.Length < checked(pixels.Stride * frame.Height))
        {
            throw new InvalidOperationException("OCR frameのstrideまたはpixel lengthが不正です。");
        }

        var packed = new byte[checked(packedStride * frame.Height)];
        var source = pixels.Bgra8.Span;
        for (var y = 0; y < frame.Height; y++)
        {
            source.Slice(y * pixels.Stride, packedStride)
                .CopyTo(packed.AsSpan(y * packedStride, packedStride));
        }

        var buffer = CryptographicBuffer.CreateFromByteArray(packed);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            frame.Width,
            frame.Height,
            BitmapAlphaMode.Ignore);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("Windows OCR engine is unavailable.");
        return await RecognizeAsync(engine, bitmap);
    }

    private static async Task<WindowsOcrSnapshot> RecognizeAsync(OcrEngine engine, SoftwareBitmap bitmap)
    {
        if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
        {
            throw new InvalidOperationException(
                $"OCR image {bitmap.PixelWidth}x{bitmap.PixelHeight} exceeds MaxImageDimension={OcrEngine.MaxImageDimension}.");
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await engine.RecognizeAsync(bitmap);
        started.Stop();
        var words = result.Lines
            .SelectMany(line => line.Words)
            .Select(word => new WindowsOcrWord(
                word.Text,
                word.BoundingRect.X,
                word.BoundingRect.Y,
                word.BoundingRect.Width,
                word.BoundingRect.Height))
            .ToArray();
        return new WindowsOcrSnapshot(
            result.Text,
            started.ElapsedMilliseconds,
            engine.RecognizerLanguage.LanguageTag,
            OcrEngine.MaxImageDimension,
            BuildVisualLines(words),
            words);
    }

    private static string[] BuildVisualLines(IReadOnlyList<WindowsOcrWord> words)
    {
        var groups = new List<List<WindowsOcrWord>>();
        foreach (var word in words.OrderBy(word => word.Y).ThenBy(word => word.X))
        {
            var group = groups.FirstOrDefault(existing => existing.Any(item => VerticallyOverlaps(item, word)));
            if (group is null)
            {
                groups.Add([word]);
            }
            else
            {
                group.Add(word);
            }
        }

        return groups
            .OrderBy(group => group.Min(word => word.Y))
            .Select(group => string.Concat(group.OrderBy(word => word.X).Select(word => word.Text)))
            .ToArray();
    }

    private static bool VerticallyOverlaps(WindowsOcrWord left, WindowsOcrWord right) =>
        left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;
}

internal sealed record WindowsOcrSnapshot(
    string Text,
    long ElapsedMs,
    string RecognizerLanguage,
    uint MaxImageDimension,
    IReadOnlyList<string> VisualLines,
    IReadOnlyList<WindowsOcrWord> Words);

internal sealed record WindowsOcrWord(
    string Text,
    double X,
    double Y,
    double Width,
    double Height);
