using System.Collections.ObjectModel;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;

namespace OpenLogicool.Playbooks;

/// <summary>
/// session recorder（t09）。journal へ追記する event と同じ列から Run ごとの projection を逐次構築し、
/// 「journal replay と projection の一致」を比較可能な形で保つ。
/// 順序は projection 計算（pure・失敗しても store 未書込）→ journal append（検証＋永続化）→ projection 確定。
/// どちらかの検証で落ちた event は store にも projection にも現れない——両者が別々の内容になる経路を持たない。
/// </summary>
public sealed class SessionRecorder
{
    private readonly RunJournal _journal;
    private readonly Dictionary<string, RunProjection> _projections;

    private SessionRecorder(RunJournal journal, Dictionary<string, RunProjection> projections)
    {
        _journal = journal;
        _projections = projections;
    }

    /// <summary>
    /// 永続化済み journal から recorder を復元する（OPS-008）。空の store なら新規 session の開始であり、
    /// crash 後は store の実 event の replay だけを根拠に projection と追記位置を再生する。
    /// pin 済み version は replay された event が運ぶ値そのままで、復元が version を変える口は無い。
    /// </summary>
    public static SessionRecorder Restore(IRunJournalStore store, IEngineeringLogSink engineeringLog)
    {
        ArgumentNullException.ThrowIfNull(store);
        var journal = RunJournal.Restore(store, engineeringLog);
        var projections = new Dictionary<string, RunProjection>(StringComparer.Ordinal);
        foreach (var pair in SessionReplayer.Replay(store))
        {
            projections[pair.Key] = pair.Value;
        }

        return new SessionRecorder(journal, projections);
    }

    /// <summary>Run ごとの現在 projection（読み取り専用ビュー）。</summary>
    public IReadOnlyDictionary<string, RunProjection> Projections => new ReadOnlyDictionary<string, RunProjection>(_projections);

    public void Record(RunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        var next = _projections.TryGetValue(runEvent.RunId, out var current)
            ? current.Apply(runEvent)
            : RunProjection.FromFirstEvent(runEvent);

        _journal.Append(runEvent);
        _projections[runEvent.RunId] = next;
    }
}
