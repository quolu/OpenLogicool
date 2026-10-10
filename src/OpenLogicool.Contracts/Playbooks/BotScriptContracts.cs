namespace OpenLogicool.Contracts.Playbooks;

public enum BotScriptPhase { Stopped, Starting, Running, Stopping, AwaitingReview, Faulted, ReviewMonitoring, UserPaused }
public sealed record BotScriptItem(string Id, string Name, string Description);
/// <summary>Botの動き方。入ると、解除するまで続く。</summary>
public sealed record BotScriptMode(string ScriptId, string Id, string Name, bool Active);
public sealed record BotScriptSnapshot(BotScriptPhase Phase, string Detail, string? ScriptId = null,
    string? EvidenceDirectory = null, long ObservationCount = 0, long LastIntervalMs = 0,
    double? HealthFraction = null, int InputCount = 0, int FoodCount = 0, int PotionCount = 0, int BandageCount = 0,
    string? Mode = null);

public interface IBotScriptIntents
{
    IReadOnlyList<BotScriptItem> ListScripts();
    BotScriptSnapshot Current();
    void Start(string scriptId);
    Task StopAsync();
    void OpenEvidence();
    IReadOnlyList<BotScriptMode> ListModes();
    /// <summary>モードへ入る。Botが動いていれば次の観測から、止まっていれば次に始めた時から効く。</summary>
    BotScriptSnapshot SetMode(string modeId);
    BotScriptSnapshot ClearMode();
}
