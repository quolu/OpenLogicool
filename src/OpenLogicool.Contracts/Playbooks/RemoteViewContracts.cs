namespace OpenLogicool.Contracts.Playbooks;

/// <summary>Standard=1280x720・30fps・3000kbps、Fine=1920x1080・30fps・8000kbps。</summary>
public enum RemoteViewQuality { Standard, Fine }

public enum RemoteViewPhase { Stopped, Starting, Streaming, Faulted }

public sealed record RemoteViewSnapshot(RemoteViewPhase Phase, string Detail, RemoteViewQuality Quality,
    string? TargetProcessName, double StreamedSeconds, long VideoFrames, bool VideoStalled, string? ViewerUrl);

/// <summary>保存済みの設定の表示用。パスワードは持たず、保存済みかどうかだけを返す。</summary>
public sealed record RemoteViewSettingsView(string? PublishUrl, string? ViewerUrl, string? PublishUser,
    bool HasPublishPassword, RemoteViewQuality Quality);

public interface IRemoteViewIntents
{
    RemoteViewSnapshot Current();
    void Start();
    Task StopAsync();
    RemoteViewSettingsView LoadSettings();

    /// <summary>publishPassword が null の時は、保存済みのパスワードを変えない。</summary>
    RemoteViewSettingsView SaveSettings(string publishUrl, string viewerUrl, string publishUser,
        string? publishPassword, RemoteViewQuality quality);
}
