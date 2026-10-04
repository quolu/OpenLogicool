using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.IO;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Input;
using OpenLogicool.Persistence;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostMacroAutomationIntentsTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"openlogicool-macro-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        File.Delete(path);
        File.Delete($"{path}.{MacroTargetSettingsStore.FileName}");
    }

    [Fact]
    public async Task Manual_create_and_saved_play_use_the_same_execution_engine()
    {
        SeedRoute();
        var engine = new RecordingEngine();
        using var intents = new HostMacroAutomationIntents(
            path, engine, () => [new MacroTargetOption("game", "Game")]);
        _ = intents.SelectTarget("game");

        _ = await intents.CreateAsync(
            new MacroCreateRequest("game", "新しい目的"), new Progress<MacroRunSnapshot>());
        var macro = Assert.Single(intents.ListMacros());
        _ = await intents.PlayAsync(
            new MacroPlaybackRequest("game", new MacroVersionReference(
                macro.RouteId, macro.VersionId, MacroPlaybackMode.AiFree)),
            new Progress<MacroRunSnapshot>());

        Assert.Null(engine.Requests[0].InitialRoute);
        Assert.Equal(MacroPlaybackMode.AiMonitored, engine.Requests[0].PlaybackMode);
        Assert.Equal(macro.VersionId, engine.Requests[1].InitialRoute!.VersionId);
        Assert.Equal(MacroPlaybackMode.AiFree, engine.Requests[1].PlaybackMode);
    }

    [Fact]
    public async Task Playback_range_is_validated_before_the_engine_and_forwarded_exactly()
    {
        SeedRoute();
        var engine = new RecordingEngine();
        using var intents = new HostMacroAutomationIntents(path, engine, () => [new MacroTargetOption("game", "Game")]);
        _ = intents.SelectTarget("game");
        var macro = Assert.Single(intents.ListMacros());
        var request = new MacroPlaybackRequest("game", new(macro.RouteId, macro.VersionId, MacroPlaybackMode.AiFree), 0, 1);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => intents.PlayAsync(request with { EndStepIndexExclusive = 0 }, new Progress<MacroRunSnapshot>()));
        Assert.Empty(engine.Requests);
        _ = await intents.PlayAsync(request, new Progress<MacroRunSnapshot>());
        Assert.Equal(1, Assert.Single(engine.Requests).EndStepIndexExclusive);
    }

    [Fact]
    public async Task Selected_game_profile_persists_and_rejects_request_side_target_override()
    {
        var available = new Func<IReadOnlyList<MacroTargetOption>>(() =>
            [new("game", "Game"), new("other", "Other")]);
        using (var first = new HostMacroAutomationIntents(path, new RecordingEngine(), available))
        {
            Assert.Null(first.CurrentTarget());
            Assert.Equal("game", first.SelectTarget("game").ProcessName);
        }

        var engine = new RecordingEngine();
        using var reopened = new HostMacroAutomationIntents(path, engine, available);
        Assert.Equal("game", reopened.CurrentTarget()!.ProcessName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.CreateAsync(
            new MacroCreateRequest("other", "別gameへ逸脱"), new Progress<MacroRunSnapshot>()));
        Assert.Empty(engine.Requests);
    }

    [Fact]
    public void Foundry_windows_adapter_reads_ready_endpoint_and_loaded_multimodal_model()
    {
        var runtime = WindowsFoundryLocalRuntimeResolver.Parse(
            """{"service":{"ready":true,"webUrls":["http://127.0.0.1:64020"]}}""",
            """{"models":[{"id":"vision:2","type":"Multimodal"}]}""");

        Assert.Equal(new Uri("http://127.0.0.1:64020"), runtime.Endpoint);
        Assert.Equal("vision:2", runtime.ModelId);
    }

    [Fact]
    public void Physical_queue_worker_runs_macros_in_fifo_order()
    {
        using var queue = new MacroInvocationQueue(4);
        var runner = new RecordingRunner();
        using var worker = new MacroAutomationWorker(queue, runner);
        worker.Start();
        var first = new MacroVersionReference("route:1", null, MacroPlaybackMode.AiFree);
        var second = new MacroVersionReference("route:2", "v2", MacroPlaybackMode.AiMonitored);

        Assert.Equal(MacroInvocationEnqueueResult.Accepted, queue.TryEnqueue(first));
        Assert.Equal(MacroInvocationEnqueueResult.Accepted, queue.TryEnqueue(second));

        Assert.True(SpinWait.SpinUntil(() => runner.Invocations.Count == 2, TimeSpan.FromSeconds(2)));
        Assert.Equal([first, second], runner.Invocations.ToArray());
        Assert.Null(worker.LastFailure);
    }

    [Fact]
    public async Task Dispose_cancels_and_waits_for_an_active_macro_before_returning()
    {
        var engine = new BlockingEngine();
        var intents = new HostMacroAutomationIntents(path, engine, () => [new MacroTargetOption("game", "Game")]);
        _ = intents.SelectTarget("game");
        var running = intents.CreateAsync(
            new MacroCreateRequest("game", "停止確認"), new Progress<MacroRunSnapshot>());
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        intents.Dispose();
        var terminal = await running;

        Assert.Equal(MacroRunPhase.Stopped, terminal.Phase);
        Assert.True(engine.Cancelled);
    }

    [Fact]
    public async Task Execution_failure_is_published_as_a_faulted_state_for_ui_and_physical_runs()
    {
        using var intents = new HostMacroAutomationIntents(
            path, new ThrowingEngine(), () => [new MacroTargetOption("game", "Game")]);
        _ = intents.SelectTarget("game");
        var states = new List<MacroRunSnapshot>();
        intents.StateChanged += states.Add;

        await Assert.ThrowsAsync<InvalidOperationException>(() => intents.CreateAsync(
            new MacroCreateRequest("game", "失敗確認"), new Progress<MacroRunSnapshot>()));

        Assert.Equal(MacroRunPhase.Faulted, states[^1].Phase);
        Assert.Contains("nano disconnected", states[^1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shared_recording_gate_refuses_playback_while_a_demonstration_is_recording()
    {
        var gate = new OpenLogicool.Playbooks.DemonstrationRecordingGate();
        Assert.True(gate.TryBeginRecording(out _));
        using var intents = new HostMacroAutomationIntents(
            path, new RecordingEngine(), () => [new MacroTargetOption("game", "Game")], gate);
        _ = intents.SelectTarget("game");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => intents.CreateAsync(
            new MacroCreateRequest("game", "録画中に再生"), new Progress<MacroRunSnapshot>()));

        Assert.Contains("記録中", exception.Message, StringComparison.Ordinal);
        gate.EndRecording();
        var terminal = await intents.CreateAsync(
            new MacroCreateRequest("game", "録画終了後は再生できる"), new Progress<MacroRunSnapshot>());
        Assert.Equal(MacroRunPhase.Completed, terminal.Phase);
    }

    [Fact]
    public async Task Macro_sqlite_ports_open_a_fresh_connection_on_each_thread()
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString()))
        {
            connection.Open();
            new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        }
        var routes = new MacroLearningRouteStore(new MacroSqliteConnectionFactory(path));

        var saved = await Task.Run(() => routes.Append(new LearningRouteDraft(
            ContractSchemaVersions.Revision03, "route:thread", null, "game", "env", "structure",
            "thread", ["edge"], LearningRouteAuthor.Ai, null, "test",
            LearningRouteStatus.Compiled, DateTimeOffset.UnixEpoch)));
        var restored = await Task.Run(() => routes.LoadLatest("route:thread"));

        Assert.NotNull(restored);
        Assert.Equal(saved.VersionId, restored.VersionId);
        Assert.Equal(saved.EdgeIds, restored.EdgeIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ui_and_physical_recorded_playback_publish_the_same_pending_confirmation(bool physical)
    {
        SeedRoute(recorded: true);
        var engine = new ConfirmingEngine();
        using var intents = new HostMacroAutomationIntents(path, engine, () => [new MacroTargetOption("game", "Game")]);
        intents.SelectTarget("game");
        var macro = Assert.Single(intents.ListMacros()); Assert.True(macro.RequiresStepConfirmation);
        var published = new TaskCompletionSource<MacroRunSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        intents.StateChanged += state => { if (state.Phase == MacroRunPhase.AwaitingConfirmation) published.TrySetResult(state); };
        var reference = new MacroVersionReference(macro.RouteId, macro.VersionId, MacroPlaybackMode.AiMonitored);
        var running = physical ? intents.RunQueuedAsync(reference, new Progress<MacroRunSnapshot>(), CancellationToken.None)
            : intents.PlayAsync(new("game", reference), new Progress<MacroRunSnapshot>());
        var pending = await published.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(running.IsCompleted); Assert.False(pending.CanStart);
        Assert.Equal(pending.PendingConfirmation, intents.CurrentRun().PendingConfirmation);
        intents.ConfirmStep(pending.PendingConfirmation!.ConfirmationId, MacroStepDecision.Accept);
        Assert.Equal(MacroRunPhase.Completed, (await running).Phase);
        Assert.Equal(MacroStepDecision.Accept, engine.Decision);
        Assert.Throws<InvalidOperationException>(() => intents.ConfirmStep(pending.PendingConfirmation.ConfirmationId, MacroStepDecision.Accept));
    }

    [Fact]
    public async Task Host_stop_cancels_a_pending_confirmation_and_clears_it()
    {
        SeedRoute(recorded: true);
        using var intents = new HostMacroAutomationIntents(path, new ConfirmingEngine(), () => [new MacroTargetOption("game", "Game")]);
        intents.SelectTarget("game"); var macro = Assert.Single(intents.ListMacros());
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        intents.StateChanged += state => { if (state.Phase == MacroRunPhase.AwaitingConfirmation) published.TrySetResult(); };
        var running = intents.PlayAsync(new("game", new(macro.RouteId, macro.VersionId, MacroPlaybackMode.AiFree)), new Progress<MacroRunSnapshot>());
        await published.Task.WaitAsync(TimeSpan.FromSeconds(2));
        intents.Stop(); Assert.Equal(MacroRunPhase.Stopped, (await running).Phase);
        Assert.Null(intents.CurrentRun().PendingConfirmation);
    }

    private sealed class ConfirmingEngine : IProductMacroExecutionEngine
    {
        public MacroStepDecision? Decision { get; private set; }
        public async Task<MacroRunSnapshot> ExecuteAsync(ProductMacroExecutionRequest request,
            IProgress<MacroRunSnapshot> progress, CancellationToken cancellationToken)
        {
            Decision = await request.StepConfirmation!.RequestAsync(new("confirmation:one", 1, "クリック", "Moved", null, null, "違いあり"), cancellationToken);
            return Snapshot(request.Goal);
        }
    }

    private void SeedRoute(bool recorded = false)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        _ = new SqliteLearningRouteStore(connection).Append(new LearningRouteDraft(
            ContractSchemaVersions.Revision03,
            "route:daily",
            null,
            "game",
            "game:live:1920x1080",
            "structure:1",
            "日課",
            ["edge:1"],
            LearningRouteAuthor.Ai,
            null,
            "seed",
            LearningRouteStatus.Compiled,
            DateTimeOffset.UnixEpoch,
            recorded ? [new DemonstrationRouteStep("demo", "op1", "click", [0.2, 0.3], null, null, null,
                null, "edge:1", OpenLogicool.Contracts.Exploration.GameTransitionJudgement.Moved, "記録済み")] : null));
    }

    private sealed class RecordingEngine : IProductMacroExecutionEngine
    {
        public List<ProductMacroExecutionRequest> Requests { get; } = [];
        public Task<MacroRunSnapshot> ExecuteAsync(ProductMacroExecutionRequest request, IProgress<MacroRunSnapshot> progress, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Snapshot(request.Goal));
        }
    }

    private sealed class RecordingRunner : IMacroInvocationRunner
    {
        public ConcurrentQueue<MacroVersionReference> Invocations { get; } = new();
        public Task<MacroRunSnapshot> RunQueuedAsync(MacroVersionReference invocation, IProgress<MacroRunSnapshot> progress, CancellationToken cancellationToken)
        {
            Invocations.Enqueue(invocation);
            return Task.FromResult(Snapshot(invocation.RouteId));
        }
    }

    private sealed class BlockingEngine : IProductMacroExecutionEngine
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }

        public async Task<MacroRunSnapshot> ExecuteAsync(
            ProductMacroExecutionRequest request,
            IProgress<MacroRunSnapshot> progress,
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled = true;
                throw;
            }
            throw new InvalidOperationException("blocking engineがcancelなしで終了しました。");
        }
    }

    private sealed class ThrowingEngine : IProductMacroExecutionEngine
    {
        public Task<MacroRunSnapshot> ExecuteAsync(
            ProductMacroExecutionRequest request,
            IProgress<MacroRunSnapshot> progress,
            CancellationToken cancellationToken) =>
            Task.FromException<MacroRunSnapshot>(new InvalidOperationException("nano disconnected"));
    }

    private static MacroRunSnapshot Snapshot(string goal) => new(
        MacroRunPhase.Completed, goal, "game", 1, "保存済み", "click", "Moved", 0, 1, "完了", true, false);
}
