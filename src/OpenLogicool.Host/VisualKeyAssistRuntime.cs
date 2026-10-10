using System.Collections.Concurrent;
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

/// <summary>進行の確認待ちはSpaceだけを止め、独立した回復監視は継続する。</summary>
public sealed class VisualKeyAssistProgress
{
    private volatile bool needsReview;
    public bool NeedsReview => needsReview;
    public void Pause() => needsReview = true;
    public void Resume() => needsReview = false;
    public VisualKeyAssistDecision Apply(VisualKeyAssistDecision decision) =>
        NeedsReview && decision != VisualKeyAssistDecision.Hold ? VisualKeyAssistDecision.Wait : decision;
}

/// <summary>利用者の画像条件を優先し、通常の待ち時間だけを乱数で決める。</summary>
public sealed class VisualKeyAssistSchedule(long startedMilliseconds, Func<int> nextInterval, bool timedInputEnabled = true)
{
    private long? nextTimed = timedInputEnabled ? startedMilliseconds + nextInterval() : null;
    private long nextCue;
    private readonly object gate = new();

    public VisualKeyAssistDecision Decide(long now, bool inhibited, bool cue)
    {
        lock (gate) return inhibited ? VisualKeyAssistDecision.Hold
        : cue ? now >= nextCue ? VisualKeyAssistDecision.Cue : VisualKeyAssistDecision.Wait
        : nextTimed is { } deadline && now >= deadline ? VisualKeyAssistDecision.Timed : VisualKeyAssistDecision.Wait;
    }

    public void RecordInput(long now)
    {
        lock (gate)
        {
            if (timedInputEnabled) nextTimed = now + nextInterval();
            nextCue = now + 750;
        }
    }
}

public sealed record VisualKeyTemplateMatch(double Difference, IReadOnlyList<double> Bounds)
{
    public bool Matches => Difference <= 18;
}

/// <summary>周囲の背景を除いた利用者画像を、小さく平滑化したRGB標本で照合する。</summary>
public sealed class VisualKeyTemplate(int width, int height, byte[] bgra, bool relativeColor = false, bool silhouette = false,
    double[][]? stableRegions = null, int searchStep = 1, bool clipsAtBottom = false)
{
    private const int Samples = 16;
    // 描画領域の下端で切れた画像も探す。外へ出てよいのは、最も下の照合領域1つ分までとする。
    private readonly double bottomClip = clipsAtBottom && stableRegions is { Length: > 2 }
        ? 1 - stableRegions.Select(area => area[1] + area[3]).OrderDescending().ElementAt(1) : 0;
    private readonly byte[] samples = Sample(bgra, width, height, relativeColor, silhouette);
    private readonly (double X, double Y, byte[] Color)[][]? stableSamples = stableRegions?.Select(area =>
        Enumerable.Range(0, 64).Select(index =>
        {
            // 2×2画素の平均を取る標本は、右隣と下隣も参照画像内に収める。
            var x = Math.Min(area[0] + (index % 8 + 0.5) * area[2] / 8, (width - 2d) / width);
            var y = Math.Min(area[1] + (index / 8 + 0.5) * area[3] / 8, (height - 2d) / height);
            var color = Enumerable.Range(0, 3).Select(channel => (byte)Value(bgra, width * 4,
                (int)(x * width), (int)(y * height), channel, relativeColor, silhouette)).ToArray();
            return (x, y, color);
        }).ToArray()).ToArray();

    public static VisualKeyTemplate Load(string path, bool relativeColor = false, bool silhouette = false,
        double[][]? stableRegions = null, int searchStep = 1, bool clipsAtBottom = false)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new FormatConvertedBitmap(
            BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0],
            PixelFormats.Bgra32, null, 0);
        if (bitmap.PixelWidth < 8 || bitmap.PixelHeight < 8)
            throw new ArgumentException("画像条件には縦横8px以上の画像が必要です。", nameof(path));
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return new(bitmap.PixelWidth, bitmap.PixelHeight, bytes, relativeColor, silhouette, stableRegions, searchStep, clipsAtBottom);
    }

    public VisualKeyTemplateMatch Find(CapturedFrame frame, IReadOnlyList<double> searchBounds) =>
        FindAtWindowScale(frame, searchBounds, 1);

    public VisualKeyTemplateMatch FindAtWindowScale(CapturedFrame frame, IReadOnlyList<double> searchBounds, double windowScale) =>
        stableRegions is not null || silhouette ? FindAtScale(frame, searchBounds, windowScale)
            : Find(frame, searchBounds, new[] { 0.8, 0.85, 0.9, 0.95, 1.0, 1.05, 1.1, 1.15, 1.2 }
                .Select(scale => ((int)Math.Round(width * scale * windowScale), (int)Math.Round(height * scale * windowScale))), 3);

    public VisualKeyTemplateMatch FindNativeSize(CapturedFrame frame, IReadOnlyList<double> searchBounds) =>
        Find(frame, searchBounds, new[] { (width, height) }, searchStep);

    public VisualKeyTemplateMatch FindAtScale(CapturedFrame frame, IReadOnlyList<double> searchBounds, double scale)
    {
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        var matches = new List<VisualKeyTemplateMatch>();
        // 小さい文字は縮小描画で平滑化される。参照画像も同じ倍率へ縮小して比較する。
        foreach (var w in Enumerable.Range(Math.Max(8, (int)Math.Round(width * scale) - 1), 3))
            foreach (var h in Enumerable.Range(Math.Max(8, (int)Math.Round(height * scale) - 1), 3))
            {
                var resized = new TransformedBitmap(source, new ScaleTransform(w / (double)width, h / (double)height));
                var bytes = new byte[resized.PixelWidth * resized.PixelHeight * 4];
                resized.CopyPixels(bytes, resized.PixelWidth * 4, 0);
                matches.Add(new VisualKeyTemplate(resized.PixelWidth, resized.PixelHeight, bytes, relativeColor, silhouette, stableRegions, searchStep, clipsAtBottom)
                    .FindNativeSize(frame, searchBounds));
            }
        return matches.MinBy(match => match.Difference)!;
    }

    private VisualKeyTemplateMatch Find(CapturedFrame frame, IReadOnlyList<double> searchBounds,
        IEnumerable<(int Width, int Height)> sizes, int step)
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
        foreach (var (sampleWidth, sampleHeight) in sizes)
        {
            var scaleBest = double.PositiveInfinity;
            var overhang = Math.Max(0, (int)(sampleHeight * bottomClip) - 2);
            for (var y = top; y + sampleHeight - overhang <= bottom; y += step)
                for (var x = left; x + sampleWidth <= right; x += step)
                {
                    if (!Candidate(bytes, pixels.Stride, x, y, sampleWidth, sampleHeight, bottom)) continue;
                    var difference = Difference(bytes, pixels.Stride, x, y, sampleWidth, sampleHeight, scaleBest, bottom);
                    if (difference >= scaleBest) continue;
                    scaleBest = difference;
                    bestX = x;
                    bestY = y;
                    bestWidth = sampleWidth;
                    bestHeight = sampleHeight;
                }
            // 粗い探索で隣の倍率が勝っても、各倍率の正確な座標まで照合する。
            if (double.IsPositiveInfinity(scaleBest)) continue;
            for (var y = Math.Max(top, bestY - 2); y <= Math.Min(bottom - bestHeight + overhang, bestY + 2); y++)
                for (var x = Math.Max(left, bestX - 2); x <= Math.Min(right - bestWidth, bestX + 2); x++)
                {
                    var difference = Difference(bytes, pixels.Stride, x, y, bestWidth, bestHeight, best, bottom);
                    if (difference >= best) continue;
                    best = difference;
                    bestBounds = [x / (double)frame.Width, y / (double)frame.Height,
                    bestWidth / (double)frame.Width, bestHeight / (double)frame.Height];
                }
        }
        return new(double.IsPositiveInfinity(best) ? 255 : best, bestBounds);
    }

    private bool Candidate(ReadOnlySpan<byte> bytes, int stride, int x, int y, int w, int h, int bottom)
    {
        if (stableSamples is not null) return StableDifference(bytes, stride, x, y, w, h, 32, bottom, coarse: true) <= 32;
        var total = 0;
        for (var sy = 1; sy < Samples; sy += 4)
            for (var sx = 1; sx < Samples; sx += 4)
            {
                var sampleX = x + (int)(w * (0.15 + 0.7 * (sx + 0.5) / Samples));
                var sampleY = y + (int)(h * (0.15 + 0.7 * (sy + 0.5) / Samples));
                var expected = (sy * Samples + sx) * 3;
                for (var channel = 0; channel < 3; channel++)
                    total += Math.Abs(Value(bytes, stride, sampleX, sampleY, channel, relativeColor, silhouette) - samples[expected + channel]);
                if (total > 32 * 16 * 3) return false;
            }
        return true;
    }

    private double Difference(ReadOnlySpan<byte> bytes, int stride, int x, int y, int w, int h, double best, int bottom)
    {
        if (stableSamples is not null) return StableDifference(bytes, stride, x, y, w, h, best, bottom);
        var total = 0;
        var count = Samples * Samples * 3;
        for (var sy = 0; sy < Samples; sy++)
            for (var sx = 0; sx < Samples; sx++)
            {
                var sampleX = x + (int)(w * (0.15 + 0.7 * (sx + 0.5) / Samples));
                var sampleY = y + (int)(h * (0.15 + 0.7 * (sy + 0.5) / Samples));
                var expected = (sy * Samples + sx) * 3;
                for (var channel = 0; channel < 3; channel++)
                    total += Math.Abs(Value(bytes, stride, sampleX, sampleY, channel, relativeColor, silhouette) - samples[expected + channel]);
                if (total > best * count) return double.PositiveInfinity;
            }
        return total / (double)count;
    }

    private double StableDifference(ReadOnlySpan<byte> bytes, int stride, int x, int y, int w, int h, double best, int bottom, bool coarse = false)
    {
        var maximum = 0d;
        // 動く針や背景を含めず、指定した各領域がそれぞれ一致することを要求する。
        foreach (var region in stableSamples!)
        {
            // 下端より外へ出た領域は照合しない。探索範囲が、外へ出る領域を最も下の1つまでに限っている。
            if (y + h > bottom && y + (int)(region[^1].Y * h) + 1 >= bottom) continue;
            var total = 0;
            foreach (var point in region)
            {
                for (var channel = 0; channel < 3; channel++)
                    total += Math.Abs(Value(bytes, stride, x + (int)(point.X * w), y + (int)(point.Y * h),
                        channel, relativeColor, silhouette) - point.Color[channel]);
                if (total > best * region.Length * 3) return double.PositiveInfinity;
            }
            maximum = Math.Max(maximum, total / (double)(region.Length * 3));
            if (coarse) break; // 文字の細部は隣接座標を探索してから照合する。
        }
        return maximum;
    }

    private static byte[] Sample(byte[] bytes, int width, int height, bool relativeColor, bool silhouette)
    {
        var result = new byte[Samples * Samples * 3];
        for (var sy = 0; sy < Samples; sy++)
            for (var sx = 0; sx < Samples; sx++)
            {
                var x = (int)(width * (0.15 + 0.7 * (sx + 0.5) / Samples));
                var y = (int)(height * (0.15 + 0.7 * (sy + 0.5) / Samples));
                for (var channel = 0; channel < 3; channel++)
                    result[(sy * Samples + sx) * 3 + channel] = (byte)Value(bytes, width * 4, x, y, channel, relativeColor, silhouette);
            }
        return result;
    }

    private static int Value(ReadOnlySpan<byte> bytes, int stride, int x, int y, int channel, bool relativeColor, bool silhouette)
    {
        if (silhouette) return Average(bytes, stride, x, y, 0) >= 235
            && Average(bytes, stride, x, y, 1) >= 235 && Average(bytes, stride, x, y, 2) >= 235 ? 255 : 0;
        var value = Average(bytes, stride, x, y, channel);
        if (!relativeColor) return value;
        // 時間表示の暗い重ね描きは明るさを変える。色の比率とアイコンの形を照合する。
        var maximum = Math.Max(Average(bytes, stride, x, y, 0),
            Math.Max(Average(bytes, stride, x, y, 1), Average(bytes, stride, x, y, 2)));
        return maximum == 0 ? 0 : value * 255 / maximum;

    }

    private static int Average(ReadOnlySpan<byte> bytes, int stride, int x, int y, int channel) =>
        (bytes[y * stride + x * 4 + channel]
        + bytes[y * stride + (x + 1) * 4 + channel]
        + bytes[(y + 1) * stride + x * 4 + channel]
        + bytes[(y + 1) * stride + (x + 1) * 4 + channel]) / 4;
}

public static class VisualKeyAssistRuntime
{
    internal static int? ReadDuration(string[] arguments)
    {
        var index = Array.IndexOf(arguments, "--duration-ms");
        if (index < 0) return null;
        if (index + 1 == arguments.Length) throw new ArgumentException("--duration-ms に実行時間が必要です。");
        var duration = int.Parse(arguments[index + 1], System.Globalization.CultureInfo.InvariantCulture);
        if (duration <= 0) throw new ArgumentException("実行時間は正のミリ秒です。");
        return duration;
    }

    public static async Task<object> RunAsync(
        string[] arguments, SerialHidResidentOutputSession nano, SerialHidEmitter emitter,
        WindowsGameTarget target, string sourceId, CancellationToken cancellationToken = default,
        Action<JsonElement>? onEvent = null, Func<ResidentPhysicalInput?>? physicalInput = null,
        Func<string?>? requestedMode = null)
    {
        string Required(string name)
        {
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1]
                : throw new ArgumentException($"{name} が必要です。");
        }
        var recoveryOnly = arguments.Contains("--recovery-only", StringComparer.Ordinal);
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
        if (!recoveryOnly && cues.Length == 0 && cueTexts.Length == 0)
            throw new ArgumentException("--cue-image または --cue-text が必要です。");
        var keys = recoveryOnly ? [] : Required("--keys").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var regionIndex = Array.IndexOf(arguments, "--search-bounds");
        var regionText = regionIndex >= 0 ? Required("--search-bounds") : "0,0,1,1";
        var region = regionText.Split(',').Select(value =>
            double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (region.Length != 4 || region.Any(value => !double.IsFinite(value) || value < 0 || value > 1)
            || region[2] <= 0 || region[3] <= 0 || region[0] + region[2] > 1 || region[1] + region[3] > 1)
            throw new ArgumentException("画像条件の探索範囲が不正です。");
        var duration = ReadDuration(arguments);
        // 計測では撮影と判定だけを実行し、前面化・Space・回復品を送出しない。
        var measureOnly = arguments.Contains("--measure-only", StringComparer.Ordinal);
        var continueRules = arguments.Contains("--continue-on-review", StringComparer.Ordinal);
        var timedInputEnabled = !arguments.Contains("--no-timed-input", StringComparer.Ordinal);
        var keepMonitoring = continueRules || arguments.Contains("--keep-monitoring-on-review", StringComparer.Ordinal);
        var evidenceDirectory = Path.GetFullPath(Required("--evidence"));
        Directory.CreateDirectory(evidenceDirectory);
        var recoveryIndex = Array.IndexOf(arguments, "--recovery-profile");
        var recoveryProfile = recoveryIndex < 0 ? null : VisualRecoveryProfile.Load(Required("--recovery-profile"));
        var recoveryRecognizer = recoveryProfile is null ? null : new VisualRecoveryRecognizer(recoveryProfile);
        // --functions は動かす機能。指定なしはメインの組み合わせ。--functions-with-main はメインに足す。
        var progressProfile = Array.IndexOf(arguments, "--progress-profile") < 0 ? null
            : VisualProgressProfile.Load(Required("--progress-profile"),
                Array.IndexOf(arguments, "--functions") < 0 ? null
                    : Required("--functions").Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                arguments.Contains("--functions-with-main", StringComparer.Ordinal));
        if (recoveryOnly && (recoveryProfile is null || progressProfile is not null
            || arguments.Contains("--observe-only", StringComparer.Ordinal)))
            throw new ArgumentException("--recovery-only には --recovery-profile が必要です。進行設定・--observe-only とは併用できません。");
        if (progressProfile is not null && recoveryRecognizer is null)
            throw new ArgumentException("進行設定には、描画領域とHUDを認識する --recovery-profile が必要です。");
        var progressRecognizer = progressProfile is null ? null : new VisualProgressRecognizer(progressProfile);
        var readWhileInhibited = progressProfile?.Rules.Any(rule => rule.AllowWhileInhibited) == true;
        var progressSchedule = progressProfile is null ? null : new VisualProgressSchedule(progressProfile);
        var reviewNotifier = VisualAssistReviewNotifier.Create(
            Array.IndexOf(arguments, "--review-mcp") < 0 ? null : Required("--review-mcp"),
            Array.IndexOf(arguments, "--assistance-db") < 0 ? null : Required("--assistance-db"));
        var recoveryStatePath = Path.GetFullPath(Required("--db")) + ".visual-recovery.json";
        // 操作を送った時に覚えた内容（受注したクエストの名前など）。Botを始め直しても引き継ぐ。
        var rememberedPath = Path.GetFullPath(Required("--db")) + ".remembered.json";
        var rememberedLines = File.Exists(rememberedPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(rememberedPath))
                ?? throw new InvalidDataException("保存した覚え書きが空です。")
            : [];
        foreach (var (name, lines) in rememberedLines) progressRecognizer?.SetRemembered(name, lines);
        // --no-recovery-input は、回復の機能を外す。描画領域とHUDの認識には回復設定を使い続ける。
        if (recoveryOnly && arguments.Contains("--no-recovery-input", StringComparer.Ordinal))
            throw new ArgumentException("--recovery-only と --no-recovery-input は併用できません。");
        var recovery = recoveryProfile is null || arguments.Contains("--no-recovery-input", StringComparer.Ordinal) ? null
            : new VisualRecoverySchedule(recoveryProfile,
            File.Exists(recoveryStatePath)
                ? JsonSerializer.Deserialize<VisualRecoveryState>(File.ReadAllText(recoveryStatePath))
                    ?? throw new InvalidDataException("保存した回復状態が空です。")
                : null);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var userInput = arguments.Contains("--pause-on-user-input", StringComparer.Ordinal)
            ? BotUserInputGate.Create(nano.DeviceIdentity
                ?? throw new InvalidOperationException("手入力の識別に必要なNanoのデバイス情報がありません。"), physicalInput, target.ProcessId) : null;
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (userInput is null && !measureOnly && !arguments.Contains("--observe-only", StringComparer.Ordinal))
                WindowsTaskbarNanoWindowActivator.EnsureForeground(target, nano.Protocol, emitter);
            var actions = new NanoGameInteractionActions(
                new SerialHidNanoGameInputDevice(nano.Protocol, emitter, new WindowsSerialHidCursorOracle()),
                new WindowsGameInteractionCoordinateMapper(() => WindowsGameTargetLocator.Locate(target.ProcessName).Bounds));
            var clock = Stopwatch.StartNew();
            var schedule = new VisualKeyAssistSchedule(0, () => Random.Shared.Next(8_000, 12_001), timedInputEnabled);
            var events = new ConcurrentQueue<object>();
            using var inputGate = new SemaphoreSlim(1, 1);
            var eventGate = new object();
            var progress = new VisualKeyAssistProgress();
            var userEventGate = new object();
            bool? userPaused = null;
            long lastUserSample = -1000;
            var reviewMonitor = new VisualProgressReviewMonitor();
            Action<CapturedFrame, string?>? pendingReviewNotification = null;
            Task notificationWork = Task.CompletedTask;
            VisualAssistNotice? reviewDecisionId = null;
            // 利用者へ直接申請した選択。回答が届いたら、同じ選択肢が表示されている間だけ実行する。
            (VisualAssistNotice Notice, VisualProgressChoice Review, string Folder)? askedUser = null;
            string? answeredOption = null;
            // 演出の途中で読めた文字で申請しないよう、続けて同じ選択肢を読めた時だけ申請する。
            VisualProgressOption[]? settlingOptions = null;
            var askQueued = false;
            var reviewNumber = 0;
            Emit(new { Event = "run-started", DurationMs = duration, AutomaticRulesContinue = continueRules, TimedInputEnabled = timedInputEnabled,
                Functions = progressProfile?.Functions, Recovery = recovery is not null,
                NotificationGraceMs = VisualProgressReviewMonitor.NotificationGraceMs, UserInputSource = userInput?.SourceDescription });
            var result = recovery is null || arguments.Contains("--observe-only", StringComparer.Ordinal)
                ? await RunProgressAsync(stop.Token)
                : await VisualKeyAssistWorkers.RunAsync(RunRecoveryAsync, RunProgressAsync, stop.Token, recoveryOnly);
            var resultJson = JsonSerializer.SerializeToElement(result);
            if (!measureOnly && !arguments.Contains("--observe-only", StringComparer.Ordinal)
                && resultJson.TryGetProperty("NeedsReview", out var needsReview) && needsReview.GetBoolean())
            {
                File.WriteAllText(Path.Combine(evidenceDirectory, "review.json"), resultJson.GetRawText());
                if (reviewNotifier is not null)
                {
                    var decisionId = await reviewNotifier.NotifyAsync(evidenceDirectory, resultJson, stop.Token);
                    File.WriteAllText(Path.Combine(evidenceDirectory, "notification.json"), JsonSerializer.Serialize(new { DecisionId = decisionId }));
                    Emit(new { Event = "review-notified", DecisionId = decisionId });
                }
            }
            return result;

            async Task<object> RunProgressAsync(CancellationToken token)
            {
                using var frames = new WindowsWgcGameFrameSource(target.Window, sourceId + ":progress", TimeSpan.FromSeconds(10));
                var sceneMonitor = new VisualProgressSceneMonitor();
                var inputSequence = new VisualProgressInputSequence(actions);
                VisualKeyAssistDecision? previous = null;

                // 利用者が決裁箱で答えた選択を押し、確定の表示を確かめてから確定キーを送る。
                // 終えた時（送出・回答どおりに進められない時）はtrue、手入力中などで次の観測へ持ち越す時はfalse。
                async Task<bool> ExecuteUserChoiceAsync((VisualAssistNotice Notice, VisualProgressChoice Review, string Folder) asked, string option)
                {
                    var source = asked.Review.ReviewSource!;
                    var index = Array.FindIndex(asked.Review.Options!, candidate => candidate.Id == option);
                    if (index < 0)
                    {
                        Emit(new { Event = "user-choice-skipped", AtMs = clock.ElapsedMilliseconds, Option = option,
                            Detail = "画面の選択肢以外の回答のため、選択を送っていません。" });
                        return true;
                    }
                    await inputGate.WaitAsync(token);
                    try
                    {
                        if (!TryForeground() || UserIsActive()) return false;
                        var fresh = await frames.CaptureAsync(token);
                        var freshViewport = WindowsGameTargetLocator.CaptureClientBounds(target.Window);
                        var current = progressRecognizer!.Recognize(await progressRecognizer.ReadOcrAsync(fresh, freshViewport, token),
                            fresh.Width, fresh.Height, freshViewport, fresh, Inhibited(fresh), progressSchedule!.Stage);
                        // 続けて別の選択が出た時に前の回答を当てないよう、回答した時と同じ選択肢の時だけ押す。
                        if (current.ReviewSource != source || current.OptionPoints is null
                            || !VisualProgressRecognizer.SameOptions(current.Options, asked.Review.Options, ignoreRecommendedMark: true))
                            return Fail("回答した時の選択肢が現在の画面と一致しないため、選択を送っていません。");
                        if (UserIsActive()) return false;
                        var click = new VisualProgressChoice(VisualProgressAction.Click, "user-choice", $"user-choice:{asked.Notice.Id}:{option}",
                            Point: current.OptionPoints[index]);
                        if (!Send(click, fresh)) return false;
                        var deadline = clock.ElapsedMilliseconds + 5000;
                        while (clock.ElapsedMilliseconds < deadline)
                        {
                            await Task.Delay(400, token);
                            var after = await frames.CaptureAsync(token);
                            var afterViewport = WindowsGameTargetLocator.CaptureClientBounds(target.Window);
                            if (UserIsActive() || !VisualProgressRecognizer.Shows(
                                await progressRecognizer.ReadOcrAsync(after, afterViewport, token), afterViewport, source.Confirm!)) continue;
                            Send(new(VisualProgressAction.Key, "user-choice-confirm", click.Signature + ":confirm", source.ConfirmKey), after);
                            return true;
                        }
                        return Fail("選択を押しましたが、確定の表示を確認できません。確定キーは送っていません。");
                    }
                    finally { inputGate.Release(); }

                    // 送れた時はtrue。矢印を動かせなかった時はfalse（次の観測へ持ち越す）。
                    bool Send(VisualProgressChoice choice, CapturedFrame bound)
                    {
                        var dispatch = inputSequence.Dispatch(choice, Observation(bound));
                        if (PointerBlocked(choice, dispatch)) return false;
                        if (dispatch.Status != GameInteractionDispatchStatus.Dispatched)
                            throw new InvalidOperationException($"回答の選択のNano入力に失敗しました: {dispatch.FailureReason}");
                        Emit(new { Event = "progress-input", AtMs = clock.ElapsedMilliseconds, choice.RuleId, choice.Signature,
                            choice.Key, choice.Point, Immediate = false, RepeatIntervalMs = 0, AfterClick = false, dispatch });
                        return true;
                    }

                    bool Fail(string detail)
                    {
                        Emit(new { Event = "user-choice-failed", AtMs = clock.ElapsedMilliseconds, Option = option, Detail = detail });
                        // 回答どおりに進められなかった時は、根拠を添えて担当へ1回知らせる。
                        QueueNotification(async () =>
                        {
                            if (reviewNotifier is null) return;
                            await reviewNotifier.NotifyAsync(asked.Folder, JsonSerializer.SerializeToElement(new { Detail = detail,
                                MonitoringContinues = true, AutomaticRulesContinue = continueRules }), token);
                        }, token);
                        return true;
                    }
                }
                try
                {
                while (duration is null || clock.ElapsedMilliseconds < duration.Value)
                {
                    token.ThrowIfCancellationRequested();
                    var frame = await frames.CaptureAsync(token);
                    _ = UserIsActive();
                    if (progressSchedule is not null && requestedMode is not null && requestedMode() is var wanted
                        && wanted != progressSchedule.ModeId)
                    {
                        var mode = wanted is null ? null : progressProfile!.Modes?.SingleOrDefault(candidate => candidate.Id == wanted)
                            ?? throw new InvalidOperationException($"進行設定にモード {wanted} がありません。bot mode off で解除してください。");
                        progressSchedule.SetMode(mode);
                        Emit(new { Event = "mode-changed", AtMs = clock.ElapsedMilliseconds, Mode = mode?.Id,
                            Detail = mode is null ? "モードを解除しました。" : $"{mode.Name}に入りました。" });
                    }
                    var viewport = recoveryRecognizer is null ? null : WindowsGameTargetLocator.CaptureClientBounds(target.Window);
                    var windowScale = viewport is null ? 1 : recoveryRecognizer!.HudScale(viewport);
                    var inhibitMatch = inhibit.FindAtWindowScale(frame, region, windowScale);
                    var cueMatch = inhibitMatch.Matches || cues.Length == 0 ? null : cues.Select(cue => cue.FindAtWindowScale(frame, region, windowScale))
                        .OrderBy(match => match.Difference).First();
                    var stage = progressSchedule?.Stage;
                    // 数値で始める規則を選べる時は、即時の画像規則だけで決めず、文字も読んで優先度で比べる。
                    var numbered = progressRecognizer is null ? null
                        : await progressRecognizer.RecognizeNumberRuleAsync(frame, viewport!, stage, progressSchedule!.MayStart,
                            progressSchedule!.ObserveUnmet, inhibitMatch.Matches, token);
                    var immediate = numbered is null ? progressRecognizer?.RecognizeImmediateImage(frame, viewport!, inhibitMatch.Matches, stage) : null;
                    // 直前に操作を送った規則の直後だけ評価する規則がある間は、即時の画像が出ていても文字を読む
                    // （Spaceを押した直後に出る知らせを読むため）。
                    var recent = progressSchedule?.RecentInput(clock.ElapsedMilliseconds);
                    var recentRule = recent?.RuleId;
                    var afterPending = recent is { } last && progressProfile!.Rules.Any(rule =>
                        rule.After == last.RuleId && last.ElapsedMs <= rule.AfterWithinMs && rule.Stage == stage);
                    // 即時の画像より先に評価する規則（受注した名前と追跡中の名前の照合など）がある間も、文字を読む。
                    var preemptPending = progressRecognizer?.HasPreempting(stage) == true;
                    var ocr = (immediate is null || afterPending || preemptPending) && (!inhibitMatch.Matches || readWhileInhibited) && (progressProfile is not null || cueTexts.Length > 0 || !string.IsNullOrWhiteSpace(recoveryProfile?.IncapacitatedText))
                        ? progressRecognizer is null ? await new WindowsGameOcrRecognizer().RecognizeAsync(frame, token)
                            : await progressRecognizer.ReadOcrAsync(frame, viewport!, token, stage) : null;
                    if (ocr is not null && progressSchedule?.PendingModeStage(clock.ElapsedMilliseconds) is { } pendingModeStage
                        && !recoveryRecognizer!.Observe(frame, viewport).HudVisible)
                    {
                        // モードの画面が既に開いている時（利用者が開いた一覧など）は、閉じてもらうのを待たずに続きから進める。
                        var probe = progressRecognizer!.Recognize(await progressRecognizer.ReadOcrAsync(frame, viewport!, token, pendingModeStage),
                            frame.Width, frame.Height, viewport!, frame, inhibitMatch.Matches, pendingModeStage);
                        if (probe.Action is VisualProgressAction.Key or VisualProgressAction.Click)
                        {
                            progressSchedule.StartModeStage();
                            Emit(new { Event = "mode-stage-started", AtMs = clock.ElapsedMilliseconds, Stage = pendingModeStage, probe.RuleId,
                                Detail = "モードの画面が既に開いているため、続きから進めます。" });
                            continue;
                        }
                    }
                    var matchedTexts = ocr is null ? [] : cueTexts.Where(cue => ContainsCue(ocr.Text, cue)).ToArray();
                    var decision = progress.Apply(schedule.Decide(clock.ElapsedMilliseconds, inhibitMatch.Matches,
                        cueMatch?.Matches == true || matchedTexts.Length > 0));
                    var recoveryObservation = arguments.Contains("--observe-only", StringComparer.Ordinal)
                        ? recoveryRecognizer?.Observe(frame, viewport, ocr?.Text) : null;
                    var flowCandidate = immediate ?? (progressRecognizer is null || ocr is null ? null
                        : progressRecognizer.Prefer(numbered,
                            progressRecognizer.Recognize(ocr, frame.Width, frame.Height, viewport!, frame, inhibitMatch.Matches, stage, recent)));
                    if (immediate is not null && (afterPending || preemptPending) && ocr is not null
                        && progressRecognizer!.Recognize(ocr, frame.Width, frame.Height, viewport!, frame, inhibitMatch.Matches, stage,
                            recent, afterOnly: true) is { Action: VisualProgressAction.Key or VisualProgressAction.Click } afterChoice)
                        flowCandidate = afterChoice;
                    if (afterPending && ocr is not null)
                    {
                        // 操作の直後に見た画面と読んだ文字を残す。画像は最新の1枚だけを上書きする。
                        File.WriteAllBytes(Path.Combine(evidenceDirectory, "after-check.png"),
                            new WindowsGameFramePngEncoder().Encode(frame).Bytes.ToArray());
                        Emit(new { Event = "after-check", AtMs = clock.ElapsedMilliseconds, After = recentRule,
                            flowCandidate?.RuleId, OcrText = ocr.Text.Length > 900 ? ocr.Text[..900] : ocr.Text });
                    }
                    var flowInhibited = inhibitMatch.Matches && flowCandidate?.AllowWhileInhibited != true;
                    var sceneActivity = progressProfile is null ? (Changed: false, Difference: 0d)
                        : sceneMonitor.Observe(frame, viewport!);
                    if (reviewMonitor.IsHolding)
                    {
                        var candidate = flowCandidate ?? new(VisualProgressAction.Normal);
                        if (!reviewMonitor.TryResume(clock.ElapsedMilliseconds, candidate, flowInhibited,
                            recoveryRecognizer?.Observe(frame, viewport).HudVisible == true, sceneActivity.Changed))
                        {
                            Emit(new { Event = "progress-monitoring", AtMs = clock.ElapsedMilliseconds,
                                Candidate = candidate.Action.ToString(), candidate.RuleId });
                            if (reviewMonitor.TryTakeNotification(clock.ElapsedMilliseconds))
                                pendingReviewNotification!(frame, ocr?.Text);
                            if (!continueRules)
                            {
                                await Task.Delay(250, token);
                                continue;
                            }
                        }
                        else
                        {
                            pendingReviewNotification = null;
                            askedUser = null;
                            askQueued = false;
                            settlingOptions = null;
                            Interlocked.Exchange(ref answeredOption, null);
                            progress.Resume();
                            if (!continueRules)
                            {
                                progressSchedule = progressProfile is null ? null : new VisualProgressSchedule(progressProfile);
                                schedule.RecordInput(clock.ElapsedMilliseconds);
                            }
                            Emit(new { Event = "progress-resumed", AtMs = clock.ElapsedMilliseconds,
                                Detail = continueRules ? "確認済みの画面に戻りました。操作規則は継続しています。" : "確認済みの画面に戻ったため進行を再開しました。" });
                            QueueNotification(async () =>
                            {
                                if (reviewNotifier is not null && reviewDecisionId is not null)
                                    await reviewNotifier.ResolveAsync(reviewDecisionId, token);
                                reviewDecisionId = null;
                            }, token);
                        }
                    }
                    if (reviewMonitor.IsHolding && askedUser is { } asked && Volatile.Read(ref answeredOption) is { } answered)
                    {
                        if (await ExecuteUserChoiceAsync(asked, answered))
                        {
                            askedUser = null;
                            Interlocked.Exchange(ref answeredOption, null);
                        }
                        await Task.Delay(250, token);
                        continue;
                    }
                    var flowTimed = flowCandidate?.RuleId is not null && progressProfile!.Rules.Single(rule => rule.Id == flowCandidate.RuleId).Timed;
                    var flowChoice = progressSchedule?.Decide(clock.ElapsedMilliseconds,
                        flowCandidate ?? new(VisualProgressAction.Normal), flowInhibited,
                        recoveryRecognizer!.Observe(frame, viewport).HudVisible,
                        schedule.Decide(clock.ElapsedMilliseconds, false, !flowTimed) is VisualKeyAssistDecision.Cue or VisualKeyAssistDecision.Timed,
                        sceneActivity.Changed);
                    if (progressSchedule?.TakeModeFailure() is { } modeFailure)
                    {
                        // 画面の詰まりとは別の失敗なので、1分の様子見を挟まずに、根拠を添えて担当へ1回知らせる。
                        var folder = Path.Combine(evidenceDirectory, $"review-{++reviewNumber:D3}");
                        Directory.CreateDirectory(folder);
                        var image = Path.Combine(folder, "progress-review.png");
                        File.WriteAllBytes(image, new WindowsGameFramePngEncoder().Encode(frame).Bytes.ToArray());
                        var review = JsonSerializer.SerializeToElement(new { Mode = "key-assist", ProductHostEntry = true,
                            NeedsReview = true, MonitoringContinues = true, AutomaticRulesContinue = continueRules,
                            Detail = modeFailure, OcrText = ocr?.Text, Image = image, AiCallCount = 0 });
                        File.WriteAllText(Path.Combine(folder, "review.json"), review.GetRawText());
                        Emit(new { Event = "mode-start-failed", AtMs = clock.ElapsedMilliseconds, Mode = progressSchedule.ModeId,
                            Detail = modeFailure, EvidenceDirectory = folder });
                        QueueNotification(async () =>
                        {
                            if (reviewNotifier is null) return;
                            var notice = await reviewNotifier.NotifyAsync(folder, review, token);
                            File.WriteAllText(Path.Combine(folder, "notification.json"), JsonSerializer.Serialize(new { DecisionId = notice }));
                            Emit(new { Event = "review-notified", DecisionId = notice, MonitoringContinues = true });
                        }, token);
                    }
                    var completingClick = inputSequence.Pending is not null && !inhibitMatch.Matches;
                    if (completingClick) flowChoice = inputSequence.Pending;
                    if (flowChoice is not null)
                        Emit(new { Event = "progress-observation", AtMs = clock.ElapsedMilliseconds,
                            sceneActivity.Changed, sceneActivity.Difference, Candidate = flowCandidate?.Action.ToString(),
                            RuleId = flowCandidate?.RuleId, Immediate = flowCandidate?.Immediate, Decision = flowChoice.Action.ToString() });
                    if (decision != previous)
                    {
                        Emit(new
                        {
                            Event = "condition",
                            Decision = decision.ToString(),
                            AtMs = clock.ElapsedMilliseconds,
                            InhibitDifference = inhibitMatch.Difference,
                            CueDifference = cueMatch?.Difference,
                            MatchedTexts = matchedTexts
                        });
                        previous = decision;
                    }
                    if (arguments.Contains("--observe-only", StringComparer.Ordinal))
                    {
                        File.WriteAllBytes(Path.Combine(evidenceDirectory, "observation.png"),
                            new WindowsGameFramePngEncoder().Encode(frame).Bytes.ToArray());
                        return new
                        {
                            Mode = "key-assist-observation",
                            decision,
                            inhibitMatch,
                            cueMatch,
                            MatchedTexts = matchedTexts,
                            OcrText = ocr?.Text,
                            OcrWords = progressProfile is null ? null : ocr?.Words,
                            Viewport = viewport,
                            Recovery = recoveryObservation,
                            Progress = flowCandidate,
                            InputCount = 0,
                            AiCallCount = 0
                        };
                    }
                    if (!measureOnly && flowChoice is not null)
                    {
                        if (flowChoice.Action == VisualProgressAction.Review)
                        {
                            if (keepMonitoring)
                            {
                                if (flowChoice.AskUserImmediately && !askQueued
                                    && !VisualProgressRecognizer.SameOptions(flowChoice.Options, settlingOptions))
                                {
                                    settlingOptions = flowChoice.Options;
                                    await Task.Delay(250, token);
                                    continue;
                                }
                                BeginMonitoring(flowCandidate ?? new(VisualProgressAction.Normal), frame,
                                    flowChoice.Detail!, flowChoice.Options, ocr?.Text, token, flowChoice.AskUserImmediately ? flowChoice : null);
                                flowChoice = VisualProgressContinuation.AfterReview(
                                    flowCandidate ?? new(VisualProgressAction.Normal), continueRules);
                                if (flowChoice.Action == VisualProgressAction.Wait)
                                {
                                    await Task.Delay(250, token);
                                    continue;
                                }
                            }
                            else
                            {
                                File.WriteAllBytes(Path.Combine(evidenceDirectory, "progress-review.png"),
                                    new WindowsGameFramePngEncoder().Encode(frame).Bytes.ToArray());
                                var review = new { Mode = "key-assist", ProductHostEntry = true, AiCallCount = 0,
                                    NeedsReview = true, Events = events, flowChoice.Detail, ReviewOptions = flowChoice.Options, OcrText = ocr?.Text,
                                    Image = Path.Combine(evidenceDirectory, "progress-review.png") };
                                File.WriteAllText(Path.Combine(evidenceDirectory, "review.json"), JsonSerializer.Serialize(review));
                                Emit(new { Event = "progress-review", AtMs = clock.ElapsedMilliseconds, flowChoice.Detail });
                                return review;
                            }
                        }
                        // 停止表示中は、停止表示中でも押してよいと設定した反復画像だけを評価する。
                        if (flowChoice.Action is VisualProgressAction.Normal or VisualProgressAction.Wait)
                            flowChoice = progressRecognizer!.RecognizeRepeatingImage(frame, viewport!,
                                rule => (!flowInhibited || rule.AllowWhileInhibited)
                                    && progressSchedule!.RepeatIsDue(clock.ElapsedMilliseconds, rule), stage) ?? flowChoice;
                        if (flowChoice.Action is VisualProgressAction.Key or VisualProgressAction.Click or VisualProgressAction.Flick)
                        {
                            await inputGate.WaitAsync(token);
                            try
                            {
                                token.ThrowIfCancellationRequested();
                                if (!TryForeground()) continue;
                                var fresh = await frames.CaptureAsync(token);
                                var freshInhibited = Inhibited(fresh);
                                var freshViewport = WindowsGameTargetLocator.CaptureClientBounds(target.Window);
                                var freshNumbered = completingClick || flowChoice.RepeatIntervalMs > 0 ? null
                                    : await progressRecognizer!.RecognizeNumberRuleAsync(fresh, freshViewport, stage, progressSchedule!.MayStart,
                                        progressSchedule!.ObserveUnmet, freshInhibited, token);
                                // 操作の直後だけ評価する規則は、即時の画像が出ていても、その規則だけを読み直して確かめる。
                                var flowRule = completingClick || flowChoice.RuleId is null ? null
                                    : progressProfile!.Rules.SingleOrDefault(rule => rule.Id == flowChoice.RuleId);
                                var current = freshInhibited && !flowChoice.AllowWhileInhibited ? null
                                    : flowRule is { After: not null } or { Preempt: true } ? progressRecognizer!.Recognize(
                                        await progressRecognizer.ReadOcrAsync(fresh, freshViewport, token, stage),
                                        fresh.Width, fresh.Height, freshViewport, fresh, freshInhibited, stage,
                                        progressSchedule!.RecentInput(clock.ElapsedMilliseconds), afterOnly: true)
                                    : completingClick ? flowChoice : flowChoice.RepeatIntervalMs > 0
                                    ? progressRecognizer!.RecognizeRepeatingImage(fresh, freshViewport, rule => rule.Id == flowChoice.RuleId)
                                    : freshNumbered is null ? progressRecognizer!.RecognizeImmediateImage(fresh, freshViewport, freshInhibited, stage) : null;
                                if (current is null)
                                {
                                    var freshOcr = await progressRecognizer!.ReadOcrAsync(fresh, freshViewport, token, stage);
                                    current = progressRecognizer.Prefer(freshNumbered,
                                        progressRecognizer.Recognize(freshOcr, fresh.Width, fresh.Height, freshViewport, fresh, freshInhibited, stage,
                                            progressSchedule!.RecentInput(clock.ElapsedMilliseconds)));
                                }
                                if (freshInhibited && !current.AllowWhileInhibited
                                    || current.Signature != flowChoice.Signature || current.Action != flowChoice.Action) continue;
                                if (UserIsActive()) continue;
                                var bound = Observation(fresh);
                                var dispatch = inputSequence.Dispatch(current, bound);
                                if (PointerBlocked(current, dispatch))
                                {
                                    // 続く時は詰まりとして担当へ知らせる。画面が進めば知らせは取り下げる。
                                    BeginMonitoring(current, fresh, "クリックの前にマウスの矢印を動かせません。ボタンは押していません。"
                                        + "手入力があった時と同じだけ待って、次の観測でやり直します。 " + dispatch.FailureReason, null, null, token);
                                    continue;
                                }
                                if (dispatch.Status != GameInteractionDispatchStatus.Dispatched)
                                    throw new InvalidOperationException($"進行操作のNano入力に失敗しました: {dispatch.FailureReason}");
                                if (!completingClick) progressSchedule!.RecordInput(clock.ElapsedMilliseconds, current);
                                if (current.RepeatIntervalMs == 0) schedule.RecordInput(clock.ElapsedMilliseconds);
                                if (!completingClick && current.Remembered is { } remembered) SaveRemembered(remembered, current.RuleId!, fresh);
                                Emit(new { Event = "progress-input", AtMs = clock.ElapsedMilliseconds, current.RuleId,
                                    current.Signature, current.Key, current.Point, current.FlickTo, current.Immediate, current.RepeatIntervalMs,
                                    AfterClick = completingClick, progressSchedule!.Stage, dispatch });
                            }
                            finally { inputGate.Release(); }
                            await Task.Delay(250, token);
                            continue;
                        }
                        if (flowChoice.Action == VisualProgressAction.Wait) { await Task.Delay(250, token); continue; }
                    }
                    if (!measureOnly && decision is VisualKeyAssistDecision.Cue or VisualKeyAssistDecision.Timed)
                    {
                        Emit(new
                        {
                            Event = "foreground",
                            AtMs = clock.ElapsedMilliseconds,
                            Identity = ForegroundAppTracker.GetForegroundIdentity(),
                            Title = ForegroundAppTracker.GetForegroundWindowTitle()
                        });
                        var beforeObservation = Observation(frame);
                        // 継続運転では通常Spaceの成否で操作を分岐しないため、結果比較を実行しない。
                        var beforeScene = !continueRules && decision == VisualKeyAssistDecision.Timed
                            ? await Scene(frame, beforeObservation, token) : null;
                        await inputGate.WaitAsync(token);
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            if (!TryForeground()) continue;
                            // OCRや前面化の間に停止画像へ変わった場合も、その画像を優先する。
                            var dispatchFrame = await frames.CaptureAsync(token);
                            if (Inhibited(dispatchFrame)) continue;
                            // 待っている間に回復キーが送られた場合は、更新後の進行期限に従う。
                            if (schedule.Decide(clock.ElapsedMilliseconds, false,
                                cueMatch?.Matches == true || matchedTexts.Length > 0) != decision) continue;
                            var dispatchObservation = Observation(dispatchFrame);
                            if (UserIsActive()) continue;
                            var dispatch = actions.KeyTap(new GameInteractionKeyTapRequest(
                                ContractSchemaVersions.Revision03, dispatchObservation.ObservationId, dispatchFrame.Sequence,
                                dispatchFrame.TransformRevision, dispatchFrame.SourceId, keys), dispatchObservation);
                            if (dispatch.Status != GameInteractionDispatchStatus.Dispatched)
                                throw new InvalidOperationException($"Nano入力に失敗しました: {dispatch.FailureReason}");
                            schedule.RecordInput(clock.ElapsedMilliseconds);
                            Emit(new { Event = "input", Decision = decision.ToString(), AtMs = clock.ElapsedMilliseconds, dispatch });
                        }
                        finally { inputGate.Release(); }
                        if (beforeScene is not null)
                        {
                            await Task.Delay(1_000, token);
                            var after = await frames.CaptureAsync(token);
                            var afterScene = await Scene(after, Observation(after), token);
                            var comparison = new GameTransitionJudge().CompareRecorded(beforeScene,
                                new GameInteractionStabilityResult(ContractSchemaVersions.Revision03,
                                    GameInteractionStabilityStatus.TimedOut, [afterScene], null, 0, 0, 1_000, null));
                            Emit(new
                            {
                                Event = "comparison",
                                AtMs = clock.ElapsedMilliseconds,
                                Judgement = comparison.Judgement.ToString(),
                                comparison.Reasons
                            });
                            if (comparison.Judgement != GameTransitionJudgement.Moved)
                            {
                                var afterHud = recoveryRecognizer?.Observe(after,
                                    WindowsGameTargetLocator.CaptureClientBounds(target.Window)).HudVisible == true;
                                if (!VisualProgressContinuation.RequiresUnchangedReview(continueRules, afterHud))
                                {
                                    Emit(new { Event = "progress-comparison-inconclusive", AtMs = clock.ElapsedMilliseconds,
                                        Detail = "HUD表示中の画面差だけでは確認要求にせず、通常の操作規則を継続します。" });
                                    await Task.Delay(250, token);
                                    continue;
                                }
                                var encoder = new WindowsGameFramePngEncoder();
                                File.WriteAllBytes(Path.Combine(evidenceDirectory, "review-before.png"), encoder.Encode(frame).Bytes.ToArray());
                                File.WriteAllBytes(Path.Combine(evidenceDirectory, "review-after.png"), encoder.Encode(after).Bytes.ToArray());
                                if (progressProfile is not null)
                                {
                                    if (keepMonitoring)
                                    {
                                        BeginMonitoring(flowCandidate ?? new(VisualProgressAction.Normal), after,
                                            continueRules ? "通常Space後の画面変化は未確認です。通常の操作規則を継続しています。"
                                                : "通常Space後の画面変化を確認できません。進行入力を止めて監視を続けています。", null, ocr?.Text, token);
                                        await Task.Delay(250, token);
                                        continue;
                                    }
                                    return new
                                    {
                                        Mode = "key-assist", ProductHostEntry = true, AiCallCount = 0,
                                        NeedsReview = true, Events = events,
                                        Detail = "通常Space後の画面変化を確認できないため、進行規則の実行を停止しました。"
                                    };
                                }
                                if (recovery is not null)
                                {
                                    progress.Pause();
                                    Emit(new
                                    {
                                        Event = "progress-paused",
                                        AtMs = clock.ElapsedMilliseconds,
                                        NeedsReview = true,
                                        Detail = "画面変化が未確認のためSpaceを停止しました。HP監視は継続します。"
                                    });
                                    continue;
                                }
                                return new
                                {
                                    Mode = "key-assist",
                                    ProductHostEntry = true,
                                    AiCallCount = 0,
                                    NeedsReview = true,
                                    comparison,
                                    Events = events,
                                    Detail = "通常間隔のキー入力後に画面の切替を確認できないため、追加入力を停止しました。"
                                };
                            }
                        }
                    }
                    await Task.Delay(250, token);
                }
                return new
                {
                    Mode = "key-assist",
                    ProductHostEntry = true,
                    AiCallCount = 0,
                    NeedsReview = progress.NeedsReview,
                    Events = events,
                    Detail = progress.NeedsReview ? "Spaceは確認待ちで停止し、指定時間までHP監視を継続しました。" : "指定時間が終了しました。"
                };

                }
                finally
                {
                    try { await notificationWork; }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                }

            }

            void BeginMonitoring(VisualProgressChoice candidate, CapturedFrame frame, string detail,
                VisualProgressOption[]? options, string? ocrText, CancellationToken token, VisualProgressChoice? askUser = null)
            {
                // 別の理由で監視中でも、利用者へ直接申請する表示は申請する。申請済みの間は重ねて出さない。
                if (reviewMonitor.IsHolding && (askUser is null || askQueued)) return;
                var detectedAt = clock.ElapsedMilliseconds;
                reviewMonitor.Hold(candidate, recoveryRecognizer?.Observe(frame,
                    WindowsGameTargetLocator.CaptureClientBounds(target.Window)).HudVisible == true, detectedAt,
                    Inhibited(frame) && !candidate.AllowWhileInhibited);
                if (!continueRules) progress.Pause();
                var folder = Path.Combine(evidenceDirectory, $"review-{++reviewNumber:D3}");
                Directory.CreateDirectory(folder);
                var image = Path.Combine(folder, "progress-review.png");
                File.WriteAllBytes(image, new WindowsGameFramePngEncoder().Encode(frame).Bytes.ToArray());
                var review = JsonSerializer.SerializeToElement(new { Mode = "key-assist", ProductHostEntry = true,
                    NeedsReview = true, MonitoringContinues = true, AutomaticRulesContinue = continueRules, Detail = detail, ReviewOptions = options,
                    OcrText = ocrText, Image = image, AiCallCount = 0 });
                File.WriteAllText(Path.Combine(folder, "review.json"), review.GetRawText());
                if (askUser is not null)
                {
                    askQueued = true;
                    // 利用者だけが選ぶ表示は、様子見と担当AIを経由せず検出した回に決裁箱へ出す。
                    // 申請に失敗した時は印を付けず、1分後の詰まり通知で担当AIへ知らせる。
                    Emit(new { Event = "progress-review-monitoring", AtMs = detectedAt,
                        Detail = detail + " 決裁箱へ選択肢を申請します。", AutomaticRulesContinue = continueRules, EvidenceDirectory = folder });
                    QueueNotification(async () =>
                    {
                        if (reviewNotifier is null) throw new InvalidOperationException("決裁箱の接続設定がないため、利用者へ直接申請できません。");
                        var notice = reviewDecisionId = await reviewNotifier.AskUserAsync(folder, review, token);
                        reviewMonitor.MarkNotified();
                        if (askUser.ReviewSource?.ConfirmKey is not null)
                        {
                            askedUser = (notice, askUser, folder);
                            _ = Task.Run(() => PollAnswerAsync(notice, token), token);
                        }
                        File.WriteAllText(Path.Combine(folder, "notification.json"), JsonSerializer.Serialize(new { DecisionId = reviewDecisionId }));
                        Emit(new { Event = "review-notified", DecisionId = reviewDecisionId, MonitoringContinues = true, AskedUser = true,
                            ElapsedMs = clock.ElapsedMilliseconds - detectedAt });
                    }, token);
                }
                else
                {
                    Emit(new { Event = "progress-review-grace", AtMs = detectedAt,
                        Detail = detail, GracePeriodMs = VisualProgressReviewMonitor.NotificationGraceMs,
                        AutomaticRulesContinue = continueRules, EvidenceDirectory = folder });
                }
                pendingReviewNotification = (latestFrame, latestOcr) =>
                {
                    File.WriteAllBytes(image, new WindowsGameFramePngEncoder().Encode(latestFrame).Bytes.ToArray());
                    var latestReview = JsonSerializer.SerializeToElement(new { Mode = "key-assist", ProductHostEntry = true,
                        NeedsReview = true, MonitoringContinues = true, AutomaticRulesContinue = continueRules,
                        Detail = detail, ReviewOptions = options, OcrText = latestOcr, Image = image, AiCallCount = 0,
                        DetectedAtMs = detectedAt, ObservedAtMs = clock.ElapsedMilliseconds,
                        GracePeriodMs = VisualProgressReviewMonitor.NotificationGraceMs });
                    File.WriteAllText(Path.Combine(folder, "review.json"), latestReview.GetRawText());
                    Emit(new { Event = "progress-review-monitoring", AtMs = clock.ElapsedMilliseconds,
                        Detail = detail, AutomaticRulesContinue = continueRules, EvidenceDirectory = folder });
                    QueueNotification(async () =>
                    {
                        if (reviewNotifier is null) return;
                        reviewDecisionId = await reviewNotifier.NotifyAsync(folder, latestReview, token);
                        File.WriteAllText(Path.Combine(folder, "notification.json"), JsonSerializer.Serialize(new { DecisionId = reviewDecisionId }));
                        Emit(new { Event = "review-notified", DecisionId = reviewDecisionId, MonitoringContinues = true });
                    }, token);
                };
            }

            async Task PollAnswerAsync(VisualAssistNotice notice, CancellationToken token)
            {
                // 決裁箱は回答を押し出さないため、申請した表示が続いている間だけ3秒おきに読む。
                while (reviewMonitor.IsHolding && askedUser?.Notice == notice)
                {
                    try
                    {
                        await Task.Delay(3000, token);
                        if (await reviewNotifier!.ReadAnswerAsync(notice, token) is not { } option) continue;
                        Interlocked.Exchange(ref answeredOption, option);
                        Emit(new { Event = "user-choice-answered", AtMs = clock.ElapsedMilliseconds, DecisionId = notice, Option = option });
                        return;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                    catch (Exception error)
                    {
                        Emit(new { Event = "user-choice-read-failed", AtMs = clock.ElapsedMilliseconds,
                            Detail = "決裁箱の回答を読めません。3秒後に読み直します。 " + error.Message });
                    }
                }
            }

            void QueueNotification(Func<Task> operation, CancellationToken token)
            {
                var previousNotification = notificationWork;
                notificationWork = Task.Run(async () =>
                {
                    await previousNotification;
                    try { await operation(); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        Emit(new { Event = "review-notification-failed", Detail = "確認通知に失敗しました。画面観測と回復監視は継続しています。 " + error.Message });
                    }
                });
            }

            // クリックの前に矢印を動かせずに失敗したか。その時はボタンを押していない。矢印は利用者か別の操作が
            // 握っているので、止まらずに手入力があった時と同じだけ待ち、次の観測でやり直す。
            bool PointerBlocked(VisualProgressChoice choice, GameInteractionDispatchReceipt dispatch)
            {
                if (userInput is null || dispatch.Status == GameInteractionDispatchStatus.Dispatched
                    || choice.Action is not (VisualProgressAction.Click or VisualProgressAction.Flick)
                    || !actions.LastDispatchPointerUnmoved) return false;
                userInput.PointerHeldByOther();
                Emit(new { Event = "progress-input-blocked", AtMs = clock.ElapsedMilliseconds, choice.RuleId, choice.Signature, choice.Point,
                    Foreground = ForegroundAppTracker.GetForegroundWindowTitle(), dispatch });
                return true;
            }

            bool UserIsActive()
            {
                if (userInput is null) return false;
                var snapshot = userInput.Snapshot();
                lock (userEventGate)
                {
                    if (clock.ElapsedMilliseconds - lastUserSample >= 1000)
                    {
                        lastUserSample = clock.ElapsedMilliseconds;
                        Emit(new { Event = "user-input-state", AtMs = clock.ElapsedMilliseconds,
                            snapshot.Paused, snapshot.HeldCount, snapshot.IdleMilliseconds, snapshot.UserEvents, snapshot.NanoEvents,
                            snapshot.LostReleases, snapshot.HeldCodes });
                    }
                    if (userPaused != snapshot.Paused)
                    {
                        userPaused = snapshot.Paused;
                        Emit(new { Event = snapshot.Paused ? "user-input-paused" : "user-input-resumed",
                            AtMs = clock.ElapsedMilliseconds, snapshot.HeldCount, snapshot.IdleMilliseconds,
                            snapshot.UserEvents, snapshot.NanoEvents, snapshot.LostReleases, snapshot.HeldCodes,
                            Detail = snapshot.Paused ? $"手入力を優先してBotの送出を一時停止しています。全解放後{UserInputPauseState.QuietMilliseconds / 1000}秒で再開します。"
                                : $"手入力がなくなって{UserInputPauseState.QuietMilliseconds / 1000}秒経過したためBotの送出を再開しました。" });
                    }
                }
                return snapshot.Paused;
            }

            bool TryForeground()
            {
                if (userInput is not null)
                    return WindowsTaskbarNanoWindowActivator.TryEnsureForeground(target, nano.Protocol, emitter, () => !UserIsActive());
                WindowsTaskbarNanoWindowActivator.EnsureForeground(target, nano.Protocol, emitter);
                return true;
            }

            async Task<object> RunRecoveryAsync(CancellationToken token)
            {
                using var frames = new WindowsWgcGameFrameSource(target.Window, sourceId + ":recovery", TimeSpan.FromSeconds(10), 250);
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                using var deathStop = CancellationTokenSource.CreateLinkedTokenSource(token);
                Task<string>? deathText = null;
                long? previousSample = null;
                VisualRecoveryAction? pendingAction = null;
                var recoveryNotices = new HashSet<string>(StringComparer.Ordinal);
                long detectedAt = 0;
                var first = true;
                try
                {
                    while (duration is null || clock.ElapsedMilliseconds < duration.Value)
                    {
                        if (!first && !await timer.WaitForNextTickAsync(token)) break;
                        first = false;
                        token.ThrowIfCancellationRequested();
                        var frame = await frames.CaptureAsync(token);
                        _ = UserIsActive();
                        var viewport = WindowsGameTargetLocator.CaptureClientBounds(target.Window);
                        var inhibitMatch = inhibit.FindAtWindowScale(frame, region, recoveryRecognizer!.HudScale(viewport));
                        var recoveryObservation = recoveryRecognizer.Observe(frame, viewport);
                        var incapacitated = false;
                        if (deathText?.IsCompleted == true)
                        {
                            var text = await deathText;
                            deathText = null;
                            if (recoveryRecognizer!.HasIncapacitatedDisplay(text))
                            {
                                incapacitated = true;
                                recoveryObservation = recoveryObservation with
                                {
                                    HudVisible = false,
                                    Problem = "対象の行動不能表示を確認しました。回復入力を終了します。"
                                };
                            }
                        }
                        if (!recoveryObservation.HudVisible && deathText is null
                            && !string.IsNullOrWhiteSpace(recoveryProfile!.IncapacitatedText)
                            && recoveryObservation.Problem is null)
                            deathText = Task.Run(async () => (await new WindowsGameOcrRecognizer().RecognizeAsync(frame, deathStop.Token)).Text, deathStop.Token);
                        var sampleAt = clock.ElapsedMilliseconds;
                        Emit(new
                        {
                            Event = "recovery-sample",
                            AtMs = sampleAt,
                            IntervalMs = previousSample is null ? (long?)null : sampleAt - previousSample,
                            FrameFreshnessMs = frame.FreshnessMs,
                            TargetIntervalMs = 250
                        });
                        previousSample = sampleAt;
                        if (recovery is not null && recoveryObservation is not null)
                        {
                            var previousRecoveryState = recovery.State;
                            var choice = recovery.Decide(DateTimeOffset.UtcNow, recoveryObservation, inhibitMatch.Matches, continueRules);
                            if (choice.Action != pendingAction)
                            {
                                pendingAction = choice.Action;
                                detectedAt = sampleAt;
                            }
                            if (!measureOnly && recovery.State != previousRecoveryState)
                            {
                                SaveRecoveryState();
                                File.WriteAllBytes(Path.Combine(evidenceDirectory, $"recovery-{events.Count}-state.png"),
                                    new WindowsGameFramePngEncoder().Encode(frame).Bytes.ToArray());
                            }
                            Emit(new
                            {
                                Event = "recovery",
                                AtMs = clock.ElapsedMilliseconds,
                                Observation = recoveryObservation,
                                Action = choice.Action.ToString(),
                                choice.Detail
                            });
                            if (choice.Action == VisualRecoveryAction.Review)
                            {
                                if (continueRules && !incapacitated)
                                {
                                    var beforeContinuation = recovery.State;
                                    recovery.ContinueAfterReview();
                                    if (recovery.State != beforeContinuation) SaveRecoveryState();
                                    if (recoveryNotices.Add(choice.Detail))
                                    {
                                        File.WriteAllBytes(Path.Combine(evidenceDirectory, "recovery-review.png"),
                                            new WindowsGameFramePngEncoder().Encode(frame).Bytes.ToArray());
                                        Emit(new { Event = "recovery-review-advisory", AtMs = clock.ElapsedMilliseconds,
                                            Detail = choice.Detail + " 通常の進行と、判定可能な回復処理は継続します。" });
                                    }
                                    continue;
                                }
                                File.WriteAllBytes(Path.Combine(evidenceDirectory, "recovery-review.png"),
                                    new WindowsGameFramePngEncoder().Encode(frame).Bytes.ToArray());
                                return new
                                {
                                    Mode = "key-assist",
                                    ProductHostEntry = true,
                                    AiCallCount = 0,
                                    NeedsReview = true,
                                    Recovery = recoveryObservation,
                                    Events = events,
                                    choice.Detail
                                };
                            }
                            if (choice.Action == VisualRecoveryAction.Wait)
                            {
                                continue;
                            }
                            if (choice.Action is VisualRecoveryAction.Food or VisualRecoveryAction.Potion or VisualRecoveryAction.Bandage)
                            {
                                if (measureOnly) continue;
                                if (UserIsActive()) continue;
                                if (!inputGate.Wait(0))
                                {
                                    Emit(new { Event = "recovery-input-busy", AtMs = clock.ElapsedMilliseconds,
                                        Action = choice.Action.ToString(), DetectedAtMs = detectedAt });
                                    continue;
                                }
                                try
                                {
                                    token.ThrowIfCancellationRequested();
                                    if (!TryForeground()) continue;
                                    var fresh = await frames.CaptureAsync(token);
                                    var freshObservation = recoveryRecognizer!.Observe(fresh, WindowsGameTargetLocator.CaptureClientBounds(target.Window));
                                    var freshChoice = recovery.Decide(DateTimeOffset.UtcNow, freshObservation, Inhibited(fresh), continueRules);
                                    if (freshChoice.Action != choice.Action)
                                    {
                                        Emit(new
                                        {
                                            Event = "recovery-input-withheld",
                                            AtMs = clock.ElapsedMilliseconds,
                                            RequestedAction = choice.Action.ToString(),
                                            Observation = freshObservation,
                                            Action = freshChoice.Action.ToString(),
                                            freshChoice.Detail
                                        });
                                        continue;
                                    }
                                    var beforeImagePath = Path.Combine(evidenceDirectory, $"recovery-{events.Count}-before.png");
                                    var bound = Observation(fresh);
                                    if (UserIsActive()) continue;
                                    var outputToken = choice.Action switch
                                    {
                                        VisualRecoveryAction.Food => recoveryProfile!.FoodKey,
                                        VisualRecoveryAction.Potion => recoveryProfile!.PotionKey,
                                        VisualRecoveryAction.Bandage => recoveryProfile!.BandageKey,
                                        _ => throw new InvalidOperationException("消費操作のキーがありません。"),
                                    };
                                    // USB送出とファイル保存の境界で終了しても、同じ消費を未実行扱いにしない。
                                    recovery.RecordAttempt(choice.Action, DateTimeOffset.UtcNow, freshObservation);
                                    SaveRecoveryState();
                                    var sent = actions.KeyTap(new GameInteractionKeyTapRequest(
                                        ContractSchemaVersions.Revision03, bound.ObservationId, fresh.Sequence,
                                        fresh.TransformRevision, fresh.SourceId, [outputToken]), bound);
                                    if (sent.Status != GameInteractionDispatchStatus.Dispatched)
                                        throw new InvalidOperationException($"回復キーのNano入力に失敗しました: {sent.FailureReason}");
                                    Emit(new
                                    {
                                        Event = "recovery-input",
                                        AtMs = clock.ElapsedMilliseconds,
                                        DetectedAtMs = detectedAt,
                                        DetectionToDispatchMs = clock.ElapsedMilliseconds - detectedAt,
                                        Action = choice.Action.ToString(),
                                        Token = outputToken,
                                        dispatch = sent
                                    });
                                    File.WriteAllBytes(beforeImagePath,
                                        new WindowsGameFramePngEncoder().Encode(fresh).Bytes.ToArray());
                                    schedule.RecordInput(clock.ElapsedMilliseconds);
                                }
                                finally { inputGate.Release(); }
                                continue;
                            }
                        }

                    }
                    return new
                    {
                        Mode = "key-assist",
                        ProductHostEntry = true,
                        AiCallCount = 0,
                        NeedsReview = progress.NeedsReview,
                        Events = events,
                        Detail = "指定時間が終了しました。"
                    };
                }
                finally
                {
                    await deathStop.CancelAsync();
                    if (deathText is not null)
                    {
                        try { await deathText; }
                        catch (OperationCanceledException) when (deathStop.IsCancellationRequested) { }
                    }
                }
            }

            void Emit(object entry)
            {
                lock (eventGate)
                {
                    events.Enqueue(entry);
                    var json = JsonSerializer.Serialize(entry);
                    File.AppendAllText(Path.Combine(evidenceDirectory, "events.jsonl"), json + "\n");
                    onEvent?.Invoke(JsonSerializer.SerializeToElement(entry));
                    Console.WriteLine(json);
                }
            }

            // 操作を送った時の領域の文字と画像を、実行の記録へ残す。
            void SaveRemembered(VisualProgressRemembered remembered, string ruleId, CapturedFrame source)
            {
                var folder = Path.Combine(evidenceDirectory, "remembered");
                Directory.CreateDirectory(folder);
                var name = FormattableString.Invariant($"{remembered.Name}-{clock.ElapsedMilliseconds:D9}");
                var x = (int)(remembered.Bounds[0] * source.Width);
                var y = (int)(remembered.Bounds[1] * source.Height);
                var width = (int)(remembered.Bounds[2] * source.Width);
                var height = (int)(remembered.Bounds[3] * source.Height);
                var pixels = source.Pixels ?? throw new InvalidOperationException("覚える領域の保存には画像が必要です。");
                var bytes = new byte[width * height * 4];
                for (var row = 0; row < height; row++)
                    pixels.Bgra8.Span.Slice((y + row) * pixels.Stride + x * 4, width * 4).CopyTo(bytes.AsSpan(row * width * 4));
                var image = Path.Combine(folder, name + ".png");
                File.WriteAllBytes(image, new WindowsGameFramePngEncoder().Encode(source with { Width = width, Height = height,
                    Pixels = new FramePixels(bytes, width * 4), Crop = null }).Bytes.ToArray());
                // 後の規則が、覚えた名前と画面の表示を照らし合わせる。Botを始め直しても忘れないよう、保存する。
                progressRecognizer!.SetRemembered(remembered.Name, remembered.Lines);
                rememberedLines[remembered.Name] = remembered.Lines;
                var temporaryRemembered = rememberedPath + ".tmp";
                File.WriteAllText(temporaryRemembered, JsonSerializer.Serialize(rememberedLines));
                File.Move(temporaryRemembered, rememberedPath, overwrite: true);
                var entry = new { Event = "remembered", AtMs = clock.ElapsedMilliseconds, remembered.Name, RuleId = ruleId,
                    remembered.Lines, Image = image, RecordedAt = DateTimeOffset.Now };
                File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(entry));
                Emit(entry);
            }

            bool Inhibited(CapturedFrame candidate) => inhibit.FindAtWindowScale(candidate, region,
                recoveryRecognizer is null ? 1 : recoveryRecognizer.HudScale(WindowsGameTargetLocator.CaptureClientBounds(target.Window))).Matches;

            void SaveRecoveryState()
            {
                var temporary = recoveryStatePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(recovery!.State));
                File.Move(temporary, recoveryStatePath, overwrite: true);
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

    private static async Task<ObservedScene> Scene(CapturedFrame frame, ObservationResult observation, CancellationToken token)
    {
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame, token);
        return LocalTargetTrackingSceneBuilder.Build(observation, frame,
            WindowsGameOcrSpanBuilder.Build(ocr, frame.Width, frame.Height), []);
    }
}
