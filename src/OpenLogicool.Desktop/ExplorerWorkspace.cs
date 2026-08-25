namespace OpenLogicool.Desktop;

/// <summary>保存済みのゲーム構造を選ぶための識別子。</summary>
public sealed record ExplorerScopeOption(string GameId, string EnvironmentScope)
{
    public string DisplayLabel => $"{GameId}　｜　{EnvironmentScope}";
}

/// <summary>構造要素の検証段階別件数。表示語彙は根拠4値と別に保持する。</summary>
public sealed record ExplorerVerificationCounts(int Candidate, int Replayed, int Verified, int Retired);

/// <summary>Explorer画面へ渡す一時点の読み取り専用表示状態。</summary>
public sealed record ExplorerScreenSnapshot(
    string GameId,
    string EnvironmentScope,
    string StructureRevisionId,
    int KnownStateCount,
    int NovelStateCount,
    IReadOnlyList<string> FrontierIds,
    string ActiveProbeLabel,
    string RiskLabel,
    int RemainingProbeCount,
    long RemainingElapsedMilliseconds,
    long RemainingInferenceMilliseconds,
    IReadOnlyList<string> RecoveryPathEdgeIds,
    string StopReasonLabel,
    ExplorerVerificationCounts VerificationCounts,
    IReadOnlyList<ExplorerNodeItem> Nodes,
    bool CanPause,
    bool CanStep,
    bool CanAbandon,
    bool CanCorrect);

public sealed record ExplorerNodeItem(string StateId, string Label, string EvidenceLevelLabel)
{
    public string DisplayLabel => $"{Label}　[{EvidenceLevelLabel}]";
}

public sealed record ExplorerLabelCorrection(string StateId, string NewLabel, string Reason);

/// <summary>
/// ExplorerのI/O境界。DesktopはSQLiteや探索runtimeを参照せず、Host実装へintentだけを渡す。
/// </summary>
public interface IExplorerIntents
{
    IReadOnlyList<ExplorerScopeOption> ListScopes();

    ExplorerScreenSnapshot Load(string gameId, string environmentScope);

    ExplorerScreenSnapshot Pause(string gameId, string environmentScope);

    ExplorerScreenSnapshot Step(string gameId, string environmentScope);

    ExplorerScreenSnapshot Abandon(string gameId, string environmentScope);

    ExplorerScreenSnapshot Correct(string gameId, string environmentScope, ExplorerLabelCorrection correction);
}

/// <summary>Windowから状態更新と入力検証を分離した薄い操作面。</summary>
public sealed class ExplorerWorkspace(IExplorerIntents intents)
{
    private readonly IExplorerIntents intents = intents ?? throw new ArgumentNullException(nameof(intents));

    public IReadOnlyList<ExplorerScopeOption> ListScopes() => intents.ListScopes();

    public ExplorerScreenSnapshot Load(ExplorerScopeOption scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return intents.Load(scope.GameId, scope.EnvironmentScope);
    }

    public ExplorerScreenSnapshot Pause(ExplorerScopeOption scope) =>
        intents.Pause(scope.GameId, scope.EnvironmentScope);

    public ExplorerScreenSnapshot Step(ExplorerScopeOption scope) =>
        intents.Step(scope.GameId, scope.EnvironmentScope);

    public ExplorerScreenSnapshot Abandon(ExplorerScopeOption scope) =>
        intents.Abandon(scope.GameId, scope.EnvironmentScope);

    public ExplorerScreenSnapshot Correct(ExplorerScopeOption scope, ExplorerLabelCorrection correction)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(correction);
        if (string.IsNullOrWhiteSpace(correction.StateId)
            || string.IsNullOrWhiteSpace(correction.NewLabel)
            || string.IsNullOrWhiteSpace(correction.Reason))
        {
            throw new ArgumentException("訂正対象、新しい名前、訂正理由をすべて入力してください。", nameof(correction));
        }

        return intents.Correct(scope.GameId, scope.EnvironmentScope, correction);
    }
}
