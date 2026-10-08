using System.IO;
using System.Text;
using System.Text.Json;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Host;

public sealed record VisualProgressText(string Text, double[] Bounds, double[][]? ChoiceBounds = null);
public sealed record VisualProgressRule(string Id, VisualProgressText[] When,
    string? Key = null, VisualProgressText? Click = null, bool Timed = false, int Priority = 0,
    string? Image = null, double[]? ImageBounds = null, int ImageClientWidth = 0, bool ImageSilhouette = false,
    bool ImageRotates = false, bool WaitForChange = false);
public sealed record VisualProgressProfile(int SchemaVersion, VisualProgressRule[] Rules,
    VisualProgressText[] ReviewWhen, int ResultTimeoutMs = 5000, int UnknownTimeoutMs = 5000)
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
            if (string.IsNullOrWhiteSpace(rule.Id) || rule.When is not { Length: > 0 }
                || (rule.Key is null ? 0 : 1) + (rule.Click is null ? 0 : 1) + (rule.WaitForChange ? 1 : 0) != 1)
                throw new InvalidDataException("進行規則には条件と、キー・クリック・待機のいずれか一つが必要です。");
            if (rule.Key is not null) OpenLogicool.Input.OutputTokens.Parse(rule.Key);
            if (rule.Image is not null && (rule.ImageBounds is null || rule.ImageClientWidth <= 0))
                throw new InvalidDataException("進行規則の画像には探索範囲と基準描画幅が必要です。");
            if (rule.ImageRotates && (rule.Image is null || rule.ImageSilhouette))
                throw new InvalidDataException("回転する印には単色画像を指定します。");
        }
        foreach (var bounds in value.Rules.SelectMany(rule => rule.When.Concat(rule.Click is null ? [] : new[] { rule.Click }))
            .Concat(value.ReviewWhen).Select(text => text.Bounds)
            .Concat(value.ReviewWhen.SelectMany(text => text.ChoiceBounds ?? []))
            .Concat(value.Rules.Where(rule => rule.Image is not null).Select(rule => rule.ImageBounds!)))
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
    VisualProgressOption[]? Options = null);

/// <summary>ゲーム固有の操作条件は設定に置き、文字・配置・画像を照合する。</summary>
public sealed class VisualProgressRecognizer(VisualProgressProfile profile)
{
    private readonly Dictionary<string, VisualKeyTemplate> templates = profile.Rules.Where(rule => rule.Image is not null && !rule.ImageRotates)
        .ToDictionary(rule => rule.Id, rule => VisualKeyTemplate.Load(rule.Image!, silhouette: rule.ImageSilhouette));
    private readonly Dictionary<string, VisualRotatingTemplate> rotatingTemplates = profile.Rules.Where(rule => rule.ImageRotates)
        .ToDictionary(rule => rule.Id, rule => new VisualRotatingTemplate(rule.Image!));

    public VisualProgressChoice Recognize(WindowsGameOcrResult ocr, int width, int height, FrameRect viewport,
        CapturedFrame? frame = null)
    {
        string Read(VisualProgressText area) => Normalize(string.Concat(ocr.Words
            .Where(word => Inside(word, area.Bounds, viewport))
            .Select(word => word.Text)));
        foreach (var review in profile.ReviewWhen)
            if (Matches(Read(review), review.Text))
                return new(VisualProgressAction.Review, Detail: $"利用者の判断が必要な表示: {review.Text}",
                    Options: review.ChoiceBounds?.Select((bounds, index) =>
                    {
                        var label = Read(new("", bounds));
                        return new VisualProgressOption($"choice-{index + 1}",
                            $"選択肢{index + 1}: " + (label.Length == 0 ? "文字を読めません（添付画像を確認）" : label));
                    }).ToArray());
        var candidates = new List<VisualProgressChoice>();
        foreach (var rule in profile.Rules)
        {
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
            }
            var texts = rule.When.Select(Read).ToArray();
            if (!rule.When.Select((condition, i) => Matches(texts[i], condition.Text)).All(x => x)) continue;
            double[]? point = null;
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
                    return new(VisualProgressAction.Review, Detail: $"クリック先が複数あります: {rule.Id}");
            }
            candidates.Add(new(rule.WaitForChange ? VisualProgressAction.Wait
                : rule.Click is null ? VisualProgressAction.Key : VisualProgressAction.Click,
                rule.Id, rule.Id + ":" + string.Join("|", rule.When.Select((condition, i) =>
                    string.IsNullOrWhiteSpace(condition.Text) ? texts[i] : Normalize(condition.Text))), rule.Key, point));
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

    private static bool Inside(WindowsGameOcrWord word, double[] area, FrameRect viewport)
    {
        var x = (word.X + word.Width / 2 - viewport.X) / viewport.Width;
        var y = (word.Y + word.Height / 2 - viewport.Y) / viewport.Height;
        return x >= area[0] && x <= area[0] + area[2] && y >= area[1] && y <= area[1] + area[3];
    }

    internal static string Normalize(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC)
        .Where(c => char.IsLetterOrDigit(c))).ToUpperInvariant();

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
    private long sentAt;
    private string? stableSignature;
    private long stableAt;
    private long? unknownAt;

    public VisualProgressChoice Decide(long now, VisualProgressChoice candidate, bool inhibited,
        bool hudVisible, bool due)
    {
        if (inhibited) { pending = null; unknownAt = null; stableSignature = null; return new(VisualProgressAction.Wait); }
        if (candidate.Action == VisualProgressAction.Review) return candidate;
        if (pending is not null)
        {
            if (candidate.Signature == pending.Signature || (candidate.Action == VisualProgressAction.Normal && !hudVisible))
                return now - sentAt >= profile.ResultTimeoutMs
                    ? new(VisualProgressAction.Review, Detail: $"{pending.RuleId} の操作結果を確認できません。再送していません。")
                    : new(VisualProgressAction.Wait);
            if (stableSignature != candidate.Signature) { stableSignature = candidate.Signature; stableAt = now; }
            if (now - stableAt < 600) return new(VisualProgressAction.Wait);
            pending = null;
        }
        if (candidate.Action == VisualProgressAction.Normal)
        {
            stableSignature = null;
            if (hudVisible) { unknownAt = null; return candidate; }
            unknownAt ??= now;
            return now - unknownAt >= profile.UnknownTimeoutMs
                ? new(VisualProgressAction.Review, Detail: "確認済みの画面規則とHUDに一致しません。")
                : new(VisualProgressAction.Wait);
        }
        unknownAt = null;
        if (stableSignature != candidate.Signature) { stableSignature = candidate.Signature; stableAt = now; }
        return now - stableAt >= 600 && due ? candidate : new(VisualProgressAction.Wait);
    }

    public void RecordInput(long now, VisualProgressChoice choice) { pending = choice; sentAt = now; }
}
