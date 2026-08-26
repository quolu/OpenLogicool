using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.AI;
using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Probe;

internal static class LiveDiscoveryObserveSmoke
{
    private const string ModelId = "qwen3-vl-2b-instruct-cuda-gpu:2";

    public static int Run(string[] arguments, string outputDirectory)
    {
        if (arguments.Length > 1)
        {
            Console.Error.WriteLine("usage: live-discovery-observe [process-name-substring]");
            return 1;
        }

        try
        {
            return RunAsync(
                arguments.Length == 1 ? arguments[0] : "nikke",
                Path.GetFullPath(outputDirectory)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string processNameSubstring, string outputDirectory)
    {
        var target = FindWindow(processNameSubstring);
        var daemon = ReadDaemonState();
        Directory.CreateDirectory(outputDirectory);

        using var source = WgcFrameSource.CreateForWindow(
            target.Window,
            $"window:live-discovery:{target.ProcessId}");
        var frame = await WaitForFrameAsync(source, TimeSpan.FromSeconds(10));
        var png = EncodePng(frame);
        var ocrPng = EncodePng(frame, scaleFactor: 2);
        var capturedAt = DateTimeOffset.UtcNow;
        var framePath = Path.Combine(
            outputDirectory,
            $"live-discovery-frame-{capturedAt:yyyyMMdd-HHmmss-fff}.png");
        var ocrFramePath = Path.Combine(
            outputDirectory,
            $"live-discovery-frame-{capturedAt:yyyyMMdd-HHmmss-fff}.ocr2x.png");
        await File.WriteAllBytesAsync(framePath, png.Bytes);
        await File.WriteAllBytesAsync(ocrFramePath, ocrPng.Bytes);
        var ocr = await WindowsOcrSmoke.RecognizeImageAsync(ocrFramePath, coordinateScale: 2);
        var visionInputs = EncodeTiles(frame, tileWidth: 640, tileHeight: 560, overlap: 64)
            .Where(tile => ocr.Words.Any(word => Intersects(tile, word)))
            .ToArray();

        FoundryVisionResult vision;
        IReadOnlyList<VisionTileRun> visionTiles;
        IReadOnlyList<OwnedTcpConnection> connections;
        bool hasNonLoopback;
        await using (var observer = new OwnedTcpConnectionObserver(
            [Environment.ProcessId, daemon.ProcessId]))
        {
            using var client = new FoundryLocalVisionClient(
                daemon.Endpoint,
                ModelId,
                TimeSpan.FromSeconds(20));
            var tileRuns = new List<VisionTileRun>(visionInputs.Length);
            foreach (var tile in visionInputs)
            {
                tileRuns.Add(new VisionTileRun(
                    tile,
                    await client.ProposeLabelsAsync(tile.Png.Bytes)));
            }
            visionTiles = tileRuns;
            vision = AggregateVision(tileRuns);
            connections = observer.Observations;
            hasNonLoopback = observer.HasNonLoopbackEstablished;
        }

        var grounded = vision.Labels.Select(label => Ground(label, ocr)).ToArray();
        var hasGroundedCandidate = grounded.Any(item => item.Status == "Grounded");
        var result = new
        {
            SchemaVersion = "1.0.0",
            Probe = "live-discovery-observe",
            CapturedAtUtc = capturedAt,
            Target = new
            {
                target.ProcessId,
                target.ProcessName,
                target.ProcessPath,
                target.PathReadStatus,
                target.WindowTitle,
                WindowRect = target.Rect,
                target.CaptureRect,
                target.Dpi,
            },
            Frame = new
            {
                frame.SourceId,
                frame.Sequence,
                frame.Width,
                frame.Height,
                frame.PixelFormat,
                frame.DpiX,
                frame.DpiY,
                frame.ColorSpace,
                frame.Rotation,
                frame.TransformRevision,
                frame.FreshnessMs,
                frame.LastChangeMs,
                Sha256 = Convert.ToHexString(SHA256.HashData(png.Bytes)).ToLowerInvariant(),
                LocalPath = framePath,
            },
            Ocr = new
            {
                Recognizer = "Windows.Media.Ocr",
                InputScale = 2,
                InputWidth = ocrPng.Width,
                InputHeight = ocrPng.Height,
                InputLocalPath = ocrFramePath,
                ocr.RecognizerLanguage,
                ocr.MaxImageDimension,
                ocr.ElapsedMs,
                ocr.Text,
                ocr.VisualLines,
                ocr.Words,
            },
            Vision = new
            {
                Runtime = $"Microsoft Foundry Local {daemon.Version}",
                Model = ModelId,
                ProviderEndpoint = daemon.Endpoint,
                InputMode = "overlapping original-scale tiles",
                TileWidth = 640,
                TileHeight = 560,
                TileOverlap = 64,
                TileCount = visionTiles.Count,
                vision.Status,
                vision.Failure,
                vision.FailureDetail,
                vision.Normalization,
                vision.Labels,
                vision.RawOutput,
                vision.ElapsedMs,
                vision.RequestBytes,
                vision.InputTokens,
                vision.OutputTokens,
            },
            VisionTiles = visionTiles.Select(run => new
            {
                run.Tile.Index,
                run.Tile.X,
                run.Tile.Y,
                run.Tile.Width,
                run.Tile.Height,
                Sha256 = Convert.ToHexString(SHA256.HashData(run.Tile.Png.Bytes)).ToLowerInvariant(),
                run.Result.Status,
                run.Result.Failure,
                run.Result.FailureDetail,
                run.Result.Normalization,
                run.Result.Labels,
                run.Result.RawOutput,
                run.Result.ElapsedMs,
                run.Result.RequestBytes,
                run.Result.InputTokens,
                run.Result.OutputTokens,
            }),
            GroundedCandidates = grounded,
            Network = new
            {
                StructuralBoundary = "IP literal loopback HTTP only; proxy disabled; redirect disabled",
                ObservedProcessIds = new[] { Environment.ProcessId, daemon.ProcessId },
                Connections = connections,
                NonLoopbackEstablishedCount = connections.Count(item =>
                    item.State == "Established"
                    && !System.Net.IPAddress.IsLoopback(System.Net.IPAddress.Parse(item.RemoteAddress))),
                HasNonLoopbackEstablished = hasNonLoopback,
                ExternalAiTransmissionCount = 0,
                ExternalAiApiKeyCount = 0,
                ExternalAiApiCostUsd = 0,
            },
            Input = new
            {
                DispatchCount = 0,
                Status = "ObserveOnly",
            },
        };

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        });
        var outputPath = Path.Combine(
            outputDirectory,
            $"live-discovery-observe-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine(json);
        Console.WriteLine($"output: {outputPath}");

        return vision.Status == FoundryVisionStatus.Completed
            && hasGroundedCandidate
            && !hasNonLoopback
            ? 0
            : 3;
    }

    internal static GroundedCandidate Ground(
        string label,
        WindowsOcrSnapshot snapshot,
        WindowsOcrWord? anchor = null)
    {
        var matches = RemoveNestedSubspans(FindCandidateSpans(label, snapshot.Words));
        var exact = matches.Where(match => match.Similarity == 1).ToArray();
        if (exact.Length == 1)
        {
            return Grounded(label, exact[0], snapshot, "ExactUnique");
        }

        var ranked = matches
            .Where(match => match.Similarity >= 0.85)
            .OrderByDescending(match => match.Similarity)
            .ToArray();
        if (ranked.Length > 0
            && (ranked.Length == 1 || ranked[0].Similarity - ranked[1].Similarity >= 0.15))
        {
            return Grounded(label, ranked[0], snapshot, "FuzzyUnique");
        }

        if (anchor is not null && FrameBoundLabelMatcher.Normalize(label).Length >= 8)
        {
            var tracked = matches
                .Where(match => match.Similarity >= 0.70 && SpatiallyMatches(match.Box, anchor))
                .Select(match => new { Match = match, Error = GeometryError(match.Box, anchor) })
                .OrderBy(item => item.Error)
                .ToArray();
            if (tracked.Length > 0
                && tracked[0].Error <= 0.08
                && (tracked.Length == 1 || tracked[1].Error >= 0.12))
            {
                return Grounded(label, tracked[0].Match, snapshot, "TrackedFuzzyUnique");
            }
        }

        return new GroundedCandidate(
            label,
            "Unknown",
            matches.Length,
            null,
            null,
            null,
            null,
            "same-frame OCR span: exact unique; similarity >= 0.85 with runner-up margin >= 0.15; or anchored tracking >= 0.70");
    }

    private static OcrSpanMatch[] RemoveNestedSubspans(IReadOnlyList<OcrSpanMatch> matches) => matches
        .Where(candidate => !matches.Any(other =>
            !ReferenceEquals(other, candidate)
            && (other.Similarity > candidate.Similarity
                || (other.Similarity == candidate.Similarity && Area(other.Box) < Area(candidate.Box)))
            && (Contains(other.Box, candidate.Box) || Contains(candidate.Box, other.Box))))
        .ToArray();

    private static double Area(WindowsOcrWord value) => value.Width * value.Height;

    private static bool Contains(WindowsOcrWord outer, WindowsOcrWord inner) =>
        outer.X <= inner.X
        && outer.Y <= inner.Y
        && outer.X + outer.Width >= inner.X + inner.Width
        && outer.Y + outer.Height >= inner.Y + inner.Height;

    private static GroundedCandidate Grounded(
        string label,
        OcrSpanMatch match,
        WindowsOcrSnapshot snapshot,
        string matchKind) => new(
            label,
            "Grounded",
            1,
            match.Box,
            snapshot.RecognizerLanguage,
            matchKind,
            match.Similarity,
            "same-frame OCR span: exact unique; similarity >= 0.85 with runner-up margin >= 0.15; or anchored tracking >= 0.70");

    private static bool SpatiallyMatches(WindowsOcrWord candidate, WindowsOcrWord anchor)
    {
        var centerX = candidate.X + candidate.Width / 2;
        var centerY = candidate.Y + candidate.Height / 2;
        var anchorCenterX = anchor.X + anchor.Width / 2;
        var anchorCenterY = anchor.Y + anchor.Height / 2;
        var widthRatio = candidate.Width / anchor.Width;
        var heightRatio = candidate.Height / anchor.Height;
        return Math.Abs(centerX - anchorCenterX) <= Math.Max(24, anchor.Width * 0.15)
            && Math.Abs(centerY - anchorCenterY) <= Math.Max(12, anchor.Height)
            && widthRatio is >= 0.60 and <= 1.40
            && heightRatio is >= 0.50 and <= 1.75;
    }

    private static double GeometryError(WindowsOcrWord candidate, WindowsOcrWord anchor)
    {
        var centerX = candidate.X + candidate.Width / 2;
        var centerY = candidate.Y + candidate.Height / 2;
        var anchorCenterX = anchor.X + anchor.Width / 2;
        var anchorCenterY = anchor.Y + anchor.Height / 2;
        return Math.Abs(centerX - anchorCenterX) / Math.Max(1, anchor.Width)
            + Math.Abs(centerY - anchorCenterY) / Math.Max(1, anchor.Height)
            + Math.Abs(candidate.Width - anchor.Width) / Math.Max(1, anchor.Width)
            + Math.Abs(candidate.Height - anchor.Height) / Math.Max(1, anchor.Height);
    }

    private static OcrSpanMatch[] FindCandidateSpans(
        string label,
        IReadOnlyList<WindowsOcrWord> words)
    {
        var results = new List<OcrSpanMatch>();
        var lines = new List<List<WindowsOcrWord>>();
        foreach (var word in words.OrderBy(word => word.Y).ThenBy(word => word.X))
        {
            var line = lines.FirstOrDefault(existing => existing.Any(item =>
                item.Y < word.Y + word.Height && word.Y < item.Y + item.Height));
            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }

        foreach (var line in lines)
        {
            var ordered = line.OrderBy(word => word.X).ToArray();
            for (var start = 0; start < ordered.Length; start++)
            {
                var text = string.Empty;
                for (var end = start; end < ordered.Length; end++)
                {
                    if (end > start)
                    {
                        var previous = ordered[end - 1];
                        var gap = ordered[end].X - (previous.X + previous.Width);
                        var allowedGap = Math.Max(12, Math.Max(previous.Height, ordered[end].Height) * 2);
                        if (gap > allowedGap)
                        {
                            break;
                        }
                    }

                    text += ordered[end].Text;
                    var similarity = FrameBoundLabelMatcher.Similarity(text, label);
                    if (similarity < 0.5)
                    {
                        continue;
                    }

                    var span = ordered[start..(end + 1)];
                    var left = span.Min(word => word.X);
                    var top = span.Min(word => word.Y);
                    var right = span.Max(word => word.X + word.Width);
                    var bottom = span.Max(word => word.Y + word.Height);
                    results.Add(new OcrSpanMatch(
                        new WindowsOcrWord(text, left, top, right - left, bottom - top),
                        similarity));
                }
            }
        }

        return results
            .GroupBy(match => new
            {
                Text = FrameBoundLabelMatcher.Normalize(match.Box.Text),
                match.Box.X,
                match.Box.Y,
            })
            .Select(group => group
                .OrderBy(match => match.Box.Width * match.Box.Height)
                .First())
            .ToArray();
    }

    private static FoundryVisionResult AggregateVision(IReadOnlyList<VisionTileRun> runs)
    {
        var failed = runs.FirstOrDefault(run => run.Result.Status != FoundryVisionStatus.Completed);
        if (failed is not null)
        {
            return new FoundryVisionResult(
                FoundryVisionStatus.Unknown,
                failed.Result.Failure,
                $"tile {failed.Tile.Index}: {failed.Result.FailureDetail}",
                FoundryVisionNormalization.None,
                [],
                string.Empty,
                runs.Sum(run => run.Result.ElapsedMs),
                runs.Sum(run => run.Result.RequestBytes),
                null,
                null);
        }

        return new FoundryVisionResult(
            FoundryVisionStatus.Completed,
            FoundryVisionFailure.None,
            null,
            runs.Aggregate(
                FoundryVisionNormalization.None,
                (current, run) => current | run.Result.Normalization),
            runs.SelectMany(run => run.Result.Labels).Distinct(StringComparer.Ordinal).ToArray(),
            string.Empty,
            runs.Sum(run => run.Result.ElapsedMs),
            runs.Sum(run => run.Result.RequestBytes),
            runs.All(run => run.Result.InputTokens.HasValue)
                ? runs.Sum(run => run.Result.InputTokens!.Value)
                : null,
            runs.All(run => run.Result.OutputTokens.HasValue)
                ? runs.Sum(run => run.Result.OutputTokens!.Value)
                : null);
    }

    private static bool Intersects(EncodedTile tile, WindowsOcrWord word) =>
        tile.X < word.X + word.Width
        && word.X < tile.X + tile.Width
        && tile.Y < word.Y + word.Height
        && word.Y < tile.Y + tile.Height;

    internal static async Task<CapturedFrame> WaitForFrameAsync(
        WgcFrameSource source,
        TimeSpan timeout)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < timeout)
        {
            if (source.PullDetailed().Result is FrameAvailable available)
            {
                return available.Frame;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("live windowのWGC frameが到着しませんでした。");
    }

    internal static EncodedPng EncodePng(
        CapturedFrame frame,
        int? maximumDimension = null,
        double scaleFactor = 1)
    {
        if (scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        }
        var bitmap = CreateBitmap(frame);
        var scale = scaleFactor;
        if (maximumDimension is { } maximum
            && Math.Max(frame.Width, frame.Height) * scale > maximum)
        {
            scale = maximum / (double)Math.Max(frame.Width, frame.Height);
        }

        BitmapSource encodedBitmap = bitmap;
        if (scale != 1)
        {
            var transformed = new TransformedBitmap(
                bitmap,
                new ScaleTransform(scale, scale));
            RenderOptions.SetBitmapScalingMode(transformed, BitmapScalingMode.HighQuality);
            encodedBitmap = transformed;
        }

        return EncodeBitmap(encodedBitmap);
    }

    internal static EncodedPng EncodeRegion(
        CapturedFrame frame,
        int x,
        int y,
        int width,
        int height,
        double scaleFactor)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0
            || x + width > frame.Width || y + height > frame.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }
        if (scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        }

        BitmapSource bitmap = new CroppedBitmap(
            CreateBitmap(frame),
            new Int32Rect(x, y, width, height));
        if (scaleFactor != 1)
        {
            var transformed = new TransformedBitmap(
                bitmap,
                new ScaleTransform(scaleFactor, scaleFactor));
            RenderOptions.SetBitmapScalingMode(transformed, BitmapScalingMode.HighQuality);
            bitmap = transformed;
        }
        return EncodeBitmap(bitmap);
    }

    private static IReadOnlyList<EncodedTile> EncodeTiles(
        CapturedFrame frame,
        int tileWidth,
        int tileHeight,
        int overlap)
    {
        var bitmap = CreateBitmap(frame);
        var xStarts = BuildTileStarts(frame.Width, tileWidth, overlap);
        var yStarts = BuildTileStarts(frame.Height, tileHeight, overlap);
        var result = new List<EncodedTile>(xStarts.Count * yStarts.Count);
        var index = 0;
        foreach (var y in yStarts)
        {
            foreach (var x in xStarts)
            {
                var width = Math.Min(tileWidth, frame.Width - x);
                var height = Math.Min(tileHeight, frame.Height - y);
                var crop = new CroppedBitmap(bitmap, new Int32Rect(x, y, width, height));
                result.Add(new EncodedTile(index++, x, y, width, height, EncodeBitmap(crop)));
            }
        }
        return result;
    }

    private static IReadOnlyList<int> BuildTileStarts(int length, int size, int overlap)
    {
        if (size <= 0 || overlap < 0 || overlap >= size)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
        if (length <= size)
        {
            return [0];
        }

        var starts = new List<int>();
        var stride = size - overlap;
        for (var start = 0; start + size < length; start += stride)
        {
            starts.Add(start);
        }
        var finalStart = length - size;
        if (starts.Count == 0 || starts[^1] != finalStart)
        {
            starts.Add(finalStart);
        }
        return starts;
    }

    private static BitmapSource CreateBitmap(CapturedFrame frame)
    {
        var pixels = frame.Pixels
            ?? throw new InvalidOperationException("capture frame pixelsがありません。");
        var packedStride = checked(frame.Width * 4);
        var packed = new byte[checked(packedStride * frame.Height)];
        for (var y = 0; y < frame.Height; y++)
        {
            pixels.Bgra8.Span.Slice(y * pixels.Stride, packedStride)
                .CopyTo(packed.AsSpan(y * packedStride, packedStride));
        }

        return BitmapSource.Create(
            frame.Width,
            frame.Height,
            frame.DpiX,
            frame.DpiY,
            PixelFormats.Bgra32,
            null,
            packed,
            packedStride);
    }

    private static EncodedPng EncodeBitmap(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new EncodedPng(stream.ToArray(), bitmap.PixelWidth, bitmap.PixelHeight);
    }

    internal static WindowTarget FindWindow(string processNameSubstring)
    {
        if (string.IsNullOrWhiteSpace(processNameSubstring))
        {
            throw new ArgumentException("process name substringが必要です。", nameof(processNameSubstring));
        }

        var candidates = new List<WindowTarget>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)
                || GetWindowTextLength(window) == 0
                || !GetWindowRect(window, out var rect)
                || rect.Right <= rect.Left
                || rect.Bottom <= rect.Top)
            {
                return true;
            }

            GetWindowThreadProcessId(window, out var processId);
            try
            {
                using var process = Process.GetProcessById(checked((int)processId));
                if (!process.ProcessName.Contains(processNameSubstring, StringComparison.OrdinalIgnoreCase)
                    || process.ProcessName.Contains("launcher", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var title = new StringBuilder(GetWindowTextLength(window) + 1);
                GetWindowText(window, title, title.Capacity);
                string? processPath = null;
                var pathReadStatus = "Confirmed";
                try
                {
                    processPath = process.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    pathReadStatus = $"Unknown: {ex.GetType().Name}";
                }

                candidates.Add(new WindowTarget(
                    window,
                    checked((int)processId),
                    process.ProcessName,
                    processPath,
                    pathReadStatus,
                    title.ToString(),
                    new WindowRectangle(rect.Left, rect.Top, rect.Right, rect.Bottom),
                    GetExtendedFrameBounds(window),
                    GetDpiForWindow(window)));
            }
            catch (ArgumentException)
            {
                // EnumWindows後に終了したprocessだけを無視する。
            }

            return true;
        }, IntPtr.Zero);

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"visible windowを持つprocess '{processNameSubstring}' がありません。"),
            _ => throw new InvalidOperationException(
                $"process '{processNameSubstring}' のvisible windowが{candidates.Count}件あり一意ではありません: "
                + string.Join(" | ", candidates.Select(item => $"{item.ProcessId}:{item.WindowTitle}"))),
        };
    }

    internal static FoundryDaemonState ReadDaemonState()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".foundry",
            "daemon.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var processId = root.GetProperty("pid").GetInt32();
        var endpoint = new Uri(root.GetProperty("web_urls")[0].GetString()
            ?? throw new InvalidOperationException("Foundry daemon endpointがありません。"));
        using var process = Process.GetProcessById(processId);
        if (!string.Equals(process.ProcessName, "foundrylocald", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Foundry daemon PID {processId} はfoundrylocaldではありません。" );
        }

        var version = root.GetProperty("daemon_version").GetString()
            ?? throw new InvalidOperationException("Foundry daemon versionがありません。");
        return new FoundryDaemonState(processId, endpoint, version);
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        int attribute,
        out NativeRect value,
        int valueSize);

    private static WindowRectangle GetExtendedFrameBounds(nint window)
    {
        const int DwmwaExtendedFrameBounds = 9;
        var result = DwmGetWindowAttribute(
            window,
            DwmwaExtendedFrameBounds,
            out var rect,
            Marshal.SizeOf<NativeRect>());
        if (result != 0)
        {
            throw new InvalidOperationException(
                $"DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) failed: 0x{result:X8}");
        }
        return new WindowRectangle(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    internal sealed record FoundryDaemonState(int ProcessId, Uri Endpoint, string Version);

    internal sealed record EncodedPng(byte[] Bytes, int Width, int Height);

    private sealed record EncodedTile(
        int Index,
        int X,
        int Y,
        int Width,
        int Height,
        EncodedPng Png);

    private sealed record VisionTileRun(EncodedTile Tile, FoundryVisionResult Result);

    internal sealed record GroundedCandidate(
        string Label,
        string Status,
        int MatchCount,
        WindowsOcrWord? Box,
        string? RecognizerLanguage,
        string? MatchKind,
        double? Similarity,
        string Rule);

    private sealed record OcrSpanMatch(WindowsOcrWord Box, double Similarity);

    internal sealed record WindowTarget(
        nint Window,
        int ProcessId,
        string ProcessName,
        string? ProcessPath,
        string PathReadStatus,
        string WindowTitle,
        WindowRectangle Rect,
        WindowRectangle CaptureRect,
        uint Dpi);

    internal sealed record WindowRectangle(int Left, int Top, int Right, int Bottom);
}
