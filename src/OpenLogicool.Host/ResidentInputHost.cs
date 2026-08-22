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

/// <summary>resident host の起動結果（内部語彙はここ、表示へ漏れたら訳す）。</summary>
public sealed record ResidentHostStatus(
    IReadOnlyList<string> LoadedProfileIds,
    IReadOnlyList<string> G13DeviceInstanceIds,
    IReadOnlyList<string> G600DeviceInstanceIds,
    IReadOnlyList<string> WiredDeviceInstanceIds,
    int AppAssociationCount,
    G600LeftoverResult? LeftoverApply);

/// <summary>
/// Input Studio の resident 実行体（計画 §6.2 の初期 process model）。
/// SQLite から mapping profile を復元し、実機 G13/G600 を列挙して fast path
/// （Device Input → Mapping Runtime → GuardedOutputEmitter＋watchdog）を起動する。
/// UI・AI・capture は含まない。profile が無い device 種別は配線しない（黙って既定値を作らない）。
/// G600 が配線されるとき、fast path の外で B変種残置を apply し、停止時に restore する。
/// </summary>
public sealed class ResidentInputHost : IDisposable
{
    private readonly string _databasePath;
    private readonly string _watchdogExePath;
    private readonly bool _enableTrace;
    private readonly G600LeftoverSession? _leftover;
    private SqliteConnection? _connection;
    private G13RawInputSource? _g13Source;
    private G600RawInputSource? _g600Source;
    private WatchdogChannel? _watchdog;
    private FastPathPump? _pump;
    private Thread? _foregroundPollThread;
    private volatile bool _foregroundPollStop;
    private volatile AppFirstData? _appFirstData;
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? _instancesByKind;
    private bool _stopped;
    private readonly ProfileSwitchDecisionRing _decisionRing = new();
    private long _decisionSequence;
    private ForegroundState? _currentForegroundState;

    public ResidentInputHost(
        string databasePath,
        string watchdogExePath,
        bool enableTrace = false,
        G600LeftoverSession? leftover = null)
    {
        _databasePath = databasePath;
        _watchdogExePath = watchdogExePath;
        _enableTrace = enableTrace;
        _leftover = leftover;
    }

    public FastPathPump Pump =>
        _pump ?? throw new InvalidOperationException("resident host は未起動です。");

    /// <summary>最近の profile 切替判断（診断表示・APP-005）。foreground 監視をしていなければ空。</summary>
    public IReadOnlyList<ProfileSwitchDecision> RecentProfileSwitchDecisions() => _decisionRing.Snapshot();

    /// <summary>
    /// 現在の foreground 状態（APP-008）。foreground 監視未開始（app 関連付けが無い）なら null。
    /// poll thread からのみ書き込む（診断表示用途であり厳密な happens-before 保証は要求しない）。
    /// </summary>
    public ForegroundState? CurrentForegroundState => _currentForegroundState;

    public ResidentHostStatus Start()
    {
        if (_pump is not null || _stopped)
        {
            throw new InvalidOperationException("resident host は一度しか起動できません。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _connection = new SqliteConnection($"Data Source={_databasePath}");
        _connection.Open();
        new SqliteMigrationRunner(InitialSqliteMigrations.All).Apply(_connection);

        var documents = new SqliteMappingProfileStore(_connection).ListAll();
        var associations = new SqliteAppAssociationStore(_connection).ListAll();
        var resolver = AppProfileResolver.Build(documents, associations);

        // 全 profile を起動時に materialize する（不正 profile は最初の切替時ではなく起動でエラーになる）
        var profilesById = documents.ToDictionary(
            document => document.ProfileId,
            MappingProfileMaterializer.ToProfile,
            StringComparer.Ordinal);

        _g13Source = new G13RawInputSource();
        _g600Source = new G600RawInputSource();
        var g13Devices = _g13Source.EnumerateDevices();
        var g600Devices = _g600Source.EnumerateDevices();

        var leftoverApply = ApplyLeftoverIfManaged(resolver, g600Devices.Count);

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
            emitter,
            enableTrace: _enableTrace);
        _pump.Start();

        _instancesByKind = instancesByKind;
        _appFirstData = new AppFirstData(resolver, profilesById, Version: 0);
        if (resolver.HasAppAssociations && instancesByKind.Count > 0)
        {
            StartForegroundPolling();
        }

        return new ResidentHostStatus(
            documents.Select(document => document.ProfileId).ToArray(),
            g13Devices.Select(device => device.DeviceInstanceId).ToArray(),
            g600Devices.Select(device => device.DeviceInstanceId).ToArray(),
            runtimes.Keys.Order(StringComparer.Ordinal).ToArray(),
            associations.Count,
            leftoverApply);
    }

    /// <summary>foreground 監視が参照する app-first データ一式（immutable・保存で丸ごと差し替える）。</summary>
    private sealed record AppFirstData(
        AppProfileResolver Resolver,
        IReadOnlyDictionary<string, MappingProfile> ProfilesById,
        long Version);

    /// <summary>
    /// 保存後に app-first データ（resolver・profile 実体・関連付け）を DB から読み直して監視へ反映する。
    /// これをしないと監視 thread は起動時 snapshot のままで、保存後の前面切替が古い profile へ
    /// 巻き戻り、起動後に初めて関連付けが出来た場合は監視自体が始まらない（実利用の欠陥 2026-08-22）。
    /// </summary>
    public void RefreshAppFirstData()
    {
        if (_connection is null || _instancesByKind is null)
        {
            return;
        }

        var documents = new SqliteMappingProfileStore(_connection).ListAll();
        var associations = new SqliteAppAssociationStore(_connection).ListAll();
        var resolver = AppProfileResolver.Build(documents, associations);
        var profilesById = documents.ToDictionary(
            document => document.ProfileId,
            MappingProfileMaterializer.ToProfile,
            StringComparer.Ordinal);

        var previous = _appFirstData;
        _appFirstData = new AppFirstData(resolver, profilesById, (previous?.Version ?? 0) + 1);

        if (_foregroundPollThread is null && resolver.HasAppAssociations && _instancesByKind.Count > 0)
        {
            StartForegroundPolling();
        }
    }

    /// <summary>
    /// foreground app の監視（app-first 切替）。fast path 外の専用 thread が 200ms 間隔で
    /// foreground EXE を観測し、適用 profile が変わる時だけ pump へ論理切替を依頼する
    /// （切替は新規 down から有効・device write はしない＝MAP-010）。
    /// データは _appFirstData を毎周期読む（保存による差し替えを次周期から反映する）。
    /// </summary>
    private void StartForegroundPolling()
    {
        var instancesByKind = _instancesByKind!;
        _foregroundPollThread = new Thread(() =>
        {
            var data = _appFirstData!;
            var seenVersion = data.Version;
            var activeProfileIdByKind = instancesByKind.Keys.ToDictionary(
                kind => kind,
                kind => data.Resolver.DefaultByKind[kind].ProfileId,
                StringComparer.Ordinal);
            ForegroundApplicationIdentity? previousIdentity = null;
            string? lastKey = null;
            var first = true;

            while (!_foregroundPollStop)
            {
                var current = _appFirstData!;
                if (current.Version != seenVersion)
                {
                    // 保存で差し替わった: 次の判断を強制し、最新の resolver／profile で引き直す
                    data = current;
                    seenVersion = current.Version;
                    first = true;
                }

                var resolver = data.Resolver;
                var profilesById = data.ProfilesById;
                var identity = ForegroundAppTracker.GetForegroundIdentity();
                // path・package のどちらかが変われば再判断する（片側だけ null の変化も逃さない）
                var key = identity is null ? null : $"{identity.NormalizedFullPath}{identity.PackageFamilyName}";
                if (first || !string.Equals(key, lastKey, StringComparison.Ordinal))
                {
                    first = false;
                    lastKey = key;

                    var decision = ProfileSwitchJudge.Decide(
                        Interlocked.Increment(ref _decisionSequence),
                        previousIdentity,
                        activeProfileIdByKind,
                        identity,
                        resolver);
                    _decisionRing.Record(decision);

                    var newState = ForegroundStateClassifier.Classify(decision.Outcomes);
                    if (ForegroundStateClassifier.HasTransitioned(_currentForegroundState, newState))
                    {
                        _currentForegroundState = newState;
                        var matchDetail = decision.Outcomes.Count == 0
                            ? null
                            : DescribeSwitchReason(decision.Outcomes[0].MatchKind, identity);
                        Console.WriteLine($"foreground state: {ForegroundStateClassifier.Describe(newState, matchDetail)}");
                    }

                    foreach (var outcome in decision.Outcomes)
                    {
                        if (!outcome.Changed)
                        {
                            continue;
                        }

                        activeProfileIdByKind[outcome.DeviceKind] = outcome.SelectedProfileId;
                        var profile = profilesById[outcome.SelectedProfileId];
                        foreach (var instanceId in instancesByKind[outcome.DeviceKind])
                        {
                            _pump!.RequestProfileChange(instanceId, profile);
                        }

                        Console.WriteLine(
                            $"profile switch: {outcome.DeviceKind} -> '{outcome.SelectedProfileId}'（{DescribeSwitchReason(outcome.MatchKind, identity)}）");
                    }

                    previousIdentity = identity;
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

    /// <summary>profile switch log の理由表示（診断表示・APP-005）。</summary>
    private static string DescribeSwitchReason(string matchKind, ForegroundApplicationIdentity? identity) =>
        matchKind switch
        {
            "package" => $"package 一致: {identity?.PackageFamilyName}",
            "path" => $"path 一致: {identity?.NormalizedFullPath}",
            "identity-unavailable" => "identity 取得不能",
            _ => "既定",
        };

    /// <summary>handled shutdown（DEV-008）: pump 停止と所有 output release、watchdog graceful 終了、残置解除。</summary>
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
        RestoreLeftover();
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

    private G600LeftoverResult? ApplyLeftoverIfManaged(AppProfileResolver resolver, int g600DeviceCount)
    {
        if (_leftover is null)
        {
            return null;
        }

        var managed = resolver.DefaultByKind.ContainsKey("G600") && g600DeviceCount > 0;
        var result = _leftover.Apply(managed);
        Console.WriteLine(G600LeftoverHostSupport.Describe(result));
        if (result.IsHardFailure)
        {
            throw new InvalidOperationException("G600 残置の適用に失敗したため常駐を開始しない。 " + result.Reason);
        }

        return result;
    }

    private void RestoreLeftover()
    {
        if (_leftover is null)
        {
            return;
        }

        var result = _leftover.Restore();
        Console.WriteLine(G600LeftoverHostSupport.Describe(result));
        if (result.IsHardFailure)
        {
            Console.Error.WriteLine("G600 残置の解除に失敗した。baseline は保持したまま。 " + result.Reason);
        }
    }
}
