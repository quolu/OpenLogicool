using System.Collections.ObjectModel;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Domain;

public sealed record RunEventSequenceState(long LastSequence, long CurrentExecutorEpoch);

public sealed class RunEventSequenceModel
{
    private readonly IReadOnlyDictionary<string, RunEventSequenceState> _runs;

    public RunEventSequenceModel()
        : this(new ReadOnlyDictionary<string, RunEventSequenceState>(new Dictionary<string, RunEventSequenceState>(StringComparer.Ordinal)))
    {
    }

    private RunEventSequenceModel(IReadOnlyDictionary<string, RunEventSequenceState> runs)
    {
        _runs = runs;
    }

    public RunEventSequenceModel Append(RunEvent runEvent)
    {
        var current = _runs.TryGetValue(runEvent.RunId, out var existing)
            ? existing
            : new RunEventSequenceState(LastSequence: 0, CurrentExecutorEpoch: runEvent.ExecutorEpoch);
        var expectedSequence = checked(current.LastSequence + 1);

        if (runEvent.RunSequence != expectedSequence)
        {
            throw new InvalidOperationException($"Run '{runEvent.RunId}' の runSequence は {expectedSequence} でなければなりません。");
        }

        if (runEvent.ExecutorEpoch < current.CurrentExecutorEpoch)
        {
            throw new InvalidOperationException($"Run '{runEvent.RunId}' の stale executor epoch は append できません。");
        }

        var runs = new Dictionary<string, RunEventSequenceState>(_runs, StringComparer.Ordinal)
        {
            [runEvent.RunId] = new RunEventSequenceState(runEvent.RunSequence, runEvent.ExecutorEpoch),
        };

        return new RunEventSequenceModel(new ReadOnlyDictionary<string, RunEventSequenceState>(runs));
    }
}
