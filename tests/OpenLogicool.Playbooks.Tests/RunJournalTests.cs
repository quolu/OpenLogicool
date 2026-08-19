using System.Reflection;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class RunJournalTests
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
        string? attemptId = null,
        string? commandId = null,
        string? observationId = null) =>
        new(
            "0.1.0",
            $"event-{sequence}",
            "run-1",
            sequence,
            "playbook-1",
            "playbook-version-1",
            null,
            commandId,
            attemptId,
            "cause-1",
            $"correlation-{sequence}",
            1,
            RunEventActorType.Automation,
            new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 0, 0, 1, TimeSpan.Zero),
            observationId,
            payloadType,
            """{"secret":"payload-body"}""");

    [Fact]
    public void Append_accepts_every_payload_type_of_the_closed_set()
    {
        var store = new RecordingStore();
        var journal = new RunJournal(store, new RecordingSink());

        long sequence = 0;
        foreach (var payloadType in RunEventPayloadTypes.All)
        {
            journal.Append(Event(
                ++sequence,
                payloadType,
                attemptId: "attempt-1",
                commandId: "command-1",
                observationId: "observation-1"));
        }

        Assert.Equal(RunEventPayloadTypes.All.Count, store.Events.Count);
        Assert.Equal(RunEventPayloadTypes.All, store.Events.Select(e => e.PayloadType));
    }

    [Fact]
    public void Append_rejects_an_unknown_payload_type_without_writing()
    {
        var store = new RecordingStore();
        var sink = new RecordingSink();
        var journal = new RunJournal(store, sink);

        Assert.Throws<ArgumentException>(() => journal.Append(Event(1, "telemetry")));
        Assert.Empty(store.Events);
        Assert.Empty(sink.Entries);
    }

    [Theory]
    [InlineData(RunEventPayloadTypes.Observation, null, null, null)]
    [InlineData(RunEventPayloadTypes.Confirmation, "attempt-1", null, null)]
    [InlineData(RunEventPayloadTypes.Confirmation, null, null, "observation-1")]
    [InlineData(RunEventPayloadTypes.Dispatch, "attempt-1", null, null)]
    [InlineData(RunEventPayloadTypes.Dispatch, null, "command-1", null)]
    [InlineData(RunEventPayloadTypes.DispatchResult, null, null, null)]
    public void Append_rejects_events_missing_their_required_ids(
        string payloadType, string? attemptId, string? commandId, string? observationId)
    {
        var store = new RecordingStore();
        var journal = new RunJournal(store, new RecordingSink());

        Assert.Throws<ArgumentException>(() => journal.Append(Event(1, payloadType, attemptId, commandId, observationId)));
        Assert.Empty(store.Events);
    }

    [Fact]
    public void Append_rejects_a_sequence_gap_without_writing()
    {
        var store = new RecordingStore();
        var sink = new RecordingSink();
        var journal = new RunJournal(store, sink);
        journal.Append(Event(1, RunEventPayloadTypes.Proposal));

        Assert.Throws<InvalidOperationException>(() => journal.Append(Event(3, RunEventPayloadTypes.Proposal)));
        Assert.Single(store.Events);
        Assert.Single(sink.Entries);
    }

    [Fact]
    public void Append_records_correlation_to_the_engineering_log_without_payload_body()
    {
        var sink = new RecordingSink();
        var journal = new RunJournal(new RecordingStore(), sink);

        journal.Append(Event(1, RunEventPayloadTypes.Observation, observationId: "observation-1"));

        var entry = Assert.Single(sink.Entries);
        Assert.Equal("correlation-1", entry.CorrelationId);
        Assert.Equal("event-1", entry.EventId);
        Assert.Equal("run-1", entry.RunId);
        Assert.Equal(1, entry.RunSequence);
        Assert.Equal(RunEventPayloadTypes.Observation, entry.PayloadType);

        // OPS-009: engineering log の行は相関情報だけを持つ。payload 本文の通り道が型として存在しない。
        var bodyCarriers = typeof(EngineeringLogEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase)
                && property.Name != "PayloadType");
        Assert.Empty(bodyCarriers);
    }

    [Fact]
    public void Correlation_id_links_the_journal_event_and_the_engineering_log_line()
    {
        var store = new RecordingStore();
        var sink = new RecordingSink();
        var journal = new RunJournal(store, sink);
        journal.Append(Event(1, RunEventPayloadTypes.Dispatch, attemptId: "attempt-1", commandId: "command-1"));
        journal.Append(Event(2, RunEventPayloadTypes.DispatchResult, attemptId: "attempt-1"));

        foreach (var journalEvent in store.Events)
        {
            var logLine = Assert.Single(sink.Entries, entry => entry.CorrelationId == journalEvent.CorrelationId);
            Assert.Equal(journalEvent.EventId, logLine.EventId);
            Assert.Equal(journalEvent.RunSequence, logLine.RunSequence);
        }
    }

    [Fact]
    public void Restore_resumes_appending_from_the_persisted_journal()
    {
        var store = new RecordingStore();
        var journal = new RunJournal(store, new RecordingSink());
        journal.Append(Event(1, RunEventPayloadTypes.Proposal));
        journal.Append(Event(2, RunEventPayloadTypes.Approval));

        // OPS-008: 再起動相当——新しい RunJournal を store の実 event だけから復元する。
        var restored = RunJournal.Restore(store, new RecordingSink());

        Assert.Throws<InvalidOperationException>(() => restored.Append(Event(2, RunEventPayloadTypes.Proposal)));
        Assert.Throws<InvalidOperationException>(() => restored.Append(Event(4, RunEventPayloadTypes.Proposal)));
        restored.Append(Event(3, RunEventPayloadTypes.Correction));
        Assert.Equal(3, store.Events.Count);
    }

    [Fact]
    public void Run_journal_has_no_mutation_api_other_than_append()
    {
        var mutators = typeof(RunJournal)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(RunJournal))
            .Where(method => method.Name is not ("Append" or "Restore"))
            .Where(method => !method.IsSpecialName);
        Assert.Empty(mutators);
    }
}
