using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class VisualProgressTests
{
    [Fact]
    public void Rotating_dialogue_cue_recognizes_the_real_screen_with_broken_OCR()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures", "visual-recovery", "mabinogi-20261008");
        var recognizer = new VisualProgressRecognizer(VisualProgressProfile.Load(Path.Combine(fixture, "progress.json")));
        var frame = ReadFrame(Path.Combine(fixture, "dialogue-ocr-broken.png"));
        var garbled = Ocr([new("だ モ バ が さ り な て 帰", 575, 130, 330, 45)]);
        var choice = recognizer.Recognize(garbled, frame.Width, frame.Height, new(1, 31, 1506, 814), frame);
        Assert.Equal(VisualProgressAction.Key, choice.Action);
        Assert.Equal("npc-dialogue-cue", choice.RuleId);
        Assert.Equal("Key:Space", choice.Key);
    }

    [Fact]
    public void Rotating_cue_is_not_found_in_saved_HUD_bonus_reward_or_text_without_the_cue()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures", "visual-recovery", "mabinogi-20261008");
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var recognizer = new VisualProgressRecognizer(profile with { Rules = profile.Rules.Where(r => r.ImageRotates).ToArray() });
        var speech = Ocr([new("会話に似た文字列", 575, 130, 330, 45)]);
        foreach (var name in new[] { "before.png", "after.png", "combat.png", "window-small.png", "window-short.png",
            "window-narrow.png", "bonus-three.png", "class-level.png" })
        {
            var frame = ReadFrame(Path.Combine(fixture, name));
            Assert.Equal(VisualProgressAction.Normal, recognizer.Recognize(speech, frame.Width, frame.Height,
                new(1, 31, frame.Width - 2, frame.Height - 32), frame).Action);
        }
        var dialogue = ReadFrame(Path.Combine(fixture, "dialogue-ocr-broken.png"));
        var bytes = dialogue.Pixels!.Bgra8.ToArray();
        for (var y = 153; y < 183; y++)
            for (var x = 908; x < 939; x++)
                for (var c = 0; c < 3; c++) bytes[y * dialogue.Pixels.Stride + x * 4 + c] = 255;
        var withoutCue = dialogue with { Pixels = new FramePixels(bytes, dialogue.Pixels.Stride) };
        Assert.Equal(VisualProgressAction.Normal, recognizer.Recognize(speech, dialogue.Width, dialogue.Height,
            new(1, 31, 1506, 814), withoutCue).Action);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(37, 1.0)]
    [InlineData(83, 0.75)]
    [InlineData(137, 1.25)]
    [InlineData(213, 1.0)]
    [InlineData(299, 0.75)]
    public void Cue_matching_handles_rotation_scale_and_location(double angle, double scale)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures", "visual-recovery", "mabinogi-20261008");
        var template = new VisualRotatingTemplate(Path.Combine(fixture, "dialogue-cue.jpg"));
        var source = ReadFrame(Path.Combine(fixture, "dialogue-cue.jpg"));
        var width = 400; var height = 240;
        var bytes = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        var radians = angle * Math.PI / 180;
        for (var y = 70; y < 150; y++)
            for (var x = 130; x < 210; x++)
            {
                var dx = (x - 170) / scale; var dy = (y - 110) / scale;
                var sx = (int)Math.Floor(Math.Cos(radians) * dx + Math.Sin(radians) * dy + source.Width / 2.0);
                var sy = (int)Math.Floor(-Math.Sin(radians) * dx + Math.Cos(radians) * dy + source.Height / 2.0);
                if (sx < 0 || sx >= source.Width || sy < 0 || sy >= source.Height) continue;
                source.Pixels!.Bgra8.Span.Slice(sy * source.Pixels.Stride + sx * 4, 4).CopyTo(bytes.AsSpan((y * width + x) * 4));
            }
        var frame = source with { Width = width, Height = height, Pixels = new FramePixels(bytes, width * 4) };
        var match = template.Find(frame, [0, 0, 1, 1], scale);
        Assert.True(match.Matches);
        Assert.InRange(match.Bounds[0] * width, 145, 170);
        Assert.InRange(match.Bounds[1] * height, 85, 110);
    }

    [Fact]
    public async Task Wide_dialogue_with_skip_uses_text_even_when_the_tail_shape_differs()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures", "visual-recovery", "mabinogi-20261008");
        var recognizer = new VisualProgressRecognizer(VisualProgressProfile.Load(Path.Combine(fixture, "progress.json")));
        var frame = ReadFrame(Path.Combine(fixture, "dialogue-wide.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, new(1, 31, 1506, 814), frame);
        Assert.Equal(VisualProgressAction.Key, choice.Action);
        Assert.Equal("Key:Space", choice.Key);
    }

    [Fact]
    public async Task Recorded_bonus_stops_and_preserves_all_three_choices_for_the_owner()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures", "visual-recovery", "mabinogi-20261008");
        var recognizer = new VisualProgressRecognizer(VisualProgressProfile.Load(Path.Combine(fixture, "progress.json")));
        var frame = ReadFrame(Path.Combine(fixture, "bonus-three.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, new(1, 31, 1506, 814), frame);
        Assert.Equal(VisualProgressAction.Review, choice.Action);
        Assert.Null(choice.Key);
        Assert.Null(choice.Point);
        Assert.Equal(3, choice.Options!.Length);
        Assert.Contains("DEX", choice.Options[0].Label);
        Assert.Contains("成長", choice.Options[1].Label);
        Assert.Contains("成長", choice.Options[2].Label);
    }

    [Fact]
    public void Unreadable_bonus_labels_still_offer_three_positions_without_guessing()
    {
        var recognizer = new VisualProgressRecognizer(new(1, [],
            [new("ボーナス", [0, 0, 1, 1], [[0, 0.4, 0.3, 0.2], [0.3, 0.4, 0.3, 0.2], [0.6, 0.4, 0.3, 0.2]])]));
        var choice = recognizer.Recognize(Ocr([new("ボーナス", 100, 50, 100, 20)]), 1000, 600, new(0, 0, 1000, 600));
        Assert.Equal(VisualProgressAction.Review, choice.Action);
        Assert.Equal(3, choice.Options!.Length);
        Assert.All(choice.Options, option => Assert.Contains("読めません", option.Label));
    }

    [Fact]
    public async Task Recorded_class_level_notice_uses_Space_without_selecting_a_bonus()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures", "visual-recovery", "mabinogi-20261008");
        var recognizer = new VisualProgressRecognizer(VisualProgressProfile.Load(Path.Combine(fixture, "progress.json")));
        var frame = ReadFrame(Path.Combine(fixture, "class-level.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, new(1, 31, 1506, 814), frame);
        Assert.Equal("class-level", choice.RuleId);
        Assert.Equal("Key:Space", choice.Key);
    }
    [Fact]
    public void Final_dialogue_without_skip_requires_the_real_speech_shape_and_text()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures", "visual-recovery", "mabinogi-20261008");
        var recognizer = new VisualProgressRecognizer(VisualProgressProfile.Load(Path.Combine(fixture, "progress.json")));
        var frame = ReadFrame(Path.Combine(fixture, "dialogue-screen.png"));
        var speech = Ocr([new("墓地の左側に花が供えられた墓がある", 570, 114, 370, 40)]);
        var viewport = new FrameRect(1, 31, 1506, 814);
        Assert.Equal(VisualProgressAction.Key, recognizer.Recognize(speech, frame.Width, frame.Height, viewport, frame).Action);
        var day = ReadFrame(Path.Combine(fixture, "dialogue-day.png"));
        Assert.Equal(VisualProgressAction.Key, recognizer.Recognize(speech, day.Width, day.Height, viewport, day).Action);
        Assert.Equal(VisualProgressAction.Normal, recognizer.Recognize(Ocr([]), frame.Width, frame.Height, viewport, frame).Action);
        foreach (var name in new[] { "before.png", "after.png", "combat.png", "window-small.png", "window-short.png", "window-narrow.png" })
        {
            var other = ReadFrame(Path.Combine(fixture, name));
            Assert.Equal(VisualProgressAction.Normal, recognizer.Recognize(speech, other.Width, other.Height,
                new(1, 31, other.Width - 2, other.Height - 32), other).Action);
        }
    }

    private static CapturedFrame ReadFrame(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return new(ContractSchemaVersions.Revision03, "progress-test", CaptureBackend.WindowsGraphicsCapture,
            1, 0, DateTimeOffset.UnixEpoch, bitmap.PixelWidth, bitmap.PixelHeight, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(bytes, bitmap.PixelWidth * 4));
    }
    private static VisualProgressProfile Profile(params VisualProgressRule[] rules) => new(1, rules,
        [new("ボーナス", [0.2, 0.1, 0.6, 0.7])]);

    [Fact]
    public void Dialogue_requires_the_skip_label_and_the_speech_text_together()
    {
        var recognizer = new VisualProgressRecognizer(Profile(new VisualProgressRule("dialogue",
            [new("スキップ", [0.85, 0, 0.15, 0.12]), new("", [0.3, 0.04, 0.4, 0.25])], "Key:Space")));
        var words = new[] { new WindowsGameOcrWord("スキップ", 900, 30, 60, 20), new("墓地のクモを倒してほしい", 400, 100, 220, 40) };
        Assert.Equal(VisualProgressAction.Key, recognizer.Recognize(Ocr(words), 1000, 600, new(0, 0, 1000, 600)).Action);
        Assert.Equal(VisualProgressAction.Normal, recognizer.Recognize(Ocr(words[..1]), 1000, 600, new(0, 0, 1000, 600)).Action);
        Assert.Equal(VisualProgressAction.Normal, recognizer.Recognize(Ocr(words[1..]), 1000, 600, new(0, 0, 1000, 600)).Action);
    }

    [Theory]
    [InlineData(1000, 600, 0, 0)]
    [InlineData(700, 1100, 8, 30)]
    [InlineData(1300, 450, 8, 30)]
    public void Loot_click_uses_the_current_client_size_and_OCR_button_position(int width, int height, int x, int y)
    {
        var recognizer = new VisualProgressRecognizer(Profile(new VisualProgressRule("equip",
            [new("発見した戦利品", [0.2, 0, 0.6, 0.3])], Click: new("自動着用", [0.2, 0.6, 0.8, 0.4]))));
        var choice = recognizer.Recognize(Ocr([
            new("発見した戦利品", x + width * 0.4, y + height * 0.1, width * 0.15, height * 0.03),
            new("自動着用", x + width * 0.65, y + height * 0.8, width * 0.12, height * 0.04)]),
            width + 2 * x, height + y + 8, new(x, y, width, height));
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.Equal((x + width * 0.71) / (width + 2 * x), choice.Point![0], 5);
        Assert.Equal((y + height * 0.82) / (height + y + 8), choice.Point[1], 5);
    }

    [Fact]
    public void Bonus_selection_and_overlapping_rules_require_review_instead_of_Space()
    {
        var rule = new VisualProgressRule("dialogue", [new("スキップ", [0, 0, 1, 1])], "Key:Space");
        var recognizer = new VisualProgressRecognizer(Profile(rule));
        Assert.Equal(VisualProgressAction.Review, recognizer.Recognize(Ocr([
            new("スキップ", 900, 30, 50, 20), new("ボーナス", 400, 150, 100, 30)]), 1000, 600, new(0, 0, 1000, 600)).Action);
        var ambiguous = new VisualProgressRecognizer(Profile(rule, rule with { Id = "other" }));
        Assert.Equal(VisualProgressAction.Review, ambiguous.Recognize(Ocr([new("スキップ", 900, 30, 50, 20)]),
            1000, 600, new(0, 0, 1000, 600)).Action);
    }

    [Fact]
    public void Same_dialogue_is_not_repeated_and_changed_dialogue_is_verified_before_next_input()
    {
        var schedule = new VisualProgressSchedule(Profile());
        var first = new VisualProgressChoice(VisualProgressAction.Key, "dialogue", "dialogue:最初の話", "Key:Space");
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(0, first, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Key, schedule.Decide(600, first, false, false, true).Action);
        schedule.RecordInput(600, first);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(1200, first, false, false, true).Action);
        var second = first with { Signature = "dialogue:次の話" };
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(1600, second, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Key, schedule.Decide(2200, second, false, false, true).Action);
    }

    [Fact]
    public void No_result_stops_without_retry_and_unknown_screen_gets_a_short_loading_grace()
    {
        var schedule = new VisualProgressSchedule(Profile());
        var candidate = new VisualProgressChoice(VisualProgressAction.Key, "reward", "reward:確認", "Key:Space");
        schedule.RecordInput(100, candidate);
        Assert.Equal(VisualProgressAction.Review, schedule.Decide(5100, candidate, false, false, true).Action);
        var unknown = new VisualProgressSchedule(Profile());
        Assert.Equal(VisualProgressAction.Wait, unknown.Decide(0, new(VisualProgressAction.Normal), false, false, true).Action);
        Assert.Equal(VisualProgressAction.Review, unknown.Decide(5000, new(VisualProgressAction.Normal), false, false, true).Action);
        Assert.Equal(VisualProgressAction.Normal, unknown.Decide(6000, new(VisualProgressAction.Normal), false, true, true).Action);
    }

    [Fact]
    public void Stop_icon_wins_and_timed_dialogue_waits_for_the_original_deadline()
    {
        var schedule = new VisualProgressSchedule(Profile());
        var candidate = new VisualProgressChoice(VisualProgressAction.Key, "dialogue", "dialogue:話", "Key:Space");
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(0, candidate, false, false, false).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(1000, candidate, false, false, false).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(10000, candidate, true, false, true).Action);
    }

    [Fact]
    public void OCR_label_matching_normalizes_spaces_and_full_width_and_allows_one_long_label_error()
    {
        Assert.True(VisualProgressRecognizer.Matches(VisualProgressRecognizer.Normalize("Ｓｐａｃｅ 確 認"), "Space"));
        Assert.True(VisualProgressRecognizer.Matches("発見した戦利晶", "発見した戦利品"));
        Assert.False(VisualProgressRecognizer.Matches("未確認", "退出"));
    }

    [Fact]
    public void Japanese_OCR_letters_keep_the_engine_line_order_despite_uneven_glyph_heights()
    {
        var recognizer = new VisualProgressRecognizer(Profile(new VisualProgressRule("dialogue",
            [new("スキップ", [0.85, 0, 0.15, 0.12]), new("", [0.3, 0.04, 0.4, 0.25])], "Key:Space")));
        var choice = recognizer.Recognize(Ocr([
            new("ス", 1394.5, 59, 14.5, 14), new("キ", 1412, 57.5, 15, 16.5),
            new("ッ", 1429.5, 61.5, 12.5, 12), new("プ", 1444.5, 56.5, 16.5, 16.5),
            new("墓地のクモ", 620, 114, 120, 20)]), 1508, 846, new(1, 31, 1506, 814));
        Assert.Equal(VisualProgressAction.Key, choice.Action);
    }

    private static WindowsGameOcrResult Ocr(WindowsGameOcrWord[] words) => new(string.Join(" ", words.Select(w => w.Text)), "ja", 0, words);
}
