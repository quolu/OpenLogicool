using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;
using OpenLogicool.Persistence;

namespace OpenLogicool.Probe;

internal static class SerialHidLiveSmoke
{
    private static readonly TimeSpan UserActionTimeout = Timeout.InfiniteTimeSpan;
    private static readonly string[] RequiredActionIds =
    [
        "g13-key", "g13-chord", "g13-mouse", "g13-sequence",
        "g600-key", "g600-chord", "g600-mouse", "g600-sequence",
        "both-devices", "g13-layer", "g600-layer", "foreground-both", "explicit-restart",
    ];

    public static int Run(string[] arguments, string outputDirectory)
    {
        var selectedDeviceInstanceId = OptionalArgument(arguments, "--device-instance-id");
        var resumePath = OptionalArgument(arguments, "--resume");
        var latencyOnly = arguments.Contains("--latency-only", StringComparer.Ordinal);
        if (latencyOnly && resumePath is not null)
        {
            throw new ArgumentException("--latency-only と --resume は同時に指定できません。");
        }
        Directory.CreateDirectory(outputDirectory);
        var result = new SerialHidLiveSmokeResult
        {
            Schema = "openlogicool.serial-hid.live-smoke.v1",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            Mode = latencyOnly ? "latency-only" : "full",
            NfrLatencyGateEvaluated = false,
        };

        ResidentInputHost? host = null;
        HidObservationWindow? observer = null;
        try
        {
            var discovery = CreateDiscovery();
            var candidates = discovery.ListCandidates();
            var candidate = SelectCandidate(candidates, selectedDeviceInstanceId);
            result.SelectedDeviceInstanceId = candidate.DeviceInstanceId;
            result.TransientPort = candidate.PortName;
            var resume = resumePath is null
                ? LiveSmokeResumeEvidence.Empty
                : LoadResumeEvidence(resumePath, candidate.DeviceInstanceId);
            result.ResumeEvidencePath = resume.Path;
            result.ResumeEvidenceSha256 = resume.Sha256;
            result.ResumedActionIds = resume.ActionIds;
            result.ResumeEvidenceValidated = resumePath is null || resume.ActionIds.Count > 0;

            var tempDirectory = Path.Combine(Path.GetTempPath(), $"openlogicool-serial-hid-live-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            var databasePath = Path.Combine(tempDirectory, "live-smoke.db");
            result.TemporaryDatabasePath = databasePath;
            SeedDefaultProfiles(databasePath);

            var settingsStore = SerialHidOutputSettingsStore.ForDatabase(databasePath);
            settingsStore.Save(new SerialHidOutputSettings(
                SerialHidOutputSettings.CurrentSchemaVersion,
                ResidentOutputRoute.SerialHid,
                candidate.DeviceInstanceId));
            var persisted = settingsStore.Load();
            result.SettingsPersisted = persisted.RequestedRoute == ResidentOutputRoute.SerialHid
                && string.Equals(persisted.SelectedDeviceInstanceId, candidate.DeviceInstanceId, StringComparison.OrdinalIgnoreCase);

            observer = HidObservationWindow.Start();
            var measurement = new EmissionMeasurement();
            host = CreateHost(databasePath, measurement);
            var status = host.Start();
            result.InitialOutputRoute = host.OutputRoute.ToString();
            result.G13DeviceInstanceIds = status.G13DeviceInstanceIds.ToArray();
            result.G600DeviceInstanceIds = status.G600DeviceInstanceIds.ToArray();
            EnsureExactlyOneEach(status);
            result.RestartAppliedPersistedRoute = result.SettingsPersisted && host.OutputRoute == ResidentOutputRoute.SerialHid;

            var g13 = status.G13DeviceInstanceIds[0];
            var g600 = status.G600DeviceInstanceIds[0];
            if (latencyOnly)
            {
                RunAction(
                    host, observer, result, "latency-both",
                    "latency確認です。G13のG1とG600のG9を同時に押して、両方とも離してください。",
                    [
                        [Event("key", "down", 0x7C), Event("key", "down", 0x80)],
                        [Event("key", "up", 0x7C), Event("key", "up", 0x80)],
                    ],
                    [(g13, "G1", "base"), (g600, "G9", "normal")],
                    checkUnexpectedHardware: true);
                CaptureHostState(host, result);
                TryStop(host, result);
                result.AckedEmissionCount = measurement.AckedEmissionCount;
                result.EmitterRoundTripMilliseconds = measurement.RoundTripMilliseconds.ToArray();
                result.InjectedFallbackEvents = CollectInjectedEvents(result, observer);
                result.Latency = LatencySummary.From(
                    result.TraceEntries.Where(entry => entry.Emitted).Select(entry => entry.DispatchLatencyMs));
                var latencyBalance = EventBalance.Analyze(result.Actions.SelectMany(action => action.HardwareEvents));
                result.WrongReleaseCount = latencyBalance.WrongReleaseCount;
                result.StuckOutputs = latencyBalance.StuckOutputs;
                result.Passed = result.SettingsPersisted
                    && result.RestartAppliedPersistedRoute
                    && result.DroppedG13 == 0
                    && result.DroppedG600 == 0
                    && result.WrongReleaseCount == 0
                    && result.StuckOutputs.Count == 0
                    && result.InjectedFallbackEvents.Count == 0
                    && result.StopErrors.Count == 0
                    && result.Actions.Count == 1
                    && result.Actions[0].Passed;
            }
            else
            {
                RunDefaultActions(host, observer, result, g13, g600);

            var foreground = ForegroundAppTracker.GetForegroundIdentity()
                ?? throw new InvalidOperationException("foreground identityを取得できませんでした。");
            if (string.IsNullOrWhiteSpace(foreground.NormalizedFullPath))
            {
                throw new InvalidOperationException("foreground full pathを取得できませんでした。");
            }
            result.ForegroundIdentity = foreground.NormalizedFullPath;
            SeedForegroundProfiles(databasePath, foreground.NormalizedFullPath);
            host.RefreshAppFirstData();
            WaitForForegroundProfiles(host, TimeSpan.FromSeconds(5));
            result.ForegroundProfileApplied = true;

            RunAction(
                host, observer, result, "foreground-both",
                "G13のG1とG600のG9を同時に押して、両方とも離してください。",
                [
                    [Event("key", "down", 0x74), Event("key", "down", 0x75)],
                    [Event("key", "up", 0x74), Event("key", "up", 0x75)],
                ],
                [(g13, "G1", "base"), (g600, "G9", "normal")],
                checkUnexpectedHardware: true);

            RunBoardDisconnect(host, observer, result, g13, candidate.DeviceInstanceId);
            CaptureHostState(host, result);
            TryStop(host, result);
            host.Dispose();
            host = null;

            WaitForPresence(candidate.DeviceInstanceId, expected: true, TimeSpan.FromSeconds(60));
            Thread.Sleep(1000);
            var recoveryMeasurement = new EmissionMeasurement();
            host = CreateHost(databasePath, recoveryMeasurement);
            var recoveryStatus = host.Start();
            result.ExplicitRestartOutputRoute = host.OutputRoute.ToString();
            result.ExplicitRestartRecovered = host.OutputRoute == ResidentOutputRoute.SerialHid
                && recoveryStatus.G13DeviceInstanceIds.Count == 1
                && recoveryStatus.G600DeviceInstanceIds.Count == 1;
            RunAction(
                host, observer, result, "explicit-restart",
                "復帰確認です。G13のG1を1回押して離してください。",
                [[Event("key", "down", 0x74)], [Event("key", "up", 0x74)]],
                [(recoveryStatus.G13DeviceInstanceIds[0], "G1", "base")],
                checkUnexpectedHardware: true);
            CaptureHostState(host, result);
            TryStop(host, result);

            result.AckedEmissionCount = measurement.AckedEmissionCount + recoveryMeasurement.AckedEmissionCount;
            result.EmitterRoundTripMilliseconds = measurement.RoundTripMilliseconds
                .Concat(recoveryMeasurement.RoundTripMilliseconds)
                .ToArray();
            result.InjectedFallbackEvents = CollectInjectedEvents(result, observer);
            result.Latency = LatencySummary.From(
                resume.DispatchLatenciesMilliseconds.Concat(
                    result.TraceEntries.Where(entry => entry.Emitted).Select(entry => entry.DispatchLatencyMs)));
            var balance = EventBalance.Analyze(
                resume.HardwareEvents.Concat(result.Actions.SelectMany(action => action.HardwareEvents)));
            result.WrongReleaseCount = balance.WrongReleaseCount;
            result.StuckOutputs = balance.StuckOutputs;
            result.Passed = result.SettingsPersisted
                && result.RestartAppliedPersistedRoute
                && result.ResumeEvidenceValidated
                && result.ForegroundProfileApplied
                && result.BoardDisconnectObserved
                && result.BoardFaultObserved
                && result.NoAutomaticResume
                && result.ExplicitRestartRecovered
                && result.DroppedG13 == 0
                && result.DroppedG600 == 0
                && result.WrongReleaseCount == 0
                && result.StuckOutputs.Count == 0
                && result.InjectedFallbackEvents.Count == 0
                && result.Actions.All(action => action.Passed)
                && RequiredActionIds.All(actionId =>
                    result.ResumedActionIds.Contains(actionId, StringComparer.Ordinal)
                    || result.Actions.Any(action => action.ActionId == actionId && action.Passed));
            }
        }
        catch (Exception exception)
        {
            result.Error = exception.ToString();
        }
        finally
        {
            if (host is not null)
            {
                CaptureHostState(host, result);
                TryStop(host, result);
                host.Dispose();
            }
            if (observer is not null)
            {
                result.InjectedFallbackEvents = CollectInjectedEvents(result, observer);
                observer.Dispose();
            }
        }

        var path = Path.Combine(outputDirectory, $"serial-hid-live-smoke-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine);
        Console.WriteLine(path);
        Console.WriteLine(result.Passed ? "PASS" : $"FAIL: {result.Error ?? "one or more checks failed"}");
        return result.Passed ? 0 : 1;
    }

    private static void RunDefaultActions(
        ResidentInputHost host,
        HidObservationWindow observer,
        SerialHidLiveSmokeResult result,
        string g13,
        string g600)
    {
        RunAction(host, observer, result, "g13-key", "G13のG1を1回押して離してください。",
            [[Event("key", "down", 0x7C)], [Event("key", "up", 0x7C)]], [(g13, "G1", "base")], false);
        RunAction(host, observer, result, "g13-chord", "G13のG2を1回押して離してください。",
            [[Event("key", "down", 0xA2), Event("key", "down", 0x7D)], [Event("key", "up", 0xA2), Event("key", "up", 0x7D)]],
            [(g13, "G2", "base")], false);
        RunAction(host, observer, result, "g13-mouse", "G13のG3を1回押して離してください。",
            [[Event("mouse", "down", 0x04)], [Event("mouse", "up", 0x04)]], [(g13, "G3", "base")], false);
        RunAction(host, observer, result, "g13-sequence", "G13のG4を1回押して離してください。",
            [
                [Event("key", "down", 0x7E)], [Event("key", "up", 0x7E)],
                [Event("key", "down", 0xA2), Event("key", "down", 0x7F)],
                [Event("key", "up", 0xA2), Event("key", "up", 0x7F)],
            ], [(g13, "G4", "base")], false);

        RunAction(host, observer, result, "g600-key", "G600のG9を1回押して離してください。",
            [[Event("key", "down", 0x80)], [Event("key", "up", 0x80)]], [(g600, "G9", "normal")], true);
        RunAction(host, observer, result, "g600-chord", "G600のG10を1回押して離してください。",
            [[Event("key", "down", 0xA0), Event("key", "down", 0x81)], [Event("key", "up", 0xA0), Event("key", "up", 0x81)]],
            [(g600, "G10", "normal")], true);
        RunAction(host, observer, result, "g600-mouse", "G600のG11を1回押して離してください。",
            [[Event("mouse", "down", 0x04)], [Event("mouse", "up", 0x04)]], [(g600, "G11", "normal")], true);
        RunAction(host, observer, result, "g600-sequence", "G600のG12を1回押して離してください。",
            [
                [Event("key", "down", 0x84)], [Event("key", "up", 0x84)],
                [Event("key", "down", 0xA4), Event("key", "down", 0x85)],
                [Event("key", "up", 0xA4), Event("key", "up", 0x85)],
            ], [(g600, "G12", "normal")], true);

        RunAction(host, observer, result, "both-devices", "G13のG5とG600のG13を同時に押して、両方とも離してください。",
            [[Event("key", "down", 0x82), Event("key", "down", 0x86)], [Event("key", "up", 0x82), Event("key", "up", 0x86)]],
            [(g13, "G5", "base"), (g600, "G13", "normal")], true);
        RunAction(host, observer, result, "g13-layer", "G13のM2を押してからG1を1回押して離してください。",
            [[Event("key", "down", 0x83)], [Event("key", "up", 0x83)]], [(g13, "G1", "m2")], false);
        RunAction(host, observer, result, "g600-layer", "G600のG6を押したままG9を1回押して離し、最後にG6を離してください。",
            [[Event("key", "down", 0x87)], [Event("key", "up", 0x87)]], [(g600, "G9", "shift")], true);
    }

    private static void RunAction(
        ResidentInputHost host,
        HidObservationWindow observer,
        SerialHidLiveSmokeResult result,
        string actionId,
        string instruction,
        IReadOnlyList<IReadOnlyList<ObservedHidEvent>> expectedGroups,
        IReadOnlyList<(string DeviceInstanceId, string ControlId, string LayerId)> expectedInputs,
        bool checkUnexpectedHardware)
    {
        if (result.ResumedActionIds.Contains(actionId, StringComparer.Ordinal))
        {
            Console.WriteLine($"ACTION_RESUME|{actionId}");
            Console.Out.Flush();
            return;
        }

        observer.Clear();
        host.Pump.DrainTrace();
        Console.WriteLine($"ACTION|{actionId}|{instruction}");
        Console.Out.Flush();
        observer.WaitForGroups(expectedGroups, 0, () => host.Failure);
        Thread.Sleep(150);
        var trace = WaitForTrace(host, expectedInputs, TimeSpan.FromSeconds(5));
        var hardware = LiveActionObservation.AroundExpected(observer.Events, expectedGroups, TimeSpan.FromMilliseconds(100));
        var injected = observer.InjectedEvents.ToArray();
        var unexpected = checkUnexpectedHardware
            ? LiveActionObservation.UnexpectedEvents(hardware, expectedGroups)
            : [];
        var action = new LiveActionResult
        {
            ActionId = actionId,
            Instruction = instruction,
            ExpectedGroups = expectedGroups,
            HardwareEvents = hardware,
            InjectedEvents = injected,
            UnexpectedHardwareEvents = unexpected,
            TraceEntries = trace,
            Passed = expectedInputs.All(expected => trace.Any(entry =>
                entry.DeviceInstanceId == expected.DeviceInstanceId
                && entry.ControlId == expected.ControlId
                && entry.Edge == PhysicalInputEdge.Down
                && entry.LayerId == expected.LayerId
                && entry.Emitted))
                && unexpected.Count == 0,
        };
        result.Actions.Add(action);
        result.TraceEntries.AddRange(trace);
        if (!action.Passed)
        {
            throw new InvalidOperationException($"action '{actionId}' のlive受入が不成立です。");
        }
        Console.WriteLine($"ACTION_PASS|{actionId}");
        Console.Out.Flush();
    }

    private static void RunBoardDisconnect(
        ResidentInputHost host,
        HidObservationWindow observer,
        SerialHidLiveSmokeResult result,
        string g13,
        string serialDeviceInstanceId)
    {
        observer.Clear();
        host.Pump.DrainTrace();
        Console.WriteLine("ACTION|board-hold|G13のG1を押したまま保持してください。まだPro Microは抜かないでください。");
        Console.Out.Flush();
        observer.WaitForGroups([[Event("key", "down", 0x74)]], 0, () => host.Failure);
        var holdTrace = WaitForTrace(host, [(g13, "G1", "base")], TimeSpan.FromSeconds(5));
        result.TraceEntries.AddRange(holdTrace);
        Console.WriteLine("ACTION|board-unplug|G13のG1を保持したまま、今Pro Microだけを抜いてください。抜いた後はG1を離して構いません。");
        Console.Out.Flush();
        result.BoardDisconnectObserved = WaitForPresence(serialDeviceInstanceId, expected: false, UserActionTimeout);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (host.Failure is null && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }
        result.BoardFaultObserved = host.Failure is not null;
        result.BoardFault = host.Failure?.ToString();
        Thread.Sleep(300);
        result.BoardDisconnectEvents = observer.Events.ToArray();
        result.BoardDisconnectInjectedEvents = observer.InjectedEvents.ToArray();
        Console.WriteLine("ACTION|board-reconnect|Pro Microを同じUSB口へ挿し直してください。");
        Console.Out.Flush();
        WaitForPresence(serialDeviceInstanceId, expected: true, UserActionTimeout);
        Thread.Sleep(500);
        result.NoAutomaticResume = host.Failure is not null && !host.Pump.IsRunning;
        if (!result.BoardDisconnectObserved || !result.BoardFaultObserved || !result.NoAutomaticResume)
        {
            throw new InvalidOperationException("board抜線のterminal fault／no automatic resumeが不成立です。");
        }
        Console.WriteLine("ACTION_PASS|board-fault");
        Console.Out.Flush();
    }

    private static IReadOnlyList<InputTraceEntry> WaitForTrace(
        ResidentInputHost host,
        IReadOnlyList<(string DeviceInstanceId, string ControlId, string LayerId)> expectedInputs,
        TimeSpan timeout)
    {
        var collected = new List<InputTraceEntry>();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            collected.AddRange(host.Pump.DrainTrace());
            if (expectedInputs.All(expected => collected.Any(entry =>
                    entry.DeviceInstanceId == expected.DeviceInstanceId
                    && entry.ControlId == expected.ControlId
                    && entry.Edge == PhysicalInputEdge.Down
                    && entry.LayerId == expected.LayerId)))
            {
                Thread.Sleep(100);
                collected.AddRange(host.Pump.DrainTrace());
                return LiveActionObservation.ExpectedTrace(collected, expectedInputs);
            }
            Thread.Sleep(20);
        }
        throw new TimeoutException("expected Raw Input traceを観測できませんでした。");
    }

    private static IReadOnlyList<ObservedHidEvent> CollectInjectedEvents(
        SerialHidLiveSmokeResult result,
        HidObservationWindow observer) =>
        result.Actions
            .SelectMany(action => action.InjectedEvents)
            .Concat(result.BoardDisconnectInjectedEvents)
            .Concat(observer.InjectedEvents)
            .Distinct()
            .OrderBy(item => item.StopwatchTicks)
            .ToArray();

    private static ResidentInputHost CreateHost(string databasePath, EmissionMeasurement measurement)
    {
        var settings = SerialHidOutputSettingsStore.ForDatabase(databasePath).Load();
        var productFactory = ResidentOutputSessionFactory.Create(
            settings,
            Path.Combine(AppContext.BaseDirectory, "OpenLogicool.Watchdog.exe"),
            CreateDiscovery());
        return new ResidentInputHost(
            databasePath,
            Path.Combine(AppContext.BaseDirectory, "OpenLogicool.Watchdog.exe"),
            enableTrace: true,
            G600LeftoverHostSupport.CreateSession(databasePath),
            G600OnboardModeStore.ForDatabase(databasePath),
            () => new MeasuredResidentOutputSession(productFactory(), measurement));
    }

    private static SerialHidDiscoveryService CreateDiscovery() =>
        new(new SetupApiSerialCandidateEnumerator(), new SerialPortExchangeFactory());

    private static SerialHidCandidate SelectCandidate(
        IReadOnlyList<SerialHidCandidate> candidates,
        string? selectedDeviceInstanceId)
    {
        var eligible = selectedDeviceInstanceId is null
            ? candidates
            : candidates.Where(candidate => string.Equals(
                candidate.DeviceInstanceId, selectedDeviceInstanceId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (eligible.Count != 1)
        {
            throw new SerialHidDiscoveryException($"Serial HID候補が一意ではありません（count={eligible.Count}）。");
        }
        return eligible[0];
    }

    private static void SeedDefaultProfiles(string databasePath)
    {
        using var connection = OpenMigrated(databasePath);
        var profiles = new SqliteMappingProfileStore(connection);
        profiles.Upsert(DefaultG13Profile());
        profiles.Upsert(DefaultG600Profile());
        var associations = new SqliteAppAssociationStore(connection);
        associations.Upsert(new AppProfileAssociation(ContractSchemaVersions.Revision01, "*", "G13", "serial-live-g13-default"));
        associations.Upsert(new AppProfileAssociation(ContractSchemaVersions.Revision01, "*", "G600", "serial-live-g600-default"));
    }

    private static void SeedForegroundProfiles(string databasePath, string foregroundPath)
    {
        using var connection = OpenMigrated(databasePath);
        var profiles = new SqliteMappingProfileStore(connection);
        profiles.Upsert(SimpleProfile("serial-live-g13-app", "G13", "base", "G1", "Key:F5"));
        profiles.Upsert(SimpleProfile("serial-live-g600-app", "G600", "normal", "G9", "Key:F6"));
        var associations = new SqliteAppAssociationStore(connection);
        associations.Upsert(new AppProfileAssociation(
            ContractSchemaVersions.Revision01, foregroundPath, "G13", "serial-live-g13-app"));
        associations.Upsert(new AppProfileAssociation(
            ContractSchemaVersions.Revision01, foregroundPath, "G600", "serial-live-g600-app"));
    }

    private static MappingProfileDocument DefaultG13Profile() => new(
        ContractSchemaVersions.Revision01,
        "serial-live-g13-default",
        "G13",
        "profile-r1",
        "map-r1",
        "base",
        ["base", "m2"],
        [new LayerSelectorEntry("M1", "base"), new LayerSelectorEntry("M2", "m2")],
        [],
        [
            new MappingBindingEntry("G1", "base", ["Key:F13"]),
            new MappingBindingEntry("G2", "base", ["Key:LCtrl", "Key:F14"]),
            new MappingBindingEntry("G3", "base", ["Mouse:Middle"]),
            new MappingBindingEntry("G4", "base", ["Tap:Key:F15", "Tap:Key:LCtrl+Key:F16"]),
            new MappingBindingEntry("G5", "base", ["Key:F19"]),
            new MappingBindingEntry("G1", "m2", ["Key:F20"]),
        ]);

    private static MappingProfileDocument DefaultG600Profile() => new(
        ContractSchemaVersions.Revision01,
        "serial-live-g600-default",
        "G600",
        "profile-r1",
        "map-r1",
        "normal",
        ["normal", "shift"],
        [],
        [new LayerSelectorEntry("G6", "shift")],
        [
            new MappingBindingEntry("G9", "normal", ["Key:F17"]),
            new MappingBindingEntry("G10", "normal", ["Key:LShift", "Key:F18"]),
            new MappingBindingEntry("G11", "normal", ["Mouse:Middle"]),
            new MappingBindingEntry("G12", "normal", ["Tap:Key:F21", "Tap:Key:LAlt+Key:F22"]),
            new MappingBindingEntry("G13", "normal", ["Key:F23"]),
            new MappingBindingEntry("G9", "shift", ["Key:F24"]),
        ]);

    private static MappingProfileDocument SimpleProfile(
        string profileId,
        string deviceKind,
        string layer,
        string control,
        string output) => new(
            ContractSchemaVersions.Revision01,
            profileId,
            deviceKind,
            "profile-r2",
            "map-r2",
            layer,
            [layer],
            [],
            [],
            [new MappingBindingEntry(control, layer, [output])]);

    private static SqliteConnection OpenMigrated(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        return connection;
    }

    private static void WaitForForegroundProfiles(ResidentInputHost host, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var decisions = host.RecentProfileSwitchDecisions();
            if (decisions.Any(decision =>
                    decision.Outcomes.Any(outcome => outcome.SelectedProfileId == "serial-live-g13-app")
                    && decision.Outcomes.Any(outcome => outcome.SelectedProfileId == "serial-live-g600-app")))
            {
                Thread.Sleep(250);
                return;
            }
            Thread.Sleep(50);
        }
        throw new TimeoutException("foreground profile切替を観測できませんでした。");
    }

    private static bool WaitForPresence(string deviceInstanceId, bool expected, TimeSpan timeout)
    {
        var finite = timeout != Timeout.InfiniteTimeSpan;
        var deadline = finite ? DateTime.UtcNow + timeout : DateTime.MaxValue;
        while (!finite || DateTime.UtcNow < deadline)
        {
            if (PnpDevicePresence.IsPresent(deviceInstanceId) == expected)
            {
                return true;
            }
            Thread.Sleep(100);
        }
        return false;
    }

    private static void EnsureExactlyOneEach(ResidentHostStatus status)
    {
        if (status.G13DeviceInstanceIds.Count != 1 || status.G600DeviceInstanceIds.Count != 1)
        {
            throw new InvalidOperationException(
                $"G13/G600を各1台必要とします（G13={status.G13DeviceInstanceIds.Count}, G600={status.G600DeviceInstanceIds.Count}）。");
        }
        if (status.WiredDeviceInstanceIds.Count != 2)
        {
            throw new InvalidOperationException($"wired device数が2ではありません（{status.WiredDeviceInstanceIds.Count}）。");
        }
    }

    private static void CaptureHostState(ResidentInputHost host, SerialHidLiveSmokeResult result)
    {
        result.TraceEntries.AddRange(host.Pump.DrainTrace());
        result.DroppedG13 = Math.Max(result.DroppedG13, host.DroppedG13InputCount);
        result.DroppedG600 = Math.Max(result.DroppedG600, host.DroppedG600InputCount);
    }

    private static void TryStop(ResidentInputHost host, SerialHidLiveSmokeResult result)
    {
        try
        {
            host.Stop();
        }
        catch (Exception exception)
        {
            result.StopErrors.Add($"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static ObservedHidEvent Event(string kind, string edge, int code) =>
        new(kind, edge, code, false, 0);

    private static string? OptionalArgument(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        return index < 0 ? null : index == arguments.Length - 1
            ? throw new ArgumentException($"{name} requires a value.")
            : arguments[index + 1];
    }

    private static LiveSmokeResumeEvidence LoadResumeEvidence(string path, string selectedDeviceInstanceId)
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(fullPath);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "openlogicool.serial-hid.live-smoke.v1"
            || root.GetProperty("machine").GetString() != Environment.MachineName
            || !string.Equals(
                root.GetProperty("selectedDeviceInstanceId").GetString(),
                selectedDeviceInstanceId,
                StringComparison.OrdinalIgnoreCase)
            || root.GetProperty("initialOutputRoute").GetString() != ResidentOutputRoute.SerialHid.ToString()
            || !root.GetProperty("settingsPersisted").GetBoolean()
            || !root.GetProperty("restartAppliedPersistedRoute").GetBoolean())
        {
            throw new InvalidOperationException("resume evidenceのmachine／device／Serial HID routeが現在の実機条件と一致しません。");
        }

        var passed = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var action in root.GetProperty("actions").EnumerateArray())
        {
            if (action.GetProperty("passed").GetBoolean()
                && action.GetProperty("injectedEvents").GetArrayLength() == 0
                && action.GetProperty("unexpectedHardwareEvents").GetArrayLength() == 0)
            {
                passed[action.GetProperty("actionId").GetString()!] = action;
            }
        }

        var actionIds = new List<string>();
        foreach (var actionId in RequiredActionIds)
        {
            if (!passed.ContainsKey(actionId))
            {
                break;
            }
            actionIds.Add(actionId);
        }
        if (actionIds.Count == 0)
        {
            throw new InvalidOperationException("resume evidenceに連続した合格済みactionがありません。");
        }

        var hardware = new List<ObservedHidEvent>();
        var latencies = new List<double>();
        foreach (var actionId in actionIds)
        {
            var action = passed[actionId];
            hardware.AddRange(JsonSerializer.Deserialize<ObservedHidEvent[]>(
                action.GetProperty("hardwareEvents").GetRawText(), JsonOptions) ?? []);
            var trace = JsonSerializer.Deserialize<InputTraceEntry[]>(
                action.GetProperty("traceEntries").GetRawText(), JsonOptions) ?? [];
            latencies.AddRange(trace.Where(entry => entry.Emitted).Select(entry => entry.DispatchLatencyMs));
        }

        return new LiveSmokeResumeEvidence(
            fullPath,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            actionIds,
            hardware,
            latencies);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

internal sealed record LiveSmokeResumeEvidence(
    string? Path,
    string? Sha256,
    IReadOnlyList<string> ActionIds,
    IReadOnlyList<ObservedHidEvent> HardwareEvents,
    IReadOnlyList<double> DispatchLatenciesMilliseconds)
{
    public static LiveSmokeResumeEvidence Empty { get; } = new(null, null, [], [], []);
}

internal sealed class MeasuredResidentOutputSession(
    IResidentOutputSession inner,
    EmissionMeasurement measurement) : IResidentOutputSession
{
    private IOutputEmitter? _emitter;

    public ResidentOutputRoute Route => inner.Route;
    public IOutputEmitter Emitter => _emitter ?? throw new InvalidOperationException("measured output session is not started.");
    public Exception? BackgroundFailure => inner.BackgroundFailure;

    public void Start()
    {
        inner.Start();
        _emitter = new MeasuredEmitter(inner.Emitter, measurement);
    }

    public void Stop() => inner.Stop();
    public void Dispose() => inner.Dispose();
}

internal sealed class MeasuredEmitter(IOutputEmitter inner, EmissionMeasurement measurement) : IOutputEmitter
{
    public void Emit(IReadOnlyList<MappedOutputEdge> edges)
    {
        var clock = Stopwatch.StartNew();
        inner.Emit(edges);
        clock.Stop();
        if (edges.Count > 0)
        {
            Interlocked.Increment(ref measurement.AckedEmissionCount);
            lock (measurement.RoundTripMilliseconds)
            {
                measurement.RoundTripMilliseconds.Add(clock.Elapsed.TotalMilliseconds);
            }
        }
    }
}

internal sealed class EmissionMeasurement
{
    public long AckedEmissionCount;
    public List<double> RoundTripMilliseconds { get; } = [];
}

internal sealed class SerialHidLiveSmokeResult
{
    public required string Schema { get; init; }
    public required string CapturedAtUtc { get; init; }
    public required string Machine { get; init; }
    public required string Mode { get; init; }
    public string? SelectedDeviceInstanceId { get; set; }
    public string? TransientPort { get; set; }
    public string? TemporaryDatabasePath { get; set; }
    public string? ResumeEvidencePath { get; set; }
    public string? ResumeEvidenceSha256 { get; set; }
    public IReadOnlyList<string> ResumedActionIds { get; set; } = [];
    public bool ResumeEvidenceValidated { get; set; }
    public bool SettingsPersisted { get; set; }
    public string? InitialOutputRoute { get; set; }
    public bool RestartAppliedPersistedRoute { get; set; }
    public IReadOnlyList<string> G13DeviceInstanceIds { get; set; } = [];
    public IReadOnlyList<string> G600DeviceInstanceIds { get; set; } = [];
    public string? ForegroundIdentity { get; set; }
    public bool ForegroundProfileApplied { get; set; }
    public bool BoardDisconnectObserved { get; set; }
    public bool BoardFaultObserved { get; set; }
    public string? BoardFault { get; set; }
    public IReadOnlyList<ObservedHidEvent> BoardDisconnectEvents { get; set; } = [];
    public IReadOnlyList<ObservedHidEvent> BoardDisconnectInjectedEvents { get; set; } = [];
    public bool NoAutomaticResume { get; set; }
    public string? ExplicitRestartOutputRoute { get; set; }
    public bool ExplicitRestartRecovered { get; set; }
    public long DroppedG13 { get; set; }
    public long DroppedG600 { get; set; }
    public long AckedEmissionCount { get; set; }
    public IReadOnlyList<double> EmitterRoundTripMilliseconds { get; set; } = [];
    public List<LiveActionResult> Actions { get; } = [];
    public List<InputTraceEntry> TraceEntries { get; } = [];
    public IReadOnlyList<ObservedHidEvent> InjectedFallbackEvents { get; set; } = [];
    public int WrongReleaseCount { get; set; }
    public IReadOnlyList<string> StuckOutputs { get; set; } = [];
    public LatencySummary Latency { get; set; } = LatencySummary.Empty;
    /// <summary>
    /// live smokeの少数物理edgeは実Raw Input経路のspot確認であり、p99受入の標本ではない。
    /// NFR-002のp99合否は200 edgeを測るserial-hid-fastpath-latencyが所有する。
    /// </summary>
    public bool NfrLatencyGateEvaluated { get; init; }
    public List<string> StopErrors { get; } = [];
    public bool Passed { get; set; }
    public string? Error { get; set; }
}

internal sealed class LiveActionResult
{
    public required string ActionId { get; init; }
    public required string Instruction { get; init; }
    public required IReadOnlyList<IReadOnlyList<ObservedHidEvent>> ExpectedGroups { get; init; }
    public required IReadOnlyList<ObservedHidEvent> HardwareEvents { get; init; }
    public required IReadOnlyList<ObservedHidEvent> InjectedEvents { get; init; }
    public required IReadOnlyList<ObservedHidEvent> UnexpectedHardwareEvents { get; init; }
    public required IReadOnlyList<InputTraceEntry> TraceEntries { get; init; }
    public bool Passed { get; init; }
}

public sealed record LatencySummary(
    int Count,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaximumMilliseconds)
{
    public static LatencySummary Empty { get; } = new(0, 0, 0, 0, 0);

    public static LatencySummary From(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0
            ? Empty
            : new LatencySummary(
                sorted.Length,
                Percentile(sorted, 0.50),
                Percentile(sorted, 0.95),
                Percentile(sorted, 0.99),
                sorted[^1]);
    }

    private static double Percentile(IReadOnlyList<double> sorted, double quantile)
    {
        var index = (int)Math.Ceiling(quantile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}

public sealed record EventBalance(int WrongReleaseCount, IReadOnlyList<string> StuckOutputs)
{
    public static EventBalance Analyze(IEnumerable<ObservedHidEvent> events)
    {
        var held = new Dictionary<string, int>(StringComparer.Ordinal);
        var wrong = 0;
        foreach (var entry in events)
        {
            var key = $"{entry.Kind}:{entry.Code}";
            held.TryGetValue(key, out var count);
            if (entry.Edge == "down")
            {
                // Windows はHID keyboardの保持中にtypematic downを反復する。
                // keyは状態として数え、mouse等の重複downだけを従来どおり保持数へ反映する。
                held[key] = entry.Kind == "key" && count > 0 ? count : count + 1;
            }
            else if (count == 0)
            {
                wrong++;
            }
            else
            {
                held[key] = count - 1;
            }
        }
        return new EventBalance(wrong, held.Where(pair => pair.Value != 0).Select(pair => $"{pair.Key}={pair.Value}").ToArray());
    }
}

public static class LiveActionObservation
{
    public static IReadOnlyList<ObservedHidEvent> UnexpectedEvents(
        IReadOnlyList<ObservedHidEvent> actual,
        IReadOnlyList<IReadOnlyList<ObservedHidEvent>> expectedGroups)
    {
        var remainingExpected = expectedGroups.SelectMany(group => group).ToList();
        var heldExpectedKeys = new HashSet<int>();
        var unexpected = new List<ObservedHidEvent>();
        foreach (var item in actual)
        {
            if (item.Kind == "key" && item.Edge == "down" && heldExpectedKeys.Contains(item.Code))
            {
                continue;
            }

            var index = remainingExpected.FindIndex(expected => SameEvent(item, expected));
            if (index < 0)
            {
                unexpected.Add(item);
                continue;
            }

            remainingExpected.RemoveAt(index);
            if (item.Kind == "key" && item.Edge == "down")
            {
                heldExpectedKeys.Add(item.Code);
            }
            else if (item.Kind == "key" && item.Edge == "up")
            {
                heldExpectedKeys.Remove(item.Code);
            }
        }
        return unexpected;
    }

    public static IReadOnlyList<ObservedHidEvent> AroundExpected(
        IReadOnlyList<ObservedHidEvent> actual,
        IReadOnlyList<IReadOnlyList<ObservedHidEvent>> expectedGroups,
        TimeSpan padding)
    {
        if (padding < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(padding));
        }

        var expected = expectedGroups.SelectMany(group => group).ToArray();
        if (expected.Length == 0)
        {
            return [];
        }

        // 待機中のキーボード操作を拾わないよう、最後に成立した期待列をaction本体とする。
        var matched = new ObservedHidEvent[expected.Length];
        var before = actual.Count;
        for (var expectedIndex = expected.Length - 1; expectedIndex >= 0; expectedIndex--)
        {
            var found = -1;
            for (var actualIndex = before - 1; actualIndex >= 0; actualIndex--)
            {
                if (SameEvent(actual[actualIndex], expected[expectedIndex]))
                {
                    found = actualIndex;
                    break;
                }
            }
            if (found < 0)
            {
                throw new InvalidOperationException("expected HID event列をaction観測から再構築できませんでした。");
            }
            matched[expectedIndex] = actual[found];
            before = found;
        }

        var paddingTicks = checked((long)Math.Ceiling(padding.TotalSeconds * Stopwatch.Frequency));
        var first = Math.Max(0, matched[0].StopwatchTicks - paddingTicks);
        var last = matched[^1].StopwatchTicks > long.MaxValue - paddingTicks
            ? long.MaxValue
            : matched[^1].StopwatchTicks + paddingTicks;
        return actual
            .Where(item => item.StopwatchTicks >= first && item.StopwatchTicks <= last)
            .ToArray();
    }

    public static IReadOnlyList<InputTraceEntry> ExpectedTrace(
        IReadOnlyList<InputTraceEntry> entries,
        IReadOnlyList<(string DeviceInstanceId, string ControlId, string LayerId)> expectedInputs) =>
        entries
            .Where(entry => expectedInputs.Any(expected =>
                entry.DeviceInstanceId == expected.DeviceInstanceId
                && entry.ControlId == expected.ControlId
                && entry.LayerId == expected.LayerId))
            .ToArray();

    private static bool SameEvent(ObservedHidEvent actual, ObservedHidEvent expected) =>
        actual.Kind == expected.Kind
        && actual.Edge == expected.Edge
        && actual.Code == expected.Code
        && actual.IsInjected == expected.IsInjected;
}
