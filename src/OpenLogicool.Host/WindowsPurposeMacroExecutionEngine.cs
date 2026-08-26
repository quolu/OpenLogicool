using Microsoft.Data.Sqlite;
using System.IO;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Input;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

/// <summary>Windows WGC／Foundry Local／Nanoを10基盤へ配線する製品macro実行体。</summary>
public sealed class WindowsPurposeMacroExecutionEngine(
    string databasePath,
    SerialHidDiscoveryService serialHidDiscovery,
    string? selectedNanoDeviceInstanceId,
    Func<SerialHidResidentOutputSession?>? borrowedNanoSession = null,
    WindowsFoundryLocalRuntimeResolver? foundry = null) : IProductMacroExecutionEngine
{
    private readonly string databasePath = Path.GetFullPath(databasePath);
    private readonly SerialHidDiscoveryService serialHidDiscovery = serialHidDiscovery;
    private readonly string? selectedNanoDeviceInstanceId = selectedNanoDeviceInstanceId;
    private readonly Func<SerialHidResidentOutputSession?> borrowedNanoSession = borrowedNanoSession ?? (() => null);
    private readonly WindowsFoundryLocalRuntimeResolver foundry = foundry ?? new();

    public async Task<MacroRunSnapshot> ExecuteAsync(
        ProductMacroExecutionRequest request,
        IProgress<MacroRunSnapshot> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = WindowsGameTargetLocator.Locate(request.TargetProcessName);
        if (request.InitialRoute is not null
            && !string.Equals(request.InitialRoute.GameId, target.ProcessName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("macro routeと対象windowのgameが一致しません。");

        string environment;
        using (var connection = Open())
        {
            environment = request.InitialRoute?.EnvironmentScope ?? ResolveEnvironment(connection, target);
        }
        var connections = new MacroSqliteConnectionFactory(databasePath);
        var unusedFoundry = new FoundryLocalRuntime(new Uri("http://127.0.0.1:1"), "lazy-not-resolved");
        using var lazyFoundry = new WindowsLazyFoundryControlDiscoveryProvider(foundry.ResolvePreferredVisionModel);
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
            var structures = new MacroGameStructureStore(connections);
            var routes = new MacroLearningRouteStore(connections);
            var profiles = new MacroLearnedSceneProfileStore(connections);
            var journal = RunJournal.Restore(new MacroRunJournalStore(connections), new MacroEngineeringLog());
            var frameDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenLogicool", "macro-evidence", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            var policy = new ExplorationPolicy(
                ContractSchemaVersions.Revision03,
                $"macro-policy:{Guid.NewGuid():N}",
                target.ProcessName,
                $"window:macro:{target.ProcessId}",
                environment,
                "visible-current-page",
                GameInteractionOperations.InputOperations,
                [],
                new ExplorationBudget(ContractSchemaVersions.Revision03, 100, 1_800_000, 1_800_000),
                "user-requested-macro",
                "saved-route-or-current-page",
                new ExplorationStopPolicy(ContractSchemaVersions.Revision03, 1_000),
                ["capture-unavailable", "budget-exhausted", "no-candidate"]);
            var gamePolicy = new GamePolicyRecord(
                ContractSchemaVersions.Revision02,
                target.ProcessName,
                GamePolicyReviewStatus.Confirmed,
                [GameAutomationMode.Observe, GameAutomationMode.Assist, GameAutomationMode.Explore]);
            using var product = WindowsProductGameExplorerComposition.Create(
                target.ProcessName,
                target.Window,
                structures,
                journal,
                new AttemptDispatchGate(journal),
                policy,
                gamePolicy,
                new ZeroSeedFrameStateRecognizer(),
                frameDirectory,
                unusedFoundry.Endpoint,
                unusedFoundry.ModelId,
                nano.Protocol,
                emitter,
                () => target.Bounds,
                profiles,
                targetIntent: request.Goal,
                includeVisualTargets: true,
                interactionWaitCondition: new ExplorationWaitCondition(
                    ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
                allowAiDiscovery: request.PlaybackMode != MacroPlaybackMode.AiFree,
                learnNonMovedRouteOutcomes: request.PlaybackMode != MacroPlaybackMode.AiFree,
                controlDiscoveryProvider: lazyFoundry,
                controlDiscoveryResource: null);
            var purpose = new PurposeDirectedExplorationRuntime(
                target.ProcessName,
                environment,
                request.Goal,
                product.Runtime,
                structures,
                routes,
                new SemanticTextGoalCompletionEvaluator(),
                playbackMode: request.PlaybackMode,
                initialRoute: request.InitialRoute);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var aiBefore = product.AiCallCount;
                var step = await purpose.ExecuteNextAsync(cancellationToken).ConfigureAwait(false);
                var aiUsed = product.AiCallCount > aiBefore;
                var terminal = step.Status is PurposeDirectedStepStatus.Completed or PurposeDirectedStepStatus.Stopped;
                var phase = step.Status switch
                {
                    PurposeDirectedStepStatus.Completed => MacroRunPhase.Completed,
                    PurposeDirectedStepStatus.Stopped => MacroRunPhase.Stopped,
                    PurposeDirectedStepStatus.LearningContinues => MacroRunPhase.Repairing,
                    _ => MacroRunPhase.Executing,
                };
                var snapshot = new MacroRunSnapshot(
                    phase,
                    request.Goal,
                    target.ProcessName,
                    step.StepIndex,
                    aiUsed ? "AI修復" : step.UsedSavedRoute ? "保存済み" : "AI探索",
                    step.Step.Target?.SemanticLabel ?? step.Step.Detail,
                    step.Step.Comparison?.Judgement.ToString() ?? "未判定",
                    product.AiCallCount,
                    step.Route?.RevisionNumber ?? 0,
                    step.Detail,
                    terminal,
                    !terminal);
                progress.Report(snapshot);
                if (terminal) return snapshot;
            }
        }
        finally
        {
            if (owned is not null)
            {
                owned.Dispose();
            }
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

    private sealed class MacroEngineeringLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry) { }
    }
}
