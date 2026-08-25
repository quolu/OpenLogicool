using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace OpenLogicool.Host;

public sealed record WindowsGameOcrWord(
    string Text,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record WindowsGameOcrResult(
    string Text,
    string RecognizerLanguage,
    long ElapsedMilliseconds,
    IReadOnlyList<WindowsGameOcrWord> Words);

public static class WindowsGameOcrSpanBuilder
{
    private const int MaximumCanonicalRegions = 24;

    public static IReadOnlyList<LocalVisionTextRegion> Build(
        WindowsGameOcrResult ocr,
        int frameWidth,
        int frameHeight)
    {
        var lines = new List<List<WindowsGameOcrWord>>();
        foreach (var word in ocr.Words.OrderBy(word => word.Y).ThenBy(word => word.X))
        {
            var line = lines.FirstOrDefault(existing => existing.Any(item => VerticalOverlap(item, word)));
            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }
        var regions = new List<LocalVisionTextRegion>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var runs = new List<List<WindowsGameOcrWord>>();
            foreach (var word in line.OrderBy(word => word.X))
            {
                var current = runs.LastOrDefault();
                if (current is null || !Near(current[^1], word))
                {
                    runs.Add([word]);
                }
                else
                {
                    current.Add(word);
                }
            }
            foreach (var run in runs)
            {
                for (var start = 0; start < run.Count; start++)
                {
                    for (var length = 1; length <= Math.Min(12, run.Count - start); length++)
                    {
                        var span = run.Skip(start).Take(length).ToArray();
                        var text = string.Concat(span.Select(word => word.Text));
                        Add(text, span);
                        var kana = NormalizeSmallKana(text);
                        if (!string.Equals(kana, text, StringComparison.Ordinal))
                        {
                            Add(kana, span);
                        }
                    }
                }
            }
        }
        return regions;

        void Add(string text, IReadOnlyList<WindowsGameOcrWord> span)
        {
            var left = span.Min(word => word.X);
            var top = span.Min(word => word.Y);
            var right = span.Max(word => word.X + word.Width);
            var bottom = span.Max(word => word.Y + word.Height);
            var bounds = new[]
            {
                left / frameWidth,
                top / frameHeight,
                (right - left) / frameWidth,
                (bottom - top) / frameHeight,
            };
            var key = $"{text}|{left:R}|{top:R}|{right:R}|{bottom:R}";
            if (seen.Add(key))
            {
                regions.Add(new LocalVisionTextRegion(
                    text,
                    new EvidenceRegion(
                        ContractSchemaVersions.Revision03,
                        "rect",
                        bounds,
                        $"windows-ocr:{ocr.RecognizerLanguage}")));
            }
        }
    }

    /// <summary>AIへ渡すOCR選択肢を、同じ行runの最大spanだけへ絞る。</summary>
    public static IReadOnlyList<LocalVisionTextRegion> Canonicalize(
        IReadOnlyList<LocalVisionTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var maximal = regions
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Text))
            .Where(candidate => candidate.Text.Trim().Length >= 2)
            .Where(candidate => !regions.Any(other =>
                !ReferenceEquals(candidate, other)
                && Area(other.EvidenceRegion.NormalizedBounds) > Area(candidate.EvidenceRegion.NormalizedBounds)
                && Contains(
                    other.EvidenceRegion.NormalizedBounds,
                    candidate.EvidenceRegion.NormalizedBounds)))
            .OrderByDescending(candidate => candidate.EvidenceRegion.NormalizedBounds[3])
            .ThenByDescending(candidate => Area(candidate.EvidenceRegion.NormalizedBounds))
            .ThenByDescending(candidate => candidate.Text.Length)
            .Take(MaximumCanonicalRegions)
            .ToArray();
        return maximal
            .OrderBy(candidate => candidate.EvidenceRegion.NormalizedBounds[1])
            .ThenBy(candidate => candidate.EvidenceRegion.NormalizedBounds[0])
            .ToArray();
    }

    private static double Area(IReadOnlyList<double> bounds) => bounds[2] * bounds[3];

    private static bool Contains(IReadOnlyList<double> outer, IReadOnlyList<double> inner) =>
        inner[0] >= outer[0]
        && inner[1] >= outer[1]
        && inner[0] + inner[2] <= outer[0] + outer[2]
        && inner[1] + inner[3] <= outer[1] + outer[3];

    private static bool VerticalOverlap(WindowsGameOcrWord left, WindowsGameOcrWord right) =>
        left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;

    private static bool Near(WindowsGameOcrWord left, WindowsGameOcrWord right)
    {
        var gap = right.X - (left.X + left.Width);
        return gap <= Math.Max(16, Math.Max(left.Height, right.Height) * 1.5);
    }

    private static string NormalizeSmallKana(string value) =>
        new(value.Select(character => character switch
        {
            'ぁ' => 'あ', 'ぃ' => 'い', 'ぅ' => 'う', 'ぇ' => 'え', 'ぉ' => 'お',
            'ゃ' => 'や', 'ゅ' => 'ゆ', 'ょ' => 'よ', 'っ' => 'つ', 'ゎ' => 'わ',
            'ァ' => 'ア', 'ィ' => 'イ', 'ゥ' => 'ウ', 'ェ' => 'エ', 'ォ' => 'オ',
            'ャ' => 'ヤ', 'ュ' => 'ユ', 'ョ' => 'ヨ', 'ッ' => 'ツ', 'ヮ' => 'ワ',
            _ => character,
        }).ToArray());
}

public interface IWindowsGameOcrRecognizer
{
    ValueTask<WindowsGameOcrResult> RecognizeAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsGameOcrRecognizer(int scaleFactor = 2) : IWindowsGameOcrRecognizer
{
    public async ValueTask<WindowsGameOcrResult> RecognizeAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        var pixels = frame.Pixels
            ?? throw new InvalidOperationException("Windows OCRにはBGRA8 frame pixelsが必要です。");
        var packedStride = checked(frame.Width * 4);
        if (pixels.Stride < packedStride
            || pixels.Bgra8.Length < checked(pixels.Stride * frame.Height))
        {
            throw new InvalidOperationException("OCR frameのstrideまたはpixel lengthが不正です。");
        }
        if (scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        }
        var scaledWidth = checked(frame.Width * scaleFactor);
        var scaledHeight = checked(frame.Height * scaleFactor);
        if (scaledWidth > OcrEngine.MaxImageDimension || scaledHeight > OcrEngine.MaxImageDimension)
        {
            throw new InvalidOperationException(
                $"OCR scaled frame {scaledWidth}x{scaledHeight} exceeds MaxImageDimension={OcrEngine.MaxImageDimension}.");
        }
        BitmapSource sourceBitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            frame.DpiX,
            frame.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels.Bgra8.ToArray(),
            pixels.Stride);
        if (scaleFactor != 1)
        {
            sourceBitmap = new TransformedBitmap(
                sourceBitmap,
                new ScaleTransform(scaleFactor, scaleFactor));
        }
        var scaledStride = checked(scaledWidth * 4);
        var scaledPixels = new byte[checked(scaledStride * scaledHeight)];
        sourceBitmap.CopyPixels(scaledPixels, scaledStride, 0);
        var buffer = CryptographicBuffer.CreateFromByteArray(scaledPixels);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            scaledWidth,
            scaledHeight,
            BitmapAlphaMode.Ignore);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("Windows OCR engine is unavailable.");
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await engine.RecognizeAsync(bitmap);
        started.Stop();
        cancellationToken.ThrowIfCancellationRequested();
        return new WindowsGameOcrResult(
            result.Text,
            engine.RecognizerLanguage.LanguageTag,
            started.ElapsedMilliseconds,
            result.Lines.SelectMany(line => line.Words).Select(word => new WindowsGameOcrWord(
                word.Text,
                word.BoundingRect.X / scaleFactor,
                word.BoundingRect.Y / scaleFactor,
                word.BoundingRect.Width / scaleFactor,
                word.BoundingRect.Height / scaleFactor)).ToArray());
    }
}

public interface IGameFramePngEncoder
{
    EncodedGameFramePng Encode(CapturedFrame frame, int maximumDimension = int.MaxValue);
}

public sealed record EncodedGameFramePng(ReadOnlyMemory<byte> Bytes, int Width, int Height);

public sealed class WindowsGameFramePngEncoder : IGameFramePngEncoder
{
    public EncodedGameFramePng Encode(CapturedFrame frame, int maximumDimension = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (maximumDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        }
        var pixels = frame.Pixels
            ?? throw new InvalidOperationException("PNG encodeにはBGRA8 frame pixelsが必要です。");
        BitmapSource bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            frame.DpiX,
            frame.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels.Bgra8.ToArray(),
            pixels.Stride);
        var longest = Math.Max(frame.Width, frame.Height);
        if (longest > maximumDimension)
        {
            var scale = maximumDimension / (double)longest;
            bitmap = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new EncodedGameFramePng(stream.ToArray(), bitmap.PixelWidth, bitmap.PixelHeight);
    }
}

/// <summary>Foundry Local controlsとWindows OCRを同じWGC frameへ束縛するadapter。</summary>
public sealed class FoundryControlTargetDiscoveryAdapter(
    ILocalControlDiscoveryProvider provider,
    IWindowsGameOcrRecognizer ocr,
    IGameFramePngEncoder pngEncoder,
    Func<string> structureRevisionId) : IProductGameTargetDiscovery
{
    private const int MaximumVisionDimension = 640;

    public async ValueTask<ObservedScene> DiscoverAsync(
        ObservationResult observation,
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(frame);
        var ocrResult = await ocr.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
        var png = pngEncoder.Encode(frame, MaximumVisionDimension);
        var textRegions = WindowsGameOcrSpanBuilder.Build(ocrResult, frame.Width, frame.Height);
        var request = new LocalVisionSceneRequest(
            ContractSchemaVersions.Revision03,
            $"scene:{observation.ObservationId}",
            observation.ObservationId,
            observation.Frame,
            observation.Frame.SourceId,
            $"crop:full:{observation.ObservationId}",
            png.Width,
            png.Height,
            $"locator:{observation.ObservationId}",
            textRegions,
            observation.StateCandidates,
            [GameInteractionOperations.Hover, GameInteractionOperations.Click],
            structureRevisionId());
        var discovered = await provider.ObserveAsync(request, png.Bytes, cancellationToken).ConfigureAwait(false);
        return discovered.Scene with
        {
            Affordances = discovered.Scene.Affordances
                .Select(candidate => EnrichWithOcr(candidate, textRegions))
                .ToArray(),
        };
    }

    private static AffordanceCandidate EnrichWithOcr(
        AffordanceCandidate candidate,
        IReadOnlyList<LocalVisionTextRegion> textRegions)
    {
        var bounds = candidate.Locator.NormalizedBounds;
        var ocrEvidence = textRegions
            .Where(region => CenterInside(region.EvidenceRegion.NormalizedBounds, bounds))
            .Select(region => region.EvidenceRegion)
            .ToArray();
        return ocrEvidence.Length == 0
            ? candidate
            : candidate with { EvidenceRegions = [.. candidate.EvidenceRegions, .. ocrEvidence] };
    }

    private static bool CenterInside(
        IReadOnlyList<double> inner,
        IReadOnlyList<double> outer)
    {
        var x = inner[0] + inner[2] / 2;
        var y = inner[1] + inner[3] / 2;
        return x >= outer[0] && x <= outer[0] + outer[2]
            && y >= outer[1] && y <= outer[1] + outer[3];
    }
}
