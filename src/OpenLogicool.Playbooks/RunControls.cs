using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;

namespace OpenLogicool.Playbooks;

/// <summary>物理入力が Run の Semantic Action へ届いた時の仲裁結果（PB-013・§6.5）。</summary>
public enum PhysicalInputArbitration
{
    /// <summary>Run の Playbook が使わない action——Run の関知外（通常の mapping 配送はそのまま）。</summary>
    NotBoundToRun,

    /// <summary>既に manual intervention 中——event を増やさず介入継続（介入中の操作は帰属させない）。</summary>
    AlreadyIntervening,

    /// <summary>同じ Semantic Action への到達を manual intervention として記録し、executor を止めた。</summary>
    ExecutorStopped,

    /// <summary>Run は Abandoned 済みで仲裁対象がない。</summary>
    RunClosed,
}

/// <summary>
/// Run 制御の統括（PB-007/013・§6.8。仲裁方式は §6.5 の宣言どおり「停止」を採用——マスクしない）。
///
/// - pause／resume は journal を書かない（durable な進行効果が無い——再起動後に自動で走る経路が無い）。
/// - 一手実行（step）は Paused でだけ、既存 gate の ArmThenDispatch を1回通す。自動継続しない。
/// - skip は journal の skip event（§6.8「skipを別eventにする」）。現在 version に実在する node だけ。
/// - manual intervention は開始・終了とも manual-intervention event として記録し、開始で executor を
///   止め、終了後は新しい Observation の記録まで進行を拒否する（§6.8）。介入中に Attempt の解決へ
///   到達する口はこの型に無い（PB-013: 介入中の操作を AI・直前 Attempt の成功原因へ帰属させない）。
/// - abandon は run 単位の abandon event を記録し、進行中 Attempt を §6.7 の合法経路だけで終端へ倒す
///   （dispatch 前は Cancelled への写像・PB-007）。
/// - version switch は Paused かつ現在停止位置での再観察後だけ（§6.8）。進捗継承は同一 stable node ID
///   かつ前後 condition 一致の node だけに許し、継承できない切替は拒否する。
/// - 全操作は現在 Run の RunId・PlaybookId・pin 済み version を運ぶ event だけを受ける
///   （version-switch だけが新 version を運ぶ——閉集合コメントどおり唯一の例外）。
/// </summary>
public sealed class RunControls
{
    private readonly RunJournal _journal;
    private readonly AttemptDispatchGate _gate;
    private readonly string _runId;
    private PlaybookRun _run;
    private RunControlState _state;

    public RunControls(RunJournal journal, AttemptDispatchGate gate, string runId, PlaybookRun run)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(run);
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("RunId が空です。", nameof(runId));
        }

        _journal = journal;
        _gate = gate;
        _runId = runId;
        _run = run;
        // event の無い新規 Run だけ Start()＝Running。既存 journal は再構築し、再観察待ちを捨てない。
        var recorded = journal.ReadRun(runId);
        _state = recorded.Count == 0 ? RunControlState.Start() : RunControlState.FromJournal(recorded);
    }

    public RunControlState State => _state;

    public PlaybookRun Run => _run;

    public void Pause() => _state = _state.Pause();

    public void Resume() => _state = _state.Resume();

    /// <summary>
    /// 一手実行（PB-007）。Paused のまま既存 gate の ArmThenDispatch を1回だけ通す。
    /// 実行後も Paused であり、次の一手は明示操作だけが出す。
    /// </summary>
    public void StepOnce(RunEvent dispatchEvent, Action externalInput)
    {
        if (!_state.CanStep)
        {
            throw new InvalidOperationException("一手実行は Paused かつ再観察待ちでない時だけ実行できます（PB-007・§6.8）。");
        }

        RequireCurrentRunAndPin(dispatchEvent);
        _gate.ArmThenDispatch(dispatchEvent, externalInput);
    }

    /// <summary>skip event を記録する（§6.8）。dispatch も Attempt も作らない。</summary>
    public void Skip(RunEvent skipEvent)
    {
        RequirePayloadType(skipEvent, RunEventPayloadTypes.Skip);
        RequireUserActor(skipEvent);
        if (_state.Phase is not (RunControlPhase.Running or RunControlPhase.Paused))
        {
            throw new InvalidOperationException($"Run 制御の {_state.Phase} で skip は実行できません（PB-007）。");
        }

        if (_state.NeedsReobservation)
        {
            throw new InvalidOperationException("manual intervention 後は新しい Observation が記録されるまで skip できません（§6.8）。");
        }

        RequireCurrentRunAndPin(skipEvent);
        var nodeOrTransitionId = skipEvent.NodeOrTransitionId
            ?? throw new ArgumentException("skip event には NodeOrTransitionId が必要です（§6.8）。", nameof(skipEvent));
        var known = _run.PinnedVersion.Nodes.Any(node => string.Equals(node.NodeId, nodeOrTransitionId, StringComparison.Ordinal))
            || _run.PinnedVersion.Edges.Any(edge => string.Equals(edge.EdgeId, nodeOrTransitionId, StringComparison.Ordinal));
        if (!known)
        {
            throw new InvalidOperationException(
                $"skip 対象 '{nodeOrTransitionId}' は pin 済み version '{_run.PinnedVersionId}' に存在しません。");
        }

        _journal.Append(skipEvent);
    }

    /// <summary>manual intervention の開始を記録し、executor を止める（PB-013）。</summary>
    public void BeginManualIntervention(RunEvent interventionEvent)
    {
        RequirePayloadType(interventionEvent, RunEventPayloadTypes.ManualIntervention);
        RequireUserActor(interventionEvent);
        RequireCurrentRunAndPin(interventionEvent);
        var next = _state.BeginManualIntervention();
        _journal.Append(interventionEvent);
        _state = next;
    }

    /// <summary>manual intervention の終了を記録する。以後、新しい Observation まで進行不可（§6.8）。</summary>
    public void EndManualIntervention(RunEvent interventionEvent)
    {
        RequirePayloadType(interventionEvent, RunEventPayloadTypes.ManualIntervention);
        RequireUserActor(interventionEvent);
        RequireCurrentRunAndPin(interventionEvent);
        var next = _state.EndManualIntervention();
        _journal.Append(interventionEvent);
        _state = next;
    }

    /// <summary>
    /// Attempt に束縛されない run-level の observation event を記録し、再照合済みへ反映する。
    /// Attempt 束縛の観測は gate（CommitObserving）だけが持つ——ここへ AttemptId 付き event を流さない。
    /// </summary>
    public void RecordObservation(RunEvent observationEvent)
    {
        RequirePayloadType(observationEvent, RunEventPayloadTypes.Observation);
        if (observationEvent.AttemptId is not null)
        {
            throw new ArgumentException(
                "Attempt に束縛された observation は gate（CommitObserving）へ渡します。RunControls は run-level の再照合観測だけを受けます。",
                nameof(observationEvent));
        }

        RequireCurrentRunAndPin(observationEvent);
        var next = _state.ObservationRecorded();
        _journal.Append(observationEvent);
        _state = next;
    }

    /// <summary>
    /// Attempt 束縛の observation を制御状態の下で gate へ通す（t07・すずね [119] note の閉鎖）。
    /// manual intervention 中は拒否——「介入開始と終了の間に observation event が現れない」journal 並び
    /// （再開照合 PB-009 の前提）を attempt 束縛側でも保証する。介入終了後は run-level の再照合
    /// （RecordObservation）が済むまで拒否する（§6.8: 進行は再観察の後）。
    /// </summary>
    public void CommitAttemptObserving(RunEvent observationEvent)
    {
        RequirePayloadType(observationEvent, RunEventPayloadTypes.Observation);
        if (_state.Phase is RunControlPhase.ManualIntervention or RunControlPhase.Abandoned)
        {
            throw new InvalidOperationException(
                $"Run 制御の {_state.Phase} で Attempt の観測は記録できません（PB-013・§6.8）。");
        }

        if (_state.NeedsReobservation)
        {
            throw new InvalidOperationException(
                "manual intervention 後は run-level の再照合（新しい Observation の記録）が済むまで Attempt の観測を進められません（§6.8）。");
        }

        RequireCurrentRunAndPin(observationEvent);
        _gate.CommitObserving(observationEvent);
    }

    /// <summary>
    /// 物理入力が Semantic Action へ解決された時の仲裁（PB-013・§6.5）。Run の Playbook が使う action
    /// なら manual intervention として停止し、Run 進行へは合流させない。それ以外は Run の関知外。
    /// </summary>
    public PhysicalInputArbitration OnPhysicalSemanticAction(string semanticActionId, Func<RunEvent> interventionEventFactory)
    {
        ArgumentNullException.ThrowIfNull(interventionEventFactory);
        if (string.IsNullOrWhiteSpace(semanticActionId))
        {
            throw new ArgumentException("SemanticActionId が空です。", nameof(semanticActionId));
        }

        if (_state.Phase == RunControlPhase.Abandoned)
        {
            return PhysicalInputArbitration.RunClosed;
        }

        var bound = _run.PinnedVersion.Nodes.Any(node =>
            node.SemanticActionId is not null && string.Equals(node.SemanticActionId, semanticActionId, StringComparison.Ordinal));
        if (!bound)
        {
            return PhysicalInputArbitration.NotBoundToRun;
        }

        if (_state.Phase == RunControlPhase.ManualIntervention)
        {
            return PhysicalInputArbitration.AlreadyIntervening;
        }

        BeginManualIntervention(interventionEventFactory());
        return PhysicalInputArbitration.ExecutorStopped;
    }

    /// <summary>
    /// Run を中止する（PB-007）。abandon event を記録し、進行中 Attempt を §6.7 の合法経路で終端へ倒す:
    /// dispatch 前は Cancelled、dispatch し得た後は OutcomeUnknown→Reconciling→Abandoned。
    /// Attempt ごとの終端は journal に別 event を持たない——復元は run 単位の abandon event から
    /// gate.Recover が同じ分類を再現する。
    /// </summary>
    public void Abandon(RunEvent abandonEvent)
    {
        RequirePayloadType(abandonEvent, RunEventPayloadTypes.Abandon);
        RequireUserActor(abandonEvent);
        RequireCurrentRunAndPin(abandonEvent);
        var next = _state.Abandon();
        _journal.Append(abandonEvent);
        _state = next;

        foreach (var attempt in _gate.Attempts.Where(candidate => !candidate.IsTerminal).ToList())
        {
            if (attempt.State is AttemptState.Proposed or AttemptState.Authorized or AttemptState.Prepared)
            {
                _gate.ResolveLocally(attempt.AttemptId, AttemptState.Cancelled);
                continue;
            }

            if (attempt.State is AttemptState.DispatchArmed or AttemptState.DispatchReported or AttemptState.Observing)
            {
                _gate.ResolveLocally(attempt.AttemptId, AttemptState.OutcomeUnknown);
            }

            if (_gate.Get(attempt.AttemptId).State == AttemptState.OutcomeUnknown)
            {
                _gate.ResolveLocally(attempt.AttemptId, AttemptState.Reconciling);
            }

            _gate.ResolveLocally(attempt.AttemptId, AttemptState.Abandoned);
        }
    }

    /// <summary>
    /// 正規の version 切替（PB-007・§6.8）。Paused かつ現在停止位置での再観察後だけ許可し、
    /// 進捗継承は同一 stable node ID・前後 condition 一致の node だけに許す。
    /// event は新 version を運ぶ（pin と異なる version を運んでよい唯一の event）。
    /// </summary>
    public void SwitchVersion(RunEvent switchEvent, PlaybookVersion newVersion, string progressNodeId)
    {
        RequirePayloadType(switchEvent, RunEventPayloadTypes.VersionSwitch);
        RequireUserActor(switchEvent);
        ArgumentNullException.ThrowIfNull(newVersion);
        if (string.IsNullOrWhiteSpace(progressNodeId))
        {
            throw new ArgumentException("進捗継承の現在 node ID が空です。", nameof(progressNodeId));
        }

        if (!_state.CanSwitchVersion)
        {
            throw new InvalidOperationException(
                "version 切替は Paused かつ現在 state を再照合（停止後に新しい Observation を記録）した後だけ許可されます（§6.8）。");
        }

        if (!string.Equals(switchEvent.RunId, _runId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"event の RunId '{switchEvent.RunId}' はこの Run '{_runId}' ではありません。", nameof(switchEvent));
        }

        if (!string.Equals(switchEvent.PlaybookId, _run.PlaybookId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"event の PlaybookId '{switchEvent.PlaybookId}' はこの Run の Playbook ではありません。", nameof(switchEvent));
        }

        if (string.Equals(newVersion.VersionId, _run.PinnedVersionId, StringComparison.Ordinal))
        {
            throw new ArgumentException("切替先が pin 済み version と同一です。切替は新 version だけを受けます。", nameof(newVersion));
        }

        if (!string.Equals(switchEvent.PlaybookVersionId, newVersion.VersionId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"version-switch event は新 version '{newVersion.VersionId}' を運ばなければなりません（実際: '{switchEvent.PlaybookVersionId}'）。",
                nameof(switchEvent));
        }

        var newGraph = PlaybookMaterializer.ToGraph(newVersion);
        var currentNode = _run.PinnedVersion.Nodes.FirstOrDefault(node => string.Equals(node.NodeId, progressNodeId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"現在 node '{progressNodeId}' は pin 済み version '{_run.PinnedVersionId}' に存在しません。");
        var inheritedNode = newGraph.Nodes.FirstOrDefault(node => string.Equals(node.NodeId, progressNodeId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"新 version '{newVersion.VersionId}' に node '{progressNodeId}' が無いため進捗を継承できません。切替は拒否します（§6.8）。");
        var compatible = currentNode.Preconditions.SequenceEqual(inheritedNode.Preconditions, StringComparer.Ordinal)
            && currentNode.ExpectedOutcomes.SequenceEqual(inheritedNode.ExpectedOutcomes, StringComparer.Ordinal);
        if (!compatible)
        {
            throw new InvalidOperationException(
                $"node '{progressNodeId}' の前後 condition が新 version と互換でないため進捗を継承できません。切替は拒否します（§6.8）。");
        }

        _journal.Append(switchEvent);
        _run = PlaybookRun.Start(_run.PlaybookId, newGraph);
    }

    private void RequireCurrentRunAndPin(RunEvent runEvent)
    {
        if (!string.Equals(runEvent.RunId, _runId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"event の RunId '{runEvent.RunId}' はこの Run '{_runId}' ではありません。", nameof(runEvent));
        }

        if (!string.Equals(runEvent.PlaybookId, _run.PlaybookId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"event の PlaybookId '{runEvent.PlaybookId}' はこの Run の Playbook ではありません。", nameof(runEvent));
        }

        if (!string.Equals(runEvent.PlaybookVersionId, _run.PinnedVersionId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"event は pin 済み version '{_run.PinnedVersionId}' を運ばなければなりません（実際: '{runEvent.PlaybookVersionId}'）。version を変えるのは version-switch event だけです。",
                nameof(runEvent));
        }
    }

    private static void RequirePayloadType(RunEvent runEvent, string expectedPayloadType)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        if (!string.Equals(runEvent.PayloadType, expectedPayloadType, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"この操作は payload type '{expectedPayloadType}' の event だけを受け取ります（実際: '{runEvent.PayloadType}'）。", nameof(runEvent));
        }
    }

    private static void RequireUserActor(RunEvent runEvent)
    {
        if (runEvent.ActorType != RunEventActorType.User)
        {
            throw new ArgumentException(
                $"run 制御 event の ActorType は User だけです（実際: {runEvent.ActorType}）。制御操作を自動化へ帰属させません（PB-013）。",
                nameof(runEvent));
        }
    }
}
