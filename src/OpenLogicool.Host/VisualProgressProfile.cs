using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Host;

public sealed record VisualProgressText(string Text, double[] Bounds, double[][]? ChoiceBounds = null);
public sealed record VisualProgressRule(string Id, VisualProgressText[] When,
    string? Key = null, VisualProgressText? Click = null, bool Timed = false, int Priority = 0,
    string? Image = null, double[]? ImageBounds = null, int ImageClientWidth = 0, bool ImageSilhouette = false,
    bool ImageRotates = false, bool WaitForChange = false, double[][]? ImageStableRegions = null, bool Immediate = false,
    int MinimumVisibleMs = 600, bool ClickImage = false, double[]? FilledQuantitiesBounds = null,
    double[]? SingleTextRunBounds = null, int[]? ImageForegroundRgb = null, bool AllowWhileInhibited = false,
    int RepeatIntervalMs = 0);
public sealed record VisualProgressProfile(int SchemaVersion, VisualProgressRule[] Rules,
    VisualProgressText[] ReviewWhen, int ResultTimeoutMs = 5000, int UnknownTimeoutMs = 10000)
{
    public static VisualProgressProfile Load(string path)
    {
        var value = JsonSerializer.Deserialize<VisualProgressProfile>(File.ReadAllText(path))
            ?? throw new InvalidDataException("進行設定が空です。");
        if (value.SchemaVersion != 1 || value.Rules is null || value.ReviewWhen is null
            || value.ResultTimeoutMs <= 0 || value.UnknownTimeoutMs <= 0
            || value.Rules.Select(rule => rule.Id).Distinct().Count() != value.Rules.Length)
            throw new InvalidDataException("進行設定の形式が不正です。");
        foreach (var rule in value.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || rule.When is null || (rule.When.Length == 0 && rule.Image is null)
                || (rule.Key is null ? 0 : 1) + (rule.Click is null ? 0 : 1) + (rule.WaitForChange ? 1 : 0) + (rule.ClickImage ? 1 : 0) != 1)
                throw new InvalidDataException("進行規則には条件と、キー・クリック・待機のいずれか一つが必要です。");
            if (rule.MinimumVisibleMs < 0)
                throw new InvalidDataException("表示待ち時間が不正です。");
            if (rule.Key is not null) OpenLogicool.Input.OutputTokens.Parse(rule.Key);
            if (rule.Image is not null && (rule.ImageBounds is null || rule.ImageClientWidth <= 0))
                throw new InvalidDataException("進行規則の画像には探索範囲と基準描画幅が必要です。");
            if (rule.ClickImage && rule.Image is null)
                throw new InvalidDataException("画像のクリック先には参照画像が必要です。");
            if (rule.ImageRotates && (rule.Image is null || rule.ImageSilhouette))
                throw new InvalidDataException("回転する印には単色画像を指定します。");
            if (rule.ImageForegroundRgb is not null && (!rule.ImageRotates
                || rule.ImageForegroundRgb.Length != 3 || rule.ImageForegroundRgb.Any(channel => channel is < 0 or > 255)))
                throw new InvalidDataException("送り印の色は回転画像にRGBの3成分で指定します。");
            if (rule.ImageStableRegions is not null && (rule.Image is null || rule.ImageRotates || rule.ImageStableRegions.Length == 0))
                throw new InvalidDataException("固定部分の照合には画像と一つ以上の領域が必要です。");
            if (rule.Immediate && (rule.Timed || rule.Key is null || rule.Image is not null && rule.When.Length != 0))
                throw new InvalidDataException("即時入力には時間待ちのない画像または文字のキー規則を指定します。");
            if (rule.RepeatIntervalMs < 0 || rule.RepeatIntervalMs > 0 && (!rule.Immediate || rule.Image is null))
                throw new InvalidDataException("反復間隔は即時画像キー規則に正の時間で指定します。");
        }
        foreach (var bounds in value.Rules.SelectMany(rule => rule.When.Concat(rule.Click is null ? [] : new[] { rule.Click }))
            .Concat(value.ReviewWhen).Select(text => text.Bounds)
            .Concat(value.ReviewWhen.SelectMany(text => text.ChoiceBounds ?? []))
            .Concat(value.Rules.Where(rule => rule.Image is not null).Select(rule => rule.ImageBounds!))
            .Concat(value.Rules.Where(rule => rule.FilledQuantitiesBounds is not null).Select(rule => rule.FilledQuantitiesBounds!))
            .Concat(value.Rules.Where(rule => rule.SingleTextRunBounds is not null).Select(rule => rule.SingleTextRunBounds!))
            .Concat(value.Rules.SelectMany(rule => rule.ImageStableRegions ?? [])))
            if (bounds is not { Length: 4 } || bounds.Any(x => !double.IsFinite(x) || x < 0 || x > 1)
                || bounds[2] <= 0 || bounds[3] <= 0
                || bounds[0] + bounds[2] > 1 || bounds[1] + bounds[3] > 1)
                throw new InvalidDataException("進行規則の描画領域内座標が不正です。");
        if (value.ReviewWhen.Any(text => text.ChoiceBounds is not null && text.ChoiceBounds.Length is < 2 or > 5))
            throw new InvalidDataException("確認画面の選択肢は2〜5領域で指定します。");
        return value with { Rules = value.Rules.Select(rule => rule.Image is null ? rule
            : rule with { Image = Path.GetFullPath(rule.Image, Path.GetDirectoryName(Path.GetFullPath(path))!) }).ToArray() };
    }
}

public enum VisualProgressAction { Wait, Normal, Key, Click, Review }
public sealed record VisualProgressOption(string Id, string Label);
public sealed record VisualProgressChoice(VisualProgressAction Action, string? RuleId = null,
    string? Signature = null, string? Key = null, double[]? Point = null, string? Detail = null,
    VisualProgressOption[]? Options = null, bool Immediate = false, bool AllowWhileInhibited = false,
    int RepeatIntervalMs = 0);

/// <summary>ゲーム固有の操作条件は設定に置き、文字・配置・画像を照合する。</summary>
public sealed class VisualProgressRecognizer(VisualProgressProfile profile)
{
    private readonly Dictionary<string, VisualKeyTemplate> templates = profile.Rules.Where(rule => rule.Image is not null && !rule.ImageRotates)
        .ToDictionary(rule => rule.Id, rule => VisualKeyTemplate.Load(rule.Image!, silhouette: rule.ImageSilhouette,
            stableRegions: rule.ImageStableRegions));
    private readonly Dictionary<string, VisualRotatingTemplate> rotatingTemplates = profile.Rules.Where(rule => rule.ImageRotates)
        .ToDictionary(rule => rule.Id, rule => new VisualRotatingTemplate(rule.Image!, rule.ImageForegroundRgb));

    public async ValueTask<WindowsGameOcrResult> ReadOcrAsync(CapturedFrame frame, FrameRect viewport, CancellationToken token = default)
    {
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame, token);
        foreach (var rule in profile.Rules.Where(rule => rule.FilledQuantitiesBounds is not null))
        {
            if (!rule.When.All(condition => Matches(Normalize(string.Concat(ocr.Words
                .Where(word => Inside(word, condition.Bounds, viewport)).Select(word => word.Text))), condition.Text))) continue;
            var area = rule.FilledQuantitiesBounds!;
            var x = (int)(viewport.X + area[0] * viewport.Width);
            var y = (int)(viewport.Y + area[1] * viewport.Height);
            var width = (int)(area[2] * viewport.Width);
            var height = (int)(area[3] * viewport.Height);
            var pixels = frame.Pixels ?? throw new InvalidOperationException("数量OCRには画像が必要です。");
            var bytes = new byte[width * height * 4];
            for (var row = 0; row < height; row++)
                pixels.Bgra8.Span.Slice((y + row) * pixels.Stride + x * 4, width * 4).CopyTo(bytes.AsSpan(row * width * 4));
            var cropped = frame with { Width = width, Height = height, Pixels = new FramePixels(bytes, width * 4), Crop = null };
            var quantities = await new WindowsGameOcrRecognizer(4).RecognizeAsync(cropped, token);
            ocr = ocr with { Text = ocr.Text + " " + quantities.Text, Words = ocr.Words
                .Where(word => !Inside(word, area, viewport))
                .Concat(quantities.Words.Select(word => word with { X = word.X + x, Y = word.Y + y })).ToArray() };
        }
        return ocr;
    }

    public VisualProgressChoice Recognize(WindowsGameOcrResult ocr, int width, int height, FrameRect viewport,
        CapturedFrame? frame = null, bool inhibited = false)
    {
        if (!inhibited && frame is not null && RecognizeImmediateImage(frame, viewport) is { } immediate) return immediate;
        string Read(VisualProgressText area) => Normalize(string.Concat(ocr.Words
            .Where(word => Inside(word, area.Bounds, viewport))
            .Select(word => word.Text)));
        foreach (var review in inhibited ? [] : profile.ReviewWhen)
            if (Matches(Read(review), review.Text))
                return new(VisualProgressAction.Review, Detail: $"利用者の判断が必要な表示: {review.Text}",
                    Options: review.ChoiceBounds?.Select((bounds, index) =>
                    {
                        var label = Read(new("", bounds));
                        return new VisualProgressOption($"choice-{index + 1}",
                            $"選択肢{index + 1}: " + (label.Length == 0 ? "文字を読めません（添付画像を確認）" : label));
                    }).ToArray());
        var candidates = new List<VisualProgressChoice>();
        foreach (var rule in profile.Rules.Where(rule => rule.RepeatIntervalMs == 0))
        {
            if (inhibited && !rule.AllowWhileInhibited) continue;
            double[]? point = null;
            if (rule.Image is not null)
            {
                if (frame is null) continue;
                var area = rule.ImageBounds!;
                double[] mapped = [(viewport.X + area[0] * viewport.Width) / width,
                    (viewport.Y + area[1] * viewport.Height) / height,
                    area[2] * viewport.Width / width, area[3] * viewport.Height / height];
                var scale = viewport.Width / rule.ImageClientWidth;
                var match = rule.ImageRotates ? rotatingTemplates[rule.Id].Find(frame, mapped, scale)
                    : templates[rule.Id].FindAtWindowScale(frame, mapped, scale);
                if (!match.Matches) continue;
                if (rule.ClickImage) point = [match.Bounds[0] + match.Bounds[2] / 2, match.Bounds[1] + match.Bounds[3] / 2];
            }
            var texts = rule.When.Select(Read).ToArray();
            if (!rule.When.Select((condition, i) => Matches(texts[i], condition.Text)).All(x => x)) continue;
            if (rule.SingleTextRunBounds is { } labelBounds
                && !HasSingleTextRun(ocr, labelBounds, viewport, width, height)) continue;
            if (rule.FilledQuantitiesBounds is { } quantityBounds)
            {
                var quantities = string.Join(" ", ocr.Words.Where(word => Inside(word, quantityBounds, viewport)).Select(word => word.Text));
                if (!QuantitiesFilled(quantities)) continue;
            }
            if (rule.Click is not null)
            {
                var spans = WindowsGameOcrSpanBuilder.Build(ocr, width, height)
                    .Where(span => Matches(Normalize(span.Text), rule.Click.Text))
                    .Where(span => Inside(new WindowsGameOcrWord(span.Text,
                        span.EvidenceRegion.NormalizedBounds[0] * width, span.EvidenceRegion.NormalizedBounds[1] * height,
                        span.EvidenceRegion.NormalizedBounds[2] * width, span.EvidenceRegion.NormalizedBounds[3] * height),
                        rule.Click.Bounds, viewport))
                    .OrderBy(span => Normalize(span.Text).Length).ToArray();
                if (spans.Length == 0) continue;
                var bounds = spans[0].EvidenceRegion.NormalizedBounds;
                point = [bounds[0] + bounds[2] / 2, bounds[1] + bounds[3] / 2];
                // 離れた同名ボタンが複数ある場合は選ばない。
                if (spans.Any(span => Math.Abs(span.EvidenceRegion.NormalizedBounds[0] - bounds[0]) > bounds[2]
                    || Math.Abs(span.EvidenceRegion.NormalizedBounds[1] - bounds[1]) > bounds[3]))
                {
                    candidates.Add(new(VisualProgressAction.Review, rule.Id, Detail: $"クリック先が複数あります: {rule.Id}"));
                    continue;
                }
            }
            candidates.Add(new(rule.WaitForChange ? VisualProgressAction.Wait
                : rule.Click is null && !rule.ClickImage ? VisualProgressAction.Key : VisualProgressAction.Click,
                rule.Id, rule.Id + ":" + string.Join("|", rule.When.Select((condition, i) =>
                    string.IsNullOrWhiteSpace(condition.Text) ? texts[i] : Normalize(condition.Text))), rule.Key, point,
                Immediate: rule.Immediate, AllowWhileInhibited: rule.AllowWhileInhibited));
        }
        var priority = candidates.Count == 0 ? 0 : candidates.Max(c => profile.Rules.Single(r => r.Id == c.RuleId).Priority);
        var preferred = candidates.Where(c => profile.Rules.Single(r => r.Id == c.RuleId).Priority == priority).ToArray();
        return preferred.Length switch
        {
            0 => new(VisualProgressAction.Normal),
            1 => preferred[0],
            _ => new(VisualProgressAction.Review, Detail: "複数の進行規則が同時に一致しました。"),
        };
    }

    public VisualProgressChoice? RecognizeImmediateImage(CapturedFrame frame, FrameRect viewport) =>
        RecognizeImageKey(frame, viewport, profile.Rules.Where(rule => rule.Immediate && rule.Image is not null && rule.RepeatIntervalMs == 0));

    public VisualProgressChoice? RecognizeRepeatingImage(CapturedFrame frame, FrameRect viewport,
        Func<VisualProgressRule, bool> isDue) =>
        RecognizeImageKey(frame, viewport, profile.Rules.Where(rule => rule.RepeatIntervalMs > 0 && isDue(rule)));

    private VisualProgressChoice? RecognizeImageKey(CapturedFrame frame, FrameRect viewport, IEnumerable<VisualProgressRule> rules)
    {
        foreach (var rule in rules.OrderByDescending(rule => rule.Priority))
        {
            var area = rule.ImageBounds!;
            double[] mapped = [(viewport.X + area[0] * viewport.Width) / frame.Width,
                (viewport.Y + area[1] * viewport.Height) / frame.Height,
                area[2] * viewport.Width / frame.Width, area[3] * viewport.Height / frame.Height];
            if (templates[rule.Id].FindAtWindowScale(frame, mapped, viewport.Width / rule.ImageClientWidth).Matches)
                return new(VisualProgressAction.Key, rule.Id, rule.Id, rule.Key, Immediate: true,
                    RepeatIntervalMs: rule.RepeatIntervalMs);
        }
        return null;
    }

    private static bool Inside(WindowsGameOcrWord word, double[] area, FrameRect viewport)
    {
        var x = (word.X + word.Width / 2 - viewport.X) / viewport.Width;
        var y = (word.Y + word.Height / 2 - viewport.Y) / viewport.Height;
        return x >= area[0] && x <= area[0] + area[2] && y >= area[1] && y <= area[1] + area[3];
    }

    internal static string Normalize(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC)
        .Where(c => char.IsLetterOrDigit(c))).ToUpperInvariant();

    internal static bool QuantitiesFilled(string text)
    {
        var quantities = Regex.Matches(text.Normalize(NormalizationForm.FormKC), @"(?<!\d)(\d+)\s*/\s*(\d+)(?!\d)");
        return quantities.Count > 0 && quantities.All(match =>
            int.TryParse(match.Groups[1].Value, out var supplied) && int.TryParse(match.Groups[2].Value, out var required)
            && required > 0 && supplied == required);
    }

    private static bool HasSingleTextRun(WindowsGameOcrResult ocr, double[] bounds, FrameRect viewport, int width, int height)
    {
        var labels = new WindowsGameOcrResult("", ocr.RecognizerLanguage, 0,
            ocr.Words.Where(word => Inside(word, bounds, viewport)).ToArray());
        var spans = WindowsGameOcrSpanBuilder.Canonicalize(WindowsGameOcrSpanBuilder.Build(labels, width, height));
        var count = 0;
        var right = double.NegativeInfinity;
        // 長い一つのラベルから作られた重複spanを、同じ横方向の文字列として数える。
        foreach (var span in spans.OrderBy(span => span.EvidenceRegion.NormalizedBounds[0]))
        {
            var rect = span.EvidenceRegion.NormalizedBounds;
            if (rect[0] > right) count++;
            right = Math.Max(right, rect[0] + rect[2]);
        }
        return count == 1;
    }

    internal static bool Matches(string observed, string expected)
    {
        expected = Normalize(expected);
        if (expected.Length == 0) return observed.Length >= 4;
        if (observed.Contains(expected, StringComparison.Ordinal)) return true;
        // 日本語OCRの空白・全半角と、長いラベルの一文字の読違いを吸収する。
        if (expected.Length < 4) return false;
        return Enumerable.Range(0, Math.Max(0, observed.Length - expected.Length + 1))
            .Any(start => observed.AsSpan(start, expected.Length).ToArray()
                .Where((c, i) => c != expected[i]).Count() <= 1);
    }
}

/// <summary>同じ画面への再送をせず、操作結果待ち・未知画面を明示する。</summary>
public sealed class VisualProgressSchedule(VisualProgressProfile profile)
{
    private VisualProgressChoice? pending;
    private string? stableSignature;
    private long stableAt;
    private long? unknownAt;
    private int unresolvedObservations;
    private string? waitingSignature;
    private string? consumedImmediate;
    private long? immediateMissingAt;
    private bool wasInhibited;
    private readonly Dictionary<string, long> repeatedAt = [];

    public bool RepeatIsDue(long now, VisualProgressRule rule) =>
        !repeatedAt.TryGetValue(rule.Id, out var last) || now - last >= rule.RepeatIntervalMs;

    public VisualProgressChoice Decide(long now, VisualProgressChoice candidate, bool inhibited,
        bool hudVisible, bool due, bool sceneChanged = false)
    {
        if (inhibited && !candidate.AllowWhileInhibited)
        {
            pending = null; stableSignature = null; waitingSignature = null;
            consumedImmediate = null; immediateMissingAt = null;
            if (!wasInhibited) ResetUnresolved();
            wasInhibited = true;
            return ObserveUnresolved(now, sceneChanged, profile.UnknownTimeoutMs,
                "停止表示のまま画面の変化が止まっています。停止表示を優先し、追加入力はしていません。");
        }
        if (wasInhibited) { wasInhibited = false; ResetUnresolved(); }
        if (candidate.Immediate)
        {
            immediateMissingAt = null;
            ResetUnresolved();
            if (candidate.Signature == consumedImmediate) return new(VisualProgressAction.Wait);
            pending = null;
            return candidate;
        }
        if (consumedImmediate is not null)
        {
            immediateMissingAt ??= now;
            if (now - immediateMissingAt >= 600) { consumedImmediate = null; immediateMissingAt = null; }
        }
        if (candidate.Action != VisualProgressAction.Wait) waitingSignature = null;
        if (candidate.Action == VisualProgressAction.Review) return candidate;
        if (pending is not null)
        {
            if (candidate.Signature == pending.Signature || (candidate.Action == VisualProgressAction.Normal && !hudVisible))
                return ObserveUnresolved(now, sceneChanged,
                    candidate.Action == VisualProgressAction.Normal ? Math.Max(profile.ResultTimeoutMs, profile.UnknownTimeoutMs) : profile.ResultTimeoutMs,
                    $"{pending.RuleId} の操作後、複数回観測して画面の変化が止まったまま結果を確認できません。再送していません。");
            if (stableSignature != candidate.Signature) { stableSignature = candidate.Signature; stableAt = now; }
            if (now - stableAt < 600) return new(VisualProgressAction.Wait);
            pending = null;
            ResetUnresolved();
        }
        if (candidate.Action == VisualProgressAction.Normal)
        {
            stableSignature = null;
            if (hudVisible) { ResetUnresolved(); return candidate; }
            return ObserveUnresolved(now, sceneChanged, profile.UnknownTimeoutMs,
                "複数回観測して画面の変化が止まりましたが、確認済みの画面規則とHUDに一致しません。");
        }
        if (candidate.Action == VisualProgressAction.Wait)
        {
            if (waitingSignature != candidate.Signature) { waitingSignature = candidate.Signature; ResetUnresolved(); }
            return ObserveUnresolved(now, sceneChanged, profile.UnknownTimeoutMs,
                $"{candidate.RuleId} の待機中、複数回観測して画面の変化が止まったままです。追加入力はしていません。");
        }
        ResetUnresolved();
        if (stableSignature != candidate.Signature) { stableSignature = candidate.Signature; stableAt = now; }
        var minimumVisible = profile.Rules.SingleOrDefault(rule => rule.Id == candidate.RuleId)?.MinimumVisibleMs ?? 600;
        return now - stableAt >= minimumVisible && due ? candidate : new(VisualProgressAction.Wait);
    }

    public void RecordInput(long now, VisualProgressChoice choice)
    {
        if (choice.RepeatIntervalMs > 0) { repeatedAt[choice.RuleId!] = now; return; }
        if (choice.Immediate) { consumedImmediate = choice.Signature; pending = null; ResetUnresolved(); return; }
        pending = choice; unknownAt = now; unresolvedObservations = 0;
    }

    private VisualProgressChoice ObserveUnresolved(long now, bool sceneChanged, int waitMs, string detail)
    {
        if (unknownAt is null || sceneChanged) { unknownAt = now; unresolvedObservations = 0; }
        unresolvedObservations++;
        return unresolvedObservations >= 3 && now - unknownAt >= waitMs
            ? new(VisualProgressAction.Review, Detail: detail)
            : new(VisualProgressAction.Wait);
    }

    private void ResetUnresolved() { unknownAt = null; unresolvedObservations = 0; }
}
