using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Perception;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace OpenLogicool.Host;

public interface ILocalAiCallCounter
{
    int AiCallCount { get; }
}

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
    Func<string> structureRevisionId,
    string? targetIntent = null,
    string interactionOperation = GameInteractionOperations.Click) : IProductGameTargetDiscovery, ILocalAiCallCounter
{
    private const int MaximumVisionDimension = 1280;
    private int discoveryCount;
    private int aiCallCount;
    private IReadOnlyList<AffordanceCandidate> initialTargets = [];

    public int AiCallCount => Volatile.Read(ref aiCallCount);

    public async ValueTask<ObservedScene> DiscoverAsync(
        ObservationResult observation,
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(frame);
        var ocrResult = await ocr.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
        var textRegions = WindowsGameOcrSpanBuilder.Build(ocrResult, frame.Width, frame.Height);
        if (Interlocked.Increment(ref discoveryCount) > 1)
        {
            return LocalTargetTrackingSceneBuilder.Build(observation, frame, textRegions, initialTargets);
        }
        var png = pngEncoder.Encode(frame, MaximumVisionDimension);
        Interlocked.Increment(ref aiCallCount);
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
            [interactionOperation],
            structureRevisionId(),
            targetIntent);
        var discovered = await provider.ObserveAsync(request, png.Bytes, cancellationToken).ConfigureAwait(false);
        var groundedTargets = discovered.Scene.Affordances
            .Select(candidate => VisualControlLocalGrounder.Ground(candidate, textRegions, frame))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate! with { SemanticKind = "probe-target" })
            .ToArray();
        initialTargets = groundedTargets;
        return discovered.Scene with
        {
            Affordances = LocalTargetTrackingSceneBuilder.MergeInitial(
                observation,
                frame,
                textRegions,
                groundedTargets),
            DiscoveryEvidence = discovered.Scene.DiscoveryEvidence! with
            {
                LocalGroundingTexts = textRegions.Select(region => region.Text).ToArray(),
                LocalGroundingRegions = textRegions
                    .Select(region => new SceneGroundingRegion(region.Text, region.EvidenceRegion))
                    .ToArray(),
            },
        };
    }

}

public static class LocalTargetTrackingSceneBuilder
{
    private const string Revision = "local-target-tracking-v1";

    public static IReadOnlyList<AffordanceCandidate> MergeInitial(
        ObservationResult observation,
        CapturedFrame frame,
        IReadOnlyList<LocalVisionTextRegion> textRegions,
        IReadOnlyList<AffordanceCandidate> targets) =>
        [.. targets, .. StructuralText(observation, frame, textRegions, targets)];

    public static ObservedScene Build(
        ObservationResult observation,
        CapturedFrame frame,
        IReadOnlyList<LocalVisionTextRegion> textRegions,
        IReadOnlyList<AffordanceCandidate> initialTargets)
    {
        var affordances = StructuralText(observation, frame, textRegions, []);
        var identity = observation.StateCandidates.Count switch
        {
            0 => StateIdentityStatus.Novel,
            1 => StateIdentityStatus.Known,
            _ => StateIdentityStatus.Ambiguous,
        };
        var groundingRegions = textRegions
            .Select(region => new SceneGroundingRegion(region.Text, region.EvidenceRegion))
            .ToArray();
        return new ObservedScene(
            ContractSchemaVersions.Revision03,
            $"scene:{observation.ObservationId}",
            observation.ObservationId,
            observation.Frame,
            CaptureAvailability.Available,
            identity,
            identity == StateIdentityStatus.Known ? observation.StateCandidates[0].StateId : null,
            observation.StateCandidates,
            affordances,
            Revision,
            new SceneDiscoveryEvidence(
                "windows-ocr-local",
                "none",
                Revision,
                "none",
                "Completed",
                "None",
                null,
                "{}",
                0,
                0,
                0m,
                LocalGroundingTexts: textRegions.Select(region => region.Text).ToArray(),
                LocalGroundingRegions: groundingRegions));
    }

    private static AffordanceCandidate? Track(
        AffordanceCandidate target,
        ObservationResult observation,
        CapturedFrame frame,
        IReadOnlyList<LocalVisionTextRegion> textRegions)
    {
        var exact = VisualControlLocalGrounder.FindUniqueExactLabelRegion(textRegions, target.SemanticLabel);
        if (exact.Region is not null)
        {
            var currentPatch = VisualPatchMatcher.Capture(frame, exact.Region.EvidenceRegion.NormalizedBounds);
            var semanticKind = target.VisualPatch is not null
                && !VisualPatchMatcher.Matches(
                    target.VisualPatch,
                    frame,
                    exact.Region.EvidenceRegion.NormalizedBounds)
                    ? "visual-changed"
                    : target.SemanticKind;
            return Rebind(
                target with { SemanticKind = semanticKind },
                observation,
                exact.Region.EvidenceRegion.NormalizedBounds,
                exact.Region.EvidenceRegion,
                currentPatch);
        }
        if (exact.Ambiguous
            || target.VisualPatch is null)
        {
            return null;
        }
        if (VisualPatchMatcher.Matches(target.VisualPatch, frame, target.Locator.NormalizedBounds))
        {
            return Rebind(target, observation, target.Locator.NormalizedBounds, null, target.VisualPatch);
        }
        return Rebind(
            target with { SemanticKind = "visual-changed" },
            observation,
            target.Locator.NormalizedBounds,
            null,
            VisualPatchMatcher.Capture(frame, target.Locator.NormalizedBounds));
    }

    private static AffordanceCandidate Rebind(
        AffordanceCandidate target,
        ObservationResult observation,
        IReadOnlyList<double> bounds,
        EvidenceRegion? localEvidence,
        VisualPatchSignature patch) => target with
        {
            ObservationId = observation.ObservationId,
            FrameSequence = observation.Frame.Sequence,
            TransformRevision = observation.Frame.TransformRevision,
            TargetWindowSourceId = observation.Frame.SourceId,
            Locator = target.Locator with
            {
                LocatorType = "local-tracked-region",
                NormalizedBounds = bounds.ToArray(),
                LocatorRevision = $"local:{observation.ObservationId}",
            },
            EvidenceRegions = localEvidence is null ? target.EvidenceRegions : [localEvidence],
            AllowedPrimitives = [],
            VisualPatch = patch,
        };

    private static IReadOnlyList<AffordanceCandidate> StructuralText(
        ObservationResult observation,
        CapturedFrame frame,
        IReadOnlyList<LocalVisionTextRegion> textRegions,
        IReadOnlyList<AffordanceCandidate> targets) =>
        WindowsGameOcrSpanBuilder.Canonicalize(textRegions)
            .Where(region => !targets.Any(target => target.SemanticLabel is not null
                && FrameBoundLabelMatcher.Equals(region.Text, target.SemanticLabel)))
            .Select((region, index) => new AffordanceCandidate(
                ContractSchemaVersions.Revision03,
                $"local-ocr:{observation.ObservationId}:{index + 1}",
                observation.ObservationId,
                observation.Frame.Sequence,
                observation.Frame.TransformRevision,
                observation.Frame.SourceId,
                new AffordanceLocator(
                    ContractSchemaVersions.Revision03,
                    "local-ocr-region",
                    region.EvidenceRegion.NormalizedBounds,
                    $"local:{observation.ObservationId}"),
                [region.EvidenceRegion],
                1,
                [],
                "ocr-text",
                region.Text,
                VisualPatchMatcher.Capture(frame, region.EvidenceRegion.NormalizedBounds)))
            .ToArray();

}

public static class VisualControlLocalGrounder
{
    public static AffordanceCandidate? Ground(
        AffordanceCandidate candidate,
        IReadOnlyList<LocalVisionTextRegion> textRegions,
        CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(textRegions);
        ArgumentNullException.ThrowIfNull(frame);
        var providerBounds = candidate.Locator.NormalizedBounds;
        var textInsideProviderBounds = textRegions
            .Where(region => CenterInside(region.EvidenceRegion.NormalizedBounds, providerBounds))
            .Select(region => region.EvidenceRegion)
            .ToArray();
        return candidate with
        {
            EvidenceRegions = [.. candidate.EvidenceRegions, .. textInsideProviderBounds],
            VisualPatch = VisualPatchMatcher.Capture(frame, providerBounds),
        };
    }

    public static (LocalVisionTextRegion? Region, bool Ambiguous) FindUniqueExactLabelRegion(
        IReadOnlyList<LocalVisionTextRegion> textRegions,
        string? semanticLabel)
    {
        if (string.IsNullOrWhiteSpace(semanticLabel))
        {
            return (null, false);
        }
        var exact = textRegions
            .Where(region => OcrTextMatcher.IsSimilar(region.Text, semanticLabel))
            .GroupBy(region => BoundsKey(region.EvidenceRegion.NormalizedBounds), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(region => Area(region.EvidenceRegion.NormalizedBounds))
            .ToArray();
        if (exact.Length == 0)
        {
            return (null, false);
        }
        var selected = exact[0];
        return exact.All(region => CentersNear(
                selected.EvidenceRegion.NormalizedBounds,
                region.EvidenceRegion.NormalizedBounds))
            ? (selected, false)
            : (null, true);
    }

    private static string BoundsKey(IReadOnlyList<double> bounds) =>
        string.Join('|', bounds.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
    private static double Area(IReadOnlyList<double> bounds) => bounds[2] * bounds[3];
    private static bool CentersNear(IReadOnlyList<double> left, IReadOnlyList<double> right) =>
        Math.Abs(left[0] + left[2] / 2 - (right[0] + right[2] / 2)) <= 0.04
        && Math.Abs(left[1] + left[3] / 2 - (right[1] + right[3] / 2)) <= 0.04;

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
