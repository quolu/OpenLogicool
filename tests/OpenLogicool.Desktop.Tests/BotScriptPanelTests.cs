using System.Windows;
using System.Windows.Controls;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Desktop;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class BotScriptPanelTests
{
    [Fact]
    public void 開始と停止のボタンから操作し判断待ちの理由を表示する()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var intents = new Fake();
                var panel = new BotScriptPanel(intents);
                var controls = Descendants(panel).ToArray();
                var start = controls.OfType<Button>().Single(button => Equals(button.Content, "Botを開始"));
                var stop = controls.OfType<Button>().Single(button => Equals(button.Content, "Botを停止"));
                Assert.True(start.IsEnabled);
                Assert.False(stop.IsEnabled);
                intents.State = new(BotScriptPhase.ReviewMonitoring, "画面観測と回復監視を継続中です。");
                panel.Refresh();
                Assert.Contains("確認事項あり・動作継続中", controls.OfType<TextBlock>().Select(text => text.Text));
                Assert.False(start.IsEnabled);
                Assert.True(stop.IsEnabled);
                intents.State = new(BotScriptPhase.UserPaused, "手入力がなくなって3秒で再開");
                panel.Refresh();
                Assert.Contains("手入力で一時停止中", controls.OfType<TextBlock>().Select(text => text.Text));
                Assert.False(start.IsEnabled);
                Assert.True(stop.IsEnabled);
                start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("test", intents.Started);
                Assert.False(start.IsEnabled);
                Assert.True(stop.IsEnabled);
                stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(intents.Stopped);
                Assert.True(start.IsEnabled);
                intents.State = new(BotScriptPhase.AwaitingReview, "ボーナスを選択してください。");
                panel.Refresh();
                Assert.Contains("画面の確認が必要です", controls.OfType<TextBlock>().Select(text => text.Text));
                Assert.Contains("ボーナスを選択してください。", controls.OfType<TextBlock>().Select(text => text.Text));
                Assert.False(stop.IsEnabled);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw failure;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private sealed class Fake : IBotScriptIntents
    {
        public string? Started;
        public bool Stopped;
        public BotScriptSnapshot State = new(BotScriptPhase.Stopped, "停止しています。");
        public IReadOnlyList<BotScriptItem> ListScripts() => [new("test", "マビノギモバイル", "ポーション70％・包帯20％")];
        public BotScriptSnapshot Current() => State;
        public void Start(string id, string? functions = null) { Started = id; State = new(BotScriptPhase.Running, "実行中"); }
        public IReadOnlyList<BotScriptFunction> ListFunctions() => [];
        public Task StopAsync() { Stopped = true; State = new(BotScriptPhase.Stopped, "停止済み"); return Task.CompletedTask; }
        public void OpenEvidence() { }
        public IReadOnlyList<BotScriptMode> ListModes() => [];
        public BotScriptSnapshot SetMode(string modeId) => State;
        public BotScriptSnapshot ClearMode() => State;
    }
}
