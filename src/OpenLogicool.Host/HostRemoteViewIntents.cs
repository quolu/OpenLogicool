using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Host;

/// <summary>
/// 遠隔表示（ゲームの窓の映像と音を、利用者の中継サーバーへ送る）の操作。
/// 対象のゲームは Game Operator の「対象」（MacroTargetSettingsStore）で決める。設定・秘密・対象が揃わない時は開始しない。
/// </summary>
public sealed class HostRemoteViewIntents : IRemoteViewIntents, IDisposable
{
    private readonly RemoteViewSettingsStore settingsStore;
    private readonly IRemoteViewSecretStore secrets;
    private readonly MacroTargetSettingsStore targetStore;
    private readonly Func<string, WindowsGameTarget> locate;
    private readonly RemoteViewRuntime runtime;
    private readonly IDisposable? owned;
    private readonly object gate = new();
    private RemoteViewSettings? settings;

    public HostRemoteViewIntents(
        RemoteViewSettingsStore settingsStore,
        IRemoteViewSecretStore secrets,
        MacroTargetSettingsStore targetStore,
        Func<string, WindowsGameTarget> locate,
        RemoteViewRuntime runtime,
        IDisposable? owned = null)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        this.targetStore = targetStore ?? throw new ArgumentNullException(nameof(targetStore));
        this.locate = locate ?? throw new ArgumentNullException(nameof(locate));
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.owned = owned;
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
            new RemoteViewRuntime(launcher.Launch, ProcessLoopbackRemoteViewAudioSource.Create),
            launcher);
    }

    public RemoteViewSnapshot Current()
    {
        var status = runtime.Status;
        var current = Settings();
        return new RemoteViewSnapshot(
            status.Phase,
            status.Detail,
            current.Quality,
            status.TargetProcessName,
            status.StreamedSeconds,
            status.VideoFrames,
            status.VideoStalled,
            current.ViewerUrl);
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
        runtime.Start(game.Window, game.ProcessId, game.ProcessName, current, $"{current.PublishUser}:{password}");
    }

    public Task StopAsync() => Task.Run(runtime.Stop);

    public RemoteViewSettingsView LoadSettings() => ViewOf(LoadFresh());

    public RemoteViewSettingsView SaveSettings(string publishUrl, string viewerUrl, string publishUser,
        string? publishPassword, RemoteViewQuality quality)
    {
        var next = new RemoteViewSettings(
            RemoteViewSettings.CurrentSchemaVersion, publishUrl, viewerUrl, publishUser, quality);
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

    public void Dispose()
    {
        runtime.Stop();
        owned?.Dispose();
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
        new(value.PublishUrl, value.ViewerUrl, value.PublishUser, secrets.Load() is not null, value.Quality);
}
