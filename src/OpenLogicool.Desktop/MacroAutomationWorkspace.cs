using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Desktop;

/// <summary>Game Operatorのmacro画面からHost intentsを呼ぶ、UI非依存の操作境界。</summary>
public sealed class MacroAutomationWorkspace(IMacroAutomationIntents intents)
{
    private readonly IMacroAutomationIntents intents = intents ?? throw new ArgumentNullException(nameof(intents));

    public IReadOnlyList<MacroTargetOption> ListTargets() => intents.ListTargets();

    public IReadOnlyList<MacroCatalogItem> ListMacros() => intents.ListMacros();

    public Task<MacroRunSnapshot> CreateAsync(
        MacroTargetOption target,
        string goal,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        return intents.CreateAsync(new MacroCreateRequest(target.ProcessName, goal.Trim()), progress, cancellationToken);
    }

    public Task<MacroRunSnapshot> PlayAsync(
        MacroTargetOption target,
        MacroCatalogItem macro,
        MacroPlaybackMode mode,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(macro);
        return intents.PlayAsync(new MacroPlaybackRequest(
            target.ProcessName,
            new MacroVersionReference(macro.RouteId, macro.VersionId, mode)), progress, cancellationToken);
    }

    public MacroCatalogItem Compose(string goal, IReadOnlyList<MacroCatalogItem> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count < 2)
        {
            throw new ArgumentException("統合マクロには2件以上のマクロが必要です。", nameof(sources));
        }
        return intents.Compose(new MacroCompositionRequest(
            goal.Trim(),
            sources.Select(source => new MacroVersionReference(
                source.RouteId, source.VersionId, MacroPlaybackMode.AiMonitored)).ToArray()));
    }

    public MacroRunSnapshot Stop() => intents.Stop();
}
