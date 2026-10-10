using System.IO;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

/// <summary>Botの規則を機能ごとに分け、組み合わせて読む。</summary>
public sealed class BotFunctionTests
{
    private static string Fixture(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "fixtures/visual-recovery/mabinogi-20261008", name);
    }

    private static readonly string Progress = Fixture("progress.json");

    private static readonly string[] Main =
        ["rest", "guide", "npc", "auto-advance", "dialogue", "results", "dungeon", "coin-refill", "ask-user", "side-quest"];

    [Fact]
    public void 機能の一覧は名前とメインに入っているかとモードを返す()
    {
        var functions = VisualProgressProfile.ListFunctions(Progress);
        Assert.Equal(Main, functions.Select(function => function.Id).ToArray());
        Assert.All(functions, function => Assert.True(function.Main));
        Assert.All(functions, function => Assert.False(string.IsNullOrWhiteSpace(function.Name)));
        Assert.Equal(["side-quest"], functions.Single(function => function.Id == "side-quest").Modes);
    }

    [Fact]
    public void 指定なしはメインの組み合わせを読む()
    {
        var main = VisualProgressProfile.Load(Progress);
        Assert.Equal(Main, main.Functions!);
        Assert.Equal(51, main.Rules.Length);
        Assert.All(main.Rules, rule => Assert.Contains(rule.Function, main.Functions!));
        Assert.Equal(6, main.Rules.Count(rule => rule.Function == "side-quest"));
        Assert.Equal("side-quest", Assert.Single(main.Modes!).Id);
        Assert.Equal(2, main.ReviewWhen.Length);
        Assert.Equal(2, main.WhiteTextBounds!.Length);
    }

    [Fact]
    public void 機能は単独でも読めて一緒に読む機能を連れてくる()
    {
        var dialogue = VisualProgressProfile.Load(Progress, ["dialogue"]);
        Assert.Equal(["dialogue"], dialogue.Functions!);
        Assert.All(dialogue.Rules, rule => Assert.Equal("dialogue", rule.Function));
        Assert.Empty(dialogue.ReviewWhen);
        Assert.Null(dialogue.WhiteTextBounds);
        Assert.Null(dialogue.Modes);

        // サイドクエストの受注は、コンパスのSpaceの直後の知らせで始まる。その規則のある機能を一緒に読む。
        var sideQuest = VisualProgressProfile.Load(Progress, ["side-quest"]);
        Assert.Equal(["auto-advance", "side-quest"], sideQuest.Functions!);
        Assert.Equal("compass-space", sideQuest.Rules.Single(rule => rule.Id == "side-quest-start").After);
    }
    [Fact]
    public void 選ぶ順を変えても機能は設定に書いた順に並ぶ()
    {
        var one = VisualProgressProfile.Load(Progress, ["dungeon", "guide", "dialogue"]);
        var other = VisualProgressProfile.Load(Progress, ["dialogue", "dungeon", "guide"]);
        Assert.Equal(["guide", "dialogue", "dungeon"], one.Functions!);
        Assert.Equal(one.Rules.Select(rule => rule.Id), other.Rules.Select(rule => rule.Id));
        // 同じ優先度で並ぶ反復の画像規則（T と B）は、分ける前と同じ先後で並ぶ。
        var guide = one.Rules.Where(rule => rule.RepeatIntervalMs > 0).Select(rule => rule.Id).ToArray();
        Assert.Equal(["feather-t", "top-left-b"], guide);
    }

    [Fact]
    public void メインに機能を足して読んでも同じ機能は重ねない()
    {
        var withSideQuest = VisualProgressProfile.Load(Progress, ["side-quest", "dialogue"], withMain: true);
        Assert.Equal(Main, withSideQuest.Functions!);
        Assert.Equal(51, withSideQuest.Rules.Length);
    }
    [Fact]
    public void 無い機能と一覧に無いファイルと機能に分けていない設定での指定は拒否する()
    {
        Assert.Contains("無い機能", Assert.Throws<InvalidDataException>(() => VisualProgressProfile.Load(Progress, ["無い機能"])).Message);

        var root = Path.Combine(Path.GetTempPath(), "openlogicool-functions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "functions"));
        try
        {
            var path = Path.Combine(root, "progress.json");
            const string rule = """{ "Id": "x", "Key": "Key:I", "When": [{ "Text": "文字", "Bounds": [0, 0, 1, 1] }] }""";
            File.WriteAllText(Path.Combine(root, "functions", "a.json"), $$"""{ "Id": "a", "Name": "機能A", "Rules": [{{rule}}] }""");
            File.WriteAllText(path, """{ "SchemaVersion": 1, "Functions": ["a"] }""");
            Assert.Equal("x", Assert.Single(VisualProgressProfile.Load(path).Rules).Id);

            // 一覧に無いファイルは、読み落とさずに誤りとして止める。
            File.WriteAllText(Path.Combine(root, "functions", "b.json"), """{ "Id": "b", "Name": "機能B" }""");
            Assert.Contains("b", Assert.Throws<InvalidDataException>(() => VisualProgressProfile.Load(path)).Message);
            File.WriteAllText(path, """{ "SchemaVersion": 1, "Functions": ["a"], "OptionalFunctions": ["b", "c"] }""");
            Assert.Contains("c", Assert.Throws<InvalidDataException>(() => VisualProgressProfile.Load(path)).Message);

            // 機能のファイルの名前と中のIdは一致させる。同じ規則の名前は、機能をまたいでも重ねない。
            File.WriteAllText(path, """{ "SchemaVersion": 1, "Functions": ["a"], "OptionalFunctions": ["b"] }""");
            File.WriteAllText(Path.Combine(root, "functions", "b.json"), """{ "Id": "違う名前", "Name": "機能B" }""");
            Assert.Throws<InvalidDataException>(() => VisualProgressProfile.Load(path));
            File.WriteAllText(Path.Combine(root, "functions", "b.json"), $$"""{ "Id": "b", "Name": "機能B", "Rules": [{{rule}}] }""");
            Assert.Throws<InvalidDataException>(() => VisualProgressProfile.Load(path, ["a", "b"]));

            // 組み合わせを書く設定には、規則を直接書かない。
            File.WriteAllText(path, $$"""{ "SchemaVersion": 1, "Functions": ["a"], "OptionalFunctions": ["b"], "ReviewWhen": [], "Rules": [{{rule}}] }""");
            Assert.Throws<InvalidDataException>(() => VisualProgressProfile.Load(path));

            // 機能に分けていない設定は今までどおり読めるが、機能は選べない。
            File.WriteAllText(path, $$"""{ "SchemaVersion": 1, "ReviewWhen": [], "Rules": [{{rule}}] }""");
            Assert.Single(VisualProgressProfile.Load(path).Rules);
            Assert.Empty(VisualProgressProfile.ListFunctions(path));
            Assert.Throws<InvalidDataException>(() => VisualProgressProfile.Load(path, ["a"]));
        }
        finally { Directory.Delete(root, true); }
    }

    // メインに入っていない機能がモードを持つ場合の並び。
    private static readonly VisualProgressFunctionInfo[] WithOptional =
        [new("dialogue", "会話を進める", true, []), new("extra", "追加の機能", false, ["extra-mode"])];

    [Fact]
    public void 指定なしで始めるとメインで入っているモードの機能がメインに無い時だけ足す()
    {
        var functions = VisualProgressProfile.ListFunctions(Progress);
        var main = BotFunctionPlanner.Plan(functions, null, null);
        Assert.True(main.Recovery);
        Assert.Equal("メイン", main.Label);
        Assert.Equal(["side-quest"], main.Modes);
        Assert.Equal(["--progress-profile", Progress], BotFunctionPlanner.Arguments(main, Progress));
        // モードの機能がメインにあれば、足すものは無い。
        Assert.Equal(["--progress-profile", Progress],
            BotFunctionPlanner.Arguments(BotFunctionPlanner.Plan(functions, " ", "side-quest"), Progress));

        var withMode = BotFunctionPlanner.Plan(WithOptional, null, "extra-mode");
        Assert.Equal(["extra-mode"], withMode.Modes);
        Assert.Equal("メイン＋extra", withMode.Label);
        Assert.Equal(["--progress-profile", Progress, "--functions", "extra", "--functions-with-main"],
            BotFunctionPlanner.Arguments(withMode, Progress));
        Assert.Empty(BotFunctionPlanner.Plan(WithOptional, null, null).Modes);
    }
    [Fact]
    public void 機能を選んで始めると選んだ機能だけを動かす()
    {
        var functions = VisualProgressProfile.ListFunctions(Progress);
        // 回復を選ばなければ、回復は動かさない。入っているモードも、その機能を選んだ時だけ動かす。
        var two = BotFunctionPlanner.Plan(functions, "dialogue, results", "side-quest");
        Assert.False(two.Recovery);
        Assert.Empty(two.Modes);
        Assert.Equal("dialogue、results", two.Label);
        Assert.Equal(["--progress-profile", Progress, "--functions", "dialogue,results", "--no-recovery-input"],
            BotFunctionPlanner.Arguments(two, Progress));

        Assert.Equal(["--recovery-only"], BotFunctionPlanner.Arguments(BotFunctionPlanner.Plan(functions, "recovery", null), Progress));
        Assert.Equal(["--progress-profile", Progress, "--functions", "dialogue"],
            BotFunctionPlanner.Arguments(BotFunctionPlanner.Plan(functions, "recovery,dialogue", null), Progress));
        Assert.Equal(["side-quest"], BotFunctionPlanner.Plan(functions, "side-quest", "side-quest").Modes);

        var unknown = Assert.Throws<ArgumentException>(() => BotFunctionPlanner.Plan(functions, "dialogue,無い機能", null));
        Assert.Contains("無い機能", unknown.Message);
        Assert.Contains("recovery、rest", unknown.Message);
    }

    [Fact]
    public async Task 機能の指定の誤りは始める前に返し動いているBotに無いモードは次に始めた時から動く()
    {
        var root = Path.Combine(Path.GetTempPath(), "openlogicool-function-run-" + Guid.NewGuid().ToString("N"));
        var functions = WithOptional;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new OpenLogicool.Playbooks.DemonstrationRecordingGate();
        try
        {
            using var intents = new HostBotScriptIntents([new("test", "試験", "説明")], root, gate,
                async (_, _, _, token) => { await release.Task.WaitAsync(token); return new(false, "終了"); },
                modes: [new("test", "extra-mode", "追加のモード", false)],
                functions: [new("test", "dialogue", "会話を進める", true)],
                planner: (_, requested, mode) => BotFunctionPlanner.Plan(functions, requested, mode));
            Assert.Equal("dialogue", Assert.Single(intents.ListFunctions()).Id);

            Assert.Throws<ArgumentException>(() => intents.Start("test", "無い機能"));
            Assert.Equal(OpenLogicool.Contracts.Playbooks.BotScriptPhase.Stopped, intents.Current().Phase);
            Assert.Equal(OpenLogicool.Playbooks.DemonstrationGateState.Free, gate.State);

            intents.Start("test", "dialogue");
            Assert.Equal("dialogue", intents.Current().Functions);
            Assert.Equal(["dialogue"], intents.CurrentPlan.Selected!);
            // 動いているBotは会話の機能だけなので、モードは次に始めた時から動く。
            Assert.Equal("追加のモード（次にBotを始めた時から）", intents.SetMode("extra-mode").Mode);
            Assert.Null(intents.RunningMode("test"));
            release.SetResult();
            await intents.StopAsync();

            // 指定なしで始めると、メインに、入っているモードの機能が足される。
            var again = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            release = again;
            intents.Start("test");
            Assert.Equal("メイン＋extra", intents.Current().Functions);
            Assert.Equal("追加のモード", intents.Current().Mode);
            Assert.Equal("extra-mode", intents.RunningMode("test"));
            again.SetResult();
            await intents.StopAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
