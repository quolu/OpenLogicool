using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Desktop;
using OpenLogicool.Devices.G13;
using OpenLogicool.Devices.G600;
using OpenLogicool.Host;
using OpenLogicool.Persistence;
using OpenLogicool.Profiles;

// OpenLogicool Input Studio resident host（計画 §6.2 初期 process model の最小形）。
// command:
//   run [--db <path>] [--watchdog <path>] [--duration-ms N] [--trace]
//       profile を復元して fast path を常駐実行する。--duration-ms 省略時は Ctrl+C まで動く。
//       --trace は fast path が処理した input（device/control/edge/layer/output/emitted）を
//       1行1 event で表示する（test field・Journey A-6。既定 off・fast path 本体の挙動は変えない）。
//   import <documents.json> [--db <path>]
//       MappingProfileDocument の JSON 配列を store へ upsert する（UI 実装までの投入経路）。
//   ui [--db <path>] [--duration-ms N]
//       表示骨格（Phase 2 Exit 条件1・4の表示系）を read-only で開く。fast path は起動しない。
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
    _ => Fail("usage: OpenLogicool.Host [run [--db <path>] [--watchdog <path>] [--duration-ms N] [--trace] | import <documents.json> [--db <path>] | ui [--db <path>] [--duration-ms N] | associate <profileId> <appFullPath|default|package:familyName> [--db <path>] | apps [--db <path>] | workspace <workspace.json> [--db <path>] [--dry-run] | undo <workspaceId> [<revisionNumber>] [--db <path>] | export <workspaceId> <out.json> [--db <path>] | revisions <workspaceId> [--db <path>] | diagnostics [--db <path>] | onboarding [--db <path>]]"),
};

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

    using var host = new ResidentInputHost(databasePath, watchdogPath, traceEnabled);
    var status = host.Start();

    Console.WriteLine($"db: {databasePath}");
    Console.WriteLine($"profiles: [{string.Join(", ", status.LoadedProfileIds)}]");
    Console.WriteLine($"app associations: {status.AppAssociationCount}");
    Console.WriteLine($"g13 devices: {status.G13DeviceInstanceIds.Count}");
    Console.WriteLine($"g600 devices: {status.G600DeviceInstanceIds.Count}");
    Console.WriteLine($"wired devices: {status.WiredDeviceInstanceIds.Count}");
    foreach (var deviceInstanceId in status.WiredDeviceInstanceIds)
    {
        Console.WriteLine($"  wired: {deviceInstanceId}");
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
    while (!stopRequested.IsSet && DateTime.UtcNow < deadline && host.Pump.Failure is null)
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

    if (host.Pump.Failure is not null)
    {
        Console.Error.WriteLine($"fast path fault: {host.Pump.Failure}");
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
    int? durationMs = null;
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
            default:
                return Fail($"unknown ui option: {arguments[i]}");
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(connection);
    var profilesByKind = AppProfileResolver.Build(
        new SqliteMappingProfileStore(connection).ListAll(),
        new SqliteAppAssociationStore(connection).ListAll()).DefaultByKind;

    int g13Count;
    int g600Count;
    using (var g13Source = new G13RawInputSource())
    using (var g600Source = new G600RawInputSource())
    {
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

    var exitCode = 0;
    var thread = new Thread(() =>
    {
        var application = new System.Windows.Application();
        var window = new InputStudioWindow(report);
        if (durationMs is not null)
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs.Value),
            };
            timer.Tick += (_, _) => window.Close();
            timer.Start();
        }

        exitCode = application.Run(window);
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    return exitCode;
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
// 保存後の全体が解決可能かも transaction 前に検証する。
static long SaveCompilation(SqliteConnection connection, WorkspaceDocument document, WorkspaceCompilation compilation)
{
    var store = new SqliteMappingProfileStore(connection);
    var associationStore = new SqliteAppAssociationStore(connection);

    var compiledIds = compilation.Profiles.Select(profile => profile.ProfileId).ToHashSet(StringComparer.Ordinal);
    var prospective = store.ListAll()
        .Where(existing => !compiledIds.Contains(existing.ProfileId))
        .Concat(compilation.Profiles)
        .ToList();
    AppProfileResolver.Build(prospective, associationStore.ListAll());

    ExecuteSql(connection, "BEGIN IMMEDIATE;");
    try
    {
        var revisionNumber = new SqliteWorkspaceRevisionStore(connection)
            .Append(document, DateTime.UtcNow.ToString("o"));
        foreach (var profile in compilation.Profiles)
        {
            store.Upsert(profile);
        }

        ExecuteSql(connection, "COMMIT;");
        return revisionNumber;
    }
    catch
    {
        // SQLite 境界の失敗時に部分保存を残さない（原因はそのまま呼び出し元へ）
        ExecuteSql(connection, "ROLLBACK;");
        throw;
    }
}

static void ExecuteSql(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
}

static bool IsHostResident()
{
    if (Mutex.TryOpenExisting(SingleInstanceGuard.DefaultName, out var mutex))
    {
        mutex.Dispose();
        return true;
    }

    return false;
}

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

    var watchdogPath = Path.Combine(AppContext.BaseDirectory, "OpenLogicool.Watchdog.exe");
    Console.WriteLine($"watchdog: {watchdogPath}（{(File.Exists(watchdogPath) ? "存在" : "不在")}）");

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
