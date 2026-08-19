using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Fakes;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.GameLab.Tests;

public sealed class GameLabRunConsoleTests
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

    private static RunEvent Event(long sequence, string payloadType, string runId = "run-1", string? attemptId = "attempt-1") =>
        new(
            "0.1.0",
            $"event-{runId}-{sequence}",
            runId,
            sequence,
            "playbook-1",
            "playbook-version-1",
            null,
            payloadType == RunEventPayloadTypes.Dispatch ? "command-1" : null,
            attemptId,
            "cause-1",
            $"correlation-{sequence}",
            1,
            RunEventActorType.Automation,
            new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 0, 0, 1, TimeSpan.Zero),
            payloadType is "observation" or "confirmation" ? "observation-1" : null,
            payloadType,
            "{}");

    // ---- UX-003: 9状態の常時表示 ----

    [Fact]
    public void Projector_reaches_every_status_of_ux003()
    {
        Assert.Equal(GameLabRunStatus.AwaitingProposal,
            GameLabStatusProjector.Project(new(false, false, false, null, null, null)));
        Assert.Equal(GameLabRunStatus.AwaitingApproval,
            GameLabStatusProjector.Project(new(false, false, false, AttemptState.Proposed, null, null)));
        Assert.Equal(GameLabRunStatus.Dispatching,
            GameLabStatusProjector.Project(new(false, false, false, AttemptState.DispatchArmed, null, null)));
        Assert.Equal(GameLabRunStatus.ConfirmingResult,
            GameLabStatusProjector.Project(new(false, false, false, AttemptState.Observing, ObservationStatus.Known, null)));
        Assert.Equal(GameLabRunStatus.UserStopped,
            GameLabStatusProjector.Project(new(true, false, false, AttemptState.Observing, null, null)));
        Assert.Equal(GameLabRunStatus.TargetMismatch,
            GameLabStatusProjector.Project(new(false, false, true, null, ObservationStatus.Known, null)));
        Assert.Equal(GameLabRunStatus.Unrecognized,
            GameLabStatusProjector.Project(new(false, false, false, AttemptState.Observing, ObservationStatus.Unknown, null)));
        Assert.Equal(GameLabRunStatus.Completed,
            GameLabStatusProjector.Project(new(false, false, false, null, ObservationStatus.Known, GameLabRunOutcome.Completed)));
        Assert.Equal(GameLabRunStatus.Failed,
            GameLabStatusProjector.Project(new(false, false, false, null, ObservationStatus.Known, GameLabRunOutcome.Failed)));
    }

    [Fact]
    public void Projector_is_total_over_every_input_combination()
    {
        // 常時表示（UX-003）: どの組合せでも例外なく必ず1状態が返る。
        var attempts = Enum.GetValues<AttemptState>().Cast<AttemptState?>().Append(null);
        var observations = Enum.GetValues<ObservationStatus>().Cast<ObservationStatus?>().Append(null);
        var outcomes = new GameLabRunOutcome?[] { null, GameLabRunOutcome.Completed, GameLabRunOutcome.Failed };
        var flags = new[] { false, true };

        var combinations = 0;
        foreach (var paused in flags)
        foreach (var stopped in flags)
        foreach (var mismatch in flags)
        foreach (var attempt in attempts)
        foreach (var observation in observations)
        foreach (var outcome in outcomes)
        {
            var status = GameLabStatusProjector.Project(
                new GameLabStatusInput(paused, stopped, mismatch, attempt, observation, outcome));
            Assert.True(Enum.IsDefined(status));
            combinations++;
        }

        Assert.Equal(2 * 2 * 2 * 17 * 5 * 3, combinations);
    }

    [Fact]
    public void User_stop_outranks_every_other_status()
    {
        var stopped = new GameLabStatusInput(
            Paused: false, EmergencyStopped: true, TargetMismatch: true,
            AttemptState.Observing, ObservationStatus.Unavailable, GameLabRunOutcome.Failed);

        Assert.Equal(GameLabRunStatus.UserStopped, GameLabStatusProjector.Project(stopped));
    }

    // ---- UX-004: pause / emergency stop は AI・capture・対象 device に依存しない ----

    [Fact]
    public void Gamelab_references_no_ai_capture_or_device_modules()
    {
        var referenced = typeof(GameLabRunConsole).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .Where(name => name.StartsWith("OpenLogicool", StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(referenced, name =>
            name.Contains("AI") || name.Contains("Capture") || name.Contains("Devices") || name.Contains("Input"));
    }

    [Fact]
    public void Pause_and_resume_gate_dispatch_immediately()
    {
        var console = new GameLabRunConsole();
        Assert.True(console.CanDispatch);

        console.Pause();
        Assert.Equal(GameLabRunStatus.UserStopped, console.CurrentStatus);
        Assert.False(console.CanDispatch);

        console.Resume();
        Assert.True(console.CanDispatch);
    }

    [Fact]
    public void Emergency_stop_is_immediate_and_has_no_release()
    {
        var console = new GameLabRunConsole();
        console.EmergencyStop();

        Assert.Equal(GameLabRunStatus.UserStopped, console.CurrentStatus);
        Assert.False(console.CanDispatch);
        Assert.Throws<InvalidOperationException>(console.Resume);
    }

    [Fact]
    public void A_finished_run_rejects_a_second_outcome_and_further_dispatch()
    {
        var console = new GameLabRunConsole();
        console.ReportOutcome(GameLabRunOutcome.Completed);

        Assert.Equal(GameLabRunStatus.Completed, console.CurrentStatus);
        Assert.False(console.CanDispatch);
        Assert.Throws<InvalidOperationException>(() => console.ReportOutcome(GameLabRunOutcome.Failed));
    }

    // ---- 現在 state の根拠は oracle / fake Observation だけ ----

    [Fact]
    public void Console_status_follows_the_oracle_and_fake_observations_only()
    {
        var console = new GameLabRunConsole();
        var source = new FakeObservationSource(
        [
            FakeObservations.Known("observation-1", "state.main-menu"),
            FakeObservations.Unknown("observation-2"),
        ]);
        var frame = new OpenLogicool.Contracts.Capture.CapturedFrame(
            "0.1.0", "fake-source", OpenLogicool.Contracts.Capture.CaptureBackend.WindowsGraphicsCapture,
            1, 1.0, new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            1920, 1080, "BGRA8", 96.0, 96.0, 1, 16, 0);

        console.ReportAttempt(AttemptState.Observing);
        console.ReportObservation(source.Observe(frame).Status);
        Assert.Equal(GameLabRunStatus.ConfirmingResult, console.CurrentStatus);

        console.ReportObservation(source.Observe(frame).Status);
        Assert.Equal(GameLabRunStatus.Unrecognized, console.CurrentStatus);

        console.ReportTargetMatch(matches: false);
        Assert.Equal(GameLabRunStatus.TargetMismatch, console.CurrentStatus);
    }

    // ---- APP-010: 実行履歴と Playbook の編集・閲覧 ----

    [Fact]
    public void Run_history_view_summarizes_journal_events_without_payload_bodies()
    {
        var store = new RecordingStore();
        var journal = new RunJournal(store, new NullEngineeringLog());
        journal.Append(Event(1, RunEventPayloadTypes.Proposal));
        journal.Append(Event(2, RunEventPayloadTypes.Dispatch));
        journal.Append(Event(1, RunEventPayloadTypes.Proposal, runId: "run-2", attemptId: null));

        Assert.Equal(["run-1", "run-2"], RunHistoryView.ListRuns(store));
        var history = RunHistoryView.Summarize(store, "run-1");
        Assert.Equal(2, history.Count);
        Assert.Equal([1L, 2L], history.Select(entry => entry.RunSequence));
        Assert.Equal([RunEventPayloadTypes.Proposal, RunEventPayloadTypes.Dispatch], history.Select(entry => entry.PayloadType));
        Assert.All(history, entry => Assert.Equal("attempt-1", entry.AttemptId));

        var bodyCarriers = typeof(RunHistoryEntry).GetProperties()
            .Where(property => property.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase)
                && property.Name != "PayloadType");
        Assert.Empty(bodyCarriers);
    }

    [Fact]
    public void Playbook_editing_from_gamelab_creates_a_new_version_and_keeps_the_old_one()
    {
        var nodes = new[]
        {
            new PlaybookNode("0.1.0", "node-1", IsEntry: true, "state.main-menu", [], "action.open-rewards", []),
        };
        var current = new PlaybookVersion("0.1.0", "ver-1", null, nodes, [], "初版");

        var revised = PlaybookCorrection.Revise(
            current, "ver-2", nodes, [], "GameLab からの訂正");

        // PB-008: 編集は新 version の作成であり、旧 version は変更されない。
        Assert.Equal("ver-2", revised.VersionId);
        Assert.Equal("ver-1", revised.ParentVersionId);
        Assert.Equal("ver-1", current.VersionId);
        Assert.Equal("初版", current.ChangeReason);
    }

    private sealed class NullEngineeringLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry)
        {
        }
    }
}
