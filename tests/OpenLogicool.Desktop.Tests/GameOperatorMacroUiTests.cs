using OpenLogicool.Contracts.Research;
using OpenLogicool.Contracts.Playbooks;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class GameOperatorMacroUiTests
{
    [Fact]
    public void Immediate_start_failure_remains_visible_after_queued_progress_is_processed()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var window = new GameOperatorWindow(new WebIntent(),
                    macroAutomationIntents: new MacroIntents { ImmediateFailure = true }, openMacroTab: true);
                var tabs = Assert.IsType<TabControl>(window.Content);
                var panel = Assert.IsAssignableFrom<UserControl>(Assert.IsType<TabItem>(tabs.SelectedItem).Content);
                var controls = Descendants(panel).ToArray();
                controls.OfType<TextBox>().First().Text = "ロビーへ戻る";
                controls.OfType<Button>().Single(button => Equals(button.Content, "AIに作ってもらう"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("ゲームの前面切替に失敗しました。", controls.OfType<TextBlock>().Select(text => text.Text));
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

                Assert.Contains("ゲームの前面切替に失敗しました。", controls.OfType<TextBlock>().Select(text => text.Text));
                Assert.False(controls.OfType<Button>().Single(button => Equals(button.Content, "停止")).IsEnabled);
                window.Close();
            }
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

    [Fact]
    public void Existing_tabs_remain_and_macro_tab_is_added_in_the_same_window()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new GameOperatorWindow(
                    new WebIntent(),
                    macroAutomationIntents: new MacroIntents());
                var tabs = Assert.IsType<TabControl>(window.Content);
                Assert.Equal(["STEP 0　Web調査", "マクロ"],
                    tabs.Items.Cast<TabItem>().Select(item => item.Header!.ToString()!).ToArray());
                Assert.IsAssignableFrom<UserControl>(((TabItem)tabs.Items[1]).Content);
                window.Close();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    [Fact]
    public void Direct_macro_entry_selects_the_macro_tab()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new GameOperatorWindow(
                    new WebIntent(),
                    macroAutomationIntents: new MacroIntents(),
                    openMacroTab: true);
                var tabs = Assert.IsType<TabControl>(window.Content);
                Assert.Equal("マクロ", Assert.IsType<TabItem>(tabs.SelectedItem).Header);
                window.Close();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    [Fact]
    public void Demonstration_recording_tab_appears_between_research_and_macro_tabs()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new GameOperatorWindow(
                    new WebIntent(),
                    macroAutomationIntents: new MacroIntents(),
                    demonstrationRecordingIntents: new RecordingIntents());
                var tabs = Assert.IsType<TabControl>(window.Content);
                Assert.Equal(["STEP 0　Web調査", "記録", "マクロ"],
                    tabs.Items.Cast<TabItem>().Select(item => item.Header!.ToString()!).ToArray());
                Assert.IsAssignableFrom<UserControl>(((TabItem)tabs.Items[1]).Content);
                window.Close();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    [Fact]
    public void Window_without_demonstration_recording_intents_has_no_recording_tab()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new GameOperatorWindow(
                    new WebIntent(),
                    macroAutomationIntents: new MacroIntents());
                var tabs = Assert.IsType<TabControl>(window.Content);
                Assert.DoesNotContain("記録", tabs.Items.Cast<TabItem>().Select(item => item.Header!.ToString()));
                window.Close();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private sealed class RecordingIntents : IDemonstrationRecordingIntents
    {
        public Task<DemonstrationSessionSummary> StartAsync(string goal, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<DemonstrationSessionSummary> StopAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public DemonstrationRecordingStatus Status() => new(DemonstrationRecorderStatus.Idle, null, 0, 0, 0, 0, 0);
        public IReadOnlyList<DemonstrationSessionSummary> ListSessions() => [];
        public IReadOnlyList<DemonstrationStepSummary> ListSteps(string sessionId) => [];
        public MacroCatalogItem CreateMacroFromSession(string sessionId) => throw new NotSupportedException();
    }

    private sealed class MacroIntents : IMacroAutomationIntents
    {
        public bool ImmediateFailure { get; init; }
        public event Action<MacroRunSnapshot>? StateChanged { add { } remove { } }
        public IReadOnlyList<MacroTargetOption> ListTargets() => [new("game", "Game")];
        public MacroTargetOption? CurrentTarget() => new("game", "Game");
        public MacroTargetOption SelectTarget(string processName) => new(processName, "Game");
        public IReadOnlyList<MacroCatalogItem> ListMacros() => [];
        public MacroCatalogItem Compose(MacroCompositionRequest request) => throw new NotSupportedException();
        public Task<MacroRunSnapshot> CreateAsync(MacroCreateRequest request, IProgress<MacroRunSnapshot> progress, CancellationToken cancellationToken = default)
        {
            if (!ImmediateFailure) throw new NotSupportedException();
            progress.Report(new MacroRunSnapshot(MacroRunPhase.Starting, request.Goal, "game", 0,
                "", "", "", 0, 0, "開始しています。", false, true));
            return Task.FromException<MacroRunSnapshot>(new InvalidOperationException("ゲームの前面切替に失敗しました。"));
        }
        public Task<MacroRunSnapshot> PlayAsync(MacroPlaybackRequest request, IProgress<MacroRunSnapshot> progress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public MacroRunSnapshot Stop() => throw new NotSupportedException();
    }

    private sealed class WebIntent : IWebResearchIntent
    {
        public WebResearchPreview Preview(Uri url, SourceTermsDisposition terms, RobotsDisposition robots, DateTimeOffset? expiresUtc) =>
            throw new NotSupportedException();
        public Task<WebResearchOperationResult> StartAsync(WebReferenceAcquisitionPlan plan, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Exclude(Uri url, string reason) => throw new NotSupportedException();
        public Task<WebResearchOperationResult> ReacquireAsync(string sourceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IReadOnlyList<WebResearchDocumentItem> ListDocuments() => [];
        public string GetMarkdown(string documentId) => throw new NotSupportedException();
        public WebReferenceDeletionPreview PreviewDelete(string sourceId) => throw new NotSupportedException();
        public void Delete(string sourceId, string reason) => throw new NotSupportedException();
    }
}
