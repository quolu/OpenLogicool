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
    G600LeftoverResult? LeftoverApply,
    bool G13LcdStarted);

/// <summary>
/// Input Studio の resident 実行体（計画 §6.2 の初期 process model）。
/// SQLite から mapping profile を復元し、実機 G13/G600 を列挙して fast path
/// （Device Input → Mapping Runtime → resident output session）を起動する。
/// UI・AI・capture は含まない。profile が無い device 種別は配線しない（黙って既定値を作らない）。
/// G600 が配線されるとき、fast path の外で route 別の legacy 抑止を apply し、停止時に restore する。
/// </summary>
public sealed class ResidentInputHost : IDisposable
{
    private readonly string _databasePath;
    private readonly bool _enableTrace;
    private readonly G600LeftoverSession? _leftover;
    private readonly G600OnboardModeStore? _onboardMode;
    private readonly Func<IResidentOutputSession> _outputSessionFactory;
    private readonly Func<G13LcdRuntime> _g13LcdRuntimeFactory;
    private volatile bool _g600OnboardSuppressed;
    private SqliteConnection? _connection;
    private G13RawInputSource? _g13Source;
    private G600RawInputSource? _g600Source;
    private G13LcdRuntime? _g13LcdRuntime;
    private IResidentOutputSession? _outputSession;
    private FastPathPump? _pump;
    private Thread? _foregroundPollThread;
    private volatile bool _foregroundPollStop;
    private volatile AppFirstData? _appFirstData;
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? _instancesByKind;
    private bool _stopped;
    private Exception? _stopFailure;
    private readonly ProfileSwitchDecisionRing _decisionRing = new();
    private long _decisionSequence;
    private ForegroundState? _currentForegroundState;

    public ResidentInputHost(
        string databasePath,
        string watchdogExePath,
        bool enableTrace = false,
        G600LeftoverSession? leftover = null,
        G600OnboardModeStore? onboardMode = null,
        Func<IResidentOutputSession>? outputSessionFactory = null,
        Func<G13LcdRuntime>? g13LcdRuntimeFactory = null)
    {
        _databasePath = databasePath;
        _enableTrace = enableTrace;
        _leftover = leftover;
        _onboardMode = onboardMode;
        _outputSessionFactory = outputSessionFactory
            ?? (() => new SendInputResidentOutputSession(watchdogExePath));
        _g13LcdRuntimeFactory = g13LcdRuntimeFactory
            ?? (() => new G13LcdRuntime(new G13LcdHidTransport()));
    }

    /// <summary>onboard 書込み中で G600 の SendInput 送出を抑止しているか（二重入力防止）。</summary>
    public bool IsG600OnboardSuppressed => _g600OnboardSuppressed;

    public ResidentOutputRoute OutputRoute =>
        _outputSession?.Route ?? throw new InvalidOperationException("resident host は未起動です。");

    /// <summary>fast pathまたはoutput sessionのresident停止原因（nullなら正常）。</summary>
    public Exception? Failure => _pump?.Failure ?? _outputSession?.BackgroundFailure ?? _stopFailure;

    /// <summary>live Raw Input queueが破棄したG13 input件数。0以外ならfast pathはfault停止する。</summary>
    public long DroppedG13InputCount => _g13Source?.DroppedInputCount ?? 0;

    /// <summary>live Raw Input queueが破棄したG600 input件数。0以外ならfast pathはfault停止する。</summary>
    public long DroppedG600InputCount => _g600Source?.DroppedInputCount ?? 0;

    public FastPathPump Pump =>
        _pump ?? throw new InvalidOperationException("resident host は未起動です。");

    /// <summary>最近の profile 切替判断（診断表示・APP-005）。foreground 監視をしていなければ空。</summary>
    public IReadOnlyList<ProfileSwitchDecision> RecentProfileSwitchDecisions() => _decisionRing.Snapshot();

    /// <summary>
    /// 現在の foreground 状態（APP-008）。foreground 監視未開始（app 関連付けが無い）なら null。
    /// poll thread からのみ書き込む（診断表示用途であり厳密な happens-before 保証は要求しない）。
    /// </summary>
    public ForegroundState? CurrentForegroundState => _currentForegroundState;

    /// <summary>G13 LCDはfast pathと独立しているため、faultはresident停止原因へ丸めず個別状態で公開する。</summary>
    public G13LcdRuntimeStatus? G13LcdStatus => _g13LcdRuntime?.Status;

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

        _outputSession = _outputSessionFactory()
            ?? throw new InvalidOperationException("output session factoryがnullを返しました。");
        _g600OnboardSuppressed = _onboardMode?.Load() is not null;
        ResidentOutputPolicy.EnsureStartAllowed(_outputSession.Route, _g600OnboardSuppressed);

        // 全 profile を起動時に materialize する（不正 profile は最初の切替時ではなく起動でエラーになる）
        var profilesById = documents.ToDictionary(
            document => document.ProfileId,
            MappingProfileMaterializer.ToProfile,
            StringComparer.Ordinal);
        var documentsById = documents.ToDictionary(
            document => document.ProfileId,
            StringComparer.Ordinal);

        _g13Source = new G13RawInputSource();
        _g600Source = new G600RawInputSource();
        var g13Devices = _g13Source.EnumerateDevices();
        var g600Devices = _g600Source.EnumerateDevices();

        resolver.DefaultByKind.TryGetValue("G13", out var initialG13Document);
        if (g13Devices.Count > 0)
        {
            _g13LcdRuntime = _g13LcdRuntimeFactory()
                ?? throw new InvalidOperationException("G13 LCD runtime factoryがnullを返しました。");
            _g13LcdRuntime.RequestFrame(G13LcdDisplayFrameSelector.Select(initialG13Document?.G13Lcd).Span);
            _g13LcdRuntime.Start();
        }

        // onboard 書込み中は本体がハードウェアとして送るため、常駐側の G600 送出を抑止し
        // （空 profile を配線）、残置（leftover）の apply も行わない（焼いた内容を上書きしない）。
        var leftoverApply = _g600OnboardSuppressed ? null : ApplyLeftoverIfManaged(resolver, g600Devices.Count);
        if (_g600OnboardSuppressed)
        {
            Console.WriteLine("g600 onboard: 本体書込み中のため G600 の送出を抑止（残置の適用もしない）");
        }

        var runtimes = new Dictionary<string, DeviceMappingRuntime>(StringComparer.Ordinal);
        var instancesByKind = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (kind, devices) in new[] { ("G13", g13Devices), ("G600", g600Devices) })
        {
            if (!resolver.DefaultByKind.TryGetValue(kind, out var document))
            {
                continue;
            }

            var profile = _g600OnboardSuppressed && kind == "G600"
                ? EmptyProfile(profilesById[document.ProfileId])
                : profilesById[document.ProfileId];
            foreach (var device in devices)
            {
                runtimes[device.DeviceInstanceId] = new DeviceMappingRuntime(device.DeviceInstanceId, profile);
            }

            instancesByKind[kind] = devices.Select(device => device.DeviceInstanceId).ToArray();
        }

        _outputSession.Start();
        var emitter = _outputSession.Emitter;

        var inputSources = ResidentInputSourceSelection.Select(
            [
                new ResidentInputSourceCandidate(
                    "G13",
                    new FastPathSource(_g13Source, () => _g13Source.DroppedInputCount)),
                new ResidentInputSourceCandidate(
                    "G600",
                    new FastPathSource(_g600Source, () => _g600Source.DroppedInputCount)),
            ],
            instancesByKind.Keys);
        _pump = new FastPathPump(
            inputSources,
            runtimes,
            emitter,
            enableTrace: _enableTrace);
        _pump.Start();

        _instancesByKind = instancesByKind;
        _appFirstData = new AppFirstData(resolver, profilesById, documentsById, Version: 0);
        if ((resolver.HasAppAssociations && instancesByKind.Count > 0) || _g13LcdRuntime is not null)
        {
            StartForegroundPolling();
        }

        return new ResidentHostStatus(
            documents.Select(document => document.ProfileId).ToArray(),
            g13Devices.Select(device => device.DeviceInstanceId).ToArray(),
            g600Devices.Select(device => device.DeviceInstanceId).ToArray(),
            runtimes.Keys.Order(StringComparer.Ordinal).ToArray(),
            associations.Count,
            leftoverApply,
            _g13LcdRuntime is not null);
    }

    /// <summary>foreground 監視が参照する app-first データ一式（immutable・保存で丸ごと差し替える）。</summary>
    private sealed record AppFirstData(
        AppProfileResolver Resolver,
        IReadOnlyDictionary<string, MappingProfile> ProfilesById,
        IReadOnlyDictionary<string, MappingProfileDocument> DocumentsById,
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
        var documentsById = documents.ToDictionary(
            document => document.ProfileId,
            StringComparer.Ordinal);

        var previous = _appFirstData;
        _appFirstData = new AppFirstData(resolver, profilesById, documentsById, (previous?.Version ?? 0) + 1);

        if (_foregroundPollThread is null &&
            ((resolver.HasAppAssociations && _instancesByKind.Count > 0) || _g13LcdRuntime is not null))
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
                var documentsById = data.DocumentsById;
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
                        // onboard 書込み中の G600 は前面切替でも送出プロファイルを復活させない（二重入力防止）
                        if (_g600OnboardSuppressed && outcome.DeviceKind == "G600")
                        {
                            continue;
                        }

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

                    var lcdSetting = G13LcdProfileSettingSelector.Select(
                        decision,
                        documentsById,
                        resolver.DefaultByKind.TryGetValue("G13", out var defaultG13Document)
                            ? defaultG13Document
                            : null);
                    UpdateG13Lcd(lcdSetting);

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

    /// <summary>
    /// handled shutdown（DEV-008）: pump停止と所有output release後にoutput sessionを閉じ、残置を解除する。
    /// Serial HIDはoutput session内でALL_UP ACK後にserialをcloseする。
    /// </summary>
    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _foregroundPollStop = true;
        _foregroundPollThread?.Join(2000);
        var failureBeforeStop = Failure;
        try
        {
            _pump?.Stop();
        }
        catch (Exception exception)
        {
            _stopFailure ??= exception;
        }

        try
        {
            _outputSession?.Stop();
        }
        catch (Exception exception)
        {
            _stopFailure ??= exception;
        }

        _g13LcdRuntime?.Stop(clearDisplay: true);

        if (!_g600OnboardSuppressed)
        {
            RestoreLeftover();
        }

        if (failureBeforeStop is null && _stopFailure is not null)
        {
            throw _stopFailure;
        }
    }

    /// <summary>
    /// onboard 書込み直後の live 切替: G600 の送出を空 profile へ差し替えて抑止する（再起動不要）。
    /// device write はここでは行わない（書込み本体は G600OnboardService）。
    /// </summary>
    public void EnterG600OnboardSuppression()
    {
        _g600OnboardSuppressed = true;
        PushG600Profile(document => EmptyProfile(MappingProfileMaterializer.ToProfile(document)));
    }

    /// <summary>onboard 解除後の live 切替: G600 の送出を既定 profile へ戻す（app 別は次の前面切替から）。</summary>
    public void ExitG600OnboardSuppression()
    {
        _g600OnboardSuppressed = false;
        PushG600Profile(MappingProfileMaterializer.ToProfile);
    }

    private void PushG600Profile(Func<MappingProfileDocument, MappingProfile> materialize)
    {
        if (_pump is null || _instancesByKind is null || _appFirstData is null)
        {
            return;
        }

        if (!_appFirstData.Resolver.DefaultByKind.TryGetValue("G600", out var document)
            || !_instancesByKind.TryGetValue("G600", out var instanceIds))
        {
            return;
        }

        var profile = materialize(document);
        foreach (var instanceId in instanceIds)
        {
            _pump.RequestProfileChange(instanceId, profile);
        }
    }

    /// <summary>binding を持たない profile（onboard 抑止中の配線用・layer 構成だけ元 profile を写す）。</summary>
    private static MappingProfile EmptyProfile(MappingProfile source) =>
        new(
            source.ProfileRevision,
            source.MappingRevision,
            source.DefaultLayerId,
            source.LayerIds,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            []);

    public void Dispose()
    {
        Stop();
        _pump?.Dispose();
        _outputSession?.Dispose();
        _g13Source?.Dispose();
        _g600Source?.Dispose();
        _g13LcdRuntime?.Dispose();
        _connection?.Dispose();
    }

    private void UpdateG13Lcd(WorkspaceG13LcdSetting? setting)
    {
        if (_g13LcdRuntime is null)
        {
            return;
        }

        _g13LcdRuntime.RequestFrame(G13LcdDisplayFrameSelector.Select(setting).Span);
    }

    private G600LeftoverResult? ApplyLeftoverIfManaged(AppProfileResolver resolver, int g600DeviceCount)
    {
        if (_leftover is null)
        {
            return null;
        }

        var managed = resolver.DefaultByKind.ContainsKey("G600") && g600DeviceCount > 0;
        var result = _leftover.Apply(managed, G600LeftoverHostSupport.SuppressionModeFor(_outputSession!.Route));
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
