using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Devices.G13;
using OpenLogicool.Devices.G600;
using OpenLogicool.Domain;
using OpenLogicool.Host;
using OpenLogicool.Host.Research;
using OpenLogicool.Persistence;
using OpenLogicool.Profiles;
using OpenLogicool.Playbooks;

// OpenLogicool Input Studio resident host（計画 §6.2 初期 process model の最小形）。
// command:
//   run [--db <path>] [--watchdog <path>] [--duration-ms N] [--trace]
//       profile を復元して fast path を常駐実行する。--duration-ms 省略時は Ctrl+C まで動く。
//       --trace は fast path が処理した input（device/control/edge/layer/output/emitted）を
//       1行1 event で表示する（test field・Journey A-6。既定 off・fast path 本体の挙動は変えない）。
//   import <documents.json> [--db <path>]
//       MappingProfileDocument の JSON 配列を store へ upsert する（UI 実装までの投入経路）。
//   ui [--db <path>] [--duration-ms N] [--resident]
//       表示骨格（Phase 2 Exit 条件1・4の表示系）を開く。既定では fast path は起動しない。
//       --resident を付けると fast path＋watchdog を同居起動し、保存時に新規 down から即時反映する
//       （device write はしない＝MAP-010）。二重起動防止は run と同じ named mutex を使う。
//   associate <profileId> <app.exe の full path | default | package:<familyName>> [--db <path>]
//       foreground app→profile の関連付けを保存する（"default" はその device 種別の既定 profile 指定・
//       "package:<familyName>" は MSIX/Store app を package family name で識別する＝APP-004）。
//   apps [--db <path>]
//       Application Workspace 一覧（app→両 device の割当）と実行中 app 一覧を表示する。
//       関連付けの path は必ずこの実行中一覧から選ぶ（手打ち path は Store app redirect の罠）。
//   workspace <workspace.json> [--db <path>] [--dry-run]
//       Action-centric workspace 文書を compile し、警告（MAP-004）を表示してから
//       revision＋device 種別ごとの profile として単一 transaction で保存する（部分保存を作らない）。
//       --dry-run は表示だけで書き込まない。export した JSON をこの command へ渡すのが import 経路。
//   undo <workspaceId> [<revisionNumber>] [--db <path>]
//       過去 revision（無指定は最新の一つ前）を新 revision として再適用する（MAP-009・append-only）。
//   export <workspaceId> <out.json> [--db <path>]
//       最新 revision の workspace 文書を JSON へ書き出す（workspace command で再取込できる形式）。
//   revisions <workspaceId> [--db <path>]
//       保存済み revision の一覧を表示する。
//   diagnostics [--db <path>]
//       接続 device・保存 profile・app 関連付け・workspace revision・foreground identity・
//       watchdog exe 所在を read-only で一覧表示する（Journey B-6）。実機・DB が無くても exit 0。
//   onboarding [--db <path>]
//       初回導入の判断材料（Journey A の機能中核）を read-only で表示する——共存ソフト検出
//       （LGS/G HUB/Logi Options+ の実行中 process 検出のみ・断定しない）・device 接続件数
//       （片側/両側未接続を明示）・G600 完全 backup 導線の有無・設定の現在地（件数のみ）。
//       device への write は一切しない。
//   leftover <apply|restore|status> [--db <path>]
//       G600 出荷時 side 割当の残置無害化（B変種）。apply は F3 を中間 usage へ書いて残す。
//       restore は残置前 baseline へ戻す。status は現在の F3 / baseline / 共存ソフトを表示する。
//       run / ui --resident は配線時に apply、handled shutdown で restore する。
//       LGS / G HUB / Options+ 実行中は書かない。foreground 切替では書かない（MAP-010）。
//   onboard <apply <workspaceId>|restore|status> [--db <path>]
//       方式A（NIKKE 実測 2026-08-22 で採用確定・SendInput は anti-cheat に届かない）:
//       workspace の G600 割当を G600 本体の onboard F3 へ書く。ハードウェアとして送信されるため
//       合成入力を拒否するゲームでも効く。apply 前に書込み前 F3 を baseline として保存し、
//       restore で戻す。書込み中フラグが立っている間、常駐は G600 の SendInput 送出を抑止し
//       残置（leftover）の apply/restore もしない。LGS / G HUB / Options+ 実行中は書かない。
//       常駐実行中の CLI apply/restore は拒否する（アプリ内の「本体に書き込む」を使う）。
//   ui-test-scenario [--out <path>]
//       t10（Phase 3 Exit 条件5）: UI test scenario（アプリ選択→操作作成→両 device binding→保存→
//       適用状態表示）を fake（in-memory）と real（新規 temp SQLite・実 device 列挙）の両方の
//       IWorkspaceEditorIntents で実行し、結果を機械的に突き合わせる（実機接続台数だけを環境差として
//       除外）。JSON 証跡を probe-output へ書く。常駐 host が動いている間は実行しない（stage 表示の
//       hostResident 判定が fake 側と食い違うため）。不一致があれば exit code 1。
//   capture-dispatch <continuity|resume> [--capture available|stale|unavailable] [--recalibrate]
//       Game Operator の dispatch 境界を CLI から明示的に一回駆動する。外部入力はこの初期 CLI では
//       コンソールへの handoff 記録だけであり、OS input を合成しない。capture/read と resume の gate は
//       製品の CaptureContinuityDispatch をそのまま通す（FastPathPump は使わない）。

var command = args.Length > 0 ? args[0] : "run";

return command switch
{
    "run" => Run(args[1..]),
    "import" when args.Length >= 2 => Import(args[1], args[2..]),
    "ui" => Ui(args[1..]),
    "associate" when args.Length >= 3 => Associate(args[1], args[2], args[3..]),
    "apps" => Apps(args[1..]),
    "workspace" when args.Length >= 2 => Workspace(args[1], args[2..]),
    "undo" when args.Length >= 2 => Undo(args[1], args[2..]),
    "export" when args.Length >= 3 => Export(args[1], args[2], args[3..]),
    "revisions" when args.Length >= 2 => Revisions(args[1], args[2..]),
    "diagnostics" => Diagnostics(args[1..]),
    "onboarding" => Onboarding(args[1..]),
    "leftover" when args.Length >= 2 => Leftover(args[1], args[2..]),
    "onboard" when args.Length >= 2 => Onboard(args[1], args[2..]),
    "ui-test-scenario" => UiTestScenarioCommand(args[1..]),
    "capture-dispatch" when args.Length >= 2 => CaptureDispatch(args[1], args[2..]),
    _ => Fail("usage: OpenLogicool.Host [run [--db <path>] [--watchdog <path>] [--duration-ms N] [--trace] | import <documents.json> [--db <path>] | ui [--db <path>] [--duration-ms N] [--resident] | associate <profileId> <appFullPath|default|package:familyName> [--db <path>] | apps [--db <path>] | workspace <workspace.json> [--db <path>] [--dry-run] | undo <workspaceId> [<revisionNumber>] [--db <path>] | export <workspaceId> <out.json> [--db <path>] | revisions <workspaceId> [<revisionNumber>] [--db <path>] | diagnostics [--db <path>] | onboarding [--db <path>] | leftover <apply|restore|status> [--db <path>] | onboard <apply <workspaceId>|restore|status> [--db <path>] | ui-test-scenario [--out <path>] | capture-dispatch <continuity|resume> [--capture available|stale|unavailable] [--recalibrate]]"),
};

static int CaptureDispatch(string mode, string[] arguments)
{
    var capture = "available";
    var recalibrate = false;
    for (var index = 0; index < arguments.Length; index++)
    {
        switch (arguments[index])
        {
            case "--capture" when index + 1 < arguments.Length:
                capture = arguments[++index];
                break;
            case "--recalibrate":
                recalibrate = true;
                break;
            default:
                return Fail($"unknown capture-dispatch option: {arguments[index]}");
        }
    }

    if (mode is not ("continuity" or "resume"))
    {
        return Fail("capture-dispatch mode は continuity または resume です。");
    }

    var frame = new CapturedFrame(
        "0.2.0", "cli:dispatch", CaptureBackend.WindowsGraphicsCapture,
        1, 1_000, DateTimeOffset.UtcNow, 8, 8, "B8G8R8A8_UNorm", 96, 96, 1, 0, 100,
        Pixels: new FramePixels(new byte[] { 1, 2, 3, 4 }, 4));
    var read = capture switch
    {
        "available" => CaptureRead.Available(frame),
        "stale" => CaptureRead.Available(frame with { FreshnessMs = 101 }),
        "unavailable" => CaptureRead.Unavailable("CLI で capture frame は未到着です。"),
        _ => throw new ArgumentException($"capture-dispatch --capture '{capture}' は未対応です。"),
    };

    var journal = new RunJournal(new CliRunJournalStore(), new CliEngineeringLogSink());
    var gate = new AttemptDispatchGate(journal);
    var graph = PlaybookMaterializer.ToGraph(new PlaybookVersion(
        ContractSchemaVersions.Revision01,
        "cli-version",
        null,
        [new PlaybookNode(ContractSchemaVersions.Revision01, "entry", true, "state:entry", [], null, [])],
        [],
        "CLI capture dispatch"));
    var controls = new RunControls(journal, gate, "cli-run", PlaybookRun.Start("cli-playbook", graph));
    gate.CommitProposed(CliEvent(1, RunEventPayloadTypes.Proposal));
    gate.CommitAuthorized(CliEvent(2, RunEventPayloadTypes.Approval, RunEventActorType.User));
    gate.MarkPrepared("cli-attempt");
    controls.Pause();

    var continuity = new CaptureContinuityGate();
    var loop = new CaptureContinuityDispatchLoop(new CaptureContinuityDispatch(controls, continuity), continuity);
    var handedOff = false;
    Action externalInput = () =>
    {
        handedOff = true;
        Console.WriteLine("dispatch handoff: 承認済み。OS input はこの CLI では送出しません。");
    };

    var allowed = mode == "continuity"
        ? loop.TryStepOnce(read, staleAfterMs: 100, recalibrate ? frame : null, CliEvent(3, RunEventPayloadTypes.Dispatch), externalInput)
        : loop.TryResumeStepOnce(
            read,
            staleAfterMs: 100,
            recalibrate ? frame : null,
            new LiveResumeBinding("cli-host.exe", "cli:window", frame.SourceId, "cli-version", "cli-version"),
            new LiveResumeContext("cli-host.exe", "cli:window", frame.SourceId, "cli:window", CliObservation(frame)),
            [],
            "cli-state",
            freshnessBudgetMs: 100,
            stabilityWindowMs: 100,
            CliEvent(3, RunEventPayloadTypes.Dispatch),
            externalInput);

    Console.WriteLine($"capture dispatch: {(allowed ? "許可" : "停止")}（handoff: {(handedOff ? "あり" : "なし")}）");
    return allowed ? 0 : 2;
}

static ObservationResult CliObservation(CapturedFrame frame) => new(
    "0.2.0",
    "observation:cli",
    new CapturedFrameReference("0.2.0", frame.SourceId, frame.Backend, frame.Sequence, frame.MonotonicMs, frame.WallClockUtc, frame.TransformRevision, frame.FreshnessMs, frame.LastChangeMs),
    ObservationStatus.Known,
    [new StateCandidate("0.2.0", "cli-state", 1, [new EvidenceRegion("0.2.0", "rect", [0d, 0d, 1d, 1d], "cli")])],
    "cli", frame.FreshnessMs, null);

static RunEvent CliEvent(long sequence, string payloadType, RunEventActorType actor = RunEventActorType.Automation) => new(
    "0.1.0", $"cli-event-{sequence}", "cli-run", sequence, "cli-playbook", "cli-version", null,
    "cli-command", "cli-attempt", "cli-cause", $"cli-correlation-{sequence}", 1, actor,
    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, payloadType, "{}");

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static string DefaultDatabasePath() =>
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenLogicool",
        "input-studio.db");

static int Run(string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    var watchdogPath = Path.Combine(AppContext.BaseDirectory, "OpenLogicool.Watchdog.exe");
    int? durationMs = null;
    var traceEnabled = false;
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            case "--watchdog" when i + 1 < arguments.Length:
                watchdogPath = Path.GetFullPath(arguments[++i]);
                break;
            case "--duration-ms" when i + 1 < arguments.Length:
                durationMs = int.Parse(arguments[++i]);
                break;
            case "--trace":
                traceEnabled = true;
                break;
            default:
                return Fail($"unknown run option: {arguments[i]}");
        }
    }

    using var guard = new SingleInstanceGuard(SingleInstanceGuard.DefaultName);
    if (!guard.IsOwner)
    {
        return Fail("OpenLogicool.Host は既に起動しています（二重起動防止・計画 §6.2）。");
    }

    using var host = new ResidentInputHost(
        databasePath,
        watchdogPath,
        traceEnabled,
        G600LeftoverHostSupport.CreateSession(databasePath),
        G600OnboardModeStore.ForDatabase(databasePath),
        CreateOutputSessionFactory(databasePath, watchdogPath));
    var status = host.Start();

    Console.WriteLine($"db: {databasePath}");
    Console.WriteLine($"profiles: [{string.Join(", ", status.LoadedProfileIds)}]");
    Console.WriteLine($"app associations: {status.AppAssociationCount}");
    Console.WriteLine($"g13 devices: {status.G13DeviceInstanceIds.Count}");
    Console.WriteLine($"g600 devices: {status.G600DeviceInstanceIds.Count}");
    Console.WriteLine($"wired devices: {status.WiredDeviceInstanceIds.Count}");
    Console.WriteLine($"output: {host.OutputRoute}");
    Console.WriteLine($"g13 lcd: {(status.G13LcdStarted ? "started" : "not present")}");
    foreach (var deviceInstanceId in status.WiredDeviceInstanceIds)
    {
        Console.WriteLine($"  wired: {deviceInstanceId}");
    }

    if (status.LeftoverApply is { } leftover)
    {
        Console.WriteLine(G600LeftoverHostSupport.Describe(leftover));
    }

    Console.WriteLine(durationMs is null
        ? "resident: 実行中（Ctrl+C で handled shutdown）"
        : $"resident: 実行中（{durationMs} ms で自動終了）");

    using var stopRequested = new ManualResetEventSlim(false);
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        stopRequested.Set();
    };

    var deadline = durationMs is null ? DateTime.MaxValue : DateTime.UtcNow.AddMilliseconds(durationMs.Value);
    while (!stopRequested.IsSet && DateTime.UtcNow < deadline && host.Failure is null)
    {
        if (traceEnabled)
        {
            foreach (var traceEntry in host.Pump.DrainTrace())
            {
                var edgeLabel = traceEntry.Edge == PhysicalInputEdge.Down ? "down" : "up";
                var outputsLabel = traceEntry.OutputTokens.Count == 0
                    ? "(割当なし)"
                    : $"[{string.Join(", ", traceEntry.OutputTokens)}]";
                Console.WriteLine(
                    $"trace: {traceEntry.DeviceInstanceId} {traceEntry.ControlId} {edgeLabel} layer={traceEntry.LayerId} -> {outputsLabel} {(traceEntry.Emitted ? "emitted" : "no-op")}");
            }
        }

        Thread.Sleep(100);
    }

    if (host.Failure is not null)
    {
        Console.Error.WriteLine($"resident fault: {host.Failure}");
        host.Stop();
        return 2;
    }

    host.Stop();
    Console.WriteLine($"resident: 停止（処理 input {host.Pump.ProcessedCount} 件・handled shutdown 完了）");
    return 0;
}

static int Ui(string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    var watchdogPath = Path.Combine(AppContext.BaseDirectory, "OpenLogicool.Watchdog.exe");
    int? durationMs = null;
    var resident = false;
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            case "--duration-ms" when i + 1 < arguments.Length:
                durationMs = int.Parse(arguments[++i]);
                break;
            case "--resident":
                resident = true;
                break;
            default:
                return Fail($"unknown ui option: {arguments[i]}");
        }
    }

    // --resident は fast path＋watchdog を UI と同居させる（設計 t09 第4段残作業④）。
    // 既に別 process が常駐している場合は黙って二重化せず、明示エラーで止まる。
    SingleInstanceGuard? residentGuard = null;
    ResidentInputHost? residentHost = null;
    ResidentHostStatus? residentStatus = null;
    var outputSettingsStore = SerialHidOutputSettingsStore.ForDatabase(databasePath);
    var serialHidDiscovery = CreateSerialHidDiscovery();
    if (resident)
    {
        residentGuard = new SingleInstanceGuard(SingleInstanceGuard.DefaultName);
        if (!residentGuard.IsOwner)
        {
            residentGuard.Dispose();
            return Fail("OpenLogicool.Host は既に起動しています（ui --resident は二重起動できません・計画 §6.2）。");
        }

        residentHost = new ResidentInputHost(
            databasePath,
            watchdogPath,
            enableTrace: true,
            G600LeftoverHostSupport.CreateSession(databasePath),
            G600OnboardModeStore.ForDatabase(databasePath),
            ResidentOutputSessionFactory.Create(outputSettingsStore.Load(), watchdogPath, serialHidDiscovery));
        residentStatus = residentHost.Start();
    }

    using var residentGuardDisposable = residentGuard;
    using var residentHostDisposable = residentHost;

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

    var documents = new SqliteMappingProfileStore(connection).ListAll();
    var associations = new SqliteAppAssociationStore(connection).ListAll();
    var resolver = AppProfileResolver.Build(documents, associations);
    var profilesByKind = resolver.DefaultByKind;

    // raw input 登録はプロセス内で usage page ごとに最後の登録が勝つため、resident 同居時に
    // 表示用の source を新設・Dispose すると resident 側の受信登録が解除される。
    // resident が居る時は起動時の列挙結果を使い、この process で source を二重に作らない。
    int g13Count;
    int g600Count;
    if (residentStatus is not null)
    {
        g13Count = residentStatus.G13DeviceInstanceIds.Count;
        g600Count = residentStatus.G600DeviceInstanceIds.Count;
    }
    else
    {
        using var g13Source = new G13RawInputSource();
        using var g600Source = new G600RawInputSource();
        g13Count = g13Source.EnumerateDevices().Count;
        g600Count = g600Source.EnumerateDevices().Count;
    }

    DeviceDisplayInput DisplayInput(string kind, int count) =>
        profilesByKind.TryGetValue(kind, out var document)
            ? new DeviceDisplayInput(kind, count, document.ProfileId, MappingProfileMaterializer.ToProfile(document))
            : new DeviceDisplayInput(kind, count, null, null);

    var report = InputStudioReportBuilder.Build(
        DisplayInput("G13", g13Count),
        DisplayInput("G600", g600Count));

    // Workspace 新シェル向け snapshot（設計 §3.6）: Host が単発観測して組み立て、Desktop へは
    // pure な値だけを渡す（Desktop は I/O を持たない）。
    var workspaceRows = ApplicationWorkspaceCatalog.Build(resolver, associations);
    var running = RunningApplicationCatalog.ListVisibleApplications();
    var railEntries = BuildRailEntries(workspaceRows, associations, running);

    var identity = ForegroundAppTracker.GetForegroundIdentity();
    var matchKinds = profilesByKind.Keys
        .Select(kind => resolver.ResolveWithReason(kind, identity).MatchKind)
        .ToArray();
    var foregroundState = ForegroundStateClassifier.Classify(matchKinds);
    var foregroundStateLabel = ForegroundStateClassifier.Describe(foregroundState);
    var foregroundWindowTitle = identity is null
        ? null
        : running.FirstOrDefault(app =>
                AppProfileResolver.NormalizePath(app.FullPath) == identity.NormalizedFullPath ||
                (identity.PackageFamilyName is { } idPkg && app.PackageFamilyName is { } appPkg &&
                    AppProfileResolver.NormalizePackage(appPkg) == AppProfileResolver.NormalizePackage(idPkg)))
            ?.WindowTitle;

    // 段階4セルは WorkspaceApplyReport（Profiles）と同一語彙を Desktop の WorkspaceStageCell へ写す
    // だけで、この画面ではまだ何も保存していないため savedRevisionNumber は null（=下書き扱い）。
    var stages = WorkspaceApplyReport.Build(savedRevisionNumber: null, IsHostResident())
        .Select(stage => new WorkspaceStageCell(stage.Stage, stage.State, stage.Detail))
        .ToArray();

    var snapshot = new WorkspaceScreenSnapshot(
        foregroundStateLabel,
        foregroundWindowTitle,
        SelectedWorkspaceRevisionNumber: null,
        stages,
        g13Count,
        g600Count,
        railEntries);

    var editorIntents = new HostWorkspaceEditorIntents(connection);
    using var webResearchClient = new HttpClient();
    webResearchClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenLogicool/0.2 STEP0-WebResearch");
    var webResearchIntent = new HostWebResearchIntent(
        new SqliteWebReferenceStore(connection),
        new WebReferenceAcquisitionService(
            new HttpClientWebReferenceTransport(webResearchClient),
            new WebReferenceHtmlNormalizer()));

    IResidentApplyIntent? residentApply = null;
    if (residentHost is not null && residentStatus is not null)
    {
        var instanceIdsByKind = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["G13"] = residentStatus.G13DeviceInstanceIds,
            ["G600"] = residentStatus.G600DeviceInstanceIds,
        };
        residentApply = new HostResidentApplyIntent(residentHost, instanceIdsByKind);
    }

    var onboardIntent = new HostG600OnboardIntent(
        G600OnboardService.CreateDefault(databasePath),
        residentHost,
        residentHost is not null ? G600LeftoverHostSupport.CreateSession(databasePath) : null);
    var serialHidSettingsIntent = new HostSerialHidSettingsIntent(
        outputSettingsStore,
        serialHidDiscovery,
        () => residentHost?.OutputRoute);

    var exitCode = 0;
    var thread = new Thread(() =>
    {
        var application = new System.Windows.Application();
        var window = new InputStudioWindow(
            snapshot,
            report,
            AppProfileResolver.DefaultMarker,
            editorIntents,
            residentApply,
            onboardIntent,
            serialHidSettingsIntent,
            new HostG13LcdSettingsIntent(),
            webResearchIntent);
        System.Windows.Threading.DispatcherTimer? residentFailureTimer = null;
        if (residentHost is not null)
        {
            residentFailureTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100),
            };
            residentFailureTimer.Tick += (_, _) =>
            {
                if (residentHost.Failure is null)
                {
                    return;
                }

                Console.Error.WriteLine($"resident fault: {residentHost.Failure}");
                exitCode = 2;
                window.Close();
            };
            residentFailureTimer.Start();
        }

        if (durationMs is not null)
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs.Value),
            };
            timer.Tick += (_, _) => window.Close();
            timer.Start();
        }

        var applicationExitCode = application.Run(window);
        if (exitCode == 0)
        {
            exitCode = applicationExitCode;
        }
        residentFailureTimer?.Stop();
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    return exitCode;
}

static SerialHidDiscoveryService CreateSerialHidDiscovery() =>
    new(new SetupApiSerialCandidateEnumerator(), new SerialPortExchangeFactory());

static Func<IResidentOutputSession> CreateOutputSessionFactory(string databasePath, string watchdogPath) =>
    ResidentOutputSessionFactory.Create(
        SerialHidOutputSettingsStore.ForDatabase(databasePath).Load(),
        watchdogPath,
        CreateSerialHidDiscovery());

// ApplicationRail の行を組み立てる（設計 §2.1: ApplicationWorkspaceCatalog の行＋実行中一覧）。
// 関連付け済み path は既に workspaceRows に含まれるため、実行中一覧からは未関連付けの app だけを
// 追加で拾う（重複は正規化 path で判定する——Store app redirect の罠は既存 catalog と同じ扱い）。
static IReadOnlyList<ApplicationRailEntryInput> BuildRailEntries(
    IReadOnlyList<ApplicationWorkspaceRow> workspaceRows,
    IReadOnlyList<AppProfileAssociation> associations,
    IReadOnlyList<RunningApplication> running)
{
    var associatedPaths = associations
        .Where(association => association.MatcherKind != AppMatcherKind.Package && association.ApplicationFullPath != AppProfileResolver.DefaultMarker)
        .Select(association => AppProfileResolver.NormalizePath(association.ApplicationFullPath))
        .ToHashSet(StringComparer.Ordinal);
    var associatedPackages = associations
        .Where(association => association.MatcherKind == AppMatcherKind.Package)
        .Select(association => AppProfileResolver.NormalizePackage(association.ApplicationFullPath))
        .ToHashSet(StringComparer.Ordinal);
    var hasDefaultAssociation = associations.Any(association => association.ApplicationFullPath == AppProfileResolver.DefaultMarker);

    var runningByPath = new Dictionary<string, RunningApplication>(StringComparer.Ordinal);
    foreach (var app in running)
    {
        runningByPath.TryAdd(AppProfileResolver.NormalizePath(app.FullPath), app);
    }

    bool IsAssociated(string normalizedPath, string? packageFamilyName) =>
        associatedPaths.Contains(normalizedPath) ||
        (packageFamilyName is { } packageValue && associatedPackages.Contains(AppProfileResolver.NormalizePackage(packageValue)));

    var entries = new List<ApplicationRailEntryInput>();
    var seenPaths = new HashSet<string>(StringComparer.Ordinal);

    foreach (var row in workspaceRows)
    {
        var isDefault = row.ApplicationFullPath == AppProfileResolver.DefaultMarker;
        if (isDefault)
        {
            entries.Add(new ApplicationRailEntryInput(row.ApplicationFullPath, "共通設定（どのアプリでもない時）", IsRunning: false, hasDefaultAssociation));
            continue;
        }

        var isRunning = runningByPath.TryGetValue(row.ApplicationFullPath, out var runningApp);
        var displayName = isRunning ? runningApp!.FullPath : row.ApplicationFullPath;
        var isAssociated = IsAssociated(row.ApplicationFullPath, runningApp?.PackageFamilyName);
        entries.Add(new ApplicationRailEntryInput(row.ApplicationFullPath, displayName, isRunning, isAssociated));
        seenPaths.Add(row.ApplicationFullPath);
    }

    foreach (var app in running)
    {
        var normalizedPath = AppProfileResolver.NormalizePath(app.FullPath);
        if (!seenPaths.Add(normalizedPath))
        {
            continue;
        }

        entries.Add(new ApplicationRailEntryInput(
            normalizedPath, app.FullPath, IsRunning: true, IsAssociated(normalizedPath, app.PackageFamilyName)));
    }

    return entries;
}

static int Associate(string profileId, string appPathOrDefault, string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            default:
                return Fail($"unknown associate option: {arguments[i]}");
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

    var document = new SqliteMappingProfileStore(connection).ListAll()
        .SingleOrDefault(candidate => candidate.ProfileId == profileId);
    if (document is null)
    {
        return Fail($"profile '{profileId}' は保存されていません。先に import してください。");
    }

    string applicationFullPath;
    string matcherKind;
    const string packagePrefix = "package:";
    if (appPathOrDefault is "default" or AppProfileResolver.DefaultMarker)
    {
        applicationFullPath = AppProfileResolver.DefaultMarker;
        matcherKind = AppMatcherKind.Path;
    }
    else if (appPathOrDefault.StartsWith(packagePrefix, StringComparison.Ordinal))
    {
        applicationFullPath = AppProfileResolver.NormalizePackage(appPathOrDefault[packagePrefix.Length..]);
        matcherKind = AppMatcherKind.Package;
    }
    else
    {
        applicationFullPath = AppProfileResolver.NormalizePath(Path.GetFullPath(appPathOrDefault));
        matcherKind = AppMatcherKind.Path;
    }

    var association = new AppProfileAssociation(
        ContractSchemaVersions.Revision01, applicationFullPath, document.DeviceKind, profileId, matcherKind);
    new SqliteAppAssociationStore(connection).Upsert(association);

    // 保存後の全体が解決可能かをその場で検証する（既定の欠落等は保存時点で顕在化させる）
    AppProfileResolver.Build(
        new SqliteMappingProfileStore(connection).ListAll(),
        new SqliteAppAssociationStore(connection).ListAll());

    Console.WriteLine(applicationFullPath == AppProfileResolver.DefaultMarker
        ? $"associated: 既定（{document.DeviceKind}）-> '{profileId}'"
        : matcherKind == AppMatcherKind.Package
            ? $"associated: package:{applicationFullPath}（{document.DeviceKind}）-> '{profileId}'"
            : $"associated: {applicationFullPath}（{document.DeviceKind}）-> '{profileId}'");
    return 0;
}

static int Apps(string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            default:
                return Fail($"unknown apps option: {arguments[i]}");
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

    var documents = new SqliteMappingProfileStore(connection).ListAll();
    var associations = new SqliteAppAssociationStore(connection).ListAll();
    var resolver = AppProfileResolver.Build(documents, associations);
    var workspaces = ApplicationWorkspaceCatalog.Build(resolver, associations);

    Console.WriteLine($"workspaces: {workspaces.Count}");
    foreach (var row in workspaces)
    {
        var name = row.ApplicationFullPath == AppProfileResolver.DefaultMarker ? "既定" : row.ApplicationFullPath;
        var assignments = row.ProfileIdByKind.Count == 0
            ? "（割当なし）"
            : string.Join(" ", row.ProfileIdByKind.Select(pair => $"{pair.Key}='{pair.Value}'"));
        Console.WriteLine($"  {name}: {assignments}");
    }

    // [assoc] 判定は path・package 両 matcher を考慮する（package matcher の値は path 列とは別名前空間）
    var associatedPaths = associations
        .Where(association => association.MatcherKind != AppMatcherKind.Package && association.ApplicationFullPath != AppProfileResolver.DefaultMarker)
        .Select(association => AppProfileResolver.NormalizePath(association.ApplicationFullPath))
        .ToHashSet(StringComparer.Ordinal);
    var associatedPackages = associations
        .Where(association => association.MatcherKind == AppMatcherKind.Package)
        .Select(association => AppProfileResolver.NormalizePackage(association.ApplicationFullPath))
        .ToHashSet(StringComparer.Ordinal);

    var running = RunningApplicationCatalog.ListVisibleApplications();
    Console.WriteLine($"running applications: {running.Count}");
    foreach (var app in running)
    {
        var isAssociated = associatedPaths.Contains(AppProfileResolver.NormalizePath(app.FullPath)) ||
            (app.PackageFamilyName is { } packageFamilyName && associatedPackages.Contains(AppProfileResolver.NormalizePackage(packageFamilyName)));
        var marker = isAssociated ? "[assoc]" : "[     ]";
        var packageSuffix = app.PackageFamilyName is null ? string.Empty : $" [pkg:{app.PackageFamilyName}]";
        Console.WriteLine($"  {marker} {app.FullPath}{packageSuffix} — \"{app.WindowTitle}\"");
    }

    return 0;
}

static int Workspace(string workspaceJsonPath, string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    var dryRun = false;
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            case "--dry-run":
                dryRun = true;
                break;
            default:
                return Fail($"unknown workspace option: {arguments[i]}");
        }
    }

    var document = JsonSerializer.Deserialize<WorkspaceDocument>(File.ReadAllText(workspaceJsonPath))
        ?? throw new InvalidOperationException($"'{workspaceJsonPath}' の JSON が null です。");

    WorkspaceCompilation compilation;
    try
    {
        compilation = WorkspaceCompiler.Compile(document);
    }
    catch (ArgumentException error)
    {
        return Fail($"workspace '{document.WorkspaceId}' は適用できません: {error.Message}");
    }

    Console.WriteLine($"workspace: {document.WorkspaceId}（action {document.Actions.Count} 件・binding {document.Bindings.Count} 件）");
    foreach (var profile in compilation.Profiles)
    {
        Console.WriteLine($"  compile: {profile.ProfileId}（{profile.DeviceKind}・binding {profile.Bindings.Count} 件）");
    }

    foreach (var warning in compilation.Warnings)
    {
        Console.WriteLine($"  警告: {warning}");
    }

    if (dryRun)
    {
        PrintStageReport(WorkspaceApplyReport.Build(savedRevisionNumber: null, IsHostResident()));
        return 0;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

    long revisionNumber;
    try
    {
        revisionNumber = SaveCompilation(connection, document, compilation);
    }
    catch (InvalidOperationException error)
    {
        return Fail($"workspace '{document.WorkspaceId}' を保存すると解決不能になります: {error.Message}");
    }

    foreach (var profile in compilation.Profiles)
    {
        Console.WriteLine($"saved: {profile.ProfileId}");
    }

    PrintStageReport(WorkspaceApplyReport.Build(revisionNumber, IsHostResident()));
    return 0;
}

// revision 追記と profile upsert を単一 transaction で行う（APP-007: G13 保存成功／G600 失敗の部分保存を作らない）。
// 実体は WorkspaceRevisionSaver（UI の Binding editor 保存/undo intent と共有——保存規則を二重化しない）。
static long SaveCompilation(SqliteConnection connection, WorkspaceDocument document, WorkspaceCompilation compilation) =>
    WorkspaceRevisionSaver.SaveCompilation(connection, document, compilation);

static bool IsHostResident() => WorkspaceRevisionSaver.IsHostResident();

static void PrintStageReport(IReadOnlyList<WorkspaceStageStatus> stages)
{
    foreach (var stage in stages)
    {
        Console.WriteLine($"  {stage.Stage}: {stage.State} — {stage.Detail}");
    }
}

static int Undo(string workspaceId, string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    long? requestedRevisionNumber = null;
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            case var value when long.TryParse(value, out var number):
                requestedRevisionNumber = number;
                break;
            default:
                return Fail($"unknown undo option: {arguments[i]}");
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

    var revisions = new SqliteWorkspaceRevisionStore(connection).ListRevisions(workspaceId);
    WorkspaceRevisionRecord target;
    try
    {
        target = WorkspaceUndo.SelectTarget(revisions, requestedRevisionNumber);
    }
    catch (InvalidOperationException error)
    {
        return Fail($"workspace '{workspaceId}' の undo: {error.Message}");
    }

    var compilation = WorkspaceCompiler.Compile(target.Document);
    foreach (var warning in compilation.Warnings)
    {
        Console.WriteLine($"  警告: {warning}");
    }

    long revisionNumber;
    try
    {
        revisionNumber = SaveCompilation(connection, target.Document, compilation);
    }
    catch (InvalidOperationException error)
    {
        return Fail($"workspace '{workspaceId}' の undo を保存すると解決不能になります: {error.Message}");
    }

    Console.WriteLine($"undo: revision {target.RevisionNumber} の内容を revision {revisionNumber} として再適用");
    PrintStageReport(WorkspaceApplyReport.Build(revisionNumber, IsHostResident()));
    return 0;
}

static int Export(string workspaceId, string outputJsonPath, string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            default:
                return Fail($"unknown export option: {arguments[i]}");
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

    var revisions = new SqliteWorkspaceRevisionStore(connection).ListRevisions(workspaceId);
    if (revisions.Count == 0)
    {
        return Fail($"workspace '{workspaceId}' に保存済み revision がありません。");
    }

    var latest = revisions[^1];
    File.WriteAllText(
        outputJsonPath,
        JsonSerializer.Serialize(latest.Document, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"exported: {workspaceId} revision {latest.RevisionNumber} -> {Path.GetFullPath(outputJsonPath)}");
    Console.WriteLine("import は `workspace <このJSON>` で行う（同一フォーマット）");
    return 0;
}

static int Revisions(string workspaceId, string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            default:
                return Fail($"unknown revisions option: {arguments[i]}");
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

    var revisions = new SqliteWorkspaceRevisionStore(connection).ListRevisions(workspaceId);
    Console.WriteLine($"workspace '{workspaceId}' revisions: {revisions.Count}");
    foreach (var revision in revisions)
    {
        Console.WriteLine(
            $"  revision {revision.RevisionNumber} ({revision.SavedAtUtc}): action {revision.Document.Actions.Count} 件・binding {revision.Document.Bindings.Count} 件");
    }

    return 0;
}

// diagnostics（Journey B-6）: 接続 device・保存 profile・app 関連付け・workspace revision・
// foreground identity・watchdog exe 所在を read-only で一覧表示する。書き込みは migrate だけ
// （既存 command と同じ前提）。実機・DB が空でも 0 件表示のまま exit 0 で終える。
static int Diagnostics(string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            default:
                return Fail($"unknown diagnostics option: {arguments[i]}");
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

    Console.WriteLine($"db: {databasePath}");

    IReadOnlyList<string> g13Ids;
    IReadOnlyList<string> g600Ids;
    using (var g13Source = new G13RawInputSource())
    using (var g600Source = new G600RawInputSource())
    {
        g13Ids = g13Source.EnumerateDevices().Select(device => device.DeviceInstanceId).ToArray();
        g600Ids = g600Source.EnumerateDevices().Select(device => device.DeviceInstanceId).ToArray();
    }

    Console.WriteLine($"devices: g13={g13Ids.Count} g600={g600Ids.Count}");
    foreach (var id in g13Ids)
    {
        Console.WriteLine($"  g13: {id}");
    }

    foreach (var id in g600Ids)
    {
        Console.WriteLine($"  g600: {id}");
    }

    var documents = new SqliteMappingProfileStore(connection).ListAll();
    Console.WriteLine($"profiles: {documents.Count}");
    foreach (var document in documents)
    {
        var profile = MappingProfileMaterializer.ToProfile(document);
        Console.WriteLine(
            $"  {document.ProfileId}（{document.DeviceKind}・revision={profile.MappingRevision}・binding {profile.Bindings.Count} 件）");
    }

    var associations = new SqliteAppAssociationStore(connection).ListAll();
    Console.WriteLine($"app associations: {associations.Count}");
    foreach (var association in associations)
    {
        var target = association.ApplicationFullPath == AppProfileResolver.DefaultMarker
            ? "既定"
            : association.ApplicationFullPath;
        Console.WriteLine($"  {association.MatcherKind}:{target}（{association.DeviceKind}）-> '{association.ProfileId}'");
    }

    using (var command = connection.CreateCommand())
    {
        command.CommandText =
            "SELECT workspace_id, COUNT(*), MAX(revision_number) FROM workspace_revisions GROUP BY workspace_id ORDER BY workspace_id;";
        using var reader = command.ExecuteReader();
        var rows = new List<(string WorkspaceId, long Count, long Latest)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        }

        Console.WriteLine($"workspaces: {rows.Count}");
        foreach (var row in rows)
        {
            Console.WriteLine($"  {row.WorkspaceId}: revision {row.Count} 件（最新 {row.Latest}）");
        }
    }

    var identity = ForegroundAppTracker.GetForegroundIdentity();
    Console.WriteLine(identity is null
        ? "foreground: 取得不能"
        : $"foreground: {identity.NormalizedFullPath ?? "取得不能"}" +
            $"{(identity.PackageFamilyName is { } pkg ? $" [pkg:{pkg}]" : string.Empty)}");

    // foreground state（APP-008）: この process 自身が持つ resolver から、常駐 host の poll thread と
    // 同じ pure 導出（ForegroundStateClassifier）で表示する。関連付け 0 件の DB では全 device 種別が
    // "default" 一致になり KnownDefault のまま——それも正直に表示する（黙って隠さない）。
    var diagnosticsResolver = AppProfileResolver.Build(documents, associations);
    var matchKinds = diagnosticsResolver.DefaultByKind.Keys
        .Select(kind => diagnosticsResolver.ResolveWithReason(kind, identity).MatchKind)
        .ToArray();
    var foregroundState = ForegroundStateClassifier.Classify(matchKinds);
    Console.WriteLine($"foreground state: {ForegroundStateClassifier.Describe(foregroundState)}");

    var watchdogPath = Path.Combine(AppContext.BaseDirectory, "OpenLogicool.Watchdog.exe");
    Console.WriteLine($"watchdog: {watchdogPath}（{(File.Exists(watchdogPath) ? "存在" : "不在")}）");

    // 最近の切替判断（診断可能化・APP-005）: decision ring は resident host の process 内メモリだけに
    // 存在し永続化しない。diagnostics は別 process 起動なので、この process 自身が resident host を
    // 起動していない限り参照できない（この command は resident host を起動しない）。
    Console.WriteLine("最近の切替判断:");
    Console.WriteLine(IsHostResident()
        ? "  常駐 host あり——切替判断は別 process 内の ring のため diagnostics からは参照できません（run 実行中の profile switch log を確認してください）。"
        : "  常駐 host なし——切替判断は run 実行中に記録されます。");

    return 0;
}

// onboarding（Journey A の機能中核）: 初回導入の判断材料を read-only で表示する。
// device への write は一切しない（共存ソフト検出・backup 導線・設定の現在地の案内だけ）。
static int Onboarding(string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            default:
                return Fail($"unknown onboarding option: {arguments[i]}");
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

    int g13Count;
    int g600Count;
    using (var g13Source = new G13RawInputSource())
    using (var g600Source = new G600RawInputSource())
    {
        g13Count = g13Source.EnumerateDevices().Count;
        g600Count = g600Source.EnumerateDevices().Count;
    }

    var profileCount = new SqliteMappingProfileStore(connection).ListAll().Count;
    var associationCount = new SqliteAppAssociationStore(connection).ListAll().Count;

    int workspaceCount;
    using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT COUNT(DISTINCT workspace_id) FROM workspace_revisions;";
        workspaceCount = Convert.ToInt32(command.ExecuteScalar());
    }

    var observations = new OnboardingObservations(
        OnboardingObservationCollector.DetectCoexistingSoftware(),
        g13Count,
        g600Count,
        OnboardingObservationCollector.CollectBackupStatus(),
        profileCount,
        associationCount,
        workspaceCount);

    foreach (var line in OnboardingReport.Build(observations))
    {
        Console.WriteLine(line);
    }

    return 0;
}

static int Leftover(string action, string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            default:
                return Fail($"unknown leftover option: {arguments[i]}");
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    var session = G600LeftoverHostSupport.CreateSession(databasePath);

    switch (action)
    {
        case "apply":
        {
            var result = session.Apply(managed: true);
            Console.WriteLine(G600LeftoverHostSupport.Describe(result));
            return result.IsHardFailure ? 2 : 0;
        }
        case "restore":
        {
            var result = session.Restore();
            Console.WriteLine(G600LeftoverHostSupport.Describe(result));
            return result.IsHardFailure ? 2 : 0;
        }
        case "status":
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
            byte[]? baseline = null;
            try
            {
                baseline = new FileG600OnboardBaselineStore(directory).LoadF3();
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine($"baseline: 壊れている（{ex.Message}）");
                return 2;
            }

            var current = G600EvidenceWrite.TryRead(
                new G600FeatureHidAccess(),
                G600EvidenceWrite.ProfileReportIdF3,
                Thread.Sleep,
                settleMs: 0);
            Console.WriteLine($"共存ソフト: {(G600LeftoverHostSupport.IsCoexistenceRunning() ? "検出" : "非検出")}");
            Console.WriteLine(current is null
                ? "F3: 読めない（未接続または feature collection を開けない）"
                : $"F3: {(G600SideRemap.IsApplied(current) ? "残置済み（中間 usage）" : "出荷割当のまま")}");
            Console.WriteLine(baseline is null ? "baseline: なし" : "baseline: あり（154-byte F3）");
            return 0;
        }
        default:
            return Fail("usage: leftover <apply|restore|status> [--db <path>]");
    }
}

// 方式A: workspace の G600 割当を onboard F3 へ焼く／戻す／状態表示。
// 常駐実行中は apply/restore を拒否する（常駐側の送出抑止と同期できないため。UI のボタンが常駐内経路）。
static int Onboard(string action, string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    string? workspaceId = null;
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            default:
                if (workspaceId is null && !arguments[i].StartsWith("--", StringComparison.Ordinal))
                {
                    workspaceId = arguments[i];
                    break;
                }

                return Fail($"unknown onboard option: {arguments[i]}");
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    var service = G600OnboardService.CreateDefault(databasePath);

    if (action is "apply" or "restore" && Mutex.TryOpenExisting(SingleInstanceGuard.DefaultName, out var residentMutex))
    {
        residentMutex.Dispose();
        return Fail("常駐（Input Studio）実行中は onboard を CLI から書けません。アプリ内の「本体に書き込む」を使うか、終了してから実行してください。");
    }

    switch (action)
    {
        case "apply" when workspaceId is not null:
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
            var profileId = $"{workspaceId}-G600";
            var document = new SqliteMappingProfileStore(connection).ListAll()
                .FirstOrDefault(candidate => candidate.ProfileId == profileId);
            if (document is null)
            {
                return Fail($"workspace '{workspaceId}' の G600 割当（{profileId}）が見つかりません。");
            }

            var result = service.Apply(workspaceId, document);
            Console.WriteLine(result.Message);
            return result.Success ? 0 : 2;
        }
        case "restore":
        {
            var result = service.Restore();
            Console.WriteLine(result.Message);
            return result.Success ? 0 : 2;
        }
        case "status":
        {
            var mode = service.CurrentMode();
            Console.WriteLine(mode is null
                ? "onboard: 書込みなし"
                : $"onboard: 書込み中（workspace '{mode.WorkspaceId}'・{mode.AppliedAtUtc:yyyy-MM-dd HH:mm:ss} UTC）");
            Console.WriteLine($"共存ソフト: {(G600LeftoverHostSupport.IsCoexistenceRunning() ? "検出" : "非検出")}");
            var slot = service.ReadActiveSlot();
            Console.WriteLine(slot is null
                ? "使用面: 読めない"
                : $"使用面: {slot}{(slot == 0 ? "（書込み対象）" : "（書込み対象は 0——apply が切り替える）")}");
            var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
            var baseline = new FileG600OnboardBaselineStore(directory).LoadF3();
            Console.WriteLine(baseline is null ? "復元元: なし" : "復元元: あり（書込み前の F3）");
            var current = G600EvidenceWrite.TryRead(
                new G600FeatureHidAccess(), G600EvidenceWrite.ProfileReportIdF3, Thread.Sleep, settleMs: 0);
            Console.WriteLine(current is null
                ? "F3: 読めない（未接続または feature collection を開けない）"
                : baseline is not null && current.SequenceEqual(baseline)
                    ? "F3: 復元元と一致（素の状態）"
                    : "F3: 復元元と不一致（書込み済みまたは残置済み）");
            return 0;
        }
        default:
            return Fail("usage: onboard <apply <workspaceId>|restore|status> [--db <path>]");
    }
}

static int Import(string documentsJsonPath, string[] arguments)
{
    var databasePath = DefaultDatabasePath();
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--db" when i + 1 < arguments.Length:
                databasePath = Path.GetFullPath(arguments[++i]);
                break;
            default:
                return Fail($"unknown import option: {arguments[i]}");
        }
    }

    var documents = JsonSerializer.Deserialize<List<MappingProfileDocument>>(File.ReadAllText(documentsJsonPath))
        ?? throw new InvalidOperationException($"'{documentsJsonPath}' の JSON が null です。");

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);

    var store = new SqliteMappingProfileStore(connection);
    foreach (var document in documents)
    {
        store.Upsert(document);
        Console.WriteLine($"imported: {document.ProfileId} ({document.DeviceKind})");
    }

    return 0;
}

// t10（Phase 3 Exit 条件5）: UI test scenario の fake/real contract 一致検証。
// fake（FakeWorkspaceEditorIntents・in-memory）と real（新規 temp SQLite・実 device 列挙）の
// 両方で同一 UiTestScenario を実行し、UiTestScenarioComparer で機械突き合わせる。
// 常駐 host が動いていると real 側だけ WorkspaceRevisionSaver.IsHostResident()=true になり、
// 段階セル表示（hostResident 分岐）が fake 側と食い違う——これは環境差ではなく前提違反のため、
// 丸めずに検出して止める。
static int UiTestScenarioCommand(string[] arguments)
{
    string? outputPath = null;
    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--out" when i + 1 < arguments.Length:
                outputPath = Path.GetFullPath(arguments[++i]);
                break;
            default:
                return Fail($"unknown ui-test-scenario option: {arguments[i]}");
        }
    }

    if (IsHostResident())
    {
        return Fail("ui-test-scenario は常駐 host が動いていない状態で実行してください（run/ui --resident を先に停止）。");
    }

    var fakeResult = OpenLogicool.Desktop.UiTestScenario.Run(new FakeWorkspaceEditorIntents(), g13ConnectedCount: 1, g600ConnectedCount: 1);

    int realG13Count;
    int realG600Count;
    using (var g13Source = new G13RawInputSource())
    using (var g600Source = new G600RawInputSource())
    {
        realG13Count = g13Source.EnumerateDevices().Count;
        realG600Count = g600Source.EnumerateDevices().Count;
    }

    // real 側は毎回まっさらな temp SQLite を使う（fake の in-memory 側も毎回まっさらなので、
    // revision 番号のような「保存済み状態に依存する」field まで実測で一致させる——丸めない）。
    var tempDatabasePath = Path.Combine(Path.GetTempPath(), $"openlogicool-t10-{Guid.NewGuid():N}.db");
    UiTestScenarioResult realResult;
    try
    {
        using var connection = new SqliteConnection($"Data Source={tempDatabasePath}");
        connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
        var realIntents = new HostWorkspaceEditorIntents(connection);
        realResult = OpenLogicool.Desktop.UiTestScenario.Run(realIntents, realG13Count, realG600Count);
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(tempDatabasePath))
        {
            File.Delete(tempDatabasePath);
        }
    }

    var comparison = UiTestScenarioComparer.Compare(fakeResult, realResult);

    Console.WriteLine($"fake vs real 一致: {(comparison.IsMatch ? "一致" : "不一致")}");
    Console.WriteLine($"除外項目: {string.Join(", ", comparison.ExcludedFields)}");
    foreach (var mismatch in comparison.Mismatches)
    {
        Console.WriteLine($"  不一致: {mismatch}");
    }

    var evidence = new
    {
        SchemaVersion = "t10-ui-test-scenario-v1",
        TimestampUtc = DateTime.UtcNow.ToString("o"),
        IsMatch = comparison.IsMatch,
        ExcludedFields = comparison.ExcludedFields,
        Mismatches = comparison.Mismatches,
        RealDeviceCounts = new { G13 = realG13Count, G600 = realG600Count },
        Fake = fakeResult,
        Real = realResult,
    };

    var probeOutputDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "probe-output"));
    Directory.CreateDirectory(probeOutputDirectory);
    var path = outputPath ?? Path.Combine(
        probeOutputDirectory, $"ui-test-scenario-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
    File.WriteAllText(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"evidence: {path}");

    return comparison.IsMatch ? 0 : 1;
}

sealed class CliRunJournalStore : IRunJournalStore
{
    private readonly List<RunEvent> events = [];

    public void Append(RunEvent runEvent) => events.Add(runEvent);
    public IReadOnlyList<RunEvent> ReadRun(string runId) => events.Where(item => item.RunId == runId).ToArray();
    public IReadOnlyList<string> ListRunIds() => events.Select(item => item.RunId).Distinct().ToArray();
    public IReadOnlyList<ExpiredRunPreview> PreviewExpiredRuns(DateTimeOffset asOfUtc, int retentionDays) => [];
    public void DeleteRun(string runId) => events.RemoveAll(item => item.RunId == runId);
}

sealed class CliEngineeringLogSink : IEngineeringLogSink
{
    public void Record(EngineeringLogEntry entry)
    {
    }
}
