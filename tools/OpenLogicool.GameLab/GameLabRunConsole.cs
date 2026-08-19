using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.GameLab;

/// <summary>
/// GameLab の Run 操作卓（UX-003／004）。
/// pause／emergency stop は内部 flag の遷移だけで即時に成立し、AI・capture・対象 device の
/// 応答を一切待たない（UX-004。GameLab assembly がそれらの module を参照しないことは
/// 参照検証テストが保証する）。
/// 現在 state の入力は GameLab oracle の状態・fake Observation・Attempt 報告だけで、
/// 実画面を受け取る口が無い。
/// </summary>
public sealed class GameLabRunConsole
{
    private bool _paused;
    private bool _emergencyStopped;
    private bool _targetMismatch;
    private AttemptState? _activeAttempt;
    private ObservationStatus? _latestObservation;
    private GameLabRunOutcome? _outcome;

    /// <summary>即時 pause。外部呼び出し・待機なし（UX-004）。</summary>
    public void Pause() => _paused = true;

    public void Resume()
    {
        if (_emergencyStopped)
        {
            throw new InvalidOperationException("emergency stop 後の再開はできません。新しい Run を開始します。");
        }

        _paused = false;
    }

    /// <summary>即時 emergency stop。解除の口は無い（新しい Run でだけ再開する）。</summary>
    public void EmergencyStop() => _emergencyStopped = true;

    /// <summary>停止中・終端後は新しい dispatch を出せない。</summary>
    public bool CanDispatch => !_paused && !_emergencyStopped && _outcome is null;

    public void ReportAttempt(AttemptState state) => _activeAttempt = state;

    /// <summary>fake Observation の status を報告する（Phase 4 の観測根拠はこれと oracle だけ）。</summary>
    public void ReportObservation(ObservationStatus status) => _latestObservation = status;

    /// <summary>再開・対象照合の結果を報告する。判定自体は照合側（t10）が所有する。</summary>
    public void ReportTargetMatch(bool matches) => _targetMismatch = !matches;

    public void ReportOutcome(GameLabRunOutcome outcome)
    {
        if (_outcome is not null)
        {
            throw new InvalidOperationException($"Run はすでに {_outcome} で終端しています。");
        }

        _outcome = outcome;
    }

    /// <summary>UX-003: 現在の表示状態。どの内部状態でも必ず1状態が返る。</summary>
    public GameLabRunStatus CurrentStatus =>
        GameLabStatusProjector.Project(new GameLabStatusInput(
            _paused, _emergencyStopped, _targetMismatch, _activeAttempt, _latestObservation, _outcome));
}

/// <summary>実行履歴の閲覧1行（APP-010）。journal の相関情報の要約で、payload 本文は運ばない。</summary>
public sealed record RunHistoryEntry(
    long RunSequence,
    string PayloadType,
    string? AttemptId,
    string CorrelationId,
    DateTimeOffset OccurredUtc);

/// <summary>
/// 実行履歴の閲覧 read model（APP-010）。journal store を読むだけで、書く口を持たない。
/// Playbook の閲覧は immutable な PlaybookVersion そのもの、編集は PlaybookCorrection（PB-008・新 version 作成）が正であり、
/// この view は複製しない。
/// </summary>
public static class RunHistoryView
{
    public static IReadOnlyList<string> ListRuns(IRunJournalStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.ListRunIds();
    }

    public static IReadOnlyList<RunHistoryEntry> Summarize(IRunJournalStore store, string runId)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.ReadRun(runId)
            .Select(runEvent => new RunHistoryEntry(
                runEvent.RunSequence,
                runEvent.PayloadType,
                runEvent.AttemptId,
                runEvent.CorrelationId,
                runEvent.OccurredUtc))
            .ToArray();
    }
}
