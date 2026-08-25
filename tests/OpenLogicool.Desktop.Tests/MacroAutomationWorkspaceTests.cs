using OpenLogicool.Contracts.Playbooks;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class MacroAutomationWorkspaceTests
{
    [Fact]
    public async Task Create_and_play_translate_user_choices_to_host_intents()
    {
        var intents = new FakeIntents();
        var workspace = new MacroAutomationWorkspace(intents);
        var target = intents.Targets[0];
        var macro = intents.Macros[0];

        await workspace.CreateAsync(target, "  日課を終える  ", new Progress<MacroRunSnapshot>());
        await workspace.PlayAsync(target, macro, MacroPlaybackMode.AiFree, new Progress<MacroRunSnapshot>());

        Assert.Equal(new MacroCreateRequest("game.exe", "日課を終える"), intents.Created);
        Assert.Equal(new MacroPlaybackRequest("game.exe",
            new MacroVersionReference("route:1", "route:1:v2", MacroPlaybackMode.AiFree)), intents.Played);
    }

    [Fact]
    public void Composition_preserves_the_selected_order_and_exact_source_versions()
    {
        var intents = new FakeIntents();
        var workspace = new MacroAutomationWorkspace(intents);

        workspace.Compose("全部の日課", [intents.Macros[1], intents.Macros[0]]);

        Assert.Equal("全部の日課", intents.Composed!.Goal);
        Assert.Equal(["route:2", "route:1"], intents.Composed.Sources.Select(source => source.RouteId));
        Assert.Equal(["route:2:v4", "route:1:v2"], intents.Composed.Sources.Select(source => source.VersionId));
    }

    private sealed class FakeIntents : IMacroAutomationIntents
    {
        public event Action<MacroRunSnapshot>? StateChanged { add { } remove { } }
        public IReadOnlyList<MacroTargetOption> Targets { get; } = [new("game.exe", "Game")];
        public IReadOnlyList<MacroCatalogItem> Macros { get; } =
        [
            new("route:1", "route:1:v2", "game", "env", "一つ目", 2, 3, "保存済み"),
            new("route:2", "route:2:v4", "game", "env", "二つ目", 4, 5, "保存済み"),
        ];
        public MacroCreateRequest? Created { get; private set; }
        public MacroPlaybackRequest? Played { get; private set; }
        public MacroCompositionRequest? Composed { get; private set; }

        public IReadOnlyList<MacroTargetOption> ListTargets() => Targets;
        public IReadOnlyList<MacroCatalogItem> ListMacros() => Macros;
        public MacroCatalogItem Compose(MacroCompositionRequest request) { Composed = request; return Macros[0]; }
        public Task<MacroRunSnapshot> CreateAsync(MacroCreateRequest request, IProgress<MacroRunSnapshot> progress, CancellationToken cancellationToken = default)
        { Created = request; return Task.FromResult(Snapshot(request.Goal)); }
        public Task<MacroRunSnapshot> PlayAsync(MacroPlaybackRequest request, IProgress<MacroRunSnapshot> progress, CancellationToken cancellationToken = default)
        { Played = request; return Task.FromResult(Snapshot("play")); }
        public MacroRunSnapshot Stop() => Snapshot("stop");
        private static MacroRunSnapshot Snapshot(string goal) => new(
            MacroRunPhase.Completed, goal, "game.exe", 1, "保存済み", "click", "Moved", 0, 1, "done", true, false);
    }
}
