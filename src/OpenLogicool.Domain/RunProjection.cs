using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Domain;

/// <summary>payload type（PB-006 閉集合）ごとの event 件数。projection の値等価比較の一部。</summary>
public sealed record RunEventTally(
    long Observations,
    long Proposals,
    long Approvals,
    long Dispatches,
    long DispatchResults,
    long Confirmations,
    long Corrections,
    long ManualInterventions,
    long Skips,
    long Abandons,
    long VersionSwitches,
    long Disarms)
{
    public static RunEventTally Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public RunEventTally Increment(string payloadType) => payloadType switch
    {
        RunEventPayloadTypes.Observation => this with { Observations = checked(Observations + 1) },
        RunEventPayloadTypes.Proposal => this with { Proposals = checked(Proposals + 1) },
        RunEventPayloadTypes.Approval => this with { Approvals = checked(Approvals + 1) },
        RunEventPayloadTypes.Dispatch => this with { Dispatches = checked(Dispatches + 1) },
        RunEventPayloadTypes.DispatchResult => this with { DispatchResults = checked(DispatchResults + 1) },
        RunEventPayloadTypes.Confirmation => this with { Confirmations = checked(Confirmations + 1) },
        RunEventPayloadTypes.Correction => this with { Corrections = checked(Corrections + 1) },
        RunEventPayloadTypes.ManualIntervention => this with { ManualInterventions = checked(ManualInterventions + 1) },
        RunEventPayloadTypes.Skip => this with { Skips = checked(Skips + 1) },
        RunEventPayloadTypes.Abandon => this with { Abandons = checked(Abandons + 1) },
        RunEventPayloadTypes.VersionSwitch => this with { VersionSwitches = checked(VersionSwitches + 1) },
        RunEventPayloadTypes.Disarm => this with { Disarms = checked(Disarms + 1) },
        _ => throw new ArgumentException($"payload type '{payloadType}' は journal の閉集合にありません（PB-006）。", nameof(payloadType)),
    };
}

/// <summary>
/// 1 Run の journal projection（pure・immutable・値等価）。逐次適用（Apply）と
/// 永続 event 列からの再生（Replay）が同じ入力で同じ値になることを比較可能にする（PB-006／OPS-008）。
/// Run が pin した Playbook version と異なる version を運ぶ event は例外で拒否する——
/// projection の version が黙って変わる経路を持たない（PB-002 の pin を replay 側でも保つ）。
/// </summary>
public sealed record RunProjection(
    string RunId,
    string PlaybookId,
    string PinnedPlaybookVersionId,
    long LastSequence,
    long CurrentExecutorEpoch,
    string LastEventId,
    string? LastObservationId,
    RunEventTally Tally)
{
    /// <summary>Run の先頭 event（runSequence は 1）から projection を開始する。</summary>
    public static RunProjection FromFirstEvent(RunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        if (runEvent.RunSequence != 1)
        {
            throw new InvalidOperationException(
                $"Run '{runEvent.RunId}' の projection は runSequence 1 の event からしか開始できません。");
        }

        return new RunProjection(
            runEvent.RunId,
            runEvent.PlaybookId,
            runEvent.PlaybookVersionId,
            LastSequence: runEvent.RunSequence,
            CurrentExecutorEpoch: runEvent.ExecutorEpoch,
            LastEventId: runEvent.EventId,
            LastObservationId: runEvent.PayloadType == RunEventPayloadTypes.Observation ? runEvent.ObservationId : null,
            Tally: RunEventTally.Empty.Increment(runEvent.PayloadType));
    }

    public RunProjection Apply(RunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        if (!string.Equals(runEvent.RunId, RunId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Run '{RunId}' の projection へ Run '{runEvent.RunId}' の event は適用できません。");
        }

        var expectedSequence = checked(LastSequence + 1);
        if (runEvent.RunSequence != expectedSequence)
        {
            throw new InvalidOperationException($"Run '{RunId}' の runSequence は {expectedSequence} でなければなりません。");
        }

        if (runEvent.ExecutorEpoch < CurrentExecutorEpoch)
        {
            throw new InvalidOperationException($"Run '{RunId}' の stale executor epoch は適用できません。");
        }

        if (!string.Equals(runEvent.PlaybookId, PlaybookId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Run '{RunId}' の PlaybookId は '{PlaybookId}' から変えられません。");
        }

        // PB-002: 黙った version 変更は拒否する。正規の切替は version-switch だけが新 version を運ぶ（PB-007）。
        var isVersionSwitch = string.Equals(runEvent.PayloadType, RunEventPayloadTypes.VersionSwitch, StringComparison.Ordinal);
        if (!isVersionSwitch
            && !string.Equals(runEvent.PlaybookVersionId, PinnedPlaybookVersionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Run '{RunId}' は Playbook version '{PinnedPlaybookVersionId}' へ pin されており、'{runEvent.PlaybookVersionId}' の event は適用できません。");
        }

        return this with
        {
            PinnedPlaybookVersionId = isVersionSwitch ? runEvent.PlaybookVersionId : PinnedPlaybookVersionId,
            LastSequence = runEvent.RunSequence,
            CurrentExecutorEpoch = runEvent.ExecutorEpoch,
            LastEventId = runEvent.EventId,
            LastObservationId = runEvent.PayloadType == RunEventPayloadTypes.Observation
                ? runEvent.ObservationId
                : LastObservationId,
            Tally = Tally.Increment(runEvent.PayloadType),
        };
    }

    /// <summary>
    /// 永続化済み journal の 1 Run 分の event 列（runSequence 昇順）から projection を再生する（OPS-008）。
    /// 逐次 Apply と同じ検証を通るため、同じ event 列からは同じ値の projection が得られる。
    /// </summary>
    public static RunProjection Replay(IEnumerable<RunEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        RunProjection? projection = null;
        foreach (var runEvent in events)
        {
            projection = projection is null ? FromFirstEvent(runEvent) : projection.Apply(runEvent);
        }

        return projection
            ?? throw new InvalidOperationException("event の無い Run は projection を再生できません。");
    }
}
