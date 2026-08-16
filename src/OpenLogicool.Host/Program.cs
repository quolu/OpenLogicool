using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
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
//   run [--db <path>] [--watchdog <path>] [--duration-ms N]
//       profile を復元して fast path を常駐実行する。--duration-ms 省略時は Ctrl+C まで動く。
//   import <documents.json> [--db <path>]
//       MappingProfileDocument の JSON 配列を store へ upsert する（UI 実装までの投入経路）。
//   ui [--db <path>] [--duration-ms N]
//       表示骨格（Phase 2 Exit 条件1・4の表示系）を read-only で開く。fast path は起動しない。
//   associate <profileId> <app.exe の full path | default> [--db <path>]
//       foreground app→profile の関連付けを保存する（"default" はその device 種別の既定 profile 指定）。

var command = args.Length > 0 ? args[0] : "run";

return command switch
{
    "run" => Run(args[1..]),
    "import" when args.Length >= 2 => Import(args[1], args[2..]),
    "ui" => Ui(args[1..]),
    "associate" when args.Length >= 3 => Associate(args[1], args[2], args[3..]),
    _ => Fail("usage: OpenLogicool.Host [run [--db <path>] [--watchdog <path>] [--duration-ms N] | import <documents.json> [--db <path>] | ui [--db <path>] [--duration-ms N] | associate <profileId> <appFullPath|default> [--db <path>]]"),
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
            default:
                return Fail($"unknown run option: {arguments[i]}");
        }
    }

    using var guard = new SingleInstanceGuard(SingleInstanceGuard.DefaultName);
    if (!guard.IsOwner)
    {
        return Fail("OpenLogicool.Host は既に起動しています（二重起動防止・計画 §6.2）。");
    }

    using var host = new ResidentInputHost(databasePath, watchdogPath);
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

    var applicationFullPath = appPathOrDefault is "default" or AppProfileResolver.DefaultMarker
        ? AppProfileResolver.DefaultMarker
        : AppProfileResolver.NormalizePath(Path.GetFullPath(appPathOrDefault));

    var association = new AppProfileAssociation(
        ContractSchemaVersions.Revision01, applicationFullPath, document.DeviceKind, profileId);
    new SqliteAppAssociationStore(connection).Upsert(association);

    // 保存後の全体が解決可能かをその場で検証する（既定の欠落等は保存時点で顕在化させる）
    AppProfileResolver.Build(
        new SqliteMappingProfileStore(connection).ListAll(),
        new SqliteAppAssociationStore(connection).ListAll());

    Console.WriteLine(applicationFullPath == AppProfileResolver.DefaultMarker
        ? $"associated: 既定（{document.DeviceKind}）-> '{profileId}'"
        : $"associated: {applicationFullPath}（{document.DeviceKind}）-> '{profileId}'");
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
