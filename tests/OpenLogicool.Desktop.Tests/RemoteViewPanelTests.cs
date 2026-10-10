using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Desktop;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class RemoteViewPanelTests
{
    [Fact]
    public void 状態ごとの表示文言を出す()
    {
        OnSta(() =>
        {
            var fake = new Fake();
            var panel = new RemoteViewPanel(fake, _ => { });
            Assert.Contains("止まっています", Texts(panel));

            fake.State = Snapshot(RemoteViewPhase.Starting);
            panel.Refresh();
            Assert.Contains("つないでいます", Texts(panel));

            fake.State = Snapshot(RemoteViewPhase.Streaming) with { StreamedSeconds = 192, TargetProcessName = "MabinogiMobile" };
            panel.Refresh();
            Assert.Contains("配信中（3分12秒・対象: MabinogiMobile）", Texts(panel));
            Assert.Equal(Visibility.Collapsed, Stalled(panel).Visibility);

            fake.State = fake.State with { VideoStalled = true };
            panel.Refresh();
            Assert.Equal(Visibility.Visible, Stalled(panel).Visibility);

            fake.State = Snapshot(RemoteViewPhase.Faulted) with { Detail = "送信先に届きません" };
            panel.Refresh();
            Assert.Contains("失敗: 送信先に届きません", Texts(panel));
            Assert.Equal(Visibility.Collapsed, Stalled(panel).Visibility);
        });
    }

    [Fact]
    public void 開始と停止を一つのボタンで切り替える()
    {
        OnSta(() =>
        {
            var fake = new Fake();
            var panel = new RemoteViewPanel(fake, _ => { });
            var toggle = Buttons(panel).Single(button => Equals(button.Content, "配信を始める"));
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, fake.StartCalls);
            Assert.Equal(0, fake.StopCalls);
            Assert.Equal("配信を止める", toggle.Content);
            Assert.Contains(Texts(panel), text => text.StartsWith("配信中", StringComparison.Ordinal));

            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, fake.StopCalls);
            Assert.Equal("配信を始める", toggle.Content);
            Assert.Contains("止まっています", Texts(panel));
        });
    }

    [Fact]
    public void 開始の例外は文言を状態の欄へ出し落とさない()
    {
        OnSta(() =>
        {
            var fake = new Fake { StartError = new InvalidOperationException("遠隔表示の対象のゲームが未設定です。") };
            var panel = new RemoteViewPanel(fake, _ => { });
            Buttons(panel).Single(button => Equals(button.Content, "配信を始める")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("遠隔表示の対象のゲームが未設定です。", Texts(panel));
            Assert.Contains("止まっています", Texts(panel));
        });
    }

    [Fact]
    public void 画質の切り替えは保存済みの設定のまま画質だけをSaveSettingsへ渡す()
    {
        OnSta(() =>
        {
            var fake = new Fake();
            var panel = new RemoteViewPanel(fake, _ => { });
            var radios = Descendants(panel).OfType<RadioButton>().ToArray();
            var standard = radios.Single(radio => Equals(radio.Content, "標準（720p・約3Mbps）"));
            var fine = radios.Single(radio => Equals(radio.Content, "きれい（1080p・約8Mbps）"));
            Assert.True(standard.IsChecked);

            fine.IsChecked = true;
            var saved = Assert.Single(fake.Saves);
            Assert.Equal(("https://relay.example.com/in", "https://view.example.com/x", "user1", (string?)null, RemoteViewQuality.Fine), saved);
            Assert.True(fine.IsChecked);

            fake.State = Snapshot(RemoteViewPhase.Streaming) with { Quality = RemoteViewQuality.Fine };
            panel.Refresh();
            Assert.False(standard.IsEnabled);
            Assert.False(fine.IsEnabled);
        });
    }

    [Fact]
    public void 画質の保存に失敗したら文言を出して選択を戻す()
    {
        OnSta(() =>
        {
            var fake = new Fake { SaveError = new InvalidDataException("送信先のURLはhttpsだけ使えます") };
            var panel = new RemoteViewPanel(fake, _ => { });
            var radios = Descendants(panel).OfType<RadioButton>().ToArray();
            radios.Single(radio => Equals(radio.Content, "きれい（1080p・約8Mbps）")).IsChecked = true;
            Assert.Contains("送信先のURLはhttpsだけ使えます", Texts(panel));
            Assert.True(radios.Single(radio => Equals(radio.Content, "標準（720p・約3Mbps）")).IsChecked);
        });
    }

    [Fact]
    public void パスワード欄が空の保存は変えない指定でnullを渡し入力があればそのまま渡す()
    {
        OnSta(() =>
        {
            var fake = new Fake();
            var panel = new RemoteViewPanel(fake, _ => { });
            Assert.Contains("保存済み（変える時だけ入力）", Texts(panel));
            var save = Buttons(panel).Single(button => Equals(button.Content, "保存"));
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(Assert.Single(fake.Saves).Password);

            Descendants(panel).OfType<PasswordBox>().Single().Password = "新しい秘密";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("新しい秘密", fake.Saves[1].Password);
            Assert.Equal("", Descendants(panel).OfType<PasswordBox>().Single().Password);
        });
    }

    [Fact]
    public void 設定の保存の例外は文言のまま表示する()
    {
        OnSta(() =>
        {
            var fake = new Fake { SaveError = new InvalidDataException("見る URLはhttpsだけ使えます") };
            var panel = new RemoteViewPanel(fake, _ => { });
            Buttons(panel).Single(button => Equals(button.Content, "保存")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("見る URLはhttpsだけ使えます", Texts(panel));
        });
    }

    [Fact]
    public void 見るURLのコピーは設定の文字列を渡し未設定では押せない()
    {
        OnSta(() =>
        {
            var copied = new List<string>();
            var fake = new Fake { State = Snapshot(RemoteViewPhase.Stopped) with { ViewerUrl = "https://view.example.com/x" } };
            var panel = new RemoteViewPanel(fake, copied.Add);
            var copy = Buttons(panel).Single(button => Equals(button.Content, "コピー"));
            Assert.True(copy.IsEnabled);
            Assert.Contains("https://view.example.com/x", Texts(panel));
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(["https://view.example.com/x"], copied);

            fake.State = Snapshot(RemoteViewPhase.Stopped) with { ViewerUrl = null };
            panel.Refresh();
            Assert.False(copy.IsEnabled);
            Assert.Contains("中継サーバーが未設定です", Texts(panel));
        });
    }

    private static TextBlock Stalled(DependencyObject panel) => Descendants(panel).OfType<TextBlock>()
        .Single(text => text.Text.StartsWith("映像のコマが届いていません", StringComparison.Ordinal));

    private static RemoteViewSnapshot Snapshot(RemoteViewPhase phase) =>
        new(phase, "", RemoteViewQuality.Standard, null, 0, 0, false, null);

    private static string[] Texts(DependencyObject panel) => Descendants(panel).OfType<TextBlock>().Select(text => text.Text).ToArray();

    private static Button[] Buttons(DependencyObject panel) => Descendants(panel).OfType<Button>().ToArray();

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private sealed class Fake : IRemoteViewIntents
    {
        public RemoteViewSnapshot State = Snapshot(RemoteViewPhase.Stopped);
        public int StartCalls;
        public int StopCalls;
        public Exception? StartError;
        public Exception? SaveError;
        public List<(string PublishUrl, string ViewerUrl, string PublishUser, string? Password, RemoteViewQuality Quality)> Saves = [];
        private RemoteViewSettingsView settings = new("https://relay.example.com/in", "https://view.example.com/x", "user1", true, RemoteViewQuality.Standard);

        public RemoteViewSnapshot Current() => State;

        public void Start()
        {
            if (StartError is not null) throw StartError;
            StartCalls++;
            State = Snapshot(RemoteViewPhase.Streaming) with { TargetProcessName = "game", StreamedSeconds = 5 };
        }

        public Task StopAsync()
        {
            StopCalls++;
            State = Snapshot(RemoteViewPhase.Stopped);
            return Task.CompletedTask;
        }

        public RemoteViewSettingsView LoadSettings() => settings;

        public RemoteViewSettingsView SaveSettings(string publishUrl, string viewerUrl, string publishUser, string? publishPassword, RemoteViewQuality quality)
        {
            if (SaveError is not null) throw SaveError;
            Saves.Add((publishUrl, viewerUrl, publishUser, publishPassword, quality));
            settings = new RemoteViewSettingsView(publishUrl, viewerUrl, publishUser, settings.HasPublishPassword || publishPassword is not null, quality);
            State = State with { Quality = quality, ViewerUrl = viewerUrl };
            return settings;
        }
    }
}
