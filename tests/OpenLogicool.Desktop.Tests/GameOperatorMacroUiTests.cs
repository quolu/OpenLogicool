using OpenLogicool.Contracts.Research;
using OpenLogicool.Contracts.Playbooks;
using System.Windows.Controls;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class GameOperatorMacroUiTests
{
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

    private sealed class MacroIntents : IMacroAutomationIntents
    {
        public event Action<MacroRunSnapshot>? StateChanged { add { } remove { } }
        public IReadOnlyList<MacroTargetOption> ListTargets() => [new("game", "Game")];
        public IReadOnlyList<MacroCatalogItem> ListMacros() => [];
        public MacroCatalogItem Compose(MacroCompositionRequest request) => throw new NotSupportedException();
        public Task<MacroRunSnapshot> CreateAsync(MacroCreateRequest request, IProgress<MacroRunSnapshot> progress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
