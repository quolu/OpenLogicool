namespace OpenLogicool.Contracts.Playbooks;

public enum BotScriptPhase { Stopped, Starting, Running, Stopping, AwaitingReview, Faulted, ReviewMonitoring }
public sealed record BotScriptItem(string Id, string Name, string Description);
public sealed record BotScriptSnapshot(BotScriptPhase Phase, string Detail, string? ScriptId = null,
    string? EvidenceDirectory = null, long ObservationCount = 0, long LastIntervalMs = 0,
    double? HealthFraction = null, int InputCount = 0, int FoodCount = 0, int PotionCount = 0, int BandageCount = 0);

public interface IBotScriptIntents
{
    IReadOnlyList<BotScriptItem> ListScripts();
    BotScriptSnapshot Current();
    void Start(string scriptId);
    Task StopAsync();
    void OpenEvidence();
}
