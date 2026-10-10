using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>サイドクエストの受注機能の流れを、実機で取得したクエスト一覧の画面で確かめる。</summary>
public sealed class SideQuestModeTests
{
    private const string Stage = "side-quest";
    private const string CardStage = "side-quest-card";

    private static string Fixture(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008", name);
    }

    private static VisualProgressProfile Profile() => VisualProgressProfile.Load(Fixture("progress.json"));

    // 帯の地の色は追跡中のクエストで変わる（緑・青・オレンジ）。帯の文字を拡大して読んで見つける。
    [Theory]
    [InlineData("side-quest-hud.png")]
    [InlineData("side-quest-hud-blue.png")]
    [InlineData("side-quest-hud-orange.png")]
    public async Task 通常の画面では帯の色にかかわらずクエストの帯を押す(string image)
    {
        var choice = await Recognize(image, Stage);
        Assert.Equal("side-quest-open", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.InRange(choice.Point![0], 0.955, 0.98);
        Assert.InRange(choice.Point[1], 0.19, 0.21);
        Assert.True(choice.AllowWhileInhibited);
    }

    [Fact]
    public async Task サイドが選ばれていなければサイドを押す()
    {
        var choice = await Recognize("side-quest-list-all.png", Stage);
        Assert.Equal("side-quest-tab", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.InRange(choice.Point![0], 0.775, 0.8);
        Assert.InRange(choice.Point[1], 0.16, 0.18);
    }

    // 通常のBotがコンパスのSpaceを押した直後に、財貨が足りない知らせが出たら、クエストの帯を押して受注の流れへ入る。
    [Fact]
    public void Spaceの直後に財貨が足りない知らせが出たら受注の流れを始める()
    {
        var recognizer = new VisualProgressRecognizer(Profile());
        var viewport = new FrameRect(0, 0, 1000, 600);
        WindowsGameOcrResult Screen(params WindowsGameOcrWord[] words) => new(string.Concat(words.Select(word => word.Text)), "ja", 0, words);
        var toast = new[] { new WindowsGameOcrWord("ミッションに", 440, 250, 120, 18),
            new WindowsGameOcrWord("チャレンジできる財貨が不足しています。", 350, 275, 300, 18) };
        var tab = new WindowsGameOcrWord("クエスト", 955, 100, 30, 12);

        var start = recognizer.Recognize(Screen([.. toast, tab]), 1000, 600, viewport, after: "compass-space");
        Assert.Equal("side-quest-start", start.RuleId);
        Assert.Equal(VisualProgressAction.Click, start.Action);
        Assert.Equal("side-quest", start.NextStage);
        Assert.InRange(start.Point![0], 0.95, 0.99);
        Assert.InRange(start.Point[1], 0.16, 0.19);
        Assert.True(start.AllowWhileInhibited);
        // 即時の画像（コンパス）が出ている間に読む時は、直後の規則だけを評価する。
        Assert.Equal("side-quest-start",
            recognizer.Recognize(Screen([.. toast, tab]), 1000, 600, viewport, after: "compass-space", afterOnly: true).RuleId);

        // Spaceの直後でなければ始めない。知らせが無い時も、ほかの規則の直後も始めない。
        Assert.NotEqual("side-quest-start", recognizer.Recognize(Screen([.. toast, tab]), 1000, 600, viewport).RuleId);
        Assert.NotEqual("side-quest-start", recognizer.Recognize(Screen([.. toast, tab]), 1000, 600, viewport, after: "feather-t").RuleId);
        Assert.Equal(VisualProgressAction.Normal,
            recognizer.Recognize(Screen(tab), 1000, 600, viewport, after: "compass-space", afterOnly: true).Action);
    }

    [Fact]
    public void 操作の直後かどうかは送ってから3秒で見る()
    {
        var schedule = new VisualProgressSchedule(Profile());
        Assert.Null(schedule.RecentRule(0));
        schedule.RecordInput(1000, new(VisualProgressAction.Key, "compass-space", "compass-space", "Key:Space", Immediate: true));
        Assert.Equal("compass-space", schedule.RecentRule(1200));
        Assert.Equal("compass-space", schedule.RecentRule(4000));
        Assert.Null(schedule.RecentRule(4001));
        // 受注の流れを始める操作で、受注の段階へ入る。
        schedule.RecordInput(4500, new(VisualProgressAction.Click, "side-quest-start", "side-quest-start:", Point: [0.97, 0.2], NextStage: Stage));
        Assert.Equal(Stage, schedule.Stage);
    }

    // サイドを選んだ後は、一番上のカードの真ん中を押してカードを開く。一覧は送らない。
    [Theory]
    [InlineData("side-quest-list-top.png")]
    [InlineData("side-quest-list-top-progress.png")]
    [InlineData("side-quest-list-middle.png")]
    [InlineData("side-quest-list-five-tabs.png")]
    [InlineData("side-quest-list-tracked-top.png")]
    public async Task サイドを選んだ後は一番上のカードの真ん中を押す(string image)
    {
        var choice = await Recognize(image, Stage);
        Assert.Equal("side-quest-select", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        // 描画領域の横80％・縦37％（一番上のカードの説明のあたり）。
        Assert.InRange(choice.Point![0], 0.795, 0.805);
        Assert.InRange(choice.Point[1], 0.382, 0.392);
        Assert.Equal(CardStage, choice.NextStage);
        Assert.Null(choice.Remembered);
    }

    // カードを押した後は、そのカードの内容を覚えてから「進行」を押し、受注の流れを終える。
    [Theory]
    [InlineData("side-quest-list-tracked-top.png", 0.542, "メモリーライト", "たまに思い出を振り返りたくなるでしょ", "トレボー")]
    [InlineData("side-quest-list-top-progress.png", 0.518, "料理入門", "食料品店で砂糖を購入", "肝試し")]
    [InlineData("side-quest-list-top.png", 0.448, "料理入門", "料理を学びたいけど", "肝試し")]
    // side-quest-list-middle.png（開いていないカードが並び、ボタンが明るい緑の画面）は、先頭の「進行」を読めず2枚目を選ぶ。
    // 受注の流れでは、先にカードを押して開く（ボタンが濃い緑になる）ので、この段階にその画面は来ない。
    // 上のカードが切れて見えている時は、押した位置より下の「進行」のカードを覚えて押す。
    [InlineData("side-quest-list-five-tabs.png", 0.577, "料理入門", "この本に書いてある通りにやれば", "鳥の巣")]
    public async Task カードを押した後は内容を覚えて進行を押し受注の流れを終える(
        string image, double y, string title, string body, string next)
    {
        var choice = await Recognize(image, CardStage);
        Assert.Equal("side-quest-accept", choice.RuleId);
        Assert.Equal(VisualProgressAction.Click, choice.Action);
        Assert.InRange(choice.Point![0], 0.9, 0.93);
        Assert.InRange(choice.Point[1], y - 0.015, y + 0.015);
        Assert.True(choice.EndStage);
        Assert.True(choice.AllowWhileInhibited);
        var remembered = choice.Remembered!;
        Assert.Equal("side-quest", remembered.Name);
        Assert.Contains(remembered.Lines, line => line.Contains(title, StringComparison.Ordinal));
        Assert.Contains(remembered.Lines, line => line.Contains(body, StringComparison.Ordinal));
        // 覚えるのは押すカードだけ。上下のカードの名前は入れない。
        Assert.DoesNotContain(remembered.Lines, line => line.Contains(next, StringComparison.Ordinal));
        Assert.DoesNotContain(remembered.Lines, line => line.Contains("賞金首", StringComparison.Ordinal));
        Assert.InRange(remembered.Bounds[1] + remembered.Bounds[3], choice.Point[1] + 0.03, choice.Point[1] + 0.06);
    }
    // タブが5つの地域（エリア任務が増える）では、サイドのタブの位置が左へずれる。
    [Fact]
    public void サイドのタブが選ばれているかはタブが4つでも5つでも見分ける()
    {
        var profile = Profile();
        var selected = profile.Rules.Single(rule => rule.Id == "side-quest-select").Areas!.Single();
        var unselected = profile.Rules.Single(rule => rule.Id == "side-quest-tab").Areas!.Single();
        foreach (var image in new[] { "side-quest-list-top.png", "side-quest-list-middle.png", "side-quest-list-five-tabs.png" })
        {
            var frame = ReadFrame(Fixture(image));
            Assert.True(VisualProgressRecognizer.AreaHolds(frame, Viewport(frame), selected), image);
            Assert.False(VisualProgressRecognizer.AreaHolds(frame, Viewport(frame), unselected), image);
        }
        var all = ReadFrame(Fixture("side-quest-list-all.png"));
        Assert.False(VisualProgressRecognizer.AreaHolds(all, Viewport(all), selected));
        Assert.True(VisualProgressRecognizer.AreaHolds(all, Viewport(all), unselected));
    }

    [Theory]
    [InlineData("side-quest-hud.png")]
    [InlineData("side-quest-hud-blue.png")]
    [InlineData("side-quest-hud-orange.png")]
    [InlineData("side-quest-list-all.png")]
    [InlineData("side-quest-list-middle.png")]
    [InlineData("side-quest-list-top.png")]
    [InlineData("side-quest-list-top-progress.png")]
    [InlineData("side-quest-list-five-tabs.png")]
    [InlineData("side-quest-list-tracked-top.png")]
    public async Task 受注の段階の外ではクエストの一覧を操作しない(string image)
    {
        var choice = await Recognize(image, null);
        Assert.False(choice.RuleId?.StartsWith("side-quest", StringComparison.Ordinal) == true, choice.RuleId);
        Assert.NotEqual(VisualProgressAction.Flick, choice.Action);
    }

    [Fact]
    public void モードは通常の画面が続いた時に段階を始め受注の操作で段階を終える()
    {
        var profile = Profile();
        var mode = profile.Modes!.Single();
        var schedule = new VisualProgressSchedule(profile);
        var normal = new VisualProgressChoice(VisualProgressAction.Normal);
        schedule.Decide(0, normal, false, true, true);
        Assert.Null(schedule.Stage);

        schedule.SetMode(mode);
        Assert.Equal("side-quest", schedule.ModeId);
        // 通常の画面でない間（会話など）は始めない。
        schedule.Decide(1000, normal, false, false, true);
        schedule.Decide(4000, normal, false, false, true);
        Assert.Null(schedule.Stage);
        // 通常の画面が2秒続いたら始める。停止表示中でも始める。
        schedule.Decide(5000, normal, true, true, true);
        schedule.Decide(6900, normal, true, true, true);
        Assert.Null(schedule.Stage);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(7000, normal, true, true, true).Action);
        Assert.Equal(Stage, schedule.Stage);

        schedule.RecordInput(7500, new(VisualProgressAction.Click, "side-quest-open", "side-quest-open:", Point: [0.97, 0.2]));
        Assert.Equal(Stage, schedule.Stage);
        schedule.RecordInput(8500, new(VisualProgressAction.Click, "side-quest-select", "side-quest-select:", Point: [0.8, 0.39], NextStage: CardStage));
        Assert.Equal(CardStage, schedule.Stage);
        schedule.RecordInput(9000, new(VisualProgressAction.Click, "side-quest-accept", "side-quest-accept:進行", Point: [0.91, 0.45], EndStage: true));
        Assert.Null(schedule.Stage);
        // 受注した後は、通常の画面が続いても始め直さない。
        schedule.Decide(10000, normal, false, true, true);
        schedule.Decide(20000, normal, false, true, true);
        Assert.Null(schedule.Stage);
    }

    [Fact]
    public void 解除するとモードが始めた段階をその場で終え始め直さない()
    {
        var profile = Profile();
        var schedule = new VisualProgressSchedule(profile);
        var normal = new VisualProgressChoice(VisualProgressAction.Normal);
        schedule.SetMode(profile.Modes!.Single());
        schedule.Decide(0, normal, false, true, true);
        schedule.Decide(2000, normal, false, true, true);
        schedule.RecordInput(2500, new(VisualProgressAction.Click, "side-quest-open", "side-quest-open:", Point: [0.97, 0.2]));
        Assert.Equal(Stage, schedule.Stage);

        schedule.SetMode(null);
        Assert.Null(schedule.Stage);
        Assert.Null(schedule.ModeId);
        schedule.Decide(3000, normal, false, true, true);
        schedule.Decide(9000, normal, false, true, true);
        Assert.Null(schedule.Stage);
    }

    [Fact]
    public void モードの段階が何も送らずに終わった時は1回だけ知らせ間を置いてやり直す()
    {
        var profile = Profile();
        var schedule = new VisualProgressSchedule(profile);
        var normal = new VisualProgressChoice(VisualProgressAction.Normal);
        schedule.SetMode(profile.Modes!.Single());
        schedule.Decide(0, normal, false, true, true);
        schedule.Decide(2000, normal, false, true, true);
        Assert.Equal(Stage, schedule.Stage);
        Assert.Null(schedule.TakeModeFailure());
        // クエストの帯を見つけられないまま、通常の画面が続いた。
        schedule.Decide(2250, normal, false, true, true);
        schedule.Decide(5250, normal, false, true, true);
        Assert.Null(schedule.Stage);
        Assert.Contains("サイドクエストモード", schedule.TakeModeFailure());
        Assert.Null(schedule.TakeModeFailure());
        // 30秒は始め直さず、その間の通常の操作は止めない。
        var compass = new VisualProgressChoice(VisualProgressAction.Key, "compass-space", "compass-space", "Key:Space", Immediate: true);
        Assert.Same(compass, schedule.Decide(20000, compass, false, true, true));
        Assert.Null(schedule.Stage);
        // 30秒たったら、通常の画面が2秒続いた時にやり直す。続けて失敗しても、知らせは重ねない。
        schedule.Decide(35250, normal, false, true, true);
        schedule.Decide(37250, normal, false, true, true);
        Assert.Equal(Stage, schedule.Stage);
        schedule.Decide(37500, normal, false, true, true);
        schedule.Decide(40500, normal, false, true, true);
        Assert.Null(schedule.Stage);
        Assert.Null(schedule.TakeModeFailure());
        // 一度でも操作を送れた後の失敗は、あらためて知らせる。
        schedule.Decide(70500, normal, false, true, true);
        schedule.Decide(72500, normal, false, true, true);
        schedule.RecordInput(73000, new(VisualProgressAction.Click, "side-quest-open", "side-quest-open:", Point: [0.97, 0.2]));
        schedule.SetMode(null);
        schedule.SetMode(profile.Modes!.Single());
        schedule.Decide(80000, normal, false, true, true);
        schedule.Decide(82000, normal, false, true, true);
        schedule.Decide(82250, normal, false, true, true);
        schedule.Decide(85250, normal, false, true, true);
        Assert.NotNull(schedule.TakeModeFailure());
    }

    [Fact]
    public void モードの段階を始めるまでは通常の画面でのほかの操作を送らない()
    {
        var profile = Profile();
        var schedule = new VisualProgressSchedule(profile);
        var compass = new VisualProgressChoice(VisualProgressAction.Key, "compass-space", "compass-space", "Key:Space", Immediate: true);
        // モードに入っていなければ、コンパスのSpaceはそのまま送る。
        Assert.Same(compass, schedule.Decide(0, compass, false, true, true));
        schedule.SetMode(profile.Modes!.Single());
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(1000, compass, false, true, true).Action);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(2900, compass, false, true, true).Action);
        Assert.Null(schedule.Stage);
        Assert.Equal(VisualProgressAction.Wait, schedule.Decide(3000, compass, false, true, true).Action);
        Assert.Equal(Stage, schedule.Stage);
        // 利用者の判断が要る表示は、モードの開始より先に扱う。
        var other = new VisualProgressSchedule(profile);
        other.SetMode(profile.Modes!.Single());
        var review = new VisualProgressChoice(VisualProgressAction.Review, Detail: "利用者の判断が必要な表示");
        Assert.Same(review, other.Decide(0, review, false, true, true));
        Assert.Null(other.Stage);
    }

    [Fact]
    public void 払う操作は始点と終点をNanoへ一回で送る()
    {
        var device = new Device();
        var sequence = new VisualProgressInputSequence(new NanoGameInteractionActions(device, new Mapper()));
        var receipt = sequence.Dispatch(new(VisualProgressAction.Flick, "side-quest-to-top", "side-quest-to-top:x",
            Point: [0.8, 0.5], FlickTo: [0.8, 0.65]), Observation(1));
        Assert.Equal(GameInteractionDispatchStatus.Dispatched, receipt.Status);
        Assert.Equal(GameInteractionOperations.Flick, receipt.Operation);
        Assert.Equal([(new SerialHidCursorPoint(800, 500), new SerialHidCursorPoint(800, 650))], device.Flicks);
        Assert.Null(sequence.Pending);
    }

    [Theory]
    [InlineData("""{ "Id": "x", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Flick": { "From": [0.5, 0.5], "To": [0.5] } }""")]
    [InlineData("""{ "Id": "x", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Flick": { "From": [0.5, 0.5], "To": [0.5, 1.2] } }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Flick": { "From": [0.5, 0.5], "To": [0.5, 0.6] } }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Areas": [] }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Areas": [{ "Bounds": [0, 0, 1, 1] }] }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Areas": [{ "Bounds": [0, 0, 1, 1], "Rgb": [0, 0] }] }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Areas": [{ "Bounds": [0, 0, 2, 1], "Flat": true }] }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Remember": { "Name": "a", "Bounds": [0, 0, 1, 1] } }""")]
    [InlineData("""{ "Id": "x", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Click": { "Text": "文字", "Bounds": [0, 0, 1, 1] }, "Remember": { "Name": "a b", "Bounds": [0, 0, 1, 1] } }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1], "Zoom": 5 }] }""")]
    [InlineData("""{ "Id": "x", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Point": [0.5] }""")]
    [InlineData("""{ "Id": "x", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Point": [0.5, 1.5] }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "Point": [0.5, 0.5] }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "After": "無い規則" }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "Immediate": true, "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "After": "x" }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "EndStage": true }""")]
    [InlineData("""{ "Id": "x", "Key": "Key:I", "Stage": "a", "NextStage": "b", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }], "EndStage": true }""")]
    public void 払う操作と画素の条件と覚える指定の不正な設定は読込みで拒否する(string rule) =>
        Assert.ThrowsAny<Exception>(() => Load($$"""{ "SchemaVersion": 1, "ReviewWhen": [], "Rules": [{{rule}}] }"""));

    [Theory]
    [InlineData("""{ "Id": "m", "Name": "モード", "Stage": "無い段階" }""")]
    [InlineData("""{ "Id": "m", "Name": "", "Stage": "a" }""")]
    [InlineData("""{ "Id": "m", "Name": "モード", "Stage": "a" }, { "Id": "m", "Name": "別", "Stage": "a" }""")]
    public void モードの不正な設定は読込みで拒否する(string modes) =>
        Assert.ThrowsAny<Exception>(() => Load($$"""
            { "SchemaVersion": 1, "ReviewWhen": [], "Modes": [{{modes}}],
              "Rules": [{ "Id": "x", "Key": "Key:I", "Stage": "a", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }] }] }
            """));

    [Fact]
    public void モードは解除するまでBotの操作の入口を作り直しても残る()
    {
        var root = Path.Combine(Path.GetTempPath(), "openlogicool-mode-" + Guid.NewGuid().ToString("N"));
        try
        {
            BotScriptMode[] modes = [new("test", "side-quest", "サイドクエストモード", false)];
            using (var intents = Intents(root, modes))
            {
                Assert.Null(intents.Current().Mode);
                Assert.Null(intents.ActiveMode("test"));
                Assert.Throws<ArgumentException>(() => intents.SetMode("無いモード"));
                Assert.Equal("サイドクエストモード", intents.SetMode("side-quest").Mode);
                Assert.Equal("side-quest", intents.ActiveMode("test"));
                Assert.True(Assert.Single(intents.ListModes()).Active);
            }
            using (var intents = Intents(root, modes))
            {
                Assert.Equal("side-quest", intents.ActiveMode("test"));
                Assert.Equal("サイドクエストモード", intents.Current().Mode);
                Assert.Null(intents.ClearMode().Mode);
                Assert.Null(intents.ActiveMode("test"));
            }
            using (var intents = Intents(root, modes))
                Assert.False(Assert.Single(intents.ListModes()).Active);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static HostBotScriptIntents Intents(string root, BotScriptMode[] modes) =>
        new([new("test", "試験", "説明")], root, new(), (_, _, _, _) => Task.FromResult(new BotScriptResult(false, "終了")), modes: modes);

    private static VisualProgressProfile Load(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            return VisualProgressProfile.Load(path);
        }
        finally { File.Delete(path); }
    }

    private static async Task<VisualProgressChoice> Recognize(string image, string? stage)
    {
        var frame = ReadFrame(Fixture(image));
        var recognizer = new VisualProgressRecognizer(Profile());
        return recognizer.Recognize(await recognizer.ReadOcrAsync(frame, Viewport(frame), stage: stage), frame.Width, frame.Height,
            Viewport(frame), frame, stage: stage);
    }

    // 窓の画像は、枠の左右各1px・上31px・下1pxを除いた部分が描画領域。
    private static FrameRect Viewport(CapturedFrame frame) => new(1, 31, frame.Width - 2, frame.Height - 32);

    private static CapturedFrame ReadFrame(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return new(ContractSchemaVersions.Revision03, "side-quest-test", CaptureBackend.WindowsGraphicsCapture,
            1, 0, DateTimeOffset.UnixEpoch, bitmap.PixelWidth, bitmap.PixelHeight, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(bytes, bitmap.PixelWidth * 4));
    }

    private static ObservationResult Observation(long sequence) => new(ContractSchemaVersions.Revision03,
        $"observation-{sequence}", new CapturedFrameReference(ContractSchemaVersions.Revision03, "window:game",
            CaptureBackend.WindowsGraphicsCapture, sequence, 0, DateTimeOffset.UnixEpoch, 1, 0, 0),
        CaptureAvailability.Available, StateIdentityStatus.Novel, [], "ocr", 0, null);

    private sealed class Mapper : IGameInteractionCoordinateMapper
    {
        public SerialHidCursorPoint MapTargetCenter(GameInteractionTargetBinding target) => new(
            (int)Math.Round((target.NormalizedBounds[0] + target.NormalizedBounds[2] / 2) * 1000),
            (int)Math.Round((target.NormalizedBounds[1] + target.NormalizedBounds[3] / 2) * 1000));
        public SerialHidCursorPoint MapNormalized(IReadOnlyList<double> point) =>
            new((int)Math.Round(point[0] * 1000), (int)Math.Round(point[1] * 1000));
    }

    private sealed class Device : INanoGameInputDevice
    {
        public List<(SerialHidCursorPoint Start, SerialHidCursorPoint Destination)> Flicks { get; } = [];
        public string Click(SerialHidCursorPoint target) => throw new NotSupportedException();
        public string KeyTap(IReadOnlyList<string> keys) => throw new NotSupportedException();
        public string Hover(SerialHidCursorPoint target) => throw new NotSupportedException();
        public string Scroll(SerialHidCursorPoint target, int verticalSteps, int horizontalSteps) => throw new NotSupportedException();
        public string Drag(SerialHidCursorPoint start, SerialHidCursorPoint destination) => throw new NotSupportedException();
        public string Flick(SerialHidCursorPoint start, SerialHidCursorPoint destination) { Flicks.Add((start, destination)); return "flick"; }
    }
}
