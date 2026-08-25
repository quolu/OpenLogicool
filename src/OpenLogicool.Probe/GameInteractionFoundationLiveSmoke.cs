using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Host;
using OpenLogicool.Input;
using OpenLogicool.Persistence;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Probe;

internal static class GameInteractionFoundationLiveSmoke
{
    public static int Run(string[] arguments, string outputDirectory)
    {
        try
        {
            return RunAsync(arguments, Path.GetFullPath(outputDirectory)).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] arguments, string outputDirectory)
    {
        var operation = Required(arguments, "--operation");
        var port = Required(arguments, "--port");
        var processName = Required(arguments, "--process");
        var modelId = Optional(
            arguments,
            "--model",
            "qwen3-vl-4b-instruct-cuda-gpu:2");
        var requestedSteps = OptionalInt(arguments, "--steps", 3);
        var dbPath = Path.GetFullPath(Required(arguments, "--db"));
        var target = LiveDiscoveryObserveSmoke.FindWindow(processName);
        WindowsGameWindowActivator.Activate(target.Window);
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var frameDirectory = Path.Combine(
            outputDirectory,
            $"game-interaction-frames-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        var daemon = LiveDiscoveryObserveSmoke.ReadDaemonState();

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        var structureStore = new SqliteGameStructureStore(connection);
        var runStore = new SqliteRunJournalStore(connection);
        var runJournal = RunJournal.Restore(runStore, new NullEngineeringLog());
        var attemptGate = new AttemptDispatchGate(runJournal);

        var exchange = new ProbeSerialPortFrameExchange(port);
        using var resident = new SerialHidResidentOutputSession(
            exchange,
            new SerialHidSemanticVersion(1, 1, 0),
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromMilliseconds(50),
            SerialHidProtocolV1.AllCapabilities);
        resident.Start();
        resident.Protocol.SendAllUp();
        var emitter = resident.Emitter as SerialHidEmitter
            ?? throw new InvalidOperationException("Nano resident sessionがSerialHidEmitterを返しませんでした。");
        var sourceId = $"window:game-interaction:{target.ProcessId}";
        var captureWidth = target.CaptureRect.Right - target.CaptureRect.Left;
        var captureHeight = target.CaptureRect.Bottom - target.CaptureRect.Top;
        var environment = $"{processName}:live:{captureWidth}x{captureHeight}:dpi{target.Dpi:F0}";
        var policy = new ExplorationPolicy(
            ContractSchemaVersions.Revision03,
            $"policy:{Guid.NewGuid():N}",
            target.ProcessName,
            sourceId,
            environment,
            "visible-safe-menu",
            GameInteractionOperations.InputOperations,
            ["purchase", "paid-resource", "rare-resource", "gacha", "combat", "delete", "account-change"],
            new ExplorationBudget(ContractSchemaVersions.Revision03, 20, 300_000, 300_000),
            "owner-delegated-2026-08-25",
            "known-menu-or-escape",
            new ExplorationStopPolicy(ContractSchemaVersions.Revision03, 1_000),
            ["capture-unavailable", "budget-exhausted", "no-candidate"]);
        var gamePolicy = new GamePolicyRecord(
            ContractSchemaVersions.Revision02,
            processName,
            GamePolicyReviewStatus.Confirmed,
            [GameAutomationMode.Observe, GameAutomationMode.Assist, GameAutomationMode.Explore]);
        using var product = WindowsProductGameExplorerComposition.Create(
            processName,
            target.Window,
            structureStore,
            runJournal,
            attemptGate,
            policy,
            gamePolicy,
            new ZeroSeedFrameStateRecognizer(),
            frameDirectory,
            daemon.Endpoint,
            modelId,
            resident.Protocol,
            emitter,
            () => new GameCaptureScreenBounds(
                target.CaptureRect.Left,
                target.CaptureRect.Top,
                captureWidth,
                captureHeight),
            new SqliteLearnedSceneProfileStore(connection));
        var runtime = product.Runtime;
        object result;
        var passed = true;

        if (operation == "explore-run")
        {
            var steps = new List<ProductGameExplorerStepResult>();
            for (var index = 0; index < requestedSteps; index++)
            {
                var step = await runtime.ExecuteNextAsync();
                steps.Add(step);
                if (!IsDeterminateLearned(step))
                {
                    break;
                }
            }
            var targetKeys = steps
                .Where(step => step.Before is not null && step.Target is not null)
                .Select(step => GameSceneSemanticComparer.TargetKey(step.Target!))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            result = new
            {
                RequestedSteps = requestedSteps,
                CompletedSteps = steps.Count,
                DistinctTargetKeys = targetKeys,
                Steps = steps,
            };
            passed = steps.Count == requestedSteps
                && steps.All(IsDeterminateLearned)
                && targetKeys.Length >= 2
                && steps.Any(step => step.Comparison?.Judgement == GameTransitionJudgement.Moved);
        }
        else if (operation == "explore-step")
        {
            var step = await runtime.ExecuteNextAsync();
            result = step;
            passed = IsDeterminateLearned(step);
        }
        else
        {
            var beforeObservation = await runtime.ObserveAsync();
            if (operation == GameInteractionOperations.Observe)
            {
                result = new { Observation = beforeObservation };
                passed = beforeObservation.Frame.Artifact is not null
                    && File.Exists(beforeObservation.Frame.Artifact.LocalPath);
            }
            else
            {
                var before = await runtime.DiscoverTargetsAsync(beforeObservation);
                if (operation == GameInteractionOperations.DiscoverTargets)
                {
                    result = new { Observation = beforeObservation, Scene = before };
                    passed = before.Affordances.Count > 0;
                }
                else
                {
                    var targetCandidate = operation is GameInteractionOperations.Hover
                        or GameInteractionOperations.Click
                        or GameInteractionOperations.Scroll
                        or GameInteractionOperations.Drag
                            ? SelectSafe(before)
                            : null;
                    GameInteractionDispatchReceipt? dispatch = operation switch
                    {
                        GameInteractionOperations.Hover =>
                            await runtime.HoverAsync(GameInteractionTargetBinding.From(targetCandidate!)),
                        GameInteractionOperations.Click =>
                            await runtime.ClickAsync(GameInteractionTargetBinding.From(targetCandidate!)),
                        GameInteractionOperations.KeyTap =>
                            await runtime.KeyTapAsync(new GameInteractionKeyTapRequest(
                                ContractSchemaVersions.Revision03,
                                before.ObservationId,
                                before.Frame.Sequence,
                                before.Frame.TransformRevision,
                                before.Frame.SourceId,
                                ["Key:Esc"])),
                        GameInteractionOperations.Scroll =>
                            await runtime.ScrollAsync(new GameInteractionScrollRequest(
                                ContractSchemaVersions.Revision03,
                                GameInteractionTargetBinding.From(targetCandidate!),
                                1,
                                0)),
                        GameInteractionOperations.Drag =>
                            await runtime.DragAsync(new GameInteractionDragRequest(
                                ContractSchemaVersions.Revision03,
                                GameInteractionTargetBinding.From(targetCandidate!),
                                DragDestination(targetCandidate!))),
                        GameInteractionOperations.WaitStable or GameInteractionOperations.Compare => null,
                        _ => throw new ArgumentException($"unsupported live operation: {operation}"),
                    };
                    var wait = new ExplorationWaitCondition(
                        ContractSchemaVersions.Revision03,
                        2,
                        1_000,
                        60_000);
                    var stability = await runtime.WaitStableAsync(before, wait);
                    var comparison = runtime.Compare(before, stability);
                    result = new
                    {
                        Observation = beforeObservation,
                        Scene = before,
                        Target = targetCandidate,
                        Dispatch = dispatch,
                        Stability = stability,
                        StabilitySignatures = stability.Observations
                            .Select(GameSceneSemanticComparer.SignatureId)
                            .ToArray(),
                        Comparison = comparison,
                    };
                    passed = dispatch is null
                        ? stability.Status == GameInteractionStabilityStatus.Stable
                            && (operation == GameInteractionOperations.WaitStable
                                || comparison.Judgement != GameTransitionJudgement.Undetermined)
                        : dispatch.Status == GameInteractionDispatchStatus.Dispatched
                            && stability.Status == GameInteractionStabilityStatus.Stable
                            && comparison.Judgement != GameTransitionJudgement.Undetermined;
                }
            }
        }

        var evidence = new
        {
            SchemaVersion = "1.0.0",
            Probe = "game-interaction-foundation-live",
            Operation = operation,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Target = new
            {
                target.ProcessId,
                target.ProcessName,
                target.WindowTitle,
                target.CaptureRect,
                target.Dpi,
            },
            Database = dbPath,
            FrameDirectory = frameDirectory,
            VisionModel = modelId,
            Nano = new
            {
                Port = port,
                Firmware = resident.Protocol.ReadyInfo.FirmwareVersion.ToString(),
                Route = "NanoSerialHid",
                Fallback = "None",
                AllUp = true,
            },
            Input = new
            {
                SendInputDispatchCount = 0,
                ComputerUseDispatchCount = 0,
                FallbackCount = 0,
                AutomaticRetryCount = 0,
                ExternalAiTransmissionCount = 0,
                ExternalAiApiCostUsd = 0,
            },
            Result = result,
            Passed = passed,
        };
        var outputPath = Path.Combine(
            outputDirectory,
            $"game-interaction-foundation-{operation}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(evidence, JsonOptions));
        Console.WriteLine(outputPath);
        Console.WriteLine(passed ? "PASS" : "FAIL");
        return passed ? 0 : 3;
    }

    private static AffordanceCandidate SelectSafe(ObservedScene scene)
    {
        var policy = UnclassifiedExplorationCandidateRiskPolicy.Default;
        return scene.Affordances
            .Where(candidate => policy.Evaluate(candidate).Level != ExplorationRiskLevel.Prohibited)
            .OrderBy(candidate => candidate.Locator.NormalizedBounds[1])
            .ThenBy(candidate => candidate.Locator.NormalizedBounds[0])
            .FirstOrDefault()
            ?? throw new InvalidOperationException("安全なaffordance candidateがありません。");
    }

    private static IReadOnlyList<double> DragDestination(AffordanceCandidate candidate)
    {
        var bounds = candidate.Locator.NormalizedBounds;
        return
        [
            Math.Clamp(bounds[0] + bounds[2] / 2, 0.05, 0.95),
            Math.Clamp(bounds[1] + bounds[3] / 2 + 0.15, 0.05, 0.95),
        ];
    }

    private static string Required(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        if (index < 0 || index == arguments.Length - 1 || string.IsNullOrWhiteSpace(arguments[index + 1]))
        {
            throw new ArgumentException($"{name} が必要です。");
        }
        return arguments[index + 1];
    }

    private static string Optional(string[] arguments, string name, string defaultValue)
    {
        var index = Array.IndexOf(arguments, name);
        return index < 0 ? defaultValue : Required(arguments, name);
    }

    private static int OptionalInt(string[] arguments, string name, int defaultValue)
    {
        var value = Optional(arguments, name, defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"{name} は正の整数でなければなりません。");
    }

    private static bool IsDeterminateLearned(ProductGameExplorerStepResult step) =>
        step.Status == ProductGameExplorerStepStatus.Learned
        && step.Stability?.Status == GameInteractionStabilityStatus.Stable
        && step.Comparison?.Judgement != GameTransitionJudgement.Undetermined
        && step.Learning?.Evidence?.Outcome != ExplorationOutcomeKind.OutcomeUnknown;

    private sealed class NullEngineeringLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry) { }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
