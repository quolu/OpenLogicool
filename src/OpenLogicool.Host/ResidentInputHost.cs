using System.IO;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Devices.G13;
using OpenLogicool.Domain;
using OpenLogicool.Devices.G600;
using OpenLogicool.Input;
using OpenLogicool.Persistence;
using OpenLogicool.Profiles;

namespace OpenLogicool.Host;

/// <summary>resident host の起動結果（何が復元され、何が配線されたか）。</summary>
public sealed record ResidentHostStatus(
    IReadOnlyList<string> LoadedProfileIds,
    IReadOnlyList<string> G13DeviceInstanceIds,
    IReadOnlyList<string> G600DeviceInstanceIds,
    IReadOnlyList<string> WiredDeviceInstanceIds,
    int AppAssociationCount);

/// <summary>
/// Input Studio の resident 実行体（計画 §6.2 の初期 process model）。
/// SQLite から mapping profile を復元し、実機 G13/G600 を列挙して fast path
/// （Device Input → Mapping Runtime → GuardedOutputEmitter＋watchdog）を起動する。
/// UI・AI・capture は含まない。profile が無い device 種別は配線しない（黙って既定値を作らない）。
/// </summary>
public sealed class ResidentInputHost : IDisposable
{
    private readonly string _databasePath;
    private readonly string _watchdogExePath;
    private SqliteConnection? _connection;
    private G13RawInputSource? _g13Source;
    private G600RawInputSource? _g600Source;
    private WatchdogChannel? _watchdog;
    private FastPathPump? _pump;
    private Thread? _foregroundPollThread;
    private volatile bool _foregroundPollStop;
    private bool _stopped;

    public ResidentInputHost(string databasePath, string watchdogExePath)
    {
        _databasePath = databasePath;
        _watchdogExePath = watchdogExePath;
    }

    public FastPathPump Pump =>
        _pump ?? throw new InvalidOperationException("resident host は未起動です。");

    public ResidentHostStatus Start()
    {
        if (_pump is not null || _stopped)
        {
            throw new InvalidOperationException("resident host は一度だけ起動できます。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _connection = new SqliteConnection($"Data Source={_databasePath}");
        _connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(_connection);

        var documents = new SqliteMappingProfileStore(_connection).ListAll();
        var associations = new SqliteAppAssociationStore(_connection).ListAll();
        var resolver = AppProfileResolver.Build(documents, associations);

        // 全 profile を起動時に materialize する（不正 profile は最初の切替時でなくここでエラーになる）
        var profilesById = documents.ToDictionary(
            document => document.ProfileId,
            MappingProfileMaterializer.ToProfile,
            StringComparer.Ordinal);

        _g13Source = new G13RawInputSource();
        _g600Source = new G600RawInputSource();
        var g13Devices = _g13Source.EnumerateDevices();
        var g600Devices = _g600Source.EnumerateDevices();

        var runtimes = new Dictionary<string, DeviceMappingRuntime>(StringComparer.Ordinal);
        var instancesByKind = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (kind, devices) in new[] { ("G13", g13Devices), ("G600", g600Devices) })
        {
            if (!resolver.DefaultByKind.TryGetValue(kind, out var document))
            {
                continue;
            }

            var profile = profilesById[document.ProfileId];
            foreach (var device in devices)
            {
                runtimes[device.DeviceInstanceId] = new DeviceMappingRuntime(device.DeviceInstanceId, profile);
            }

            instancesByKind[kind] = devices.Select(device => device.DeviceInstanceId).ToArray();
        }

        _watchdog = WatchdogChannel.Start(_watchdogExePath);
        var emitter = new GuardedOutputEmitter(new SendInputEmitter(), _watchdog);

        _pump = new FastPathPump(
            [
                new FastPathSource(_g13Source, () => _g13Source.DroppedInputCount),
                new FastPathSource(_g600Source, () => _g600Source.DroppedInputCount),
            ],
            runtimes,
            emitter);
        _pump.Start();

        if (resolver.HasAppAssociations && instancesByKind.Count > 0)
        {
            StartForegroundPolling(resolver, profilesById, instancesByKind);
        }

        return new ResidentHostStatus(
            documents.Select(document => document.ProfileId).ToArray(),
            g13Devices.Select(device => device.DeviceInstanceId).ToArray(),
            g600Devices.Select(device => device.DeviceInstanceId).ToArray(),
            runtimes.Keys.Order(StringComparer.Ordinal).ToArray(),
            associations.Count);
    }

    /// <summary>
    /// foreground app の監視（app-first 切替）。fast path 外の専用 thread が 200ms 間隔で
    /// foreground EXE を観測し、適用 profile が変わる時だけ pump へ差し替えを依頼する
    /// （切替は新規 down から有効・device write はしない＝MAP-010）。
    /// </summary>
    private void StartForegroundPolling(
        AppProfileResolver resolver,
        IReadOnlyDictionary<string, MappingProfile> profilesById,
        IReadOnlyDictionary<string, IReadOnlyList<string>> instancesByKind)
    {
        _foregroundPollThread = new Thread(() =>
        {
            var activeProfileIdByKind = instancesByKind.Keys.ToDictionary(
                kind => kind,
                kind => resolver.DefaultByKind[kind].ProfileId,
                StringComparer.Ordinal);
            string? lastPath = null;
            var first = true;

            while (!_foregroundPollStop)
            {
                var path = ForegroundAppTracker.GetForegroundProcessFullPath();
                if (first || !string.Equals(path, lastPath, StringComparison.OrdinalIgnoreCase))
                {
                    first = false;
                    lastPath = path;
                    foreach (var (kind, instanceIds) in instancesByKind)
                    {
                        var target = resolver.Resolve(kind, path)!;
                        if (target.ProfileId == activeProfileIdByKind[kind])
                        {
                            continue;
                        }

                        activeProfileIdByKind[kind] = target.ProfileId;
                        var profile = profilesById[target.ProfileId];
                        foreach (var instanceId in instanceIds)
                        {
                            _pump!.RequestProfileChange(instanceId, profile);
                        }

                        Console.WriteLine($"profile switch: {kind} -> '{target.ProfileId}'（foreground: {path ?? "<不明>"}）");
                    }
                }

                Thread.Sleep(200);
            }
        })
        {
            IsBackground = true,
            Name = "OpenLogicoolForegroundPoll",
        };
        _foregroundPollThread.Start();
    }

    /// <summary>handled shutdown（DEV-008）: pump 停止→所有 output release→watchdog graceful 終了。</summary>
    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _foregroundPollStop = true;
        _foregroundPollThread?.Join(2000);
        _pump?.Stop();
        _watchdog?.Shutdown();
    }

    public void Dispose()
    {
        Stop();
        _pump?.Dispose();
        _watchdog?.Dispose();
        _g13Source?.Dispose();
        _g600Source?.Dispose();
        _connection?.Dispose();
    }
}
