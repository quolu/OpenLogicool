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
    public void Existing_partial_candidate_opens_macro_tab_without_recreating_or_losing_its_steps()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new GameOperatorWindow(new WebIntent(), macroAutomationIntents: new MacroIntents(),
                    demonstrationRecordingIntents: new RecordingIntents { PendingCandidate = true });
                var tabs = Assert.IsType<TabControl>(window.Content);
                var recording = (TabItem)tabs.Items[1];
                tabs.SelectedItem = recording;
                var button = Descendants((DependencyObject)recording.Content).OfType<Button>()
                    .Single(item => Equals(item.Content, "この候補をマクロ画面で開く"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("マクロ", Assert.IsType<TabItem>(tabs.SelectedItem).Header);
                window.Close();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
    }
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

    [Fact]
    public void Another_goal_completion_does_not_move_the_selected_macro_resume_position()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var intents = new MacroIntents { Macros = [new("daily", "v1", "game", "env", "日課", 1, 51, "保存済み")] };
                var window = new GameOperatorWindow(new WebIntent(), macroAutomationIntents: intents, openMacroTab: true);
                var tabs = Assert.IsType<TabControl>(window.Content);
                var panel = (DependencyObject)Assert.IsType<TabItem>(tabs.SelectedItem).Content;
                var controls = Descendants(panel).ToArray();
                controls.OfType<ListBox>().First().SelectedIndex = 0;
                var start = controls.OfType<TextBox>().First(box => box.Width == 65);
                intents.Publish(new(MacroRunPhase.Completed, "ロビーへ戻る", "game", 1, "", "", "", 0, 1, "完了", true, false));
                Assert.Equal("1", start.Text);
                intents.Publish(new(MacroRunPhase.Faulted, "日課", "game", 1, "", "", "", 0, 1, "前面切替失敗", true, false));
                Assert.Equal("2", start.Text);
                intents.Publish(new(MacroRunPhase.Completed, "日課", "game", 2, "", "", "", 0, 1, "範囲完了", true, false));
                Assert.Equal("3", start.Text);
                window.Close();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
    }

    [Fact]
    public void Recorded_macro_opens_a_pending_confirmation_after_the_tab_is_loaded()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var review = new MacroStepConfirmationRequest("confirmation:pending", 3, "クリック", "Moved", null, null, "違いあり");
                var intents = new MacroIntents
                {
                    Macros = [new("daily", "v1", "game", "env", "日課", 1, 51, "保存済み", true)],
                    Current = new(MacroRunPhase.AwaitingConfirmation, "日課", "game", 2, "保存済み", "クリック", "Moved", 0, 1,
                        "確認待ち", false, true, PendingConfirmation: review),
                };
                var window = new GameOperatorWindow(new WebIntent(), macroAutomationIntents: intents, openMacroTab: true);
                window.Show();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                var dialog = Assert.Single(window.OwnedWindows.OfType<MacroStepConfirmationDialog>());
                Assert.Equal(review.ConfirmationId, dialog.ConfirmationId);
                var panel = (DependencyObject)Assert.IsType<TabItem>(((TabControl)window.Content).SelectedItem).Content;
                Assert.False(Descendants(panel).OfType<Button>().Single(button => Equals(button.Content, "再生")).IsEnabled);
                intents.Publish(intents.Current!);
                Assert.Single(window.OwnedWindows.OfType<MacroStepConfirmationDialog>());
                Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "違う・ここで補正"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal((review.ConfirmationId, MacroStepDecision.Correct), intents.Confirmed);
                window.Close();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
    }

    private sealed class RecordingIntents : IDemonstrationRecordingIntents
    {
        public bool PendingCandidate { get; init; }
        public DemonstrationCandidate? LoadCandidate(string sessionId) => PendingCandidate
            ? new(new MacroCatalogItem("route", "version", "game", "env", "日課", 2, 51, "確認待ち 46件"),
                [new DemonstrationCandidateStep(1, "クリック", "確認待ち", "未判定", null, null)]) : null;
        public Task<DemonstrationSessionSummary> ReanalyzeAsync(string sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<DemonstrationSessionSummary> StartAsync(string goal, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<DemonstrationSessionSummary> StopAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public DemonstrationRecordingStatus Status() => new(DemonstrationRecorderStatus.Idle, null, 0, 0, 0, 0, 0);
        public IReadOnlyList<DemonstrationSessionSummary> ListSessions() => PendingCandidate
            ? [new("demo", "日課", "game", "env", DemonstrationSessionState.Stopped, 51, DateTimeOffset.UnixEpoch)] : [];
        public IReadOnlyList<DemonstrationStepSummary> ListSteps(string sessionId) => [];
        public MacroCatalogItem CreateMacroFromSession(string sessionId) => throw new NotSupportedException();
    }

    private sealed class MacroIntents : IMacroAutomationIntents
    {
        public bool ImmediateFailure { get; init; }
        public MacroRunSnapshot? Current { get; init; }
        public (string, MacroStepDecision)? Confirmed { get; private set; }
        public MacroRunSnapshot? CurrentRun() => Current;
        public void ConfirmStep(string id, MacroStepDecision decision) => Confirmed = (id, decision);
        public event Action<MacroRunSnapshot>? StateChanged;
        public void Publish(MacroRunSnapshot snapshot) => StateChanged?.Invoke(snapshot);
        public IReadOnlyList<MacroCatalogItem> Macros { get; init; } = [];
        public IReadOnlyList<MacroTargetOption> ListTargets() => [new("game", "Game")];
        public MacroTargetOption? CurrentTarget() => new("game", "Game");
        public MacroTargetOption SelectTarget(string processName) => new(processName, "Game");
        public IReadOnlyList<MacroCatalogItem> ListMacros() => Macros;
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
