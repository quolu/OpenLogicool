using System.IO;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Host;

/// <summary>
/// 遠隔表示（ゲームの窓の映像と音を、利用者の中継サーバーへ送る）の操作。
/// 対象のゲームは Game Operator の「対象」（MacroTargetSettingsStore）で決める。設定・秘密・対象が揃わない時は開始しない。
/// <para>
/// 「受け付ける」が入の間は、中継サーバーへ見ている端末の数を聞きに行き、見に来た時に送り始め、いなくなると止める。
/// 入・切は設定に保存するので、アプリを起動し直しても続く。切の間は送らない。
/// </para>
/// </summary>
public sealed class HostRemoteViewIntents : IRemoteViewIntents, IDisposable
{
    private readonly RemoteViewSettingsStore settingsStore;
    private readonly IRemoteViewSecretStore secrets;
    private readonly MacroTargetSettingsStore targetStore;
    private readonly Func<string, WindowsGameTarget> locate;
    private readonly Func<nint, (int Width, int Height)> clientSize;
    private readonly RemoteViewRuntime runtime;
    private readonly IDisposable? owned;
    private readonly IRemoteViewViewerQuery? viewerQuery;
    private readonly Func<TimeSpan> clock;
    private readonly RemoteViewOnDemandPolicy policy = new(IdleStop);
    private readonly object gate = new();
    private readonly object pollGate = new();
    private readonly CancellationTokenSource watchStop = new();
    private Thread? watchThread;
    private RemoteViewSettings? settings;
    private int viewers;
    private string? watchError;
    private string? startError;

    /// <summary>見ている端末がいなくなってから止めるまでの時間。視聴ページの再試行（数秒おき）の合間では止めない。</summary>
    public static readonly TimeSpan IdleStop = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public HostRemoteViewIntents(
        RemoteViewSettingsStore settingsStore,
        IRemoteViewSecretStore secrets,
        MacroTargetSettingsStore targetStore,
        Func<string, WindowsGameTarget> locate,
        Func<nint, (int Width, int Height)> clientSize,
        RemoteViewRuntime runtime,
        IDisposable? owned = null,
        IRemoteViewViewerQuery? viewerQuery = null,
        Func<TimeSpan>? clock = null)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        this.targetStore = targetStore ?? throw new ArgumentNullException(nameof(targetStore));
        this.locate = locate ?? throw new ArgumentNullException(nameof(locate));
        this.clientSize = clientSize ?? throw new ArgumentNullException(nameof(clientSize));
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.owned = owned;
        this.viewerQuery = viewerQuery;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        this.clock = clock ?? (() => watch.Elapsed);
    }

    /// <summary>実物の ffmpeg 起動・資格情報マネージャー・process loopback で組み立てる。</summary>
    public static HostRemoteViewIntents Create(string databasePath)
    {
        var launcher = new RemoteViewFfmpegLauncher();
        return new HostRemoteViewIntents(
            RemoteViewSettingsStore.ForDatabase(databasePath),
            new RemoteViewSecretStore(),
            MacroTargetSettingsStore.ForDatabase(databasePath),
            WindowsGameTargetLocator.Locate,
            window =>
            {
                var bounds = WindowsGameTargetLocator.CaptureClientBounds(window);
                return ((int)bounds.Width, (int)bounds.Height);
            },
            new RemoteViewRuntime(launcher.Launch, ProcessLoopbackRemoteViewAudioSource.Create),
            launcher,
            new HttpRemoteViewViewerQuery()).StartWatching();
    }

    /// <summary>見ている端末の数を定期に聞きに行く低優先度の thread を始める。fast path には触れない。</summary>
    public HostRemoteViewIntents StartWatching()
    {
        if (viewerQuery is null)
        {
            throw new InvalidOperationException("視聴の有無を問い合わせる部品がありません。");
        }

        watchThread = new Thread(() =>
        {
            while (!watchStop.Token.WaitHandle.WaitOne(PollInterval))
            {
                PollViewers();
            }
        })
        {
            IsBackground = true,
            Name = "remote-view-viewers",
            Priority = ThreadPriority.BelowNormal,
        };
        watchThread.Start();
        return this;
    }

    /// <summary>1 回ぶんの問い合わせと判断。受け付けていない間は何もしない。失敗は状態へ残し、次の回も同じように聞く。</summary>
    public void PollViewers()
    {
        lock (pollGate)
        {
            RemoteViewSettings current;
            try
            {
                current = LoadFresh();
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                SetWatch(0, $"遠隔表示の設定を読めません: {exception.Message}");
                return;
            }

            if (!current.AcceptViewers || viewerQuery is null)
            {
                policy.Reset();
                startError = null;
                SetWatch(0, null);
                return;
            }

            int count;
            try
            {
                var password = secrets.Load()
                    ?? throw new InvalidOperationException("遠隔表示の送信用パスワードが保存されていません。");
                count = viewerQuery.CountViewers(
                    current.PublishUrl ?? throw new InvalidOperationException("遠隔表示の送信先のURLが設定されていません。"),
                    $"{current.PublishUser}:{password}");
            }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException
                or System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                // 聞けなかった回は、送り始めも止めもしない（分からないまま動かさない）。
                SetWatch(0, $"中継サーバーへ視聴の有無を問い合わせできません: {exception.Message}");
                return;
            }

            var running = runtime.Status.Phase is RemoteViewPhase.Starting or RemoteViewPhase.Streaming;
            switch (policy.Step(clock(), count, running))
            {
                case RemoteViewOnDemandAction.Start:
                    try
                    {
                        Start();
                        startError = null;
                    }
                    catch (InvalidOperationException exception)
                    {
                        // 見ている端末が居続ける間は送り直さない。理由は、いなくなるまで状態へ出したままにする。
                        startError = exception.Message;
                    }

                    break;
                case RemoteViewOnDemandAction.Stop:
                    runtime.Stop();
                    break;
            }

            if (!policy.Engaged)
            {
                startError = null;
            }

            SetWatch(count, startError);
        }
    }

    public RemoteViewSnapshot Current()
    {
        var status = runtime.Status;
        var current = Settings();
        int watchedViewers;
        string? watchedError;
        lock (gate)
        {
            watchedViewers = viewers;
            watchedError = watchError;
        }

        var detail = status.Phase == RemoteViewPhase.Stopped && current.AcceptViewers
            ? watchedError ?? "見に来るのを待っています。"
            : status.Detail;
        return new RemoteViewSnapshot(
            status.Phase,
            detail,
            current.Quality,
            status.TargetProcessName,
            status.StreamedSeconds,
            status.VideoFrames,
            status.VideoStalled,
            current.ViewerUrl,
            current.AcceptViewers,
            watchedViewers);
    }

    public void Start()
    {
        var current = LoadFresh();
        if (string.IsNullOrEmpty(current.PublishUrl) || string.IsNullOrEmpty(current.PublishUser))
        {
            throw new InvalidOperationException("遠隔表示の送信先のURLとユーザー名が設定されていません。");
        }

        var password = secrets.Load()
            ?? throw new InvalidOperationException("遠隔表示の送信用パスワードが保存されていません。");
        var target = targetStore.Load()
            ?? throw new InvalidOperationException("遠隔表示の対象のゲームが未設定です。Game Operator の対象を設定してください。");
        var game = locate(target.ProcessName);
        var size = clientSize(game.Window);
        runtime.Start(new RemoteViewSource(game.Window, size.Width, size.Height), game.ProcessId, game.ProcessName, current,
            $"{current.PublishUser}:{password}");
    }

    public Task StopAsync() => Task.Run(runtime.Stop);

    public RemoteViewSettingsView LoadSettings() => ViewOf(LoadFresh());

    public RemoteViewSettingsView SaveSettings(string publishUrl, string viewerUrl, string publishUser,
        string? publishPassword, RemoteViewQuality quality)
    {
        var next = new RemoteViewSettings(
            RemoteViewSettings.CurrentSchemaVersion, publishUrl, viewerUrl, publishUser, quality, LoadFresh().AcceptViewers);
        next.Validate();
        if (publishPassword is not null)
        {
            if (publishPassword.Length == 0)
            {
                throw new ArgumentException("パスワードが空です。変えない時は指定しないでください。", nameof(publishPassword));
            }

            secrets.Save(publishPassword);
        }

        settingsStore.Save(next);
        lock (gate)
        {
            settings = next;
        }

        return ViewOf(next);
    }

    public Task<RemoteViewSettingsView> SetAcceptViewersAsync(bool enabled) =>
        enabled ? Task.FromResult(SetAcceptViewers(true)) : Task.Run(() => SetAcceptViewers(false));

    public RemoteViewSettingsView SetAcceptViewers(bool enabled)
    {
        var current = LoadFresh();
        if (enabled)
        {
            if (string.IsNullOrEmpty(current.PublishUrl) || string.IsNullOrEmpty(current.PublishUser))
            {
                throw new InvalidOperationException("遠隔表示の送信先のURLとユーザー名が設定されていません。先に中継サーバーの設定を保存してください。");
            }

            if (secrets.Load() is null)
            {
                throw new InvalidOperationException("遠隔表示の送信用パスワードが保存されていません。");
            }

            // 問い合わせる先を決められない URL では受け付けを入れない（入れても見に来たことが分からない）。
            _ = HttpRemoteViewViewerQuery.ViewersUrl(current.PublishUrl);
        }

        var next = current with { AcceptViewers = enabled };
        settingsStore.Save(next);
        lock (gate)
        {
            settings = next;
        }

        if (!enabled)
        {
            // 切の間は送らない。送っている配信も止める。
            lock (pollGate)
            {
                policy.Reset();
                startError = null;
                runtime.Stop();
                SetWatch(0, null);
            }
        }

        return ViewOf(next);
    }

    public void Dispose()
    {
        watchStop.Cancel();
        watchThread?.Join(TimeSpan.FromSeconds(8));
        runtime.Stop();
        owned?.Dispose();
        (viewerQuery as IDisposable)?.Dispose();
        watchStop.Dispose();
    }

    private void SetWatch(int count, string? error)
    {
        lock (gate)
        {
            viewers = count;
            watchError = error;
        }
    }

    private RemoteViewSettings Settings()
    {
        lock (gate)
        {
            return settings ??= settingsStore.Load();
        }
    }

    private RemoteViewSettings LoadFresh()
    {
        var loaded = settingsStore.Load();
        lock (gate)
        {
            settings = loaded;
        }

        return loaded;
    }

    private RemoteViewSettingsView ViewOf(RemoteViewSettings value) =>
        new(value.PublishUrl, value.ViewerUrl, value.PublishUser, secrets.Load() is not null, value.Quality, value.AcceptViewers);
}
