using System.Collections.ObjectModel;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;

namespace OpenLogicool.Playbooks;

/// <summary>
/// session replayer（t09）。永続化済み journal の実 event だけから Run ごとの projection を再生する（OPS-008）。
/// store の読み取り API（ListRunIds／ReadRun）しか呼ばず、書き込みの口を持たない——
/// replay が journal や active Run の pin 済み version を変えない性質は構造で成立する。
/// </summary>
public static class SessionReplayer
{
    public static IReadOnlyDictionary<string, RunProjection> Replay(IRunJournalStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var projections = new Dictionary<string, RunProjection>(StringComparer.Ordinal);
        foreach (var runId in store.ListRunIds())
        {
            projections[runId] = RunProjection.Replay(store.ReadRun(runId));
        }

        return new ReadOnlyDictionary<string, RunProjection>(projections);
    }
}
