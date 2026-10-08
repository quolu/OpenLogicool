using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

public enum VisualKeyAssistDecision { Hold, Wait, Cue, Timed }

/// <summary>利用者の画像条件を優先し、通常の待ち時間だけを乱数で決める。</summary>
public sealed class VisualKeyAssistSchedule(long startedMilliseconds, Func<int> nextInterval)
{
    private long nextTimed = startedMilliseconds + nextInterval();
    private long nextCue;

    public VisualKeyAssistDecision Decide(long now, bool inhibited, bool cue) =>
        inhibited ? VisualKeyAssistDecision.Hold
        : cue ? now >= nextCue ? VisualKeyAssistDecision.Cue : VisualKeyAssistDecision.Wait
        : now >= nextTimed ? VisualKeyAssistDecision.Timed : VisualKeyAssistDecision.Wait;

    public void RecordInput(long now)
    {
        nextTimed = now + nextInterval();
        nextCue = now + 750;
    }
}

public sealed record VisualKeyTemplateMatch(double Difference, IReadOnlyList<double> Bounds)
{
    public bool Matches => Difference <= 18;
}

/// <summary>周囲の背景を除いた利用者画像を、小さく平滑化したRGB標本で照合する。</summary>
public sealed class VisualKeyTemplate(int width, int height, byte[] bgra)
{
    private const int Samples = 16;
    private readonly byte[] samples = Sample(bgra, width, height);

    public static VisualKeyTemplate Load(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new FormatConvertedBitmap(
            BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0],
            PixelFormats.Bgra32, null, 0);
        if (bitmap.PixelWidth < 8 || bitmap.PixelHeight < 8)
            throw new ArgumentException("画像条件には縦横8px以上の画像が必要です。", nameof(path));
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return new(bitmap.PixelWidth, bitmap.PixelHeight, bytes);
    }

    public VisualKeyTemplateMatch Find(CapturedFrame frame, IReadOnlyList<double> searchBounds)
    {
        var pixels = frame.Pixels ?? throw new InvalidOperationException("画像条件の照合にpixelsがありません。");
        var bytes = pixels.Bgra8.Span;
        var left = (int)(searchBounds[0] * frame.Width);
        var top = (int)(searchBounds[1] * frame.Height);
        var right = (int)((searchBounds[0] + searchBounds[2]) * frame.Width);
        var bottom = (int)((searchBounds[1] + searchBounds[3]) * frame.Height);
        var best = double.PositiveInfinity;
        IReadOnlyList<double> bestBounds = [];
        var bestX = 0;
        var bestY = 0;
        var bestWidth = 0;
        var bestHeight = 0;
        foreach (var scale in new[] { 0.8, 0.85, 0.9, 0.95, 1.0, 1.05, 1.1, 1.15, 1.2 })
        {
            var sampleWidth = (int)Math.Round(width * scale);
            var sampleHeight = (int)Math.Round(height * scale);
            for (var y = top; y + sampleHeight <= bottom; y += 3)
            for (var x = left; x + sampleWidth <= right; x += 3)
            {
                if (!Candidate(bytes, pixels.Stride, x, y, sampleWidth, sampleHeight)) continue;
                var difference = Difference(bytes, pixels.Stride, x, y, sampleWidth, sampleHeight, best);
                if (difference >= best) continue;
                best = difference;
                bestX = x;
                bestY = y;
                bestWidth = sampleWidth;
                bestHeight = sampleHeight;
                bestBounds = [x / (double)frame.Width, y / (double)frame.Height,
                    sampleWidth / (double)frame.Width, sampleHeight / (double)frame.Height];
            }
        }
        if (bestWidth > 0)
        {
            for (var y = Math.Max(top, bestY - 2); y <= Math.Min(bottom - bestHeight, bestY + 2); y++)
            for (var x = Math.Max(left, bestX - 2); x <= Math.Min(right - bestWidth, bestX + 2); x++)
            {
                var difference = Difference(bytes, pixels.Stride, x, y, bestWidth, bestHeight, best);
                if (difference >= best) continue;
                best = difference;
                bestBounds = [x / (double)frame.Width, y / (double)frame.Height,
                    bestWidth / (double)frame.Width, bestHeight / (double)frame.Height];
            }
        }
        return new(double.IsPositiveInfinity(best) ? 255 : best, bestBounds);
    }

    private bool Candidate(ReadOnlySpan<byte> bytes, int stride, int x, int y, int w, int h)
    {
        var total = 0;
        for (var sy = 1; sy < Samples; sy += 4)
        for (var sx = 1; sx < Samples; sx += 4)
        {
            var sampleX = x + (int)(w * (0.15 + 0.7 * (sx + 0.5) / Samples));
            var sampleY = y + (int)(h * (0.15 + 0.7 * (sy + 0.5) / Samples));
            var expected = (sy * Samples + sx) * 3;
            for (var channel = 0; channel < 3; channel++)
                total += Math.Abs(Average(bytes, stride, sampleX, sampleY, channel) - samples[expected + channel]);
            if (total > 32 * 16 * 3) return false;
        }
        return true;
    }

    private double Difference(ReadOnlySpan<byte> bytes, int stride, int x, int y, int w, int h, double best)
    {
        var total = 0;
        var count = Samples * Samples * 3;
        for (var sy = 0; sy < Samples; sy++)
        for (var sx = 0; sx < Samples; sx++)
        {
            var sampleX = x + (int)(w * (0.15 + 0.7 * (sx + 0.5) / Samples));
            var sampleY = y + (int)(h * (0.15 + 0.7 * (sy + 0.5) / Samples));
            var expected = (sy * Samples + sx) * 3;
            for (var channel = 0; channel < 3; channel++)
                total += Math.Abs(Average(bytes, stride, sampleX, sampleY, channel) - samples[expected + channel]);
            if (total > best * count) return double.PositiveInfinity;
        }
        return total / (double)count;
    }

    private static byte[] Sample(byte[] bytes, int width, int height)
    {
        var result = new byte[Samples * Samples * 3];
        for (var sy = 0; sy < Samples; sy++)
        for (var sx = 0; sx < Samples; sx++)
        {
            var x = (int)(width * (0.15 + 0.7 * (sx + 0.5) / Samples));
            var y = (int)(height * (0.15 + 0.7 * (sy + 0.5) / Samples));
            for (var channel = 0; channel < 3; channel++)
                result[(sy * Samples + sx) * 3 + channel] = (byte)Average(bytes, width * 4, x, y, channel);
        }
        return result;
    }

    private static int Average(ReadOnlySpan<byte> bytes, int stride, int x, int y, int channel) =>
        (bytes[y * stride + x * 4 + channel]
        + bytes[y * stride + (x + 1) * 4 + channel]
        + bytes[(y + 1) * stride + x * 4 + channel]
        + bytes[(y + 1) * stride + (x + 1) * 4 + channel]) / 4;
}

public static class VisualKeyAssistRuntime
{
    public static async Task<object> RunAsync(
        string[] arguments, SerialHidResidentOutputSession nano, SerialHidEmitter emitter,
        WindowsGameTarget target, string sourceId)
    {
        string Required(string name)
        {
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1]
                : throw new ArgumentException($"{name} が必要です。");
        }
        var inhibit = VisualKeyTemplate.Load(Required("--inhibit-image"));
        var cues = arguments.Select((value, index) => (value, index))
            .Where(item => item.value == "--cue-image")
            .Select(item => item.index + 1 < arguments.Length
                ? VisualKeyTemplate.Load(arguments[item.index + 1])
                : throw new ArgumentException("--cue-image に画像のパスが必要です。"))
            .ToArray();
        var cueTexts = arguments.Select((value, index) => (value, index))
            .Where(item => item.value == "--cue-text")
            .Select(item => item.index + 1 < arguments.Length ? arguments[item.index + 1]
                : throw new ArgumentException("--cue-text に文字列が必要です。"))
            .ToArray();
        if (cues.Length == 0 && cueTexts.Length == 0)
            throw new ArgumentException("--cue-image または --cue-text が必要です。");
        var keys = Required("--keys").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var regionIndex = Array.IndexOf(arguments, "--search-bounds");
        var regionText = regionIndex >= 0 ? Required("--search-bounds") : "0,0,1,1";
        var region = regionText.Split(',').Select(value =>
            double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (region.Length != 4 || region.Any(value => !double.IsFinite(value) || value < 0 || value > 1)
            || region[2] <= 0 || region[3] <= 0 || region[0] + region[2] > 1 || region[1] + region[3] > 1)
            throw new ArgumentException("画像条件の探索範囲が不正です。");
        var duration = int.Parse(Required("--duration-ms"), System.Globalization.CultureInfo.InvariantCulture);
        if (duration <= 0) throw new ArgumentException("実行時間は正のミリ秒です。");
        var evidenceDirectory = Path.GetFullPath(Required("--evidence"));
        Directory.CreateDirectory(evidenceDirectory);
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (!arguments.Contains("--observe-only", StringComparer.Ordinal))
                WindowsTaskbarNanoWindowActivator.EnsureForeground(target, nano.Protocol, emitter);
            using var frames = new WindowsWgcGameFrameSource(target.Window, sourceId, TimeSpan.FromSeconds(10));
            var actions = new NanoGameInteractionActions(
                new SerialHidNanoGameInputDevice(nano.Protocol, emitter, new WindowsSerialHidCursorOracle()),
                new WindowsGameInteractionCoordinateMapper(() => target.Bounds));
            var clock = Stopwatch.StartNew();
            var schedule = new VisualKeyAssistSchedule(0, () => Random.Shared.Next(8_000, 12_001));
            var events = new List<object>();
            VisualKeyAssistDecision? previous = null;
            while (clock.ElapsedMilliseconds < duration)
            {
                stop.Token.ThrowIfCancellationRequested();
                var frame = await frames.CaptureAsync(stop.Token);
                var inhibitMatch = inhibit.Find(frame, region);
                var cueMatch = inhibitMatch.Matches || cues.Length == 0 ? null : cues.Select(cue => cue.Find(frame, region))
                    .OrderBy(match => match.Difference).First();
                var ocr = !inhibitMatch.Matches && cueTexts.Length > 0
                    ? await new WindowsGameOcrRecognizer().RecognizeAsync(frame, stop.Token) : null;
                var matchedTexts = ocr is null ? [] : cueTexts.Where(cue => ContainsCue(ocr.Text, cue)).ToArray();
                var decision = schedule.Decide(clock.ElapsedMilliseconds, inhibitMatch.Matches,
                    cueMatch?.Matches == true || matchedTexts.Length > 0);
                if (decision != previous)
                {
                    Emit(new { Event = "condition", Decision = decision.ToString(), AtMs = clock.ElapsedMilliseconds,
                        InhibitDifference = inhibitMatch.Difference, CueDifference = cueMatch?.Difference, MatchedTexts = matchedTexts });
                    previous = decision;
                }
                if (arguments.Contains("--observe-only", StringComparer.Ordinal))
                {
                    File.WriteAllBytes(Path.Combine(evidenceDirectory, "observation.png"),
                        new WindowsGameFramePngEncoder().Encode(frame).Bytes.ToArray());
                    return new { Mode = "key-assist-observation", decision, inhibitMatch, cueMatch,
                        MatchedTexts = matchedTexts, OcrText = ocr?.Text, InputCount = 0, AiCallCount = 0 };
                }
                if (decision is VisualKeyAssistDecision.Cue or VisualKeyAssistDecision.Timed)
                {
                    Emit(new { Event = "foreground", AtMs = clock.ElapsedMilliseconds,
                        Identity = ForegroundAppTracker.GetForegroundIdentity(),
                        Title = ForegroundAppTracker.GetForegroundWindowTitle() });
                    WindowsTaskbarNanoWindowActivator.EnsureForeground(target, nano.Protocol, emitter);
                    var beforeObservation = Observation(frame);
                    var beforeScene = decision == VisualKeyAssistDecision.Timed ? await Scene(frame, beforeObservation) : null;
                    // OCRや前面化の間に停止画像へ変わった場合も、その画像を優先する。
                    var dispatchFrame = await frames.CaptureAsync(stop.Token);
                    if (inhibit.Find(dispatchFrame, region).Matches) continue;
                    var dispatchObservation = Observation(dispatchFrame);
                    var dispatch = actions.KeyTap(new GameInteractionKeyTapRequest(
                        ContractSchemaVersions.Revision03, dispatchObservation.ObservationId, dispatchFrame.Sequence,
                        dispatchFrame.TransformRevision, dispatchFrame.SourceId, keys), dispatchObservation);
                    if (dispatch.Status != GameInteractionDispatchStatus.Dispatched)
                        throw new InvalidOperationException($"Nano入力に失敗しました: {dispatch.FailureReason}");
                    schedule.RecordInput(clock.ElapsedMilliseconds);
                    Emit(new { Event = "input", Decision = decision.ToString(), AtMs = clock.ElapsedMilliseconds, dispatch });
                    if (beforeScene is not null)
                    {
                        await Task.Delay(1_000, stop.Token);
                        var after = await frames.CaptureAsync(stop.Token);
                        var afterScene = await Scene(after, Observation(after));
                        var comparison = new GameTransitionJudge().CompareRecorded(beforeScene,
                            new GameInteractionStabilityResult(ContractSchemaVersions.Revision03,
                                GameInteractionStabilityStatus.TimedOut, [afterScene], null, 0, 0, 1_000, null));
                        Emit(new { Event = "comparison", AtMs = clock.ElapsedMilliseconds,
                            Judgement = comparison.Judgement.ToString(), comparison.Reasons });
                        if (comparison.Judgement != GameTransitionJudgement.Moved)
                        {
                            var encoder = new WindowsGameFramePngEncoder();
                            File.WriteAllBytes(Path.Combine(evidenceDirectory, "review-before.png"), encoder.Encode(frame).Bytes.ToArray());
                            File.WriteAllBytes(Path.Combine(evidenceDirectory, "review-after.png"), encoder.Encode(after).Bytes.ToArray());
                            return new { Mode = "key-assist", ProductHostEntry = true, AiCallCount = 0,
                                NeedsReview = true, comparison, Events = events,
                                Detail = "通常間隔のキー入力後に画面の切替を確認できないため、追加入力を停止しました。" };
                        }
                    }
                }
                await Task.Delay(250, stop.Token);
            }
            return new { Mode = "key-assist", ProductHostEntry = true, AiCallCount = 0,
                NeedsReview = false, Events = events, Detail = "指定時間が終了しました。" };

            void Emit(object entry)
            {
                events.Add(entry);
                var json = JsonSerializer.Serialize(entry);
                File.AppendAllText(Path.Combine(evidenceDirectory, "events.jsonl"), json + "\n");
                Console.WriteLine(json);
            }
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    internal static bool ContainsCue(string observed, string cue)
    {
        static string Normalize(string value) => string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
        ArgumentException.ThrowIfNullOrWhiteSpace(cue);
        return Normalize(observed).Contains(Normalize(cue), StringComparison.OrdinalIgnoreCase);
    }

    private static ObservationResult Observation(CapturedFrame frame) => new(
        ContractSchemaVersions.Revision03, $"visual-key:{frame.SourceId}:{frame.Sequence}",
        new CapturedFrameReference(ContractSchemaVersions.Revision03, frame.SourceId, frame.Backend,
            frame.Sequence, frame.MonotonicMs, frame.WallClockUtc, frame.TransformRevision, frame.FreshnessMs, frame.LastChangeMs),
        CaptureAvailability.Available, StateIdentityStatus.Novel, [], "visual-key-local", frame.FreshnessMs, null);

    private static async Task<ObservedScene> Scene(CapturedFrame frame, ObservationResult observation)
    {
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        return LocalTargetTrackingSceneBuilder.Build(observation, frame,
            WindowsGameOcrSpanBuilder.Build(ocr, frame.Width, frame.Height), []);
    }
}
