using System.Diagnostics;
using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Input;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

internal sealed record BotScriptPackage(string Id, string Name, string ProcessName, bool TimedInputEnabled = true)
{
    public string Directory { get; init; } = "";
    public string File(string name) => Path.Combine(Directory, name);
    public static BotScriptPackage Load(string path)
    {
        var package = JsonSerializer.Deserialize<BotScriptPackage>(System.IO.File.ReadAllText(path))
            ?? throw new InvalidDataException("Bot設定が空です。");
        if (string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Name)
            || string.IsNullOrWhiteSpace(package.ProcessName))
            throw new InvalidDataException("Bot設定の名前・対象が不正です。");
        return package with { Directory = Path.GetDirectoryName(Path.GetFullPath(path))! };
    }
}

internal sealed record BotScriptResult(bool NeedsReview, string Detail);

/// <summary>
/// Botを始める時の機能の選び方。Selected は進行設定から選ぶ機能（null はメイン）、WithMain はメインに Selected を足す。
/// Modes は、この実行で入れるモード。
/// </summary>
internal sealed record BotFunctionPlan(bool Recovery, bool Progress, string[]? Selected, bool WithMain, string[] Modes, string Label)
{
    public static BotFunctionPlan Main { get; } = new(true, true, null, false, [], "メイン");
}

internal static class BotFunctionPlanner
{
    // 回復は規則ではなく監視の側にある機能。進行設定の機能と同じ並びで選べるようにする。
    public const string RecoveryId = "recovery";
    public const string RecoveryName = "回復する（ポーション・包帯・食事）";

    public static BotFunctionPlan Plan(IReadOnlyList<VisualProgressFunctionInfo> functions, string? requested, string? activeMode)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            // 指定なしはメイン。入っているモードの機能がメインに無い時は、メインに足す。
            var owner = activeMode is null ? null : functions.FirstOrDefault(function => !function.Main && function.Modes.Contains(activeMode));
            return new(true, true, owner is null ? null : [owner.Id], owner is not null,
                functions.Where(function => function.Main || function == owner).SelectMany(function => function.Modes).ToArray(),
                owner is null ? "メイン" : "メイン＋" + owner.Id);
        }
        var ids = requested.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        var unknown = ids.Where(id => id != RecoveryId && functions.All(function => function.Id != id)).ToArray();
        if (ids.Length == 0 || unknown.Length > 0)
            throw new ArgumentException($"機能 {string.Join("、", unknown)} がありません。"
                + $"指定できる機能: {string.Join("、", functions.Select(function => function.Id).Prepend(RecoveryId))}");
        var progress = ids.Where(id => id != RecoveryId).ToArray();
        return new(ids.Contains(RecoveryId), progress.Length > 0, progress.Length > 0 ? progress : null, false,
            functions.Where(function => progress.Contains(function.Id)).SelectMany(function => function.Modes).ToArray(),
            string.Join("、", ids));
    }

    /// <summary>機能の選び方を、Botの実行の引数へ直す。</summary>
    public static IReadOnlyList<string> Arguments(BotFunctionPlan plan, string progressPath)
    {
        if (!plan.Progress) return ["--recovery-only"];
        var arguments = new List<string> { "--progress-profile", progressPath };
        if (plan.Selected is not null) arguments.AddRange(["--functions", string.Join(",", plan.Selected)]);
        if (plan.WithMain) arguments.Add("--functions-with-main");
        if (!plan.Recovery) arguments.Add("--no-recovery-input");
        return arguments;
    }
}

/// <summary>画面からの開始・停止を、既存Bot runtimeと記録／再生の排他へ接続する。</summary>
internal sealed class HostBotScriptIntents : IBotScriptIntents, IDisposable
{
    private readonly Lock gate = new();
    private readonly DemonstrationRecordingGate executionGate;
    private readonly Func<string, string, Action<JsonElement>, CancellationToken, Task<BotScriptResult>> execute;
    private readonly IReadOnlyList<BotScriptItem> scripts;
    private readonly string dataDirectory;
    private readonly Func<string, string, Task>? reportFault;
    private CancellationTokenSource? stop;
    private Task? worker;
    private bool disposed;
    private BotScriptPhase? userResumePhase;
    private string? userResumeDetail;
    private BotScriptSnapshot state = new(BotScriptPhase.Stopped, "Botは停止しています。");
    private readonly IReadOnlyList<BotScriptMode> modes;
    private readonly IReadOnlyList<BotScriptFunction> functions;
    private readonly Func<string, string?, string?, BotFunctionPlan>? planner;
    // 今の実行（止まっている時は直前の実行）の機能の選び方。
    private BotFunctionPlan plan = BotFunctionPlan.Main;
    // Botごとの、入っているモード。解除するまで、Botとアプリを起動し直しても残す。
    private readonly Dictionary<string, string> activeModes;
    private string ModeFile => Path.Combine(dataDirectory, "modes.json");

    internal HostBotScriptIntents(IReadOnlyList<BotScriptItem> scripts, string dataDirectory,
        DemonstrationRecordingGate executionGate,
        Func<string, string, Action<JsonElement>, CancellationToken, Task<BotScriptResult>> execute,
        Func<string, string, Task>? reportFault = null, IReadOnlyList<BotScriptMode>? modes = null,
        IReadOnlyList<BotScriptFunction>? functions = null, Func<string, string?, string?, BotFunctionPlan>? planner = null)
    {
        this.functions = functions ?? [];
        this.planner = planner;
        this.scripts = scripts;
        this.dataDirectory = dataDirectory;
        this.executionGate = executionGate;
        this.execute = execute;
        this.reportFault = reportFault;
        this.modes = modes ?? [];
        activeModes = System.IO.File.Exists(ModeFile)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(ModeFile))
                ?? throw new InvalidDataException("保存したBotのモードが空です。")
            : [];
    }

    /// <summary>Botが入っているモード。</summary>
    internal string? ActiveMode(string scriptId) { lock (gate) return activeModes.GetValueOrDefault(scriptId); }

    /// <summary>今の実行で動かすモード。実行中のBotが観測ごとに読む。機能を読み込んでいないモードは、次に始めた時から動く。</summary>
    internal string? RunningMode(string scriptId)
    {
        lock (gate) return activeModes.GetValueOrDefault(scriptId) is { } mode && plan.Modes.Contains(mode) ? mode : null;
    }

    /// <summary>今の実行の機能の選び方。</summary>
    internal BotFunctionPlan CurrentPlan { get { lock (gate) return plan; } }

    public IReadOnlyList<BotScriptFunction> ListFunctions() => functions;

    public IReadOnlyList<BotScriptMode> ListModes()
    {
        lock (gate) return modes.Select(mode => mode with { Active = activeModes.GetValueOrDefault(mode.ScriptId) == mode.Id }).ToArray();
    }

    public BotScriptSnapshot SetMode(string modeId)
    {
        lock (gate)
        {
            var matches = modes.Where(mode => mode.Id == modeId).ToArray();
            if (matches.Length != 1) throw new ArgumentException(matches.Length == 0
                ? $"モード {modeId} がありません。指定できるモード: {string.Join("、", modes.Select(mode => mode.Id))}"
                : $"モード {modeId} は複数のBotにあります。");
            activeModes[matches[0].ScriptId] = modeId;
            SaveModes();
        }
        return Current();
    }

    public BotScriptSnapshot ClearMode()
    {
        lock (gate) { activeModes.Clear(); SaveModes(); }
        return Current();
    }

    private void SaveModes()
    {
        System.IO.Directory.CreateDirectory(dataDirectory);
        var temporary = ModeFile + ".tmp";
        System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(activeModes));
        System.IO.File.Move(temporary, ModeFile, overwrite: true);
    }

    public static HostBotScriptIntents Create(string databasePath, SerialHidDiscoveryService discovery,
        Func<SerialHidResidentOutputSession?> borrowedNano, DemonstrationRecordingGate executionGate,
        string? selectedDeviceId, Func<ResidentPhysicalInput?>? physicalInput = null)
    {
        var packages = System.IO.Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "BotScripts"),
            "bot.json", SearchOption.AllDirectories).Select(BotScriptPackage.Load).ToDictionary(package => package.Id);
        var modes = new List<BotScriptMode>();
        var functions = new List<BotScriptFunction>();
        var catalogs = new Dictionary<string, IReadOnlyList<VisualProgressFunctionInfo>>();
        HostBotScriptIntents? intents = null;
        var scripts = packages.Values.Select(package =>
        {
            var recovery = VisualRecoveryProfile.Load(package.File("profile.json"));
            // メインの組み合わせを読めること（設定の誤りは起動時に出す）と、機能・モードの一覧を確かめる。
            _ = VisualProgressProfile.Load(package.File("progress.json"));
            var catalog = catalogs[package.Id] = VisualProgressProfile.ListFunctions(package.File("progress.json"));
            functions.Add(new(package.Id, BotFunctionPlanner.RecoveryId, BotFunctionPlanner.RecoveryName, true));
            functions.AddRange(catalog.Select(function => new BotScriptFunction(package.Id, function.Id, function.Name, function.Main)));
            modes.AddRange((VisualProgressProfile.Load(package.File("progress.json"), catalog.Select(function => function.Id).ToArray()).Modes ?? [])
                .Select(mode => new BotScriptMode(package.Id, mode.Id, mode.Name, false)));
            return new BotScriptItem(package.Id, package.Name,
                $"Nanoで入力・会話は表示の安定後に送る・{(package.TimedInputEnabled ? "通常Spaceは8〜12秒間隔" : "表示条件のある入力のみ・定期Spaceは停止中")}\n回復監視は毎秒4回 ／ ポーション: HP {recovery.PotionThreshold:P0}以下 ／ 包帯: 白 {recovery.BandageThreshold:P0}以上 ／ 時間制限なし");
        }).ToArray();
        var dataDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "bot-runs");
        var reviewSettings = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "bot-review-mcp.json");
        return intents = new(scripts, dataDirectory, executionGate, async (id, evidence, report, token) =>
        {
            var package = packages[id];
            var target = WindowsGameTargetLocator.Locate(package.ProcessName);
            SerialHidResidentOutputSession? owned = null;
            var nano = borrowedNano();
            try
            {
                if (nano is null)
                {
                    owned = discovery.Resolve(selectedDeviceId, SerialHidProtocolV1.AllCapabilities).Session;
                    owned.Start();
                    owned.Protocol.SendAllUp();
                    nano = owned;
                }
                token.ThrowIfCancellationRequested();
                var emitter = nano.Emitter as SerialHidEmitter
                    ?? throw new InvalidOperationException("Nanoの入力送出を取得できません。");
                var arguments = new List<string> { "--db", Path.Combine(dataDirectory, id + ".db"),
                    "--inhibit-image", package.File("stop.png"), "--cue-text", "Space",
                    "--cue-text", "画面を押してください", "--keys", "Key:Space",
                    "--recovery-profile", package.File("profile.json"),
                    "--evidence", evidence, "--continue-on-review", "--pause-on-user-input", "--assistance-db", databasePath };
                arguments.AddRange(BotFunctionPlanner.Arguments(intents!.CurrentPlan, package.File("progress.json")));
                if (!package.TimedInputEnabled) arguments.Add("--no-timed-input");
                if (System.IO.File.Exists(reviewSettings)) arguments.AddRange(["--review-mcp", reviewSettings]);
                var result = await VisualKeyAssistRuntime.RunAsync(arguments.ToArray(), nano, emitter, target,
                    $"window:bot:{target.ProcessId}", token, report, physicalInput, () => intents!.RunningMode(id));
                var json = JsonSerializer.SerializeToElement(result);
                System.IO.File.WriteAllText(Path.Combine(evidence, "result.json"), json.GetRawText());
                return new(json.TryGetProperty("NeedsReview", out var review) && review.GetBoolean(),
                    json.TryGetProperty("Detail", out var detail) ? detail.GetString()! : "実行が終了しました。");
            }
            finally { owned?.Dispose(); }
        }, async (evidence, detail) =>
        {
            if (!new BotAssistanceStore(databasePath).Exists) return;
            var fault = JsonSerializer.SerializeToElement(new { Kind = "fault", Detail = detail });
            System.IO.File.WriteAllText(Path.Combine(evidence, "assistance-fault.json"), fault.GetRawText());
            await VisualAssistReviewNotifier.Create(reviewSettings, databasePath)!.NotifyAsync(evidence, fault, CancellationToken.None);
        }, modes, functions, (id, requested, activeMode) => BotFunctionPlanner.Plan(catalogs[id], requested, activeMode));
    }

    public IReadOnlyList<BotScriptItem> ListScripts() => scripts;
    public BotScriptSnapshot Current()
    {
        lock (gate)
        {
            // 実行中・直前のBotのモードを示す。Botをまだ始めていない時は、入っているモードをすべて示す。
            var running = worker is { IsCompleted: false };
            var active = modes.Where(mode => activeModes.GetValueOrDefault(mode.ScriptId) == mode.Id
                && (state.ScriptId is null || state.ScriptId == mode.ScriptId))
                // 動いているBotが機能を読み込んでいないモードは、次に始めた時から動く。
                .Select(mode => mode.Name + (running && !plan.Modes.Contains(mode.Id) ? "（次にBotを始めた時から）" : "")).ToArray();
            return state with { Mode = active.Length == 0 ? null : string.Join("、", active) };
        }
    }

    public void Start(string scriptId, string? functions = null)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker is { IsCompleted: false }) throw new InvalidOperationException("Botは既に実行中です。");
            if (!scripts.Any(script => script.Id == scriptId)) throw new ArgumentException("選択したBotがありません。");
            // 機能の指定の誤りは、Botを始める前に返す。
            var next = planner is null ? BotFunctionPlan.Main : planner(scriptId, functions, activeModes.GetValueOrDefault(scriptId));
            if (!executionGate.TryBeginPlayback(out var refusal)) throw new InvalidOperationException(refusal);
            stop?.Dispose();
            stop = new CancellationTokenSource();
            userResumePhase = null;
            userResumeDetail = null;
            var evidence = Path.Combine(dataDirectory, scriptId, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
            plan = next;
            state = new(BotScriptPhase.Starting, "ゲームとNanoへ接続しています。", scriptId, evidence) { Functions = next.Label };
            var token = stop.Token;
            worker = Task.Run(async () =>
            {
                try
                {
                    System.IO.Directory.CreateDirectory(evidence);
                    var result = await execute(scriptId, evidence, Observe, token);
                    lock (gate) state = state with { Phase = result.NeedsReview ? BotScriptPhase.AwaitingReview : BotScriptPhase.Stopped, Detail = result.Detail };
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    lock (gate) state = state with { Phase = BotScriptPhase.Stopped, Detail = "入力と監視を終了しました。" };
                }
                catch (Exception error)
                {
                    lock (gate) state = state with { Phase = BotScriptPhase.Faulted, Detail = error.Message };
                    if (reportFault is not null)
                    {
                        try { await reportFault(evidence, error.Message); }
                        catch (Exception delivery)
                        {
                            lock (gate) state = state with { Detail = state.Detail + " AI支援通知に失敗しました: " + delivery.Message };
                        }
                    }
                }
                finally
                {
                    executionGate.EndPlayback();
                    System.IO.File.WriteAllText(Path.Combine(evidence, "status.json"), JsonSerializer.Serialize(Current()));
                }
            });
        }
    }

    private void Observe(JsonElement entry)
    {
        lock (gate)
        {
            if (state.Phase is not (BotScriptPhase.Starting or BotScriptPhase.Running or BotScriptPhase.ReviewMonitoring or BotScriptPhase.UserPaused)) return;
            var eventName = entry.GetProperty("Event").GetString();
            if (eventName == "user-input-paused")
            {
                userResumePhase ??= state.Phase;
                userResumeDetail ??= state.Detail;
                state = state with { Phase = BotScriptPhase.UserPaused, Detail = entry.GetProperty("Detail").GetString()! };
                return;
            }
            if (eventName == "user-input-resumed")
            {
                state = state with { Phase = userResumePhase ?? BotScriptPhase.Running,
                    Detail = userResumeDetail ?? entry.GetProperty("Detail").GetString()! };
                userResumePhase = null; userResumeDetail = null;
                return;
            }
            var paused = userResumePhase is not null;
            if (paused) state = state with { Phase = userResumePhase!.Value, Detail = userResumeDetail! };
            if (state.Phase == BotScriptPhase.Starting) state = state with { Phase = BotScriptPhase.Running };
            switch (eventName)
            {
                case "recovery-sample":
                    state = state with { ObservationCount = state.ObservationCount + 1,
                        LastIntervalMs = entry.GetProperty("IntervalMs").ValueKind == JsonValueKind.Null ? 0 : entry.GetProperty("IntervalMs").GetInt64() };
                    break;
                case "recovery":
                    var health = entry.GetProperty("Observation").GetProperty("HealthFraction");
                    state = state with { HealthFraction = health.ValueKind == JsonValueKind.Number ? health.GetDouble() : null,
                        Detail = state.Phase == BotScriptPhase.ReviewMonitoring ? state.Detail : entry.GetProperty("Detail").GetString()! };
                    break;
                case "progress-review-grace":
                    state = state with { Detail = "詰まりを観測しました。1分間は通知せず、観測と既存の操作を継続します。 "
                        + entry.GetProperty("Detail").GetString() };
                    break;
                case "progress-review-monitoring":
                    state = state with { Phase = BotScriptPhase.ReviewMonitoring,
                        Detail = entry.GetProperty("Detail").GetString() + (entry.TryGetProperty("AutomaticRulesContinue", out var continuing) && continuing.GetBoolean()
                            ? " 通常の操作規則・画面観測・回復監視を継続中です。" : " 画面観測と回復監視は継続中です。") };
                    break;
                case "recovery-review-advisory":
                    state = state with { Phase = BotScriptPhase.ReviewMonitoring, Detail = entry.GetProperty("Detail").GetString()! };
                    break;
                case "progress-resumed":
                    state = state with { Phase = BotScriptPhase.Running, Detail = entry.GetProperty("Detail").GetString()! };
                    break;
                case "review-notification-failed":
                    state = state with { Detail = entry.GetProperty("Detail").GetString()! };
                    break;
                case "input": case "progress-input":
                    state = state with { InputCount = state.InputCount + 1 };
                    break;
                case "recovery-input":
                    var action = entry.GetProperty("Action").GetString();
                    state = state with { FoodCount = state.FoodCount + (action == "Food" ? 1 : 0),
                        PotionCount = state.PotionCount + (action == "Potion" ? 1 : 0),
                        BandageCount = state.BandageCount + (action == "Bandage" ? 1 : 0) };
                    break;
            }
            if (paused)
            {
                userResumePhase = state.Phase; userResumeDetail = state.Detail;
                state = state with { Phase = BotScriptPhase.UserPaused,
                    Detail = $"手入力を優先してBotの送出を一時停止しています。全解放後{UserInputPauseState.QuietMilliseconds / 1000}秒で再開します。" };
            }
        }
    }

    public async Task StopAsync()
    {
        Task? pending;
        lock (gate)
        {
            pending = worker;
            if (pending is null || pending.IsCompleted) return;
            state = state with { Phase = BotScriptPhase.Stopping, Detail = "停止しています。送出中の有限入力と監視の終了を待っています。" };
            stop!.Cancel();
        }
        await pending.ConfigureAwait(false);
    }

    public void OpenEvidence()
    {
        var evidence = Current().EvidenceDirectory ?? throw new InvalidOperationException("実行記録はまだありません。");
        Process.Start(new ProcessStartInfo(evidence) { UseShellExecute = true });
    }

    public void Dispose()
    {
        lock (gate) disposed = true;
        StopAsync().GetAwaiter().GetResult();
        stop?.Dispose();
    }
}
