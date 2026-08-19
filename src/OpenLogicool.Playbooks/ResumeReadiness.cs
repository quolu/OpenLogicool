using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Playbooks;

/// <summary>
/// 再開照合が journal から読む事実（pure・1 Run の event 列は runSequence 昇順前提）。
/// t05 が追加する payload type（interface 決定 room [90]・[97]）を event 直読で扱う:
/// abandon は run の閉止、version-switch は採用 version の移動、manual-intervention は
/// 「最後の manual-intervention event の後に新しい observation が commit されるまで進行不可」。
/// RunProjection には依存しない（照合は event 直読。tally 側の 3 type は projection が数える）。
/// </summary>
public static class ResumeReadiness
{
    /// <summary>abandon event を持つ run は閉じており、再開できない。</summary>
    public static bool IsRunClosed(IReadOnlyList<RunEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events.Any(e => string.Equals(e.PayloadType, RunEventPayloadTypes.Abandon, StringComparison.Ordinal));
    }

    /// <summary>
    /// 採用 version（UX-005「採用version」・PB-009 の version 照合対象）:
    /// version-switch event があれば最後の switch event が運ぶ PlaybookVersionId、無ければ先頭 event の pin。
    /// </summary>
    public static string AdoptedVersionId(IReadOnlyList<RunEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            throw new InvalidOperationException("event の無い Run に採用 version はありません。");
        }

        var lastSwitch = events.LastOrDefault(e =>
            string.Equals(e.PayloadType, RunEventPayloadTypes.VersionSwitch, StringComparison.Ordinal));
        return (lastSwitch ?? events[0]).PlaybookVersionId;
    }

    /// <summary>
    /// §6.8「manual intervention 終了後は必ず新 Observation から照合する」:
    /// 最後の manual-intervention event より後に、再開照合へ使う Observation の observation event が
    /// commit されている時だけ真。manual intervention が無い run は常に真。
    /// 開始 event だけで observation が続かない run（介入中 crash 等）は偽＝再開不可の安全側に落ちる。
    /// </summary>
    public static bool SatisfiesReobservation(IReadOnlyList<RunEvent> events, string resumeObservationId)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (string.IsNullOrWhiteSpace(resumeObservationId))
        {
            throw new ArgumentException("再開照合に使う ObservationId が空です。", nameof(resumeObservationId));
        }

        var lastIntervention = events.LastOrDefault(e =>
            string.Equals(e.PayloadType, RunEventPayloadTypes.ManualIntervention, StringComparison.Ordinal));
        if (lastIntervention is null)
        {
            return true;
        }

        return events.Any(e =>
            string.Equals(e.PayloadType, RunEventPayloadTypes.Observation, StringComparison.Ordinal)
            && e.RunSequence > lastIntervention.RunSequence
            && string.Equals(e.ObservationId, resumeObservationId, StringComparison.Ordinal));
    }
}
