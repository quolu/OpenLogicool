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
    public void 休憩メニューの退出は停止表示中でも一回だけ許可し料理や食事は選ばない()
    {
        var profile = VisualProgressProfile.Load(Path.Combine(AppContext.BaseDirectory, "BotScripts", "MabinogiMobile", "progress.json"));
        var recognizer = new VisualProgressRecognizer(profile);
        var words = new[] { new WindowsGameOcrWord("料理", 740, 495, 35, 20),
            new WindowsGameOcrWord("フードを食べる", 820, 495, 100, 20), new WindowsGameOcrWord("Space", 940, 565, 40, 20) };
        var choice = recognizer.Recognize(Ocr(words), 1000, 600, new(0, 0, 1000, 600), inhibited: true);
        Assert.Equal("rest-exit", choice.RuleId);
        Assert.Equal(VisualProgressAction.Key, choice.Action);
        Assert.Equal("Key:Space", choice.Key);
        Assert.Null(choice.Point);
        Assert.True(choice.AllowWhileInhibited);
        var schedule = new VisualProgressSchedule(profile);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(0, choice, true, true, true).Action);
        Assert.Equal(VisualProgressAction.Key, schedule.Decide(600, choice, true, true, true).Action);
        schedule.RecordInput(600, choice);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(1200, choice, true, true, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(3000, choice, true, true, true).Action);
        Assert.Equal(VisualProgressAction.Review, schedule.Decide(6000, choice, true, true, true).Action);
        foreach (var missing in words)
        {
            var incomplete = recognizer.Recognize(Ocr(words.Where(word => word != missing).ToArray()),
                1000, 600, new(0, 0, 1000, 600), inhibited: true);
            Assert.NotEqual("rest-exit", incomplete.RuleId);
            Assert.False(incomplete.AllowWhileInhibited);
        }
    }

    [Fact]
    public void 停止表示の静止も詰まりとして観測し通常のキーやクリックへ切り替えない()
    {
        var schedule = new VisualProgressSchedule(Profile());
        var candidate = new VisualProgressChoice(VisualProgressAction.Key, "通常", "通常", "Key:Space");
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(0, candidate, true, true, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(9999, candidate, true, true, true).Action);
        var choice = schedule.Decide(10000, candidate, true, true, true);
        Assert.Equal(VisualProgressAction.Review, choice.Action);
        Assert.Null(choice.Key);
        Assert.Null(choice.Point);
        Assert.Contains("停止表示", choice.Detail);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(20000, candidate, true, true, true, sceneChanged: true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(29999, candidate, true, true, true).Action);
        Assert.Equal(VisualProgressAction.Review, schedule.Decide(30000, candidate, true, true, true).Action);
    }

    [Fact]
    public async Task クエストの必要アイテム不足では再入力をせず待機する()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var frame = ReadFrame(Path.Combine(fixture, "quest-items-missing.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var candidate = new VisualProgressRecognizer(profile).Recognize(ocr, frame.Width, frame.Height,
            new(1, 31, frame.Width - 2, frame.Height - 32), frame);
        Assert.Equal("missing-quest-items", candidate.RuleId);
        Assert.Equal(VisualProgressAction.Wait, candidate.Action);
        Assert.Null(candidate.Key);
        Assert.Null(candidate.Point);
        Assert.Equal(VisualProgressAction.Wait, VisualProgressContinuation.AfterReview(candidate, true).Action);
    }

    [Theory]
    [InlineData("npc-quest-menu.png")]
    [InlineData("npc-quest-menu-2.png")]
    public async Task NPCのクエスト印とメニューが揃ったらクエスト名を固定せず項目を選ぶ(string filename)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var frame = ReadFrame(Path.Combine(fixture, filename));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var recognizer = new VisualProgressRecognizer(profile);
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("npc-quest-option", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.InRange(choice.Point![0], 0.34, 0.45);
        var renamed = new WindowsGameOcrResult("", "ja", 0, ocr.Words
            .Where(word => word.Y < 1000 || word.X < 575 || word.X > 770)
            .Append(new("別のクエスト", 630, 1032, 110, 26)).ToArray());
        Assert.Equal("npc-quest-option", recognizer.Recognize(renamed, frame.Width, frame.Height, viewport, frame).RuleId);
        var bytes = frame.Pixels!.Bgra8.ToArray();
        var color = bytes.AsSpan(1025 * frame.Pixels.Stride + 610 * 4, 4).ToArray();
        for (var y = 1028; y < 1060; y++)
            for (var x = 600; x < 620; x++) color.CopyTo(bytes.AsSpan(y * frame.Pixels.Stride + x * 4));
        var noMarker = frame with { Pixels = new FramePixels(bytes, frame.Pixels.Stride) };
        Assert.NotEqual("npc-quest-option", recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, noMarker).RuleId);
        var merchant = ReadFrame(Path.Combine(fixture, "merchant-menu.png"));
        var merchantOcr = await new WindowsGameOcrRecognizer().RecognizeAsync(merchant);
        Assert.Equal("merchant-exit", recognizer.Recognize(merchantOcr, merchant.Width, merchant.Height,
            new(1, 31, merchant.Width - 2, merchant.Height - 32), merchant).RuleId);
    }

    [Fact]
    public async Task NPCのメニュー項目が増えてクエスト位置が移っても印から選ぶ()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var frame = ReadFrame(Path.Combine(fixture, "npc-quest-menu-extra.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var recognizer = new VisualProgressRecognizer(VisualProgressProfile.Load(Path.Combine(fixture, "progress.json")));
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("npc-quest-option", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.InRange(choice.Point![0], 0.25, 0.39);
        Assert.InRange(choice.Point[1], 0.9, 0.99);

        var bytes = frame.Pixels!.Bgra8.ToArray();
        var color = bytes.AsSpan(1040 * frame.Pixels.Stride + 445 * 4, 4).ToArray();
        for (var y = 1028; y < 1060; y++)
            for (var x = 450; x < 470; x++) color.CopyTo(bytes.AsSpan(y * frame.Pixels.Stride + x * 4));
        var noMarker = frame with { Pixels = new FramePixels(bytes, frame.Pixels.Stride) };
        Assert.NotEqual("npc-quest-option", recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, noMarker).RuleId);
    }

    [Fact]
    public async Task 会話の選択肢が表示されている間はSpaceを送らず待機する()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var frame = ReadFrame(Path.Combine(fixture, "dialogue-topic-choices.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var recognizer = new VisualProgressRecognizer(profile);
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("dialogue-choice-wait", choice.RuleId);
        Assert.Equal(VisualProgressAction.Wait, choice.Action);
        Assert.Null(choice.Key);
        Assert.Null(choice.Point);
        var renamed = new WindowsGameOcrResult("", "ja", 0, ocr.Words.Where(word => word.Y < 1000)
            .Concat([new("別の話題A", 625, 1032, 160, 26), new("別の話題B", 850, 1032, 200, 26)]).ToArray());
        Assert.Equal(VisualProgressAction.Wait, recognizer.Recognize(renamed, frame.Width, frame.Height, viewport, frame).Action);
        var noChoices = new WindowsGameOcrResult("", "ja", 0, ocr.Words.Where(word => word.Y < 1000).ToArray());
        Assert.Equal(VisualProgressAction.Key, recognizer.Recognize(noChoices, frame.Width, frame.Height, viewport, frame).Action);
        var schedule = new VisualProgressSchedule(profile);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(0, choice, false, false, false).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(5000, choice, false, false, false).Action);
        Assert.Equal(VisualProgressAction.Review, schedule.Decide(10001, choice, false, false, false).Action);
    }

    [Fact]
    public async Task 会話の単一ラベルにSpaceが明示されている時だけ確定する()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var frame = ReadFrame(Path.Combine(fixture, "dialogue-single-space.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var recognizer = new VisualProgressRecognizer(VisualProgressProfile.Load(Path.Combine(fixture, "progress.json")));
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("dialogue-space-confirm", choice.RuleId);
        Assert.Equal(VisualProgressAction.Key, choice.Action);
        Assert.Equal("Key:Space", choice.Key);
        var noHint = new WindowsGameOcrResult("", "ja", 0,
            ocr.Words.Where(word => word.Y < 990 || word.Y > 1030).ToArray());
        Assert.Equal(VisualProgressAction.Wait, recognizer.Recognize(noHint, frame.Width, frame.Height, viewport, frame).Action);

        var multiple = ReadFrame(Path.Combine(fixture, "dialogue-topic-choices.png"));
        var multipleOcr = await new WindowsGameOcrRecognizer().RecognizeAsync(multiple);
        var multipleWithHint = new WindowsGameOcrResult("", "ja", 0,
            multipleOcr.Words.Append(new("Space", 610, 1010, 42, 18)).ToArray());
        Assert.Equal(VisualProgressAction.Wait, recognizer.Recognize(multipleWithHint,
            multiple.Width, multiple.Height, new(1, 31, multiple.Width - 2, multiple.Height - 32), multiple).Action);
    }

    [Fact]
    public async Task 台詞が同じでも下部の送り印が現れたら別の確認段階として一度送る()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var frame = ReadFrame(Path.Combine(fixture, "dialogue-item-reveal.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var recognizer = new VisualProgressRecognizer(profile);
        var revealed = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("dialogue-lower-cue", revealed.RuleId);
        Assert.Equal("Key:Space", revealed.Key);
        var bytes = frame.Pixels!.Bgra8.ToArray();
        var color = bytes.AsSpan(1025 * frame.Pixels.Stride + 820 * 4, 4).ToArray();
        for (var y = 1010; y < 1050; y++)
            for (var x = 835; x < 875; x++) color.CopyTo(bytes.AsSpan(y * frame.Pixels.Stride + x * 4));
        var beforeFrame = frame with { Pixels = new FramePixels(bytes, frame.Pixels.Stride) };
        var before = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, beforeFrame);
        Assert.Equal("npc-dialogue-cue", before.RuleId);
        Assert.NotEqual(before.Signature, revealed.Signature);
        var schedule = new VisualProgressSchedule(profile);
        schedule.RecordInput(0, before);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(1000, revealed, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Key, schedule.Decide(1700, revealed, false, false, true).Action);
        schedule.RecordInput(1700, revealed);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(2000, revealed, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(4000, revealed, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Review, schedule.Decide(6701, revealed, false, false, true).Action);
    }

    [Fact]
    public async Task 推奨の見出しを読めないレベルガイドでは活動を自動選択せず待機する()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var frame = ReadFrame(Path.Combine(fixture, "level-guide.png"));
        var recognized = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var ocr = new WindowsGameOcrResult("", "ja", 0,
            recognized.Words.Where(word => word.Y < 395 || word.Y > 492).ToArray());
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var recognizer = new VisualProgressRecognizer(profile);
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("level-guide-wait", choice.RuleId);
        Assert.Equal(VisualProgressAction.Wait, choice.Action);
        Assert.Null(choice.Key);
        Assert.Null(choice.Point);
        var renamed = new WindowsGameOcrResult("", "ja", 0, ocr.Words.Where(word => word.Y < 190)
            .Append(new("Lv.55 別の活動", 400, 500, 250, 30)).ToArray());
        Assert.Equal(VisualProgressAction.Wait, recognizer.Recognize(renamed, frame.Width, frame.Height, viewport, frame).Action);
        var noTitle = new WindowsGameOcrResult("", "ja", 0, ocr.Words.Where(word => word.Y > 100).ToArray());
        Assert.NotEqual("level-guide-wait", recognizer.Recognize(noTitle, frame.Width, frame.Height, viewport, frame).RuleId);
        var schedule = new VisualProgressSchedule(profile);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(0, choice, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(5000, choice, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Review, schedule.Decide(10001, choice, false, false, true).Action);
    }

    [Theory]
    [InlineData("level-guide.png")]
    [InlineData("level-guide-area2.png")]
    public async Task レベルガイドの明確な推奨と一意な移動先がある時だけ選ぶ(string filename)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var frame = ReadFrame(Path.Combine(fixture, filename));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var recognizer = new VisualProgressRecognizer(VisualProgressProfile.Load(Path.Combine(fixture, "progress.json")));
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("level-guide-recommended", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.InRange(choice.Point![0], 0.4, 0.55);
        Assert.InRange(choice.Point[1], 0.54, 0.64);
        var multiple = new WindowsGameOcrResult("", "ja", 0,
            ocr.Words.Append(new("今すぐ移動", 1200, 640, 120, 25)).ToArray());
        Assert.Equal(VisualProgressAction.Review, recognizer.Recognize(multiple,
            frame.Width, frame.Height, viewport, frame).Action);
    }

    [Fact]
    public async Task 商人メニューは通常会話のSpaceよりEsc終了を優先する()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var frame = ReadFrame(Path.Combine(fixture, "merchant-menu.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var recognizer = new VisualProgressRecognizer(profile);
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("merchant-exit", choice.RuleId);
        Assert.Equal("Key:Esc", choice.Key);
        Assert.Null(choice.Point);
        var withoutExit = new WindowsGameOcrResult("", "ja", 0,
            ocr.Words.Where(word => word.X < viewport.X + viewport.Width * 0.58
                || word.Y < viewport.Y + viewport.Height * 0.88).ToArray());
        Assert.NotEqual("merchant-exit", recognizer.Recognize(withoutExit, frame.Width, frame.Height, viewport, frame).RuleId);
    }

    [Fact]
    public async Task 戦利品のSpaceは見出しを十秒確認して一回だけ送る()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var frame = ReadFrame(Path.Combine(fixture, "loot-reveal.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var candidate = new VisualProgressRecognizer(profile).Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal("loot-continue", candidate.RuleId);
        Assert.Equal(VisualProgressAction.Key, candidate.Action);
        Assert.Equal("Key:Space", candidate.Key);
        Assert.Null(candidate.Point);
        var schedule = new VisualProgressSchedule(profile);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(0, candidate, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(9999, candidate, false, false, true, sceneChanged: true).Action);
        Assert.Equal(VisualProgressAction.Key, schedule.Decide(10000, candidate, false, false, true, sceneChanged: true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(10000, candidate, true, false, true).Action);
        schedule.RecordInput(10000, candidate);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(12000, candidate, false, false, true).Action);
    }

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
    public void 既知の演出では待機し操作可能な会話が現れたら入力規則へ移る()
    {
        var wait = new VisualProgressRule("animation", [new("スキップ", [0.8, 0, 0.2, 0.2])], Priority: -10, WaitForChange: true);
        var dialogue = new VisualProgressRule("dialogue", [new("スキップ", [0.8, 0, 0.2, 0.2]), new("", [0.2, 0.2, 0.6, 0.3])], "Key:Space");
        var profile = new VisualProgressProfile(1, [wait, dialogue], []);
        var recognizer = new VisualProgressRecognizer(profile);
        var skip = new WindowsGameOcrWord("スキップ", 900, 40, 60, 20);
        var waiting = recognizer.Recognize(Ocr([skip]), 1000, 600, new(0, 0, 1000, 600));
        Assert.Equal(VisualProgressAction.Wait, waiting.Action);
        Assert.Null(waiting.Key);
        var schedule = new VisualProgressSchedule(profile);
        schedule.RecordInput(0, new(VisualProgressAction.Key, "previous", "previous", "Key:Space"));
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(100, waiting, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(800, waiting, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(30000, waiting, false, false, true, sceneChanged: true).Action);
        var ready = recognizer.Recognize(Ocr([skip, new("会話の本文", 300, 150, 200, 30)]), 1000, 600, new(0, 0, 1000, 600));
        Assert.Equal(VisualProgressAction.Key, ready.Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(31000, ready, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Key, schedule.Decide(31600, ready, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(32000, ready, true, false, true).Action);
    }

    [Fact]
    public async Task Spaceと確認が表示された案内はゲーム固有の見出しを要求せず進める()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var frame = ReadFrame(Path.Combine(fixture, "space-confirm.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var choice = new VisualProgressRecognizer(profile).Recognize(ocr, frame.Width, frame.Height,
            new(1, 31, frame.Width - 2, frame.Height - 32), frame);
        Assert.Equal(VisualProgressAction.Key, choice.Action);
        Assert.Equal("Key:Space", choice.Key);
        Assert.Equal("space-confirm", choice.RuleId);
    }

    [Fact]
    public async Task 承諾とやめるがある会話ではSpaceより承諾クリックを優先する()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var frame = ReadFrame(Path.Combine(fixture, "dialogue-accept.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var recognizer = new VisualProgressRecognizer(profile);
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.Equal("dialogue-accept", choice.RuleId);
        Assert.InRange(choice.Point![0], 0.44, 0.49);
        Assert.InRange(choice.Point[1], 0.90, 0.96);
        Assert.NotEqual("dialogue-accept", recognizer.Recognize(
            Ocr(ocr.Words.Where(word => !(word.X >= frame.Width * 0.5 && word.Y >= frame.Height * 0.75)).ToArray()),
            frame.Width, frame.Height, viewport, frame).RuleId);
        Assert.NotEqual("dialogue-accept", recognizer.Recognize(
            Ocr([new("承諾", 200, 150, 60, 30), new("やめる", 280, 150, 60, 30)]),
            frame.Width, frame.Height, viewport, frame).RuleId);
    }

    [Fact]
    public async Task 自動着用の確認窓は背景の同名ボタンより優先してSpaceで確定する()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var frame = ReadFrame(Path.Combine(fixture, "loot-equip-confirm.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var choice = new VisualProgressRecognizer(profile).Recognize(ocr, frame.Width, frame.Height,
            new(1, 31, frame.Width - 2, frame.Height - 32), frame);
        Assert.Equal(VisualProgressAction.Key, choice.Action);
        Assert.Equal("Key:Space", choice.Key);
        Assert.Equal("auto-equip-confirm", choice.RuleId);
    }

    [Fact]
    public async Task 戦利品の見出しが描画領域の中央寄りでも表示済みの操作ボタンを認識する()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008");
        var profile = VisualProgressProfile.Load(Path.Combine(fixture, "progress.json"));
        var frame = ReadFrame(Path.Combine(fixture, "loot-buttons.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var viewport = new FrameRect(1, 31, frame.Width - 2, frame.Height - 32);
        var choice = new VisualProgressRecognizer(profile).Recognize(ocr, frame.Width, frame.Height, viewport, frame);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.Equal("loot-auto-equip", choice.RuleId);
        var exitProfile = profile with { Rules = profile.Rules.Where(rule => rule.Id != "loot-auto-equip").ToArray() };
        Assert.Equal("dungeon-exit", new VisualProgressRecognizer(exitProfile).Recognize(ocr, frame.Width, frame.Height, viewport, frame).RuleId);
    }

    [Fact]
    public void 既知の待機画面も複数回観測して静止が続けば入力せず通知する()
    {
        var schedule = new VisualProgressSchedule(Profile());
        var waiting = new VisualProgressChoice(VisualProgressAction.Wait, "演出", "演出");
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(0, waiting, false, false, false).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(12000, waiting, false, false, false).Action);
        var review = schedule.Decide(12500, waiting, false, false, false);
        Assert.Equal(VisualProgressAction.Review, review.Action);
        Assert.Contains("演出", review.Detail);
        Assert.Null(review.Key);
        Assert.Null(review.Point);
    }

    [Fact]
    public void 待機画面の変化は静止時間を数え直し別の待機画面にも猶予を与える()
    {
        var schedule = new VisualProgressSchedule(Profile());
        var waiting = new VisualProgressChoice(VisualProgressAction.Wait, "演出", "演出");
        foreach (var at in new[] { 0, 5000, 12000, 18000 })
            Assert.Equal(VisualProgressAction.Wait, schedule.Decide(at, waiting, false, false, true, sceneChanged: true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(23000, waiting, false, false, true).Action);
        var next = waiting with { RuleId = "戦利品", Signature = "戦利品" };
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(27000, next, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(32000, next, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Review, schedule.Decide(37000, next, false, false, true).Action);
    }

    [Fact]
    public void 固定ラベルの周囲のOCR変動は操作候補を変えず会話本文の変更は区別する()
    {
        var rule = new VisualProgressRule("prompt", [new("画面を押してください", [0, 0, 1, 1])], "Key:Space");
        var recognizer = new VisualProgressRecognizer(new(1, [rule], []));
        var first = recognizer.Recognize(Ocr([new("イこ画面を押してください", 100, 100, 200, 30)]), 1000, 600, new(0, 0, 1000, 600));
        var second = recognizer.Recognize(Ocr([new("画面を押してください", 100, 100, 200, 30)]), 1000, 600, new(0, 0, 1000, 600));
        Assert.Equal(first.Signature, second.Signature);
        var schedule = new VisualProgressSchedule(new(1, [rule], []));
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(0, first, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Key, schedule.Decide(600, second, false, false, true).Action);
        var dialogue = new VisualProgressRecognizer(new(1, [rule with { When = [new("", [0, 0, 1, 1])] }], []));
        Assert.NotEqual(dialogue.Recognize(Ocr([new("最初の会話", 100, 100, 200, 30)]), 1000, 600, new(0, 0, 1000, 600)).Signature,
            dialogue.Recognize(Ocr([new("次の会話", 100, 100, 200, 30)]), 1000, 600, new(0, 0, 1000, 600)).Signature);
    }

    [Fact]
    public async Task クリア画面の協力ボーナスと行動不能の説明を選択や敗北と誤認しない()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "fixtures", "visual-recovery", "mabinogi-20261008");
        var frame = ReadFrame(Path.Combine(fixture, "dungeon-clear.png"));
        var ocr = await new WindowsGameOcrRecognizer().RecognizeAsync(frame);
        var recovery = new VisualRecoveryRecognizer(VisualRecoveryProfile.Load(Path.Combine(fixture, "profile.json")));
        Assert.False(recovery.HasIncapacitatedDisplay(ocr.Text));
        var recognizer = new VisualProgressRecognizer(VisualProgressProfile.Load(Path.Combine(fixture, "progress.json")));
        var choice = recognizer.Recognize(ocr, frame.Width, frame.Height, new(1, 31, 1506, 814), frame);
        Assert.Equal(VisualProgressAction.Key, choice.Action);
        Assert.Equal("screen-prompt", choice.RuleId);
        Assert.Equal("Key:Space", choice.Key);
        var defeat = ReadFrame(Path.Combine(fixture, "../../../evidence/mabinogi-key-assist-20261008/potion-monitor-defeat.png"));
        var defeatOcr = await new WindowsGameOcrRecognizer().RecognizeAsync(defeat);
        Assert.True(recovery.HasIncapacitatedDisplay(defeatOcr.Text));
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
    public void 会話は通常の十二秒待ちを使わず次の台詞の安定後に送る()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var profile = VisualProgressProfile.Load(Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008/progress.json"));
        foreach (var id in new[] { "npc-dialogue-cue", "npc-dialogue-skip", "npc-dialogue" })
        {
            var rule = profile.Rules.Single(rule => rule.Id == id);
            var timer = new VisualKeyAssistSchedule(0, () => 12000);
            var flow = new VisualProgressSchedule(profile);
            var first = new VisualProgressChoice(VisualProgressAction.Key, id, id + ":最初の台詞", "Key:Space");
            bool Due(long now) => timer.Decide(now, false, !rule.Timed) is VisualKeyAssistDecision.Cue or VisualKeyAssistDecision.Timed;
            Assert.Equal(VisualProgressAction.Wait, flow.Decide(0, first, false, false, Due(0)).Action);
            Assert.Equal(VisualProgressAction.Key, flow.Decide(600, first, false, false, Due(600)).Action);
            flow.RecordInput(600, first); timer.RecordInput(600);
            Assert.Equal(VisualProgressAction.Wait, flow.Decide(1400, first, false, false, Due(1400)).Action);
            var next = first with { Signature = id + ":次の台詞" };
            Assert.Equal(VisualProgressAction.Wait, flow.Decide(1500, next, false, false, Due(1500)).Action);
            Assert.Equal(VisualProgressAction.Key, flow.Decide(2100, next, false, false, Due(2100)).Action);
            Assert.Equal(VisualKeyAssistDecision.Wait, timer.Decide(2100, false, false));
            Assert.Equal(VisualProgressAction.Wait, flow.Decide(2200, next, true, false, true).Action);
        }
    }

    [Fact]
    public void No_result_stops_without_retry_and_unknown_screen_gets_a_short_loading_grace()
    {
        var schedule = new VisualProgressSchedule(Profile());
        var candidate = new VisualProgressChoice(VisualProgressAction.Key, "reward", "reward:確認", "Key:Space");
        schedule.RecordInput(100, candidate);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(1000, candidate, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(3000, candidate, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Review, schedule.Decide(5100, candidate, false, false, true).Action);
        var unknown = new VisualProgressSchedule(Profile());
        Assert.Equal(VisualProgressAction.Wait, unknown.Decide(0, new(VisualProgressAction.Normal), false, false, true).Action);
        Assert.Equal(VisualProgressAction.Wait, unknown.Decide(5000, new(VisualProgressAction.Normal), false, false, true).Action);
        Assert.Equal(VisualProgressAction.Review, unknown.Decide(10000, new(VisualProgressAction.Normal), false, false, true).Action);
        Assert.Equal(VisualProgressAction.Normal, unknown.Decide(11000, new(VisualProgressAction.Normal), false, true, true).Action);
    }

    [Fact]
    public void 未知画面は変化中なら待ち続け同じ画面を複数回観測してから通知する()
    {
        var schedule = new VisualProgressSchedule(Profile());
        var unknown = new VisualProgressChoice(VisualProgressAction.Normal);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(0, unknown, false, false, true).Action);
        // 長い観測間隔だけで判定を成立させない。
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(12000, unknown, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(12500, unknown, false, false, true, sceneChanged: true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(18000, unknown, false, false, true, sceneChanged: true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(24000, unknown, false, false, true, sceneChanged: true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(29000, unknown, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Review, schedule.Decide(34000, unknown, false, false, true).Action);
    }

    [Fact]
    public void 操作後の未知の演出も観測を続け既知画面へ到着したら結果待ちを解消する()
    {
        var schedule = new VisualProgressSchedule(Profile());
        schedule.RecordInput(0, new(VisualProgressAction.Key, "prompt", "prompt", "Key:Space"));
        var unknown = new VisualProgressChoice(VisualProgressAction.Normal);
        foreach (var at in new[] { 1000, 6000, 12000, 18000 })
            Assert.Equal(VisualProgressAction.Wait, schedule.Decide(at, unknown, false, false, true, sceneChanged: true).Action);
        var known = new VisualProgressChoice(VisualProgressAction.Key, "next", "next", "Key:Space");
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(20000, known, false, false, true).Action);
        Assert.Equal(VisualProgressAction.Key, schedule.Decide(20600, known, false, false, true).Action);
    }

    [Fact]
    public void 画像の小さな揺れは静止と扱いゆっくりした変化は基準画像から累積して検出する()
    {
        CapturedFrame Solid(byte luma)
        {
            var pixels = new byte[32 * 16 * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = pixels[i + 1] = pixels[i + 2] = luma; pixels[i + 3] = 255; }
            return new(ContractSchemaVersions.Revision03, "test", CaptureBackend.WindowsGraphicsCapture,
                1, 0, DateTimeOffset.UnixEpoch, 32, 16, "BGRA8", 96, 96, 1, 0, 0,
                Pixels: new FramePixels(pixels, 32 * 4));
        }
        var monitor = new VisualProgressSceneMonitor();
        var viewport = new FrameRect(0, 0, 32, 16);
        Assert.False(monitor.Observe(Solid(20), viewport).Changed);
        Assert.False(monitor.Observe(Solid(22), viewport).Changed);
        Assert.False(monitor.Observe(Solid(25), viewport).Changed);
        Assert.True(monitor.Observe(Solid(26), viewport).Changed);
        Assert.False(monitor.Observe(Solid(26), viewport).Changed);
    }

    [Fact]
    public void 保存済みクリア画面から戦利品表示への変化を画像で検出する()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        var frame = ReadFrame(Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008/dungeon-clear.png"));
        var next = ReadFrame(Path.Combine(directory.FullName, "evidence/mabinogi-key-assist-20261008/recovery-only-loot.png"));
        var monitor = new VisualProgressSceneMonitor();
        var viewport = new FrameRect(1, 31, 1506, 814);
        Assert.False(monitor.Observe(frame, viewport).Changed);
        Assert.False(monitor.Observe(frame, viewport).Changed);
        Assert.True(monitor.Observe(next, viewport).Changed);
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
