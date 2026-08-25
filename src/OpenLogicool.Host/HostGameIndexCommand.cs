using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Input;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

public static class HostGameIndexCommand
{
    public static int Run(string mode, string[] arguments)
    {
        try
        {
            return RunAsync(mode, arguments).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string mode, string[] arguments)
    {
        var processName = Required(arguments, "--process");
        var databasePath = Path.GetFullPath(Required(arguments, "--db"));
        var deviceId = Optional(arguments, "--device-id");
        var allowExplore = arguments.Contains("--allow-explore", StringComparer.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        var profiles = new SqliteLearnedSceneProfileStore(connection);
        var target = WindowsGameTargetLocator.Locate(processName);
        WindowsGameWindowActivator.Activate(target.Window);
        var discovery = new SerialHidDiscoveryService(
            new SetupApiSerialCandidateEnumerator(),
            new SerialPortExchangeFactory());
        using var nano = discovery.Resolve(deviceId, SerialHidProtocolV1.AllCapabilities).Session;
        nano.Start();
        nano.Protocol.SendAllUp();
        var emitter = nano.Emitter as SerialHidEmitter
            ?? throw new InvalidOperationException("Serial HID emitterを取得できません。");
        var sourceId = $"window:game-index:{target.ProcessId}";
        var environment = $"{processName}:known-index:{target.Bounds.Width}x{target.Bounds.Height}";

        object result = mode switch
        {
            "discover" => await DiscoverAsync(
                arguments,
                connection,
                profiles,
                nano,
                emitter,
                target,
                sourceId,
                environment,
                allowExplore),
            "execute" => await ExecuteAsync(
                arguments,
                profiles,
                nano,
                emitter,
                target,
                sourceId,
                environment,
                allowExplore),
            "back" => await BackAsync(nano, emitter, target, sourceId),
            "inspect" => Inspect(profiles, target.ProcessName, environment),
            _ => throw new ArgumentException("game-index modeはdiscover、execute、back、inspectです。"),
        };
        nano.Protocol.SendAllUp();
        var json = JsonSerializer.Serialize(result, Json);
        var outputPath = Optional(arguments, "--out");
        if (outputPath is not null)
        {
            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, json);
        }
        Console.WriteLine(json);
        return 0;
    }

    private static async Task<object> DiscoverAsync(
        string[] arguments,
        SqliteConnection connection,
        SqliteLearnedSceneProfileStore profiles,
        SerialHidResidentOutputSession nano,
        SerialHidEmitter emitter,
        WindowsGameTarget target,
        string sourceId,
        string environment,
        bool allowExplore)
    {
        var endpoint = new Uri(Required(arguments, "--foundry-endpoint"));
        var goal = Required(arguments, "--goal");
        var model = Optional(arguments, "--model") ?? "qwen3-vl-4b-instruct-cuda-gpu:2";
        var frameDirectory = Path.GetFullPath(Optional(arguments, "--frames")
            ?? Path.Combine("probe-output", $"host-game-index-{DateTime.Now:yyyyMMdd-HHmmss-fff}"));
        var structureStore = new SqliteGameStructureStore(connection);
        var runJournal = new RunJournal(new SqliteRunJournalStore(connection), new NullLog());
        var attemptGate = new AttemptDispatchGate(runJournal);
        var policy = Policy(target.ProcessName, sourceId, environment);
        using var product = WindowsProductGameExplorerComposition.Create(
            target.ProcessName,
            target.Window,
            structureStore,
            runJournal,
            attemptGate,
            policy,
            new GamePolicyRecord(
                ContractSchemaVersions.Revision02,
                target.ProcessName,
                allowExplore ? GamePolicyReviewStatus.Confirmed : GamePolicyReviewStatus.Unverified,
                allowExplore
                    ? [GameAutomationMode.Observe, GameAutomationMode.Assist, GameAutomationMode.Explore]
                    : [GameAutomationMode.Observe]),
            new ZeroSeedFrameStateRecognizer(),
            frameDirectory,
            endpoint,
            model,
            nano.Protocol,
            emitter,
            () => target.Bounds,
            profiles,
            goal);
        var explorerIntents = new HostExplorerIntents(connection, product.Runtime);
        var step = await product.Runtime.ExecuteNextAsync();
        var indexed = profiles.Load(target.ProcessName, environment);
        return new
        {
            Mode = "discover",
            ProductHostEntry = true,
            step.Status,
            Target = step.Target?.SemanticLabel,
            Goal = goal,
            ActionId = indexed?.States.SelectMany(state => state.Affordances).LastOrDefault()?.CandidateId,
            DestinationStateId = indexed?.States.SelectMany(state => state.Affordances).LastOrDefault()?.DestinationStateId,
            IndexedStateCount = indexed?.States.Count ?? 0,
            IndexedActionCount = indexed?.States.Sum(state => state.Affordances.Count) ?? 0,
            ExplorerRuntimeConnected = explorerIntents.Load(target.ProcessName, environment).CanPause,
            AiCallCount = 1,
            Database = connection.DataSource,
        };
    }

    private static async Task<object> ExecuteAsync(
        string[] arguments,
        SqliteLearnedSceneProfileStore profiles,
        SerialHidResidentOutputSession nano,
        SerialHidEmitter emitter,
        WindowsGameTarget target,
        string sourceId,
        string environment,
        bool allowExplore)
    {
        var actionId = Required(arguments, "--action");
        using var frames = new WindowsWgcGameFrameSource(target.Window, sourceId, TimeSpan.FromSeconds(10));
        var observation = new WindowsKnownScreenObservationRuntime(
            frames,
            new WindowsGameOcrRecognizer(),
            profiles,
            target.ProcessName,
            environment);
        var actions = new NanoGameInteractionActions(
            new SerialHidNanoGameInputDevice(nano.Protocol, emitter, new WindowsSerialHidCursorOracle()),
            new WindowsGameInteractionCoordinateMapper(() => target.Bounds));
        var stability = new GameInteractionStabilityRuntime(
            observation,
            new SystemGameInteractionClock(),
            TimeSpan.FromMilliseconds(100));
        var runtime = new KnownScreenActionRuntime(
            observation,
            actions,
            stability,
            new GameTransitionJudge(),
            profiles,
            target.ProcessName,
            environment,
            new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
            new WindowsGameExplorationCandidateRiskPolicy(
                DeterministicExplorationCandidateRiskPolicy.SafeMenuDefault),
            gamePolicyAllowsExecute: allowExplore);
        var execution = await runtime.ExecuteKnownAsync(actionId);
        return new
        {
            Mode = "execute",
            ProductHostEntry = true,
            execution.ActionId,
            execution.SourceStateId,
            execution.ExpectedDestinationStateId,
            execution.ObservedDestinationStateId,
            execution.DestinationMatched,
            execution.AiCallCount,
            execution.Dispatch,
            execution.Comparison,
        };
    }

    private static async Task<object> BackAsync(
        SerialHidResidentOutputSession nano,
        SerialHidEmitter emitter,
        WindowsGameTarget target,
        string sourceId)
    {
        using var frames = new WindowsWgcGameFrameSource(target.Window, sourceId, TimeSpan.FromSeconds(10));
        var frame = await frames.CaptureAsync();
        var observation = new ObservationResult(
            ContractSchemaVersions.Revision03,
            $"observation:{sourceId}:{frame.Sequence}",
            new CapturedFrameReference(
                ContractSchemaVersions.Revision03,
                frame.SourceId,
                frame.Backend,
                frame.Sequence,
                frame.MonotonicMs,
                frame.WallClockUtc,
                frame.TransformRevision,
                frame.FreshnessMs,
                frame.LastChangeMs),
            CaptureAvailability.Available,
            StateIdentityStatus.Novel,
            [],
            "host-back-no-ai",
            frame.FreshnessMs,
            null);
        var actions = new NanoGameInteractionActions(
            new SerialHidNanoGameInputDevice(nano.Protocol, emitter, new WindowsSerialHidCursorOracle()),
            new WindowsGameInteractionCoordinateMapper(() => target.Bounds));
        var dispatch = actions.KeyTap(
            new GameInteractionKeyTapRequest(
                ContractSchemaVersions.Revision03,
                observation.ObservationId,
                observation.Frame.Sequence,
                observation.Frame.TransformRevision,
                observation.Frame.SourceId,
                ["Key:Esc"]),
            observation);
        return new { Mode = "back", ProductHostEntry = true, dispatch, AiCallCount = 0 };
    }

    private static object Inspect(
        SqliteLearnedSceneProfileStore profiles,
        string gameId,
        string environment)
    {
        var profile = profiles.Load(gameId, environment)
            ?? throw new InvalidOperationException("既知ページ索引がありません。");
        return new
        {
            Mode = "inspect",
            ProductHostEntry = true,
            profile.ProfileId,
            profile.ProfileVersion,
            StateCount = profile.States.Count,
            ActionCount = profile.States.Sum(state => state.Affordances.Count),
            States = profile.States.Select(state => new
            {
                state.StateId,
                Anchors = state.Anchors.Select(anchor => new
                {
                    anchor.Text,
                    anchor.NormalizedBounds,
                }).ToArray(),
            }).ToArray(),
            Actions = profile.States.SelectMany(state => state.Affordances.Select(action => new
            {
                SourceStateId = state.StateId,
                ActionId = action.CandidateId,
                action.Text,
                action.NormalizedBounds,
                action.DestinationStateId,
            })).ToArray(),
            AiCallCount = 0,
        };
    }

    private static ExplorationPolicy Policy(string gameId, string sourceId, string environment) =>
        new(
            ContractSchemaVersions.Revision03,
            $"policy:{Guid.NewGuid():N}",
            gameId,
            sourceId,
            environment,
            "visible-safe-menu",
            GameInteractionOperations.InputOperations,
            ["purchase", "paid-resource", "rare-resource", "gacha", "combat", "activity-start", "delete", "account-change"],
            new ExplorationBudget(ContractSchemaVersions.Revision03, 20, 300_000, 300_000),
            true,
            "owner-delegated-known-index",
            "known-menu-or-escape",
            new ExplorationStopPolicy(ContractSchemaVersions.Revision03, 1_000, 1, 3, 3),
            ["capture-unavailable", "budget-exhausted", "no-candidate"]);

    private static string Required(string[] arguments, string name) =>
        Optional(arguments, name) ?? throw new ArgumentException($"{name} が必要です。");

    private static string? Optional(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }

    private sealed class NullLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry) { }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
