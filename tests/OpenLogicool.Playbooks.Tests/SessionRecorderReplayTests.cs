using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class SessionRecorderReplayTests
{
    private sealed class RecordingStore : IRunJournalStore
    {
        public List<RunEvent> Events { get; } = [];

        public void Append(RunEvent runEvent) => Events.Add(runEvent);

        public IReadOnlyList<RunEvent> ReadRun(string runId) =>
            Events.Where(e => e.RunId == runId).OrderBy(e => e.RunSequence).ToArray();

        public IReadOnlyList<string> ListRunIds() =>
            Events.Select(e => e.RunId).Distinct(StringComparer.Ordinal).Order().ToArray();

        public IReadOnlyList<ExpiredRunPreview> PreviewExpiredRuns(DateTimeOffset asOfUtc, int retentionDays) => [];

        public void DeleteRun(string runId) => Events.RemoveAll(e => e.RunId == runId);
    }

    private sealed class RecordingSink : IEngineeringLogSink
    {
        public List<EngineeringLogEntry> Entries { get; } = [];

        public void Record(EngineeringLogEntry entry) => Entries.Add(entry);
    }

    private static RunEvent Event(
        long sequence,
        string payloadType,
        string runId = "run-1",
        string versionId = "playbook-version-1",
        string? attemptId = null,
        string? commandId = null,
        string? observationId = null) =>
        new(
            "0.1.0",
            $"{runId}-event-{sequence}",
            runId,
            sequence,
            "playbook-1",
            versionId,
            null,
            commandId,
            attemptId,
            "cause-1",
            $"{runId}-correlation-{sequence}",
            1,
            RunEventActorType.Automation,
            new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 0, 0, 1, TimeSpan.Zero),
            observationId,
            payloadType,
            """{"body":"x"}""");

    private static SessionRecorder RecordSampleSession(RecordingStore store)
    {
        var recorder = SessionRecorder.Restore(store, new RecordingSink());
        recorder.Record(Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"));
        recorder.Record(Event(1, RunEventPayloadTypes.Observation, runId: "run-2", versionId: "playbook-version-2", observationId: "observation-a"));
        recorder.Record(Event(2, RunEventPayloadTypes.Proposal));
        recorder.Record(Event(3, RunEventPayloadTypes.Dispatch, attemptId: "attempt-1", commandId: "command-1"));
        recorder.Record(Event(2, RunEventPayloadTypes.Proposal, runId: "run-2", versionId: "playbook-version-2"));
        return recorder;
    }

    [Fact]
    public void Replay_of_the_persisted_journal_matches_the_recorded_projections()
    {
        var store = new RecordingStore();
        var recorder = RecordSampleSession(store);

        var replayed = SessionReplayer.Replay(store);

        // 受入条件4: journal replay と projection が一致する（run を跨いだ interleave 込み）。
        Assert.Equal(recorder.Projections.Keys.Order(), replayed.Keys.Order());
        foreach (var pair in recorder.Projections)
        {
            Assert.Equal(pair.Value, replayed[pair.Key]);
        }
    }

    [Fact]
    public void Replay_reads_only_and_leaves_the_journal_unchanged()
    {
        var store = new RecordingStore();
        RecordSampleSession(store);
        var before = store.Events.ToArray();

        var first = SessionReplayer.Replay(store);
        var second = SessionReplayer.Replay(store);

        Assert.Equal(before, store.Events);
        Assert.Equal(first, second, ReferenceEqualityIgnoringComparer());
    }

    private static IEqualityComparer<IReadOnlyDictionary<string, RunProjection>> ReferenceEqualityIgnoringComparer() =>
        EqualityComparer<IReadOnlyDictionary<string, RunProjection>>.Create(
            (left, right) => left!.Count == right!.Count
                && left.All(pair => right.TryGetValue(pair.Key, out var value) && pair.Value.Equals(value)),
            _ => 0);

    [Fact]
    public void Record_rejects_an_event_the_journal_refuses_without_touching_the_projection()
    {
        var store = new RecordingStore();
        var recorder = SessionRecorder.Restore(store, new RecordingSink());
        recorder.Record(Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"));
        var before = recorder.Projections["run-1"];

        // dispatch は AttemptId＋CommandId 必須（t03 の journal 検証）——落ちた event は store にも projection にも現れない。
        Assert.Throws<ArgumentException>(() => recorder.Record(Event(2, RunEventPayloadTypes.Dispatch)));
        Assert.Single(store.Events);
        Assert.Equal(before, recorder.Projections["run-1"]);
    }

    [Fact]
    public void Record_rejects_a_version_change_before_anything_is_persisted()
    {
        var store = new RecordingStore();
        var recorder = SessionRecorder.Restore(store, new RecordingSink());
        recorder.Record(Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"));

        // 受入条件5: pin と異なる version の event は projection 検証で拒否され、journal へも書かれない。
        Assert.Throws<InvalidOperationException>(() =>
            recorder.Record(Event(2, RunEventPayloadTypes.Proposal, versionId: "playbook-version-9")));
        Assert.Single(store.Events);
        Assert.Equal("playbook-version-1", recorder.Projections["run-1"].PinnedPlaybookVersionId);
    }

    [Fact]
    public void Restore_after_a_crash_reproduces_the_projections_and_keeps_the_pinned_version()
    {
        var store = new RecordingStore();
        var recorder = RecordSampleSession(store);
        var recordedProjections = recorder.Projections.ToDictionary(pair => pair.Key, pair => pair.Value);

        // crash 相当: 元 recorder を捨て、store の実 event だけから復元する（OPS-008）。
        var restored = SessionRecorder.Restore(store, new RecordingSink());

        foreach (var pair in recordedProjections)
        {
            Assert.Equal(pair.Value, restored.Projections[pair.Key]);
        }

        // 受入条件5: 復元で pin 済み version が変わらない。
        Assert.Equal("playbook-version-1", restored.Projections["run-1"].PinnedPlaybookVersionId);
        Assert.Equal("playbook-version-2", restored.Projections["run-2"].PinnedPlaybookVersionId);

        // 復元後は続きの sequence から追記でき、replay との一致も保たれる。
        restored.Record(Event(4, RunEventPayloadTypes.DispatchResult, attemptId: "attempt-1"));
        Assert.Equal(restored.Projections["run-1"], SessionReplayer.Replay(store)["run-1"]);
    }

    [Fact]
    public void Restore_on_an_empty_store_starts_an_empty_session()
    {
        var recorder = SessionRecorder.Restore(new RecordingStore(), new RecordingSink());

        Assert.Empty(recorder.Projections);
    }
}
