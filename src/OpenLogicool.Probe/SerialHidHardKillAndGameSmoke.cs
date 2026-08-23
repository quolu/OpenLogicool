using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpenLogicool.Contracts.Devices.Shared;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;
using OpenLogicool.Persistence;

namespace OpenLogicool.Probe;

internal static class SerialHidHardKillAndGameSmoke
{
    private const int VkEscape = 0x1B;
    private const int VkF13 = 0x7C;

    public static int RunHardKill(string[] arguments, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var result = new HardKillSmokeResult
        {
            Schema = "openlogicool.serial-hid.hard-kill.v1",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
        };

        Process? child = null;
        try
        {
            var candidate = SelectCandidate(arguments);
            result.DeviceInstanceId = candidate.DeviceInstanceId;
            result.TransientPort = candidate.PortName;
            var databasePath = CreateDatabase(candidate, "Key:F13");
            result.TemporaryDatabasePath = databasePath;

            using var observer = HidObservationWindow.Start();
            observer.Clear();
            child = StartHostChild(databasePath, out var diagnosticsPath);
            result.ChildDiagnosticsPath = diagnosticsPath;
            WaitForReady(child, TimeSpan.FromSeconds(15));
            result.ChildProcessId = child.Id;

            var marker = observer.Count;
            Console.WriteLine("ACTION_REQUIRED:HOLD_G13_G1");
            Console.Out.Flush();
            observer.WaitForGroups(
                [[Event("down", VkF13)]],
                marker,
                () => child.HasExited ? new InvalidOperationException($"child exited: {child.ExitCode}") : null);

            var down = observer.Events.Skip(marker).First(item => IsKey(item, "down", VkF13));
            result.DownObservedBeforeKill = true;
            var killRequestedTicks = Stopwatch.GetTimestamp();
            child.Kill();
            child.WaitForExit();
            var killCompletedTicks = Stopwatch.GetTimestamp();
            result.ChildExitCode = child.ExitCode;

            var release = WaitForKeyUp(observer, VkF13, killRequestedTicks, TimeSpan.FromSeconds(3));
            if (release is null)
            {
                result.EmergencyAllUpAttempted = true;
                SendEmergencyAllUp(candidate);
                release = WaitForKeyUp(observer, VkF13, killRequestedTicks, TimeSpan.FromSeconds(2));
            }

            var analysis = HardKillReleaseAnalysis.Analyze(
                down.StopwatchTicks,
                killRequestedTicks,
                killCompletedTicks,
                release?.StopwatchTicks);
            result.ReleaseObserved = analysis.ReleaseObserved;
            result.ReleaseLatencyMillisecondsFromKillRequest = analysis.ReleaseLatencyMillisecondsFromKillRequest;
            result.ReleaseLatencyMillisecondsFromKillCompletion = analysis.ReleaseLatencyMillisecondsFromKillCompletion;
            result.Meets250MillisecondBudget = analysis.Meets250MillisecondBudget;
            result.Events = observer.Events.Skip(marker).Where(item => item.Kind == "key" && item.Code == VkF13).ToArray();
            result.Passed = result.DownObservedBeforeKill
                && result.ReleaseObserved
                && result.Meets250MillisecondBudget
                && !result.EmergencyAllUpAttempted;
            Console.WriteLine("ACTION_COMPLETE:RELEASE_G13_G1");
            Console.Out.Flush();
        }
        catch (Exception exception)
        {
            result.Error = $"{exception.GetType().Name}: {exception.Message}";
            TryKill(child);
        }

        return WriteResult(outputDirectory, "serial-hid-hard-kill", result, result.Passed);
    }

    public static int RunGameObservation(string[] arguments, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var result = new GameObservationResult
        {
            Schema = "openlogicool.serial-hid.game-observation.v2",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            TargetGame = "NIKKE",
            PhysicalControl = "G13:G1",
            OutputToken = "Key:Esc",
        };

        Process? child = null;
        try
        {
            var candidate = SelectCandidate(arguments);
            result.DeviceInstanceId = candidate.DeviceInstanceId;
            result.TransientPort = candidate.PortName;
            var databasePath = CreateDatabase(candidate, "Key:Esc");
            result.TemporaryDatabasePath = databasePath;

            using var observer = HidObservationWindow.Start();
            observer.Clear();
            child = StartHostChild(databasePath, out var diagnosticsPath);
            result.ChildDiagnosticsPath = diagnosticsPath;
            WaitForReady(child, TimeSpan.FromSeconds(15));
            result.ChildProcessId = child.Id;

            var marker = observer.Count;
            Console.WriteLine("ACTION_REQUIRED:FOCUS_NIKKE_AND_TAP_G13_G1_ONCE");
            Console.Out.Flush();
            var snapshot = WaitForGameTrace(diagnosticsPath, child, TimeSpan.FromSeconds(30));
            Thread.Sleep(100);
            StopHostChild(child);
            var events = observer.Events.Skip(marker)
                .Where(item => item.Kind == "key" && item.Code == VkEscape)
                .ToArray();
            var hookAnalysis = GameInputObservationAnalysis.Analyze(events);
            var traceAnalysis = GameTraceObservationAnalysis.Analyze(snapshot);
            result.UsbHidEvents = events;
            result.InputTraceEntries = snapshot.TraceEntries;
            result.LogicalPressCount = traceAnalysis.LogicalPressCount;
            result.WrongReleaseCount = traceAnalysis.WrongReleaseCount;
            result.Stuck = traceAnalysis.Stuck;
            result.InjectedEventCount = observer.InjectedEvents.Count(item => item.Kind == "key" && item.Code == VkEscape);
            result.UsbHidOneToOne = hookAnalysis.IsOneToOne && result.InjectedEventCount == 0;
            result.SerialHidAcknowledgedOneToOne = traceAnalysis.IsOneToOne;
            result.WindowsHookObservation = result.UsbHidOneToOne
                ? "observed"
                : "not-observed-in-nikke-foreground";

            Console.WriteLine("GAME_RESPONSE_REQUIRED:once|none|multiple|uncertain");
            Console.Out.Flush();
            result.GameResponse = NormalizeGameResponse(Console.ReadLine());
            result.GameAcceptedOneToOne = result.GameResponse == "once";
            result.Passed = result.SerialHidAcknowledgedOneToOne && result.GameAcceptedOneToOne;
        }
        catch (Exception exception)
        {
            result.Error = $"{exception.GetType().Name}: {exception.Message}";
            TryStopOrKill(child);
        }

        return WriteResult(outputDirectory, "serial-hid-game-observation", result, result.Passed);
    }

    public static int RunGameObservationFinalize(string[] arguments, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var attemptPath = Path.GetFullPath(RequiredArgument(arguments, "--attempt"));
        var response = NormalizeGameResponse(RequiredArgument(arguments, "--game-response"));
        var attempt = JsonSerializer.Deserialize<GameObservationResult>(
            File.ReadAllText(attemptPath),
            JsonOptions) ?? throw new InvalidOperationException("game observation attempt を読み取れませんでした。");
        if (attempt.TargetGame != "NIKKE" || attempt.PhysicalControl != "G13:G1" || attempt.OutputToken != "Key:Esc")
        {
            throw new InvalidOperationException("対象外の game observation attempt です。");
        }

        var diagnosticsPath = attempt.ChildDiagnosticsPath
            ?? throw new InvalidOperationException("child diagnostics path がありません。");
        var diagnosticsBytes = File.ReadAllBytes(diagnosticsPath);
        var snapshot = JsonSerializer.Deserialize<ChildHostDiagnosticSnapshot>(diagnosticsBytes, JsonOptions)
            ?? throw new InvalidOperationException("child diagnostics を読み取れませんでした。");
        var traceAnalysis = GameTraceObservationAnalysis.Analyze(snapshot);
        var hookAnalysis = GameInputObservationAnalysis.Analyze(attempt.UsbHidEvents);
        var result = new GameObservationResult
        {
            Schema = "openlogicool.serial-hid.game-observation.v2",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = attempt.Machine,
            OsVersion = attempt.OsVersion,
            TargetGame = attempt.TargetGame,
            PhysicalControl = attempt.PhysicalControl,
            OutputToken = attempt.OutputToken,
            DeviceInstanceId = attempt.DeviceInstanceId,
            TransientPort = attempt.TransientPort,
            TemporaryDatabasePath = attempt.TemporaryDatabasePath,
            ChildDiagnosticsPath = diagnosticsPath,
            ChildProcessId = attempt.ChildProcessId,
            SourceAttemptPath = attemptPath,
            SourceAttemptError = attempt.Error,
            ChildDiagnosticsSha256 = Convert.ToHexString(SHA256.HashData(diagnosticsBytes)).ToLowerInvariant(),
            UsbHidEvents = attempt.UsbHidEvents,
            InputTraceEntries = snapshot.TraceEntries,
            LogicalPressCount = traceAnalysis.LogicalPressCount,
            WrongReleaseCount = traceAnalysis.WrongReleaseCount,
            Stuck = traceAnalysis.Stuck,
            InjectedEventCount = attempt.InjectedEventCount,
            UsbHidOneToOne = hookAnalysis.IsOneToOne && attempt.InjectedEventCount == 0,
            SerialHidAcknowledgedOneToOne = traceAnalysis.IsOneToOne,
            WindowsHookObservation = hookAnalysis.IsOneToOne
                ? "observed"
                : "not-observed-in-nikke-foreground",
            GameResponse = response,
            GameAcceptedOneToOne = response == "once",
        };
        result.Passed = result.SerialHidAcknowledgedOneToOne && result.GameAcceptedOneToOne;
        if (!result.Passed)
        {
            result.Error = "Serial HID ACK trace またはゲーム内1回反応の条件を満たしませんでした。";
        }

        return WriteResult(outputDirectory, "serial-hid-game-observation", result, result.Passed);
    }

    public static int RunHostChild(string[] arguments)
    {
        var databasePath = RequiredArgument(arguments, "--db");
        var diagnosticsPath = RequiredArgument(arguments, "--diagnostics");
        using var host = CreateHost(databasePath);
        var status = host.Start();
        if (status.G13DeviceInstanceIds.Count != 1 || status.WiredDeviceInstanceIds.Count != 1)
        {
            throw new InvalidOperationException(
                $"G13を1台だけ配線できませんでした（G13={status.G13DeviceInstanceIds.Count}, wired={status.WiredDeviceInstanceIds.Count}）。");
        }

        using var diagnosticsStop = new ManualResetEventSlim(false);
        var diagnosticsThread = new Thread(() => CaptureChildDiagnostics(host, diagnosticsPath, diagnosticsStop))
        {
            IsBackground = true,
            Name = "OpenLogicoolT09Diagnostics",
        };
        diagnosticsThread.Start();

        try
        {
            Console.WriteLine("READY");
            Console.Out.Flush();
            var command = Console.ReadLine();
            host.Stop();
            Console.WriteLine("STOPPED");
            Console.Out.Flush();
            return command == "STOP" ? 0 : 1;
        }
        finally
        {
            diagnosticsStop.Set();
            diagnosticsThread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private static string CreateDatabase(SerialHidCandidate candidate, string outputToken)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"openlogicool-serial-hid-t09-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var databasePath = Path.Combine(temporaryDirectory, "t09.db");
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
            var profiles = new SqliteMappingProfileStore(connection);
            profiles.Upsert(new MappingProfileDocument(
                ContractSchemaVersions.Revision01,
                "serial-hid-t09-g13",
                "G13",
                "profile-r1",
                "map-r1",
                "base",
                ["base"],
                [],
                [],
                [new MappingBindingEntry("G1", "base", [outputToken])]));
            var associations = new SqliteAppAssociationStore(connection);
            associations.Upsert(new AppProfileAssociation(
                ContractSchemaVersions.Revision01,
                "*",
                "G13",
                "serial-hid-t09-g13"));
        }

        SerialHidOutputSettingsStore.ForDatabase(databasePath).Save(new SerialHidOutputSettings(
            SerialHidOutputSettings.CurrentSchemaVersion,
            ResidentOutputRoute.SerialHid,
            candidate.DeviceInstanceId));
        return databasePath;
    }

    private static ResidentInputHost CreateHost(string databasePath)
    {
        var settings = SerialHidOutputSettingsStore.ForDatabase(databasePath).Load();
        var watchdogPath = Path.Combine(AppContext.BaseDirectory, "OpenLogicool.Watchdog.exe");
        var sessionFactory = ResidentOutputSessionFactory.Create(settings, watchdogPath, CreateDiscovery());
        return new ResidentInputHost(
            databasePath,
            watchdogPath,
            enableTrace: true,
            G600LeftoverHostSupport.CreateSession(databasePath),
            G600OnboardModeStore.ForDatabase(databasePath),
            sessionFactory);
    }

    private static SerialHidCandidate SelectCandidate(string[] arguments)
    {
        var selectedId = OptionalArgument(arguments, "--device-instance-id");
        var candidates = CreateDiscovery().ListCandidates();
        var eligible = selectedId is null
            ? candidates
            : candidates.Where(candidate => string.Equals(
                candidate.DeviceInstanceId,
                selectedId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (eligible.Count != 1)
        {
            throw new SerialHidDiscoveryException($"Serial HID候補が一意ではありません（count={eligible.Count}）。");
        }
        return eligible[0];
    }

    private static SerialHidDiscoveryService CreateDiscovery() =>
        new(new SetupApiSerialCandidateEnumerator(), new SerialPortExchangeFactory());

    private static Process StartHostChild(string databasePath, out string diagnosticsPath)
    {
        diagnosticsPath = Path.Combine(Path.GetDirectoryName(databasePath)!, "child-diagnostics.json");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = $"serial-hid-probe-host --db \"{databasePath}\" --diagnostics \"{diagnosticsPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("probe host childを起動できませんでした。");
        }
        return process;
    }

    private static void CaptureChildDiagnostics(
        ResidentInputHost host,
        string diagnosticsPath,
        ManualResetEventSlim stop)
    {
        var trace = new List<InputTraceEntry>();
        var temporaryPath = diagnosticsPath + ".tmp";
        while (true)
        {
            trace.AddRange(host.Pump.DrainTrace());
            var snapshot = new ChildHostDiagnosticSnapshot
            {
                CapturedAtUtc = DateTime.UtcNow.ToString("O"),
                PumpProcessedCount = host.Pump.ProcessedCount,
                PumpIsRunning = host.Pump.IsRunning,
                PumpFailure = host.Pump.Failure?.ToString(),
                HostFailure = host.Failure?.ToString(),
                DroppedG13InputCount = host.DroppedG13InputCount,
                TraceEntries = trace.ToArray(),
            };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions) + Environment.NewLine);
            File.Move(temporaryPath, diagnosticsPath, overwrite: true);
            if (stop.Wait(TimeSpan.FromMilliseconds(50)))
            {
                return;
            }
        }
    }

    private static ChildHostDiagnosticSnapshot WaitForGameTrace(
        string diagnosticsPath,
        Process child,
        TimeSpan timeout)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < timeout)
        {
            if (File.Exists(diagnosticsPath))
            {
                try
                {
                    var snapshot = JsonSerializer.Deserialize<ChildHostDiagnosticSnapshot>(
                        File.ReadAllText(diagnosticsPath),
                        JsonOptions);
                    if (snapshot is not null)
                    {
                        if (snapshot.PumpFailure is not null || snapshot.HostFailure is not null)
                        {
                            throw new InvalidOperationException(snapshot.PumpFailure ?? snapshot.HostFailure);
                        }
                        if (GameTraceObservationAnalysis.Analyze(snapshot).IsOneToOne)
                        {
                            return snapshot;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Atomic replace の境界だけ再読する。
                }
            }
            if (child.HasExited)
            {
                throw new InvalidOperationException($"child exited: {child.ExitCode}");
            }
            Thread.Sleep(25);
        }
        throw new TimeoutException("G13 G1 の Serial HID ACK trace を30秒以内に観測できませんでした。");
    }

    private static void WaitForReady(Process child, TimeSpan timeout)
    {
        var ready = Task.Run(() =>
        {
            string? line;
            while ((line = child.StandardOutput.ReadLine()) is not null)
            {
                if (line == "READY")
                {
                    return true;
                }
            }
            return false;
        });
        if (!ready.Wait(timeout) || !ready.Result)
        {
            var error = child.StandardError.ReadToEnd();
            TryKill(child);
            throw new TimeoutException($"probe host childがREADYになりませんでした。{error}");
        }
    }

    private static void StopHostChild(Process child)
    {
        child.StandardInput.WriteLine("STOP");
        child.StandardInput.Flush();
        if (!child.WaitForExit(5000) || child.ExitCode != 0)
        {
            TryKill(child);
            throw new InvalidOperationException("probe host childをhandled stopできませんでした。");
        }
    }

    private static ObservedHidEvent? WaitForKeyUp(
        HidObservationWindow observer,
        int virtualKey,
        long afterTicks,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = observer.Events.FirstOrDefault(item =>
                IsKey(item, "up", virtualKey) && item.StopwatchTicks >= afterTicks);
            if (match is not null)
            {
                return match;
            }
            Thread.Sleep(2);
        }
        return null;
    }

    private static void SendEmergencyAllUp(SerialHidCandidate candidate)
    {
        using var exchange = new ProbeSerialPortFrameExchange(candidate.PortName);
        var session = SerialHidProtocolSession.Connect(
            exchange,
            SerialHidDiscoveryService.HostVersion,
            TimeSpan.FromMilliseconds(300));
        session.SendAllUp();
    }

    private static int WriteResult<T>(string outputDirectory, string prefix, T result, bool passed)
    {
        var path = Path.Combine(outputDirectory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine);
        Console.WriteLine(path);
        Console.WriteLine(passed ? "PASS" : "FAIL");
        return passed ? 0 : 1;
    }

    private static string NormalizeGameResponse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "once" => "once",
        "none" => "none",
        "multiple" => "multiple",
        _ => "uncertain",
    };

    private static void TryStopOrKill(Process? process)
    {
        if (process is null || process.HasExited)
        {
            return;
        }
        try
        {
            process.StandardInput.WriteLine("STOP");
            process.StandardInput.Flush();
            if (process.WaitForExit(2000))
            {
                return;
            }
        }
        catch { }
        TryKill(process);
    }

    private static void TryKill(Process? process)
    {
        if (process is null || process.HasExited)
        {
            return;
        }
        try
        {
            process.Kill();
            process.WaitForExit(2000);
        }
        catch { }
    }

    private static bool IsKey(ObservedHidEvent item, string edge, int code) =>
        item.Kind == "key" && item.Edge == edge && item.Code == code && !item.IsInjected;

    private static ObservedHidEvent Event(string edge, int code) =>
        new("key", edge, code, false, 0);

    private static string RequiredArgument(string[] arguments, string name) =>
        OptionalArgument(arguments, name)
        ?? throw new ArgumentException($"{name} requires a value.");

    private static string? OptionalArgument(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        return index < 0 ? null : index == arguments.Length - 1
            ? throw new ArgumentException($"{name} requires a value.")
            : arguments[index + 1];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

public static class HardKillReleaseAnalysis
{
    public static HardKillReleaseResult Analyze(
        long downTicks,
        long killRequestedTicks,
        long killCompletedTicks,
        long? releaseTicks)
    {
        var releaseObserved = releaseTicks is not null && releaseTicks >= killRequestedTicks && downTicks < killRequestedTicks;
        double? fromRequest = releaseObserved
            ? Stopwatch.GetElapsedTime(killRequestedTicks, releaseTicks!.Value).TotalMilliseconds
            : null;
        double? fromCompletion = releaseObserved && releaseTicks >= killCompletedTicks
            ? Stopwatch.GetElapsedTime(killCompletedTicks, releaseTicks!.Value).TotalMilliseconds
            : null;
        return new HardKillReleaseResult(
            releaseObserved,
            fromRequest,
            fromCompletion,
            fromRequest is <= 250);
    }
}

public sealed record HardKillReleaseResult(
    bool ReleaseObserved,
    double? ReleaseLatencyMillisecondsFromKillRequest,
    double? ReleaseLatencyMillisecondsFromKillCompletion,
    bool Meets250MillisecondBudget);

public static class GameInputObservationAnalysis
{
    public static GameInputObservationResult Analyze(IReadOnlyList<ObservedHidEvent> events)
    {
        var held = false;
        var presses = 0;
        var wrongRelease = 0;
        foreach (var item in events)
        {
            if (item.Edge == "down")
            {
                if (!held)
                {
                    held = true;
                    presses++;
                }
            }
            else if (item.Edge == "up")
            {
                if (!held)
                {
                    wrongRelease++;
                }
                else
                {
                    held = false;
                }
            }
        }
        return new GameInputObservationResult(
            presses,
            wrongRelease,
            held,
            presses == 1 && wrongRelease == 0 && !held);
    }
}

public static class GameTraceObservationAnalysis
{
    public static GameInputObservationResult Analyze(ChildHostDiagnosticSnapshot snapshot)
    {
        if (snapshot.PumpFailure is not null
            || snapshot.HostFailure is not null
            || snapshot.DroppedG13InputCount != 0)
        {
            return new GameInputObservationResult(0, 0, false, false);
        }

        var entries = snapshot.TraceEntries
            .Where(item => item.ControlId == "G1")
            .ToArray();
        var exactPair = entries.Length == 2
            && entries[0].Edge == PhysicalInputEdge.Down
            && entries[1].Edge == PhysicalInputEdge.Up
            && entries.All(item => item.Emitted)
            && entries.All(item => item.OutputTokens.SequenceEqual(["Key:Esc"]))
            && entries[0].Sequence < entries[1].Sequence;
        return new GameInputObservationResult(
            entries.Count(item => item.Edge == PhysicalInputEdge.Down),
            entries.Length > 0 && entries[0].Edge == PhysicalInputEdge.Up ? 1 : 0,
            entries.Length > 0 && entries[^1].Edge == PhysicalInputEdge.Down,
            exactPair);
    }
}

public sealed record GameInputObservationResult(
    int LogicalPressCount,
    int WrongReleaseCount,
    bool Stuck,
    bool IsOneToOne);

internal sealed class HardKillSmokeResult
{
    public required string Schema { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string OsVersion { get; init; }
    public string? DeviceInstanceId { get; set; }
    public string? TransientPort { get; set; }
    public string? TemporaryDatabasePath { get; set; }
    public string? ChildDiagnosticsPath { get; set; }
    public int? ChildProcessId { get; set; }
    public int? ChildExitCode { get; set; }
    public bool DownObservedBeforeKill { get; set; }
    public bool ReleaseObserved { get; set; }
    public double? ReleaseLatencyMillisecondsFromKillRequest { get; set; }
    public double? ReleaseLatencyMillisecondsFromKillCompletion { get; set; }
    public bool Meets250MillisecondBudget { get; set; }
    public bool EmergencyAllUpAttempted { get; set; }
    public IReadOnlyList<ObservedHidEvent> Events { get; set; } = [];
    public string? Error { get; set; }
    public bool Passed { get; set; }
}

internal sealed class GameObservationResult
{
    public required string Schema { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string OsVersion { get; init; }
    public required string TargetGame { get; init; }
    public required string PhysicalControl { get; init; }
    public required string OutputToken { get; init; }
    public string? DeviceInstanceId { get; set; }
    public string? TransientPort { get; set; }
    public string? TemporaryDatabasePath { get; set; }
    public string? ChildDiagnosticsPath { get; set; }
    public int? ChildProcessId { get; set; }
    public string? SourceAttemptPath { get; set; }
    public string? SourceAttemptError { get; set; }
    public string? ChildDiagnosticsSha256 { get; set; }
    public IReadOnlyList<ObservedHidEvent> UsbHidEvents { get; set; } = [];
    public IReadOnlyList<InputTraceEntry> InputTraceEntries { get; set; } = [];
    public int LogicalPressCount { get; set; }
    public int WrongReleaseCount { get; set; }
    public bool Stuck { get; set; }
    public int InjectedEventCount { get; set; }
    public bool UsbHidOneToOne { get; set; }
    public bool SerialHidAcknowledgedOneToOne { get; set; }
    public string? WindowsHookObservation { get; set; }
    public string? GameResponse { get; set; }
    public bool GameAcceptedOneToOne { get; set; }
    public string? Error { get; set; }
    public bool Passed { get; set; }
}

public sealed class ChildHostDiagnosticSnapshot
{
    public required string CapturedAtUtc { get; init; }
    public long PumpProcessedCount { get; init; }
    public bool PumpIsRunning { get; init; }
    public string? PumpFailure { get; init; }
    public string? HostFailure { get; init; }
    public long DroppedG13InputCount { get; init; }
    public IReadOnlyList<InputTraceEntry> TraceEntries { get; init; } = [];
}
