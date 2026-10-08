using System.Diagnostics;
using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Input;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

internal sealed record BotScriptPackage(string Id, string Name, string ProcessName, int DurationMs)
{
    public string Directory { get; init; } = "";
    public string File(string name) => Path.Combine(Directory, name);
    public static BotScriptPackage Load(string path)
    {
        var package = JsonSerializer.Deserialize<BotScriptPackage>(System.IO.File.ReadAllText(path))
            ?? throw new InvalidDataException("Bot設定が空です。");
        if (string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Name)
            || string.IsNullOrWhiteSpace(package.ProcessName) || package.DurationMs <= 0)
            throw new InvalidDataException("Bot設定の名前・対象・実行時間が不正です。");
        return package with { Directory = Path.GetDirectoryName(Path.GetFullPath(path))! };
    }
}

internal sealed record BotScriptResult(bool NeedsReview, string Detail);

/// <summary>画面からの開始・停止を、既存Bot runtimeと記録／再生の排他へ接続する。</summary>
internal sealed class HostBotScriptIntents : IBotScriptIntents, IDisposable
{
    private readonly Lock gate = new();
    private readonly DemonstrationRecordingGate executionGate;
    private readonly Func<string, string, Action<JsonElement>, CancellationToken, Task<BotScriptResult>> execute;
    private readonly IReadOnlyList<BotScriptItem> scripts;
    private readonly string dataDirectory;
    private CancellationTokenSource? stop;
    private Task? worker;
    private bool disposed;
    private BotScriptSnapshot state = new(BotScriptPhase.Stopped, "Botは停止しています。");

    internal HostBotScriptIntents(IReadOnlyList<BotScriptItem> scripts, string dataDirectory,
        DemonstrationRecordingGate executionGate,
        Func<string, string, Action<JsonElement>, CancellationToken, Task<BotScriptResult>> execute)
    {
        this.scripts = scripts;
        this.dataDirectory = dataDirectory;
        this.executionGate = executionGate;
        this.execute = execute;
    }

    public static HostBotScriptIntents Create(string databasePath, SerialHidDiscoveryService discovery,
        Func<SerialHidResidentOutputSession?> borrowedNano, DemonstrationRecordingGate executionGate,
        string? selectedDeviceId)
    {
        var packages = System.IO.Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "BotScripts"),
            "bot.json", SearchOption.AllDirectories).Select(BotScriptPackage.Load).ToDictionary(package => package.Id);
        var scripts = packages.Values.Select(package =>
        {
            var recovery = VisualRecoveryProfile.Load(package.File("profile.json"));
            _ = VisualProgressProfile.Load(package.File("progress.json"));
            return new BotScriptItem(package.Id, package.Name,
                $"Nanoで入力・Spaceは8〜12秒間隔・回復監視は毎秒4回\nポーション: HP {recovery.PotionThreshold:P0}以下 ／ 包帯: 白 {recovery.BandageThreshold:P0}以上 ／ 最長{package.DurationMs / 60000}分");
        }).ToArray();
        var dataDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "bot-runs");
        var reviewSettings = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "bot-review-mcp.json");
        return new(scripts, dataDirectory, executionGate, async (id, evidence, report, token) =>
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
                    "--duration-ms", package.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--recovery-profile", package.File("profile.json"), "--progress-profile", package.File("progress.json"),
                    "--evidence", evidence, "--keep-monitoring-on-review" };
                if (System.IO.File.Exists(reviewSettings)) arguments.AddRange(["--review-mcp", reviewSettings]);
                var result = await VisualKeyAssistRuntime.RunAsync(arguments.ToArray(), nano, emitter, target,
                    $"window:bot:{target.ProcessId}", token, report);
                var json = JsonSerializer.SerializeToElement(result);
                System.IO.File.WriteAllText(Path.Combine(evidence, "result.json"), json.GetRawText());
                return new(json.TryGetProperty("NeedsReview", out var review) && review.GetBoolean(),
                    json.TryGetProperty("Detail", out var detail) ? detail.GetString()! : "実行が終了しました。");
            }
            finally { owned?.Dispose(); }
        });
    }

    public IReadOnlyList<BotScriptItem> ListScripts() => scripts;
    public BotScriptSnapshot Current() { lock (gate) return state; }

    public void Start(string scriptId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker is { IsCompleted: false }) throw new InvalidOperationException("Botは既に実行中です。");
            if (!scripts.Any(script => script.Id == scriptId)) throw new ArgumentException("選択したBotがありません。");
            if (!executionGate.TryBeginPlayback(out var refusal)) throw new InvalidOperationException(refusal);
            stop?.Dispose();
            stop = new CancellationTokenSource();
            var evidence = Path.Combine(dataDirectory, scriptId, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
            state = new(BotScriptPhase.Starting, "ゲームとNanoへ接続しています。", scriptId, evidence);
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
            if (state.Phase is not (BotScriptPhase.Starting or BotScriptPhase.Running or BotScriptPhase.ReviewMonitoring)) return;
            if (state.Phase == BotScriptPhase.Starting) state = state with { Phase = BotScriptPhase.Running };
            switch (entry.GetProperty("Event").GetString())
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
                case "progress-review-monitoring":
                    state = state with { Phase = BotScriptPhase.ReviewMonitoring,
                        Detail = entry.GetProperty("Detail").GetString() + " 画面観測と回復監視は継続中です。" };
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
