using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Desktop;
using OpenLogicool.Host;
using OpenLogicool.Input;
using OpenLogicool.Persistence;

namespace OpenLogicool.Probe;

internal static class SupervisedMacroLiveSmoke
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static int Run(string[] arguments, string outputDirectory)
    {
        try
        {
            var databasePath = Path.GetFullPath(RequiredArgument(arguments, "--db"));
            var gameId = RequiredArgument(arguments, "--game");
            var environmentScope = RequiredArgument(arguments, "--environment");
            var routeId = RequiredArgument(arguments, "--route");
            var routeVersionId = RequiredArgument(arguments, "--version");
            var selectedDeviceInstanceId = OptionalArgument(arguments, "--device-instance-id");
            var observeOnly = arguments.Contains("--observe-only", StringComparer.Ordinal);
            var focusWithNano = arguments.Contains("--focus-with-nano", StringComparer.Ordinal);
            Directory.CreateDirectory(outputDirectory);

            var discovery = new SerialHidDiscoveryService(
                new SetupApiSerialCandidateEnumerator(),
                new SerialPortExchangeFactory());
            var candidates = discovery.ListCandidates();
            if (selectedDeviceInstanceId is null)
            {
                if (candidates.Count != 1)
                {
                    throw new InvalidOperationException(
                        $"Nano候補は{candidates.Count}台です。1台だけ接続するか--device-instance-idで選んでください。");
                }
                selectedDeviceInstanceId = candidates[0].DeviceInstanceId;
            }
            var selected = candidates.SingleOrDefault(candidate => string.Equals(
                candidate.DeviceInstanceId, selectedDeviceInstanceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("指定したNano device instanceが現在の候補にありません。");

            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
            var focusAltTabCount = 0;
            if (focusWithNano)
            {
                var profile = new SqliteLearnedSceneProfileStore(connection).Load(gameId, environmentScope)
                    ?? throw new InvalidOperationException("Nano focus対象のscene profileがありません。");
                var target = LiveDiscoveryObserveSmoke.FindWindow(profile.ProcessName);
                using var focusSession = discovery.Resolve(
                    selected.DeviceInstanceId,
                    SerialHidProtocolV1.AllCapabilities).Session;
                focusSession.Start();
                var focusEmitter = focusSession.Emitter as SerialHidEmitter
                    ?? throw new InvalidOperationException("Nano focus sessionがSerial HID emitterではありません。");
                focusSession.Protocol.SendAllUp();
                try
                {
                    focusAltTabCount = LiveDiscoveryNanoActionSmoke.FocusTargetWithNano(
                        target.Window,
                        focusEmitter);
                }
                finally
                {
                    focusSession.Protocol.SendAllUp();
                }
            }
            var ocrSnapshots = new List<OpenLogicool.Perception.OcrFrameSnapshot>();
            var observedScenes = new List<OpenLogicool.Contracts.Perception.ObservedScene>();
            var transitions = new List<SupervisedMacroTransitionObservation>();
            using var runtime = new ProductSupervisedMacroRuntime(
                new SqliteLearnedSceneProfileStore(connection),
                new WindowsSupervisedWindowLocator(),
                new WindowsOcrFrameReader(ocrSnapshots.Add),
                new SerialHidSupervisedNanoSessionFactory(discovery, selected.DeviceInstanceId),
                sceneObserver: observedScenes.Add,
                transitionObserver: transitions.Add);
            var intents = new HostSupervisedMacroIntents(
                connection,
                runtime,
                new NullEngineeringLog(),
                authorizationSource: SupervisedMacroAuthorizationSource.OwnerDelegatedAutomation);
            var snapshots = observeOnly
                ? new[] { intents.Start(gameId, environmentScope, routeId, routeVersionId) }
                : RunToTerminal(intents, gameId, environmentScope, routeId, routeVersionId);
            var terminal = snapshots[^1];
            var events = new SqliteRunJournalStore(connection).ReadRun(terminal.RunId);
            var dispatches = events.Where(item => item.PayloadType == RunEventPayloadTypes.Dispatch).ToArray();
            var dispatchReports = events.Where(item => item.PayloadType == RunEventPayloadTypes.DispatchResult).ToArray();
            var confirmations = events.Where(item => item.PayloadType == RunEventPayloadTypes.Confirmation).ToArray();
            var authorizations = events.Where(item => item.PayloadType == RunEventPayloadTypes.Authorization).ToArray();
            var passed = observeOnly
                ? terminal.State == SupervisedMacroRunState.ReadyToDispatch
                    && dispatches.Length == 0
                    && authorizations.Length == 0
                : terminal.State == SupervisedMacroRunState.Completed
                    && terminal.History.Count == terminal.TotalSteps
                    && dispatches.Length == terminal.TotalSteps
                    && dispatchReports.Length == terminal.TotalSteps
                    && confirmations.Length == terminal.TotalSteps
                    && authorizations.Length == terminal.TotalSteps
                    && authorizations.All(item => item.ActorType == RunEventActorType.Automation)
                    && authorizations.All(item => item.PayloadJson.Contains("owner-delegated-automation", StringComparison.Ordinal));
            var evidence = new
            {
                SchemaVersion = "1.0.0",
                Probe = "supervised-macro-live",
                Mode = observeOnly ? "ObserveOnly" : "OwnerDelegatedAutomation",
                CapturedAtUtc = DateTimeOffset.UtcNow,
                DatabasePath = databasePath,
                GameId = gameId,
                EnvironmentScope = environmentScope,
                RouteId = routeId,
                RouteVersionId = routeVersionId,
                Nano = new
                {
                    selected.DeviceInstanceId,
                    selected.PortName,
                    Route = "NanoSerialHid",
                    Fallback = "None",
                },
                Authorization = new
                {
                    Source = SupervisedMacroAuthorizationSource.OwnerDelegatedAutomation,
                    Actors = authorizations.Select(item => item.ActorType).ToArray(),
                },
                Snapshots = snapshots,
                OcrSnapshots = ocrSnapshots,
                ObservedScenes = observedScenes,
                Transitions = transitions.Select(item => new
                {
                    StabilityStatus = item.Stability.Status,
                    ObservationCount = item.Stability.Observations.Count,
                    item.Stability.StableFramesObserved,
                    item.Stability.StableMillisecondsObserved,
                    item.Stability.ElapsedMilliseconds,
                    item.Stability.FailureReason,
                    Judgement = item.Comparison.Judgement,
                    item.Comparison.Reasons,
                    FinalStateId = item.FinalScene?.StateHypothesisId,
                    item.DestinationMatched,
                }).ToArray(),
                Journal = events.Select(item => new
                {
                    item.RunSequence,
                    item.PayloadType,
                    item.ActorType,
                    item.ObservationId,
                    item.AttemptId,
                }).ToArray(),
                Input = new
                {
                    FocusAltTabCount = focusAltTabCount,
                    DispatchArmedCount = dispatches.Length,
                    NanoDispatchReportedCount = dispatchReports.Length,
                    SendInputDispatchCount = 0,
                    ComputerUseDispatchCount = 0,
                    RetryCount = 0,
                    FallbackCount = 0,
                },
                Passed = passed,
            };
            var outputPath = Path.Combine(
                outputDirectory,
                $"supervised-macro-live-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            File.WriteAllText(outputPath, JsonSerializer.Serialize(evidence, Json));
            Console.WriteLine(outputPath);
            Console.WriteLine(passed ? "PASS" : $"FAIL: terminal={terminal.State}");
            return passed ? 0 : 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    internal static IReadOnlyList<SupervisedMacroRunSnapshot> RunToTerminal(
        ISupervisedMacroIntents intents,
        string gameId,
        string environmentScope,
        string routeId,
        string routeVersionId)
    {
        ArgumentNullException.ThrowIfNull(intents);
        var snapshots = new List<SupervisedMacroRunSnapshot>
        {
            intents.Start(gameId, environmentScope, routeId, routeVersionId),
        };
        var attemptedSteps = new HashSet<int>();
        while (snapshots[^1].State == SupervisedMacroRunState.ReadyToDispatch)
        {
            if (!attemptedSteps.Add(snapshots[^1].CurrentStepSequence))
            {
                throw new InvalidOperationException(
                    $"step {snapshots[^1].CurrentStepSequence} が再びReadyになりました。自動retryせず停止します。");
            }
            snapshots.Add(intents.Next());
        }
        return snapshots;
    }

    private static string RequiredArgument(string[] arguments, string name)
    {
        var value = OptionalArgument(arguments, name);
        return value ?? throw new ArgumentException($"{name} が必要です。");
    }

    private static string? OptionalArgument(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        return index < 0 || index == arguments.Length - 1 || string.IsNullOrWhiteSpace(arguments[index + 1])
            ? null
            : arguments[index + 1];
    }

    private sealed class NullEngineeringLog : IEngineeringLogSink
    {
        public void Record(EngineeringLogEntry entry) { }
    }
}
