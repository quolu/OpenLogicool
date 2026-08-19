using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;

namespace OpenLogicool.Playbooks;

/// <summary>
/// Execution Journal の append 統括（PB-006、OPS-008／009。journal の統括は Playbooks——§6.3）。
/// payload type 閉集合・event 種別ごとの必須 ID・run ごとの連番・stale epoch を検証してから
/// store へ追記し、同じ遷移を correlation 情報だけで engineering log へ記録する。
/// 訂正（correction）も新しい event の追記であり、確定済み event を書き換える口は無い。
/// </summary>
public sealed class RunJournal
{
    private readonly IRunJournalStore _store;
    private readonly IEngineeringLogSink _engineeringLog;
    private RunEventSequenceModel _model;

    public RunJournal(IRunJournalStore store, IEngineeringLogSink engineeringLog)
        : this(store, engineeringLog, new RunEventSequenceModel())
    {
    }

    private RunJournal(IRunJournalStore store, IEngineeringLogSink engineeringLog, RunEventSequenceModel model)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(engineeringLog);
        _store = store;
        _engineeringLog = engineeringLog;
        _model = model;
    }

    /// <summary>
    /// app 再起動後、永続化済み journal から sequence 状態を再生成して追記を再開できる形にする（OPS-008）。
    /// 復元は store の実 event の replay だけを根拠にし、checkpoint 等の別経路を持たない。
    /// </summary>
    public static RunJournal Restore(IRunJournalStore store, IEngineeringLogSink engineeringLog)
    {
        ArgumentNullException.ThrowIfNull(store);
        var model = RunEventSequenceModel.Replay(
            store.ListRunIds().SelectMany(store.ReadRun));
        return new RunJournal(store, engineeringLog, model);
    }

    public void Append(RunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        if (!RunEventPayloadTypes.IsKnown(runEvent.PayloadType))
        {
            throw new ArgumentException(
                $"payload type '{runEvent.PayloadType}' は journal の閉集合にありません（PB-006）。", nameof(runEvent));
        }

        RequireEventIds(runEvent);

        // sequence／epoch 検証（Domain）を通ってから永続化する。検証で落ちた event は store に現れない。
        var next = _model.Append(runEvent);
        _store.Append(runEvent);
        _model = next;

        _engineeringLog.Record(new EngineeringLogEntry(
            runEvent.SchemaVersion,
            runEvent.PersistedUtc,
            runEvent.CorrelationId,
            runEvent.CausationId,
            runEvent.RunId,
            runEvent.RunSequence,
            runEvent.EventId,
            runEvent.PayloadType));
    }

    private static void RequireEventIds(RunEvent runEvent)
    {
        switch (runEvent.PayloadType)
        {
            // 観測 event は Observation を必ず束縛する（§6.7: Observing 以降の event は observationId 必須）。
            case RunEventPayloadTypes.Observation when runEvent.ObservationId is null:
                throw new ArgumentException("observation event には ObservationId が必要です。", nameof(runEvent));
            // 確定は Attempt ID と Observation ID を併記した RunEvent だけで成立する（§6.7 契約4）。
            case RunEventPayloadTypes.Confirmation when runEvent.AttemptId is null || runEvent.ObservationId is null:
                throw new ArgumentException("confirmation event には AttemptId と ObservationId の併記が必要です（§6.7 契約4）。", nameof(runEvent));
            case RunEventPayloadTypes.Dispatch when runEvent.AttemptId is null || runEvent.CommandId is null:
                throw new ArgumentException("dispatch event には AttemptId と CommandId が必要です。", nameof(runEvent));
            case RunEventPayloadTypes.DispatchResult when runEvent.AttemptId is null:
                throw new ArgumentException("dispatch-result event には AttemptId が必要です。", nameof(runEvent));
            // skip は「どの手順を飛ばしたか」が本体であり、node／transition の束縛なしでは意味を持たない（§6.8）。
            case RunEventPayloadTypes.Skip when runEvent.NodeOrTransitionId is null:
                throw new ArgumentException("skip event には NodeOrTransitionId が必要です（§6.8）。", nameof(runEvent));
            // run 制御3種（t05）は利用者操作の記録だけを受ける（PB-013: 制御操作を自動化へ帰属させない）。
            case RunEventPayloadTypes.Skip or RunEventPayloadTypes.Abandon or RunEventPayloadTypes.VersionSwitch
                when runEvent.ActorType != RunEventActorType.User:
                throw new ArgumentException(
                    $"{runEvent.PayloadType} event の ActorType は User だけです（PB-013）。", nameof(runEvent));
            // disarm はどの Attempt を保証付きで止めたかが本体（§6.7・t07）。
            case RunEventPayloadTypes.Disarm when runEvent.AttemptId is null:
                throw new ArgumentException("disarm event には AttemptId が必要です（§6.7）。", nameof(runEvent));
            // disarm は runtime の保証判定の記録であり、利用者操作でも自動化の成功でもない（t07）。
            case RunEventPayloadTypes.Disarm when runEvent.ActorType != RunEventActorType.System:
                throw new ArgumentException(
                    "disarm event の ActorType は System だけです（runtime の保証判定の記録）。", nameof(runEvent));
            default:
                break;
        }
    }
}
