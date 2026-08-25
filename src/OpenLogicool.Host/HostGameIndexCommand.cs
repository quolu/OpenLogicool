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
            "learn-operation" => await LearnOperationAsync(
                arguments,
                profiles,
                nano,
                emitter,
                target,
                sourceId,
                environment,
                allowExplore),
            "back" => await BackAsync(nano, emitter, target, sourceId),
            "point" => await PointAsync(arguments, nano, emitter, target, sourceId),
            "inspect" => Inspect(profiles, target.ProcessName, environment),
            _ => throw new ArgumentException("game-index modeはdiscover、execute、learn-operation、back、point、inspectです。"),
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
        var goal = Required(arguments, "--goal");
        var includeVisualTargets = arguments.Contains("--include-visual-targets", StringComparer.Ordinal);
        var operation = Optional(arguments, "--operation") ?? GameInteractionOperations.Click;
        if (!GameInteractionOperations.InputOperations.Contains(operation, StringComparer.Ordinal))
        {
            throw new ArgumentException("game-index discoverの--operationが未対応です。");
        }
        var keyTokens = operation == GameInteractionOperations.KeyTap
            ? Required(arguments, "--keys").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : null;
        var verticalScrollSteps = operation == GameInteractionOperations.Scroll
            ? RequiredInt(arguments, "--vertical-steps")
            : (int?)null;
        var horizontalScrollSteps = operation == GameInteractionOperations.Scroll
            ? OptionalInt(arguments, "--horizontal-steps", 0)
            : (int?)null;
        var dragDestination = operation == GameInteractionOperations.Drag
            ? new[] { RequiredUnitDouble(arguments, "--destination-x"), RequiredUnitDouble(arguments, "--destination-y") }
            : null;
        var known = await TryExecuteKnownForGoalAsync(
            profiles,
            nano,
            emitter,
            target,
            sourceId,
            environment,
            goal,
            operation,
            allowExplore);
        if (known.Execution is
            {
                DestinationMatched: true,
                Comparison.Judgement: GameTransitionJudgement.Moved,
            } execution)
        {
            return new
            {
                Mode = "known-before-discover",
                ProductHostEntry = true,
                Goal = goal,
                Operation = operation,
                RediscoveryStarted = false,
                execution.ActionId,
                execution.SourceStateId,
                execution.ExpectedDestinationStateId,
                execution.ObservedDestinationStateId,
                execution.DestinationMatched,
                execution.Dispatch,
                execution.Comparison,
                execution.AiCallCount,
                Database = connection.DataSource,
            };
        }
        var endpoint = new Uri(Required(arguments, "--foundry-endpoint"));
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
            goal,
            includeVisualTargets,
            operation,
            keyTokens,
            verticalScrollSteps,
            horizontalScrollSteps,
            dragDestination);
        var explorerIntents = new HostExplorerIntents(connection, product.Runtime);
        var step = await product.Runtime.ExecuteNextAsync();
        var indexed = profiles.Load(target.ProcessName, environment);
        return new
        {
            Mode = "discover",
            ProductHostEntry = true,
            step.Status,
            step.Detail,
            step.Dispatch,
            step.Comparison,
            Target = step.Target?.SemanticLabel,
            TargetBounds = step.Target?.Locator.NormalizedBounds,
            VisionStatus = step.Before?.DiscoveryEvidence?.Status,
            VisionFailure = step.Before?.DiscoveryEvidence?.Failure,
            VisionFailureDetail = step.Before?.DiscoveryEvidence?.FailureDetail,
            VisionRawResponse = step.Before?.DiscoveryEvidence?.RawResponse,
            Goal = goal,
            Operation = operation,
            IncludeVisualTargets = includeVisualTargets,
            RediscoveryStarted = true,
            RediscoveryReason = known.Reason,
            ActionId = indexed?.States.SelectMany(state => state.Affordances).LastOrDefault()?.CandidateId,
            DestinationStateId = indexed?.States.SelectMany(state => state.Affordances).LastOrDefault()?.DestinationStateId,
            IndexedStateCount = indexed?.States.Count ?? 0,
            IndexedActionCount = indexed?.States.Sum(state => state.Affordances.Count) ?? 0,
            ExplorerRuntimeConnected = explorerIntents.Load(target.ProcessName, environment).CanPause,
            product.AiCallCount,
            Database = connection.DataSource,
        };
    }

    private static async Task<(KnownScreenActionExecutionResult? Execution, string Reason)> TryExecuteKnownForGoalAsync(
        ILearnedSceneProfileStore profiles,
        SerialHidResidentOutputSession nano,
        SerialHidEmitter emitter,
        WindowsGameTarget target,
        string sourceId,
        string environment,
        string goal,
        string operation,
        bool allowExplore)
    {
        var profile = profiles.Load(target.ProcessName, environment);
        if (profile is null)
        {
            return (null, "現在ページ用の保存済みボタンデータがありません。");
        }
        using var frames = new WindowsWgcGameFrameSource(target.Window, sourceId, TimeSpan.FromSeconds(10));
        var observation = new WindowsKnownScreenObservationRuntime(
            frames,
            new WindowsGameOcrRecognizer(),
            profiles,
            target.ProcessName,
            environment);
        var observed = await observation.ObserveAsync();
        var scene = await observation.DiscoverTargetsAsync(observed);
        if (scene.StateIdentity != StateIdentityStatus.Known || scene.StateHypothesisId is null)
        {
            return (null, "現在ページを保存済みボタンデータへ一意に照合できません。");
        }
        var state = profile.States.Single(item => item.StateId == scene.StateHypothesisId);
        var selection = KnownGoalActionSelector.Select(state, goal, operation);
        if (selection.Kind != KnownGoalActionSelectionKind.UseKnown)
        {
            return (null, selection.Reason);
        }
        var actions = new NanoGameInteractionActions(
            new SerialHidNanoGameInputDevice(nano.Protocol, emitter, new WindowsSerialHidCursorOracle()),
            new WindowsGameInteractionCoordinateMapper(() => target.Bounds));
        var runtime = new KnownScreenActionRuntime(
            observation,
            actions,
            new GameInteractionStabilityRuntime(observation, new SystemGameInteractionClock(), TimeSpan.FromMilliseconds(100)),
            new GameTransitionJudge(),
            profiles,
            target.ProcessName,
            environment,
            new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
            new WindowsGameExplorationCandidateRiskPolicy(DeterministicExplorationCandidateRiskPolicy.SafeMenuDefault),
            gamePolicyAllowsExecute: allowExplore);
        var execution = await runtime.ExecuteKnownAsync(selection.Action!.CandidateId);
        return execution.DestinationMatched
            && execution.Comparison.Judgement == GameTransitionJudgement.Moved
            ? (execution, "保存済みボタンで正常に遷移しました。")
            : (execution, "保存済みボタンで正常な画面遷移を確認できませんでした。");
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

    private static async Task<object> LearnOperationAsync(
        string[] arguments,
        SqliteLearnedSceneProfileStore profiles,
        SerialHidResidentOutputSession nano,
        SerialHidEmitter emitter,
        WindowsGameTarget target,
        string sourceId,
        string environment,
        bool allowExplore)
    {
        var sourceActionId = Required(arguments, "--source-action");
        var operation = Required(arguments, "--operation");
        if (operation is not (GameInteractionOperations.Hover
            or GameInteractionOperations.KeyTap
            or GameInteractionOperations.Scroll
            or GameInteractionOperations.Drag))
        {
            throw new ArgumentException("learn-operationはhover、key-tap、scroll、dragを受理します。");
        }
        var original = profiles.Load(target.ProcessName, environment)
            ?? throw new InvalidOperationException("既知ページ索引がありません。");
        var sourceState = original.States.SingleOrDefault(state =>
                state.Affordances.Any(action => action.CandidateId == sourceActionId))
            ?? throw new InvalidOperationException("派生元actionが索引にありません。");
        var sourceAction = sourceState.Affordances.Single(action => action.CandidateId == sourceActionId);
        if (sourceAction.VisualPatch is null)
        {
            throw new InvalidOperationException("Hover実受理の判定には派生元actionのvisual patchが必要です。");
        }
        var actionId = IncrementalKnownScreenIndex.CreateActionId(sourceState.StateId, sourceAction.Text, operation);
        var evidenceId = $"known-operation:{Guid.NewGuid():N}";
        var destinationStateId = Optional(arguments, "--destination-state") ?? sourceState.StateId;
        if (original.States.All(state => state.StateId != destinationStateId))
        {
            throw new InvalidOperationException("指定destination stateが既知索引にありません。");
        }
        var keyTokens = operation == GameInteractionOperations.KeyTap
            ? Required(arguments, "--keys").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : null;
        var verticalSteps = operation == GameInteractionOperations.Scroll
            ? RequiredInt(arguments, "--vertical-steps")
            : (int?)null;
        var horizontalSteps = operation == GameInteractionOperations.Scroll
            ? OptionalInt(arguments, "--horizontal-steps", 0)
            : (int?)null;
        var dragDestination = operation == GameInteractionOperations.Drag
            ? new[] { RequiredUnitDouble(arguments, "--destination-x"), RequiredUnitDouble(arguments, "--destination-y") }
            : null;
        var derived = sourceAction with
        {
            CandidateId = actionId,
            AllowedPrimitives = [operation],
            EvidenceIds = sourceAction.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
            DestinationStateId = destinationStateId,
            KeyTokens = keyTokens,
            VerticalScrollSteps = verticalSteps,
            HorizontalScrollSteps = horizontalSteps,
            DragDestinationNormalized = dragDestination,
        };
        var candidateProfile = original with
        {
            States = original.States
                .Where(state => state.StateId != sourceState.StateId)
                .Append(sourceState with
                {
                    Affordances = sourceState.Affordances
                        .Where(action => action.CandidateId != actionId)
                        .Append(derived)
                        .ToArray(),
                    EvidenceIds = sourceState.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
                })
                .ToArray(),
            EvidenceIds = original.EvidenceIds.Append(evidenceId).Distinct(StringComparer.Ordinal).ToArray(),
        };
        var overlay = new SingleProfileStore(candidateProfile);
        using var frames = new WindowsWgcGameFrameSource(target.Window, sourceId, TimeSpan.FromSeconds(10));
        var observation = new WindowsKnownScreenObservationRuntime(
            frames,
            new WindowsGameOcrRecognizer(),
            overlay,
            target.ProcessName,
            environment);
        var actions = new NanoGameInteractionActions(
            new SerialHidNanoGameInputDevice(nano.Protocol, emitter, new WindowsSerialHidCursorOracle()),
            new WindowsGameInteractionCoordinateMapper(() => target.Bounds));
        var wait = new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 300, 10_000);
        var runtime = new KnownScreenActionRuntime(
            observation,
            actions,
            new GameInteractionStabilityRuntime(observation, new SystemGameInteractionClock(), TimeSpan.FromMilliseconds(100)),
            new GameTransitionJudge(),
            overlay,
            target.ProcessName,
            environment,
            wait,
            new WindowsGameExplorationCandidateRiskPolicy(DeterministicExplorationCandidateRiskPolicy.SafeMenuDefault),
            gamePolicyAllowsExecute: allowExplore);
        var execution = await runtime.ExecuteKnownAsync(actionId);
        if (!execution.DestinationMatched)
        {
            throw new InvalidOperationException($"{operation}後の遷移を確認できなかったため索引へ保存しません。");
        }
        profiles.Upsert(candidateProfile);
        return new
        {
            Mode = "learn-operation",
            ProductHostEntry = true,
            SourceActionId = sourceActionId,
            Operation = operation,
            ActionId = actionId,
            Saved = true,
            execution.Dispatch,
            execution.Comparison,
            execution.AiCallCount,
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

    private static async Task<object> PointAsync(
        string[] arguments,
        SerialHidResidentOutputSession nano,
        SerialHidEmitter emitter,
        WindowsGameTarget target,
        string sourceId)
    {
        var x = RequiredUnitDouble(arguments, "--x");
        var y = RequiredUnitDouble(arguments, "--y");
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
            "host-point-no-ai",
            frame.FreshnessMs,
            null);
        var actions = new NanoGameInteractionActions(
            new SerialHidNanoGameInputDevice(nano.Protocol, emitter, new WindowsSerialHidCursorOracle()),
            new WindowsGameInteractionCoordinateMapper(() => target.Bounds));
        var binding = new GameInteractionTargetBinding(
            ContractSchemaVersions.Revision03,
            observation.ObservationId,
            observation.Frame.Sequence,
            observation.Frame.TransformRevision,
            observation.Frame.SourceId,
            "host-point",
            "host-point-v1",
            [Math.Clamp(x - 0.0005, 0, 0.999), Math.Clamp(y - 0.0005, 0, 0.999), 0.001, 0.001]);
        var dispatch = actions.Hover(binding, observation);
        return new { Mode = "point", ProductHostEntry = true, X = x, Y = y, dispatch, AiCallCount = 0 };
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

    private static double RequiredUnitDouble(string[] arguments, string name)
    {
        var raw = Required(arguments, name);
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
            || value is < 0 or > 1)
        {
            throw new ArgumentException($"{name} は0から1の数値で指定します。");
        }
        return value;
    }

    private static int RequiredInt(string[] arguments, string name)
    {
        var raw = Required(arguments, name);
        return int.TryParse(raw, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"{name} は整数で指定します。");
    }

    private static int OptionalInt(string[] arguments, string name, int fallback)
    {
        var raw = Optional(arguments, name);
        return raw is null ? fallback : RequiredInt(arguments, name);
    }

    private sealed class NullLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry) { }
    }

    private sealed class SingleProfileStore(LearnedSceneProfileDocument profile) : ILearnedSceneProfileStore
    {
        public void Upsert(LearnedSceneProfileDocument document) => throw new NotSupportedException();
        public LearnedSceneProfileDocument? Load(string gameId, string environmentScope) => profile;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
