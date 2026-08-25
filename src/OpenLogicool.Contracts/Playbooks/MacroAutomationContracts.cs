namespace OpenLogicool.Contracts.Playbooks;

public sealed record MacroTargetOption(string ProcessName, string WindowTitle)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(WindowTitle)
        ? ProcessName
        : $"{WindowTitle}　｜　{ProcessName}";
}

public sealed record MacroCatalogItem(
    string RouteId,
    string VersionId,
    string GameId,
    string EnvironmentScope,
    string Goal,
    long RevisionNumber,
    int StepCount,
    string StatusLabel)
{
    public string DisplayLabel => $"{Goal}　{StepCount} step　[版 {RevisionNumber} / {StatusLabel}]";
}

public sealed record MacroCompositionRequest(
    string Goal,
    IReadOnlyList<MacroVersionReference> Sources);

public enum MacroRunPhase
{
    Idle,
    Starting,
    Executing,
    Repairing,
    Completed,
    Stopped,
    Faulted,
}

public sealed record MacroRunSnapshot(
    MacroRunPhase Phase,
    string Goal,
    string TargetProcessName,
    int StepNumber,
    string ActionSourceLabel,
    string ActionLabel,
    string TransitionLabel,
    int AiCallCount,
    long RouteRevision,
    string Detail,
    bool CanStart,
    bool CanStop);

public sealed record MacroCreateRequest(
    string TargetProcessName,
    string Goal);

public sealed record MacroPlaybackRequest(
    string TargetProcessName,
    MacroVersionReference Macro);

public interface IMacroAutomationIntents
{
    event Action<MacroRunSnapshot>? StateChanged;

    IReadOnlyList<MacroTargetOption> ListTargets();

    IReadOnlyList<MacroCatalogItem> ListMacros();

    MacroCatalogItem Compose(MacroCompositionRequest request);

    Task<MacroRunSnapshot> CreateAsync(
        MacroCreateRequest request,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken = default);

    Task<MacroRunSnapshot> PlayAsync(
        MacroPlaybackRequest request,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken = default);

    MacroRunSnapshot Stop();
}
