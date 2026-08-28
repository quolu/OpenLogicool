using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Input;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

/// <summary>ChatGPT subscriptionのCodex App Serverを目的統括に使い、入力と学習は既存10基盤へ限定する。</summary>
public sealed class CodexPurposeMacroExecutionEngine(
    string databasePath,
    SerialHidDiscoveryService serialHidDiscovery,
    string? selectedNanoDeviceInstanceId,
    IGameAgentWorkspaceManager workspaceManager,
    IProductMacroExecutionEngine aiFreeEngine,
    Func<SerialHidResidentOutputSession?>? borrowedNanoSession = null,
    Func<string, ICodexAppServerTransport>? transportFactory = null) : IProductMacroExecutionEngine
{
    private readonly string databasePath = Path.GetFullPath(databasePath);
    private readonly Func<SerialHidResidentOutputSession?> borrowedNanoSession = borrowedNanoSession ?? (() => null);
    private readonly Func<string, ICodexAppServerTransport> transportFactory =
        transportFactory ?? WindowsCodexAppServerTransport.Start;

    public async Task<MacroRunSnapshot> ExecuteAsync(
        ProductMacroExecutionRequest request,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PlaybackMode == MacroPlaybackMode.AiFree)
            return await aiFreeEngine.ExecuteAsync(request, progress, cancellationToken).ConfigureAwait(false);

        var target = WindowsGameTargetLocator.Locate(request.TargetProcessName);
        if (request.InitialRoute is not null
            && !string.Equals(request.InitialRoute.GameId, target.ProcessName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("macro routeと対象windowのgameが一致しません。");
        string environment;
        using (var connection = Open())
            environment = request.InitialRoute?.EnvironmentScope ?? ResolveEnvironment(connection, target);

        var workspace = workspaceManager.Ensure(target.ProcessName);
        var runId = $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}";
        var runDirectory = Path.Combine(workspace.ResolvedPath, "runs", runId);
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "goal.json"), JsonSerializer.Serialize(new
        {
            SchemaVersion = "1.0.0",
            Goal = request.Goal,
            TargetProfileId = workspace.Reference.ProfileId,
        }, new JsonSerializerOptions { WriteIndented = true }));

        var borrowed = borrowedNanoSession();
        SerialHidResidentOutputSession? owned = null;
        var nano = borrowed;
        if (nano is null)
        {
            owned = serialHidDiscovery.Resolve(
                selectedNanoDeviceInstanceId,
                SerialHidProtocolV1.AllCapabilities).Session;
            owned.Start();
            owned.Protocol.SendAllUp();
            nano = owned;
        }
        try
        {
            var emitter = nano.Emitter as SerialHidEmitter
                ?? throw new InvalidOperationException("Nano sessionがSerialHidEmitterを返しませんでした。");
            _ = WindowsTaskbarNanoWindowActivator.EnsureForeground(target, nano.Protocol, emitter);
            var connections = new MacroSqliteConnectionFactory(databasePath);
            var structures = new MacroGameStructureStore(connections);
            var routes = new MacroLearningRouteStore(connections);
            var profiles = new MacroLearnedSceneProfileStore(connections);
            var journal = RunJournal.Restore(new MacroRunJournalStore(connections), new MacroEngineeringLog());
            var policy = new ExplorationPolicy(
                ContractSchemaVersions.Revision03,
                $"codex-policy:{Guid.NewGuid():N}",
                target.ProcessName,
                $"window:codex:{target.ProcessId}",
                environment,
                "visible-current-page",
                GameInteractionOperations.InputOperations,
                [],
                new ExplorationBudget(ContractSchemaVersions.Revision03, 200, 3_600_000, 3_600_000),
                "user-requested-codex-goal",
                "saved-route-or-current-page",
                new ExplorationStopPolicy(ContractSchemaVersions.Revision03, 1_000),
                ["capture-unavailable", "budget-exhausted"]);
            var targetDiscovery = new CodexSuppliedTargetDiscovery(
                new WindowsGameOcrRecognizer(),
                profiles,
                target.ProcessName,
                environment);
            using var product = WindowsProductGameExplorerComposition.Create(
                target.ProcessName,
                target.Window,
                structures,
                journal,
                new AttemptDispatchGate(journal),
                policy,
                new GamePolicyRecord(
                    ContractSchemaVersions.Revision02,
                    target.ProcessName,
                    GamePolicyReviewStatus.Confirmed,
                    [GameAutomationMode.Observe, GameAutomationMode.Assist, GameAutomationMode.Explore]),
                new ZeroSeedFrameStateRecognizer(),
                Path.Combine(runDirectory, "evidence"),
                new Uri("http://127.0.0.1:1"),
                "codex-app-server-no-foundry",
                nano.Protocol,
                emitter,
                () => target.Bounds,
                profiles,
                request.Goal,
                includeVisualTargets: false,
                interactionWaitCondition: new ExplorationWaitCondition(
                    ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
                allowAiDiscovery: false,
                learnNonMovedRouteOutcomes: true,
                targetDiscoveryOverride: targetDiscovery,
                comparisonNormalizer: new WindowsInformationScrollComparisonNormalizer());
            var recorder = new CodexLearningRouteRecorder(
                target.ProcessName,
                environment,
                request.Goal,
                structures,
                routes);
            var dynamicTools = new CodexGameDynamicTools(
                new CodexProductGameToolRuntime(product.Runtime),
                recorder);
            var session = workspaceManager.LoadSession(workspace);
            var codex = new CodexAppServerClient(transportFactory, dynamicTools);
            var result = await codex.RunAsync(
                workspace,
                session,
                request.Goal,
                BuildDeveloperInstructions(target.ProcessName, request.Goal),
                cancellationToken).ConfigureAwait(false);
            workspaceManager.SaveSession(workspace, result.ThreadId);
            var completed = result.Status == "completed"
                && dynamicTools.IsCompleted
                && dynamicTools.IsReplayableCompletion;
            var detail = completed
                ? dynamicTools.FinalSummary
                : dynamicTools.IsCompleted && !dynamicTools.IsReplayableCompletion
                    ? "Codexはgoal完了を報告しましたが、実行actionをrouteへcommitできなかったため停止しました。"
                : string.IsNullOrWhiteSpace(result.FinalText)
                    ? $"Codex turnは{result.Status}で終了し、finishされませんでした。"
                    : result.FinalText;
            File.WriteAllText(Path.Combine(runDirectory, "result.json"), JsonSerializer.Serialize(new
            {
                SchemaVersion = "1.0.0",
                result.ThreadId,
                result.TurnId,
                result.Status,
                result.ToolCallCount,
                Completed = completed,
                Summary = detail,
                Facts = dynamicTools.FinalFacts,
                RouteRevision = recorder.RevisionNumber,
                dynamicTools.ActionCallCount,
                dynamicTools.ToolErrors,
            }, new JsonSerializerOptions { WriteIndented = true }));
            return new MacroRunSnapshot(
                completed ? MacroRunPhase.Completed : MacroRunPhase.Stopped,
                request.Goal,
                target.ProcessName,
                recorder.StepNumber,
                "Codex",
                "OpenLogicool dynamic tools",
                completed ? "Completed" : result.Status,
                1,
                recorder.RevisionNumber,
                detail,
                CanStart: true,
                CanStop: false,
                Information: dynamicTools.FinalFacts);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static string ResolveEnvironment(SqliteConnection connection, WindowsGameTarget target)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT environment_scope, MAX(event_sequence) FROM structure_events WHERE game_id = $game GROUP BY environment_scope ORDER BY MAX(event_sequence) DESC;";
        command.Parameters.AddWithValue("$game", target.ProcessName);
        using var reader = command.ExecuteReader();
        var resolution = $"{target.Bounds.Width}x{target.Bounds.Height}";
        string? first = null;
        while (reader.Read())
        {
            var scope = reader.GetString(0);
            first ??= scope;
            if (scope.Contains(resolution, StringComparison.OrdinalIgnoreCase)) return scope;
        }
        return first ?? $"{target.ProcessName}:live:{resolution}";
    }

    private static string BuildDeveloperInstructions(string processName, string goal) => $"""
        The OpenLogicool application fixed the game profile to `{processName}`.
        The user's only goal is: {goal}
        Use the OpenLogicool dynamic tools until the goal is complete. After every action call observe again.
        If the goal is not complete and observe reports SavedAction, call use_saved_action before any new action.
        If the goal is already complete on the current page, call finish immediately even when SavedAction remains. Do not execute a saved tail after all requested facts are collected.
        Never claim success without calling finish with all collected facts.
        """;

    private sealed class MacroEngineeringLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry) { }
    }
}
