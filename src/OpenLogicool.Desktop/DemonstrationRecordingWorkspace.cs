using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Exploration;

namespace OpenLogicool.Desktop;

/// <summary>Game Operatorの記録画面からHost intentsを呼ぶ、UI非依存の操作境界。</summary>
public sealed class DemonstrationRecordingWorkspace(IDemonstrationRecordingIntents intents)
{
    private readonly IDemonstrationRecordingIntents intents = intents ?? throw new ArgumentNullException(nameof(intents));

    public Task<DemonstrationSessionSummary> StartAsync(string goal, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        return intents.StartAsync(goal.Trim(), cancellationToken);
    }

    public Task<DemonstrationSessionSummary> StopAsync(CancellationToken cancellationToken = default) =>
        intents.StopAsync(cancellationToken);

    public DemonstrationRecordingStatus Status() => intents.Status();

    public Task<DemonstrationSessionSummary> ReanalyzeAsync(string sessionId, CancellationToken cancellationToken = default) =>
        intents.ReanalyzeAsync(sessionId, cancellationToken);

    public IReadOnlyList<DemonstrationSessionSummary> ListSessions() => intents.ListSessions();

    public IReadOnlyList<DemonstrationStepSummary> ListSteps(string sessionId) => intents.ListSteps(sessionId);

    public MacroCatalogItem CreateMacroFromSession(string sessionId) => intents.CreateMacroFromSession(sessionId);

    public DemonstrationCandidate? LoadCandidate(string sessionId) => intents.LoadCandidate(sessionId);

    public DemonstrationCandidate ReviewStep(string sessionId, string versionId, int stepNumber,
        GameTransitionJudgement expected, string reason) => intents.ReviewStep(sessionId, versionId, stepNumber, expected, reason);

    public DemonstrationCandidate ReplaceStep(string sessionId, string versionId, int stepNumber, string replacementSessionId) =>
        intents.ReplaceStep(sessionId, versionId, stepNumber, replacementSessionId);
}
