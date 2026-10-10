using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Host;

public sealed record VisualProgressText(string Text, double[] Bounds, double[][]? ChoiceBounds = null,
    bool AskUserImmediately = false, VisualProgressText? Confirm = null, string? ConfirmKey = null,
    double[]? ChoiceBand = null, VisualProgressText? Recommended = null, bool Exact = false);
/// <summary>画面の数値の条件。OutOf を指定した表示は「現在値/上限」の形で、上限まで読めた時だけ現在値を使う。</summary>
public sealed record VisualProgressNumber(double[] Bounds, int? AtMost = null, int? Exactly = null, int? OutOf = null);
public sealed record VisualProgressRule(string Id, VisualProgressText[] When,
    string? Key = null, VisualProgressText? Click = null, bool Timed = false, int Priority = 0,
    string? Image = null, double[]? ImageBounds = null, int ImageClientWidth = 0, bool ImageSilhouette = false,
    bool ImageRotates = false, bool WaitForChange = false, double[][]? ImageStableRegions = null, bool Immediate = false,
    int MinimumVisibleMs = 600, bool ClickImage = false, double[]? FilledQuantitiesBounds = null,
    double[]? SingleTextRunBounds = null, int[]? ImageForegroundRgb = null, bool AllowWhileInhibited = false,
    int RepeatIntervalMs = 0, string? AfterClickKey = null, double[]? ClickImagePoint = null, int ImageSearchStep = 1,
    bool ImageClipsAtBottom = false, bool RepeatAfterChange = false, VisualProgressNumber? Number = null,
    string? Stage = null, string? NextStage = null, string[]? ThenKeys = null);
public sealed record VisualProgressProfile(int SchemaVersion, VisualProgressRule[] Rules,
    VisualProgressText[] ReviewWhen, int ResultTimeoutMs = 5000, int UnknownTimeoutMs = 10000,
    double[][]? WhiteTextBounds = null)
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
            if (string.IsNullOrWhiteSpace(rule.Id) || rule.When is null || (rule.When.Length == 0 && rule.Image is null && rule.Number is null)
                || (rule.Key is null ? 0 : 1) + (rule.Click is null ? 0 : 1) + (rule.WaitForChange ? 1 : 0) + (rule.ClickImage ? 1 : 0) != 1)
                throw new InvalidDataException("進行規則には条件と、キー・クリック・待機のいずれか一つが必要です。");
            if (rule.MinimumVisibleMs < 0)
                throw new InvalidDataException("表示待ち時間が不正です。");
            if (rule.Key is not null) OpenLogicool.Input.OutputTokens.Parse(rule.Key);
            if (rule.AfterClickKey is not null)
            {
                if (rule.Click is null && !rule.ClickImage)
                    throw new InvalidDataException("クリック後のキーにはクリック規則を指定します。");
                OpenLogicool.Input.OutputTokens.Parse(rule.AfterClickKey);
            }
            if (rule.Image is not null && (rule.ImageBounds is null || rule.ImageClientWidth <= 0))
                throw new InvalidDataException("進行規則の画像には探索範囲と基準描画幅が必要です。");
            if (rule.ClickImage && rule.Image is null)
                throw new InvalidDataException("画像のクリック先には参照画像が必要です。");
            if (rule.ClickImagePoint is not null && (!rule.ClickImage || rule.ClickImagePoint.Length != 2
                || rule.ClickImagePoint.Any(value => !double.IsFinite(value) || value < 0 || value > 1)))
                throw new InvalidDataException("画像内のクリック位置は、画像のクリック規則に0〜1の2値で指定します。");
            // 粗い探索の後は周囲2pxを正確に照合する。刻みはその範囲に収まる3までとする。
            if (rule.ImageSearchStep is < 1 or > 3 || rule.ImageSearchStep > 1 && rule.ImageStableRegions is null)
                throw new InvalidDataException("画像探索の刻みは、固定部分の照合に1〜3で指定します。");
            if (rule.RepeatAfterChange && (rule.Key is null || rule.Immediate || rule.Timed || rule.RepeatIntervalMs > 0))
                throw new InvalidDataException("画面が進むたびに送り直す指定は、時間待ちと反復のないキー規則に指定します。");
            if (rule.ImageClipsAtBottom && rule.ImageStableRegions is not { Length: > 2 })
                throw new InvalidDataException("下端で切れる画像には、固定部分の領域を3つ以上指定します。");
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
            if (rule.Number is { } number && ((number.AtMost is null) == (number.Exactly is null) || number.OutOf <= 0
                || rule.Immediate || rule.RepeatIntervalMs > 0))
                throw new InvalidDataException("数値の条件は、上限か一致のどちらか一つを、即時・反復でない規則に指定します。");
            if (rule.Stage == "" || rule.NextStage == "" || rule.NextStage is not null && rule.WaitForChange
                || rule.Stage is not null && (rule.Immediate || rule.RepeatIntervalMs > 0))
                throw new InvalidDataException("段階は、即時・反復でない規則に名前で指定します。次の段階は操作を送る規則に指定します。");
            if (rule.Number is not null && rule.When.Length == 0 && rule.Key is null && !rule.ClickImage)
                throw new InvalidDataException("文字の条件を持たない数値の規則には、キーまたは画像のクリックを指定します。");
            if (rule.When.Any(text => text.Exact && string.IsNullOrWhiteSpace(text.Text)))
                throw new InvalidDataException("完全一致の条件には文字を指定します。");
            if (rule.ThenKeys is not null)
            {
                if (rule.ThenKeys.Length == 0 || rule.WaitForChange || rule.Immediate || rule.RepeatAfterChange)
                    throw new InvalidDataException("続けて送るキーは、即時・反復でないキーまたはクリックの規則に一つ以上指定します。");
                foreach (var key in rule.ThenKeys) OpenLogicool.Input.OutputTokens.Parse(key);
            }
        }
        foreach (var bounds in value.Rules.SelectMany(rule => rule.When.Concat(rule.Click is null ? [] : new[] { rule.Click }))
            .Concat(value.ReviewWhen).Concat(value.ReviewWhen.Where(text => text.Confirm is not null).Select(text => text.Confirm!))
            .Concat(value.ReviewWhen.Where(text => text.Recommended is not null).Select(text => text.Recommended!))
            .Select(text => text.Bounds)
            .Concat(value.ReviewWhen.Where(text => text.ChoiceBand is not null).Select(text => text.ChoiceBand!))
            .Concat(value.ReviewWhen.SelectMany(text => text.ChoiceBounds ?? []))
            .Concat(value.Rules.Where(rule => rule.Image is not null).Select(rule => rule.ImageBounds!))
            .Concat(value.Rules.Where(rule => rule.FilledQuantitiesBounds is not null).Select(rule => rule.FilledQuantitiesBounds!))
            .Concat(value.Rules.Where(rule => rule.SingleTextRunBounds is not null).Select(rule => rule.SingleTextRunBounds!))
            .Concat(value.Rules.Where(rule => rule.Number is not null).Select(rule => rule.Number!.Bounds))
            .Concat(value.Rules.SelectMany(rule => rule.ImageStableRegions ?? []))
            .Concat(value.WhiteTextBounds ?? []))
            if (bounds is not { Length: 4 } || bounds.Any(x => !double.IsFinite(x) || x < 0 || x > 1)
                || bounds[2] <= 0 || bounds[3] <= 0
                || bounds[0] + bounds[2] > 1 || bounds[1] + bounds[3] > 1)
                throw new InvalidDataException("進行規則の描画領域内座標が不正です。");
        if (value.ReviewWhen.Any(text => text.ChoiceBounds is not null && text.ChoiceBounds.Length is < 2 or > 5))
            throw new InvalidDataException("確認画面の選択肢は2〜5領域で指定します。");
        if (value.ReviewWhen.Any(text => text.AskUserImmediately && text.ChoiceBounds is null && text.ChoiceBand is null)
            || value.Rules.SelectMany(rule => rule.When.Concat(rule.Click is null ? [] : new[] { rule.Click })).Any(text => text.AskUserImmediately))
            throw new InvalidDataException("利用者への即時申請は、選択肢の領域を持つ確認画面に指定します。");
        foreach (var review in value.ReviewWhen.Where(text => text.Confirm is not null || text.ConfirmKey is not null))
        {
            // 回答された選択を押した後の確定は、確定の表示とキーの両方を設定した即時申請にだけ許す。
            if (!review.AskUserImmediately || review.Confirm is null || review.ConfirmKey is null
                || review.Confirm.Confirm is not null || review.Confirm.ChoiceBounds is not null)
                throw new InvalidDataException("選択後の確定は、利用者への即時申請に確定の表示とキーの組で指定します。");
            OpenLogicool.Input.OutputTokens.Parse(review.ConfirmKey);
        }
        // 停止表示が止めるのはSpaceを押す操作だけ。Spaceを送らない規則は停止表示中も評価する。
        // Spaceを送る規則は、停止表示より優先すると明示したものだけを停止表示中に評価する。
        return value with { Rules = value.Rules.Select(rule => (rule.Image is null ? rule
            : rule with { Image = Path.GetFullPath(rule.Image, Path.GetDirectoryName(Path.GetFullPath(path))!) })
            // 段階を始める規則と段階の中の規則は、続きでSpaceを押すため、明示した時だけ停止表示中に評価する。
            with { AllowWhileInhibited = rule.AllowWhileInhibited || !PressesSpace(rule.Key) && !PressesSpace(rule.AfterClickKey)
                && rule.ThenKeys?.Any(PressesSpace) != true && rule.Stage is null && rule.NextStage is null }).ToArray() };
    }

    private static bool PressesSpace(string? key) => key?.Contains("Key:Space", StringComparison.OrdinalIgnoreCase) == true;
}

public enum VisualProgressAction { Wait, Normal, Key, Click, Review }
public sealed record VisualProgressOption(string Id, string Label);
public sealed record VisualProgressChoice(VisualProgressAction Action, string? RuleId = null,
    string? Signature = null, string? Key = null, double[]? Point = null, string? Detail = null,
    VisualProgressOption[]? Options = null, bool Immediate = false, bool AllowWhileInhibited = false,
    int RepeatIntervalMs = 0, string? AfterClickKey = null, bool AskUserImmediately = false,
    VisualProgressText? ReviewSource = null, double[][]? OptionPoints = null, string? NextStage = null,
    string[]? ThenKeys = null);

/// <summary>ゲーム固有の操作条件は設定に置き、文字・配置・画像を照合する。</summary>
public sealed class VisualProgressRecognizer(VisualProgressProfile profile)
{
    private readonly Dictionary<string, VisualKeyTemplate> templates = profile.Rules.Where(rule => rule.Image is not null && !rule.ImageRotates)
        .ToDictionary(rule => rule.Id, rule => VisualKeyTemplate.Load(rule.Image!, silhouette: rule.ImageSilhouette,
            stableRegions: rule.ImageStableRegions, searchStep: rule.ImageSearchStep, clipsAtBottom: rule.ImageClipsAtBottom));
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
        foreach (var rule in profile.Rules.Where(rule => rule.Number is not null && rule.When.Length > 0))
        {
            // 小さい数字と1桁の数字は通常の読み取りで落ちる。条件の画面の時だけ、数値の領域を拡大して読み直す。
            if (!rule.When.All(condition => Matches(Normalize(string.Concat(ocr.Words
                .Where(word => Inside(word, condition.Bounds, viewport)).Select(word => word.Text))), condition))) continue;
            var area = rule.Number!.Bounds;
            var words = ocr.Words.Where(word => !Inside(word, area, viewport));
            if (await ReadNumberAsync(frame, viewport, rule.Number, token) is { } reading)
            {
                var text = rule.Number.OutOf is { } outOf ? FormattableString.Invariant($"{reading}/{outOf}")
                    : reading.ToString(System.Globalization.CultureInfo.InvariantCulture);
                words = words.Append(new(text, viewport.X + area[0] * viewport.Width, viewport.Y + area[1] * viewport.Height,
                    area[2] * viewport.Width, area[3] * viewport.Height));
                ocr = ocr with { Text = ocr.Text + " " + text };
            }
            ocr = ocr with { Words = words.ToArray() };
        }
        foreach (var area in profile.WhiteTextBounds ?? [])
        {
            // 明るい背景に重なった白文字は通常の読み取りで落ちる。純白の画素だけを残した画像を読み、元の読み取りへ足す。
            var x = (int)(viewport.X + area[0] * viewport.Width);
            var y = (int)(viewport.Y + area[1] * viewport.Height);
            var width = (int)(area[2] * viewport.Width);
            var height = (int)(area[3] * viewport.Height);
            var pixels = frame.Pixels ?? throw new InvalidOperationException("白文字の読み直しには画像が必要です。");
            var bytes = new byte[width * height * 4];
            for (var row = 0; row < height; row++)
                pixels.Bgra8.Span.Slice((y + row) * pixels.Stride + x * 4, width * 4).CopyTo(bytes.AsSpan(row * width * 4));
            for (var i = 0; i < bytes.Length; i += 4)
            {
                var pure = bytes[i] >= 250 && bytes[i + 1] >= 250 && bytes[i + 2] >= 250;
                bytes[i] = bytes[i + 1] = bytes[i + 2] = (byte)(pure ? 0 : 255);
                bytes[i + 3] = 255;
            }
            var cropped = frame with { Width = width, Height = height, Pixels = new FramePixels(bytes, width * 4), Crop = null };
            var white = await new WindowsGameOcrRecognizer().RecognizeAsync(cropped, token);
            ocr = ocr with { Text = ocr.Text + " " + white.Text, Words = ocr.Words
                .Concat(white.Words.Select(word => word with { X = word.X + x, Y = word.Y + y })).ToArray() };
        }
        return ocr;
    }

    /// <summary>
    /// 数値の領域を、拡大率を変えて読む。小さい数字は拡大率によって読めない回があるため、
    /// 読めた回の値がすべて一致した時だけ、その値を返す。
    /// </summary>
    private static async ValueTask<int?> ReadNumberAsync(CapturedFrame frame, FrameRect viewport, VisualProgressNumber number,
        CancellationToken token)
    {
        int? value = null;
        foreach (var zoom in new[] { 2, 3, 4 })
        {
            var read = await ReadAreaAsync(frame, viewport, number.Bounds, zoom, token);
            if (ReadNumber(string.Concat(read.Words.OrderBy(word => word.X).Select(word => word.Text)), number) is not { } reading) continue;
            if (value is not null && value != reading) return null;
            value = reading;
        }
        return value;
    }

    /// <summary>領域を切り出し、拡大して読む。</summary>
    private static async ValueTask<WindowsGameOcrResult> ReadAreaAsync(CapturedFrame frame, FrameRect viewport, double[] area,
        int zoom, CancellationToken token)
    {
        var x = (int)(viewport.X + area[0] * viewport.Width);
        var y = (int)(viewport.Y + area[1] * viewport.Height);
        var width = (int)(area[2] * viewport.Width);
        var height = (int)(area[3] * viewport.Height);
        var pixels = frame.Pixels ?? throw new InvalidOperationException("数値の読み取りには画像が必要です。");
        var bytes = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            pixels.Bgra8.Span.Slice((y + row) * pixels.Stride + x * 4, width * 4).CopyTo(bytes.AsSpan(row * width * 4));
        var cropped = frame with { Width = width, Height = height, Pixels = new FramePixels(bytes, width * 4), Crop = null };
        return await new WindowsGameOcrRecognizer(zoom).RecognizeAsync(cropped, token);
    }

    /// <summary>
    /// 領域の文字から数値を読む。数字が一つだけの時にその値を返す。上限つきの表示は「現在値/上限」の形で、
    /// 上限が設定と一致した時だけ現在値を返す。読み違いで操作を始めないよう、それ以外は読めなかったものとする。
    /// </summary>
    internal static int? ReadNumber(string text, VisualProgressNumber number)
    {
        var normalized = string.Concat(text.Normalize(NormalizationForm.FormKC).Where(c => c != ',' && !char.IsWhiteSpace(c)));
        var digits = Regex.Matches(normalized, @"\d+");
        if (number.OutOf is not { } outOf)
            return digits.Count == 1 && int.TryParse(digits[0].Value, out var only) ? only : null;
        var pair = Regex.Match(normalized, @"(?<!\d)(\d+)/(\d+)(?!\d)");
        return digits.Count == 2 && pair.Success && int.TryParse(pair.Groups[2].Value, out var limit) && limit == outOf
            && int.TryParse(pair.Groups[1].Value, out var value) && value <= outOf ? value : null;
    }

    private static bool Satisfies(int reading, VisualProgressNumber number) =>
        number.AtMost is { } most ? reading <= most : reading == number.Exactly;

    private VisualKeyTemplateMatch FindImage(VisualProgressRule rule, CapturedFrame frame, FrameRect viewport)
    {
        var area = rule.ImageBounds!;
        double[] mapped = [(viewport.X + area[0] * viewport.Width) / frame.Width,
            (viewport.Y + area[1] * viewport.Height) / frame.Height,
            area[2] * viewport.Width / frame.Width, area[3] * viewport.Height / frame.Height];
        return templates[rule.Id].FindAtWindowScale(frame, mapped, viewport.Width / rule.ImageClientWidth);
    }

    /// <summary>
    /// 文字の条件を持たない数値の規則を読む。画面の種類でなく数値で始める操作なので、文字を読む前に評価し、
    /// ほかの規則との優先は Prefer で決める。
    /// 数値を読めて条件を外れていた規則は unmet へ知らせ、mayStart が許さない規則は選ばない。
    /// </summary>
    public async ValueTask<VisualProgressChoice?> RecognizeNumberRuleAsync(CapturedFrame frame, FrameRect viewport,
        string? stage, Func<VisualProgressRule, bool> mayStart, Action<VisualProgressRule> unmet, bool inhibited = false,
        CancellationToken token = default)
    {
        foreach (var rule in profile.Rules.Where(rule => rule.Number is not null && rule.When.Length == 0 && rule.Stage == stage
            && (!inhibited || rule.AllowWhileInhibited)).OrderByDescending(rule => rule.Priority))
        {
            if (await ReadNumberAsync(frame, viewport, rule.Number!, token) is not { } reading) continue;
            if (!Satisfies(reading, rule.Number!)) { unmet(rule); continue; }
            if (!mayStart(rule)) continue;
            double[]? point = null;
            if (rule.Image is not null)
            {
                var match = FindImage(rule, frame, viewport);
                if (!match.Matches) continue;
                if (rule.ClickImage) point = [match.Bounds[0] + match.Bounds[2] * (rule.ClickImagePoint?[0] ?? 0.5),
                    match.Bounds[1] + match.Bounds[3] * (rule.ClickImagePoint?[1] ?? 0.5)];
            }
            return new(rule.ClickImage ? VisualProgressAction.Click : VisualProgressAction.Key, rule.Id,
                FormattableString.Invariant($"{rule.Id}:{reading}"), rule.Key, point,
                AllowWhileInhibited: rule.AllowWhileInhibited, NextStage: rule.NextStage, ThenKeys: rule.ThenKeys);
        }
        return null;
    }

    /// <summary>
    /// 数値で始める規則は、ほかに進める表示が無い時と、ほかの規則より優先度が高い時に選ぶ。
    /// 利用者や担当の確認が要る表示には譲る。
    /// </summary>
    public VisualProgressChoice Prefer(VisualProgressChoice? numbered, VisualProgressChoice recognized)
    {
        int Priority(string id) => profile.Rules.Single(rule => rule.Id == id).Priority;
        return numbered is not null && (recognized.Action == VisualProgressAction.Normal
            || recognized.RuleId is not null && Priority(numbered.RuleId!) > Priority(recognized.RuleId)) ? numbered : recognized;
    }

    public VisualProgressChoice Recognize(WindowsGameOcrResult ocr, int width, int height, FrameRect viewport,
        CapturedFrame? frame = null, bool inhibited = false, string? stage = null)
    {
        if (frame is not null && RecognizeImmediateImage(frame, viewport, inhibited, stage) is { } immediate) return immediate;
        string Read(VisualProgressText area) => Normalize(string.Concat(ocr.Words
            .Where(word => Inside(word, area.Bounds, viewport))
            .Select(word => word.Text)));
        foreach (var review in inhibited ? [] : profile.ReviewWhen)
            if (Matches(Read(review), review.Text))
            {
                var choices = ReadChoices(review, ocr, viewport, width, height);
                // 利用者へ直接申請する表示は、演出の途中で選択肢を読めない間は確認画面として扱わず待つ。
                if (review.AskUserImmediately && (choices.Length < 2 || choices.Any(choice => choice.Label.Length == 0)))
                    return new(VisualProgressAction.Wait, Signature: "choices-settling:" + Normalize(review.Text), Detail: "選択肢の表示");
                return new(VisualProgressAction.Review, Detail: $"利用者の判断が必要な表示: {review.Text}",
                    Options: review.ChoiceBounds is null && review.ChoiceBand is null ? null : choices.Select((choice, index) =>
                        new VisualProgressOption($"choice-{index + 1}", $"選択肢{index + 1}: "
                            + (choice.Label.Length == 0 ? "文字を読めません（添付画像を確認）" : choice.Label)
                            + (choice.Recommended ? RecommendedMark : ""))).ToArray(),
                    AskUserImmediately: review.AskUserImmediately, ReviewSource: review,
                    OptionPoints: choices.Select(choice => choice.Point).ToArray());
            }
        var candidates = new List<VisualProgressChoice>();
        // 段階の中では、その段階の規則だけを評価する。文字の条件を持たない数値の規則は RecognizeNumberRuleAsync が読む。
        foreach (var rule in profile.Rules.Where(rule => rule.RepeatIntervalMs == 0 && rule.Stage == stage
            && (rule.Number is null || rule.When.Length > 0)))
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
                // 指し示す画像は、画像の中心ではなく指定した位置（指先など）を押す。
                if (rule.ClickImage) point = [match.Bounds[0] + match.Bounds[2] * (rule.ClickImagePoint?[0] ?? 0.5),
                    match.Bounds[1] + match.Bounds[3] * (rule.ClickImagePoint?[1] ?? 0.5)];
            }
            var texts = rule.When.Select(Read).ToArray();
            if (!rule.When.Select((condition, i) => Matches(texts[i], condition)).All(x => x)) continue;
            if (rule.SingleTextRunBounds is { } labelBounds
                && !HasSingleTextRun(ocr, labelBounds, viewport, width, height)) continue;
            if (rule.FilledQuantitiesBounds is { } quantityBounds)
            {
                var quantities = string.Join(" ", ocr.Words.Where(word => Inside(word, quantityBounds, viewport)).Select(word => word.Text));
                if (!QuantitiesFilled(quantities)) continue;
            }
            if (rule.Number is { } number && !(ReadNumber(string.Concat(ocr.Words.Where(word => Inside(word, number.Bounds, viewport))
                .OrderBy(word => word.X).Select(word => word.Text)), number) is { } reading && Satisfies(reading, number))) continue;
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
                    string.IsNullOrWhiteSpace(condition.Text) ? texts[i] : Normalize(condition.Text)))
                    // 指し示す先が別の場所へ移ったら、同じ操作の結果待ちではなく次の操作として扱う。
                    + (rule.ClickImagePoint is null ? "" : FormattableString.Invariant(
                        $"@{Math.Round(point![0] * 20) / 20:0.00},{Math.Round(point[1] * 20) / 20:0.00}")), rule.Key, point,
                Immediate: rule.Immediate, AllowWhileInhibited: rule.AllowWhileInhibited, AfterClickKey: rule.AfterClickKey,
                NextStage: rule.NextStage, ThenKeys: rule.ThenKeys));
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

    /// <summary>指定した領域に、指定した文字が表示されているかを読む。</summary>
    public static bool Shows(WindowsGameOcrResult ocr, FrameRect viewport, VisualProgressText text) =>
        Matches(Normalize(string.Concat(ocr.Words.Where(word => Inside(word, text.Bounds, viewport)).Select(word => word.Text))), text.Text);

    private const string RecommendedMark = "（ゲーム内推奨）";

    /// <summary>
    /// 選択肢の並びが同じ表示かを比べる。OCRの数文字の読み違いは同じとみなし、別の選択肢は区別する。
    /// 推奨の印は光って読めない回があるため、同じ選択肢かを確かめる時は ignoreRecommendedMark で印を比較から外す。
    /// </summary>
    public static bool SameOptions(VisualProgressOption[]? observed, VisualProgressOption[]? expected,
        bool ignoreRecommendedMark = false)
    {
        string Name(string label) => ignoreRecommendedMark && label.EndsWith(RecommendedMark, StringComparison.Ordinal)
            ? label[..^RecommendedMark.Length] : label;
        return observed is not null && expected is not null && observed.Length == expected.Length
            && observed.Zip(expected).All(pair => pair.First.Id == pair.Second.Id
                && OpenLogicool.Contracts.Perception.OcrTextMatcher.Similarity(Name(pair.First.Label), Name(pair.Second.Label)) >= 0.75);
    }

    /// <summary>
    /// 選択肢の名前・押す位置・推奨の印を読む。帯を指定した表示は、帯の中の文字を横の間隔でまとめて
    /// 選択肢の数と位置を決める（枚数や並びが変わっても読める）。領域を個別に指定した表示は、その領域を読む。
    /// </summary>
    private static (string Label, double[] Point, bool Recommended)[] ReadChoices(VisualProgressText review,
        WindowsGameOcrResult ocr, FrameRect viewport, int width, int height)
    {
        if (review.ChoiceBand is null)
            return (review.ChoiceBounds ?? []).Select(bounds => (
                Normalize(string.Concat(ocr.Words.Where(word => Inside(word, bounds, viewport)).Select(word => word.Text))),
                new[] { (viewport.X + (bounds[0] + bounds[2] / 2) * viewport.Width) / width,
                    (viewport.Y + (bounds[1] + bounds[3] / 2) * viewport.Height) / height }, false)).ToArray();
        var groups = new List<List<WindowsGameOcrWord>>();
        var right = double.NegativeInfinity;
        foreach (var word in ocr.Words.Where(word => Inside(word, review.ChoiceBand, viewport)).OrderBy(word => word.X))
        {
            // 同じカードの文字は詰まって並び、隣のカードとは大きく離れる。
            if (word.X - right > 0.025 * viewport.Width) groups.Add([]);
            groups[^1].Add(word);
            right = Math.Max(right, word.X + word.Width);
        }
        var recommended = review.Recommended is null ? [] : ocr.Words
            .Where(word => Inside(word, review.Recommended.Bounds, viewport) && review.Recommended.Text.Contains(word.Text.Trim(), StringComparison.Ordinal)
                && word.Text.Trim().Length > 0)
            .Select(word => word.X + word.Width / 2).ToArray();
        return groups.Select(group =>
        {
            var left = group.Min(word => word.X);
            var end = group.Max(word => word.X + word.Width);
            var top = group.Min(word => word.Y);
            var bottom = group.Max(word => word.Y + word.Height);
            // 名前と段階が2行に分かれている時は、上の行から読む。
            var twoLines = bottom - top > 1.6 * group.Max(word => word.Height);
            var middle = (top + bottom) / 2;
            var label = Normalize(string.Concat(group
                .OrderBy(word => twoLines && word.Y + word.Height / 2 > middle ? 1 : 0).ThenBy(word => word.X)
                .Select(word => word.Text)));
            var center = (left + end) / 2;
            return (label, new[] { center / width, middle / height },
                recommended.Any(x => Math.Abs(x - center) < 0.06 * viewport.Width));
        }).Where(choice => choice.label.Length >= 2).ToArray();
    }

    // 即時・反復の画像規則は段階を持たない。段階の中では評価しない。
    public VisualProgressChoice? RecognizeImmediateImage(CapturedFrame frame, FrameRect viewport, bool inhibited = false,
        string? stage = null) => stage is not null ? null
        : RecognizeImageKey(frame, viewport, profile.Rules.Where(rule => rule.Immediate && rule.Image is not null && rule.RepeatIntervalMs == 0
            && (!inhibited || rule.AllowWhileInhibited)));

    public VisualProgressChoice? RecognizeRepeatingImage(CapturedFrame frame, FrameRect viewport,
        Func<VisualProgressRule, bool> isDue, string? stage = null) => stage is not null ? null
        : RecognizeImageKey(frame, viewport, profile.Rules.Where(rule => rule.RepeatIntervalMs > 0 && isDue(rule)));

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
                    AllowWhileInhibited: rule.AllowWhileInhibited, RepeatIntervalMs: rule.RepeatIntervalMs);
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

    private static bool Matches(string observed, VisualProgressText condition) => condition.Exact
        ? observed.Contains(Normalize(condition.Text), StringComparison.Ordinal) : Matches(observed, condition.Text);

    internal static bool Matches(string observed, string expected)
    {
        expected = Normalize(expected);
        if (expected.Length == 0) return observed.Length >= 4;
        if (observed.Contains(expected, StringComparison.Ordinal)) return true;
        // 日本語OCRの空白・全半角と、長いラベルの一文字の読違いを吸収する。
        if (expected.Length < 4) return false;
        if (Enumerable.Range(0, Math.Max(0, observed.Length - expected.Length + 1))
            .Any(start => observed.AsSpan(start, expected.Length).ToArray()
                .Where((c, i) => c != expected[i]).Count() <= 1)) return true;
        // OCRは文字を落とす・余分に読むこともある。短い語は別の語と区別できないため6文字以上に限り、
        // 抜け・余分・読違いの合計が長さの4分の1以下なら同じ表示とする。
        if (expected.Length < 6) return false;
        var previous = new int[observed.Length + 1]; // 観測文字列のどこから始まってもよい。
        var current = new int[observed.Length + 1];
        for (var i = 1; i <= expected.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= observed.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (expected[i - 1] == observed[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous.Min() <= expected.Length / 4;
    }
}

/// <summary>同じ画面への再送をせず、操作結果待ち・未知画面を明示する。</summary>
public sealed class VisualProgressSchedule(VisualProgressProfile profile)
{
    private VisualProgressChoice? pending;
    private bool pendingChanged;
    private string? stableSignature;
    private long stableAt;
    private long? unknownAt;
    private int unresolvedObservations;
    private string? waitingSignature;
    private string? consumedImmediate;
    private long? immediateMissingAt;
    private bool wasInhibited;
    private readonly Dictionary<string, long> repeatedAt = [];
    private readonly HashSet<string> started = [];
    private long? stageHudAt;
    // 段階の途中で通常の画面がこの時間続いたら、段階を終える。画面を開くキーを送ってから切り替わるまでの間は終えない。
    private const int StageEndMs = 3000;

    /// <summary>進行中の段階。段階の中では、その段階の規則だけを評価する。</summary>
    public string? Stage { get; private set; }

    /// <summary>段階を始めた数値の規則は、数値が条件を外れたのを見るまで、もう一度は始めない。</summary>
    public bool MayStart(VisualProgressRule rule) => rule.NextStage is null || !started.Contains(rule.Id);

    public void ObserveUnmet(VisualProgressRule rule) => started.Remove(rule.Id);
    // 同じ待機画面とみなす読みの類似度の境。利用者の指定で0.5とする。
    // 実測: 同じ会話の選択画面を続けて読んだ読みどうしは0.61以上、別の会話の画面の読みとは0.12以下。
    private const double SameWaitSimilarity = 0.5;

    public bool RepeatIsDue(long now, VisualProgressRule rule) =>
        !repeatedAt.TryGetValue(rule.Id, out var last) || now - last >= rule.RepeatIntervalMs;

    public VisualProgressChoice Decide(long now, VisualProgressChoice candidate, bool inhibited,
        bool hudVisible, bool due, bool sceneChanged = false)
    {
        if (Stage is not null && candidate.Action == VisualProgressAction.Normal && hudVisible)
        {
            stageHudAt ??= now;
            if (now - stageHudAt >= StageEndMs) { Stage = null; stageHudAt = null; }
        }
        else stageHudAt = null;
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
            // 押すたびに少しずつ進む表示は、操作の後に画面が変わって落ち着いたら、同じ表示でも次の操作として送る。
            // 押しても画面が変わらない時は送り直さず、下の結果待ちが通知へ進める。
            var advanced = false;
            if (candidate.Signature == pending.Signature
                && profile.Rules.SingleOrDefault(rule => rule.Id == candidate.RuleId)?.RepeatAfterChange == true)
            {
                if (sceneChanged) pendingChanged = true;
                else if (pendingChanged) { advanced = true; pending = null; pendingChanged = false; ResetUnresolved(); }
            }
            if (advanced) { }
            else if (candidate.Signature == pending!.Signature || (candidate.Action == VisualProgressAction.Normal && !hudVisible))
                return ObserveUnresolved(now, sceneChanged,
                    candidate.Action == VisualProgressAction.Normal ? Math.Max(profile.ResultTimeoutMs, profile.UnknownTimeoutMs) : profile.ResultTimeoutMs,
                    $"{pending.RuleId} の操作後、複数回観測して画面の変化が止まったまま結果を確認できません。再送していません。");
            else
            {
                if (stableSignature != candidate.Signature) { stableSignature = candidate.Signature; stableAt = now; }
                if (now - stableAt < 600) return new(VisualProgressAction.Wait);
                pending = null;
                ResetUnresolved();
            }
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
            // 同じ待機画面かは、読みの類似度で見分ける。文字認識の読みは、同じ画面でも余分な文字や語順でゆれる。
            var reading = candidate.Signature ?? candidate.RuleId ?? candidate.Detail ?? "";
            if (waitingSignature is null || waitingSignature != reading
                && OpenLogicool.Contracts.Perception.OcrTextMatcher.Similarity(waitingSignature, reading) < SameWaitSimilarity)
            { waitingSignature = reading; ResetUnresolved(); }
            return ObserveUnresolved(now, sceneChanged, profile.UnknownTimeoutMs,
                $"{candidate.RuleId ?? candidate.Detail} の待機中、複数回観測して画面の変化が止まったままです。追加入力はしていません。");
        }
        ResetUnresolved();
        if (stableSignature != candidate.Signature) { stableSignature = candidate.Signature; stableAt = now; }
        var minimumVisible = profile.Rules.SingleOrDefault(rule => rule.Id == candidate.RuleId)?.MinimumVisibleMs ?? 600;
        return now - stableAt >= minimumVisible && due ? candidate : new(VisualProgressAction.Wait);
    }

    public void RecordInput(long now, VisualProgressChoice choice)
    {
        if (choice.NextStage is not null)
        {
            if (Stage is null) started.Add(choice.RuleId!);
            Stage = choice.NextStage;
            stageHudAt = null;
        }
        if (choice.RepeatIntervalMs > 0) { repeatedAt[choice.RuleId!] = now; return; }
        if (choice.Immediate) { consumedImmediate = choice.Signature; pending = null; ResetUnresolved(); return; }
        pending = choice; pendingChanged = false; unknownAt = now; unresolvedObservations = 0;
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
