using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenLogicool.Desktop;
using Xunit;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;

namespace OpenLogicool.Host.Tests;

/// <summary>メイン画面の図の押し方（空のキーへ載せる・光っているキーはその操作を選ぶ）。</summary>
public sealed class InputStudioWindowFigureTests
{
    [Fact]
    public void Clicking_a_lit_key_selects_its_action_and_clicking_an_empty_key_assigns_the_selected_action()
    {
        RunSta(() =>
        {
            var intents = new FakeWorkspaceEditorIntents();
            var document = intents.LoadDocument("*").Document;
            document = WorkspaceDocumentEditor.AddAction(document, "dodge", "回避", ["Key:Space"]);
            document = WorkspaceDocumentEditor.AddAction(document, "menu", "メニュー", ["Key:Esc"]);
            document = WorkspaceDocumentEditor.SetBinding(document, "dodge", "G13", "G4", "base");
            document = WorkspaceDocumentEditor.SetBinding(document, "menu", "G13", "G22", "base");
            var saved = intents.Save(document, "*");
            var window = new InputStudioWindow(
                new WorkspaceScreenSnapshot("共通設定を適用中", null, saved.RevisionNumber, saved.Stages, 1, 1,
                    [new ApplicationRailEntryInput("*", "共通設定", false, true)]),
                InputStudioReportBuilder.Build(new DeviceDisplayInput("G13", 1, null, null), new DeviceDisplayInput("G600", 1, null, null)),
                "*",
                intents);
            var actions = Descendants(window).OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "操作一覧");
            actions.SelectedIndex = 0;

            // 「回避」を選んだまま、「メニュー」が載っている G22 を押す: 重ねずに「メニュー」を選ぶ。
            Key(window, "G22（メニュー）").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, actions.SelectedIndex);
            Assert.False(window.HasUnsavedChanges);

            // 空の G5 を押す: 選んでいる「メニュー」をそこへ載せる（同じ配置の G22 から移る）。
            Key(window, "G5（未割当）").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.HasUnsavedChanges);
            Assert.NotNull(Key(window, "G5（メニュー）"));
            Assert.NotNull(Key(window, "G22（未割当）"));
            Assert.NotNull(Key(window, "G4（回避）"));

            // 図の M2 を押す: 配置が M2 へ切り替わり、「いつも」の割当は図から消える。
            Descendants(window).OfType<Button>()
                .Single(button => AutomationProperties.GetName(button).StartsWith("M2：", StringComparison.Ordinal))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(Key(window, "G4（未割当）"));
        });
    }

    private static Button Key(Window window, string automationName) =>
        Descendants(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == automationName);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Descendants(child)) yield return item;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
