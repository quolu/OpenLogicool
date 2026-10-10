namespace OpenLogicool.Contracts.Playbooks;

/// <summary>Standard=1280x720・30fps・3000kbps、Fine=1920x1080・30fps・8000kbps。</summary>
public enum RemoteViewQuality { Standard, Fine }

public enum RemoteViewPhase { Stopped, Starting, Streaming, Faulted }

/// <param name="AcceptingViewers">遠隔表示を受け付けている（見に来た時に送り始める）か。</param>
/// <param name="Viewers">中継サーバーで、いま見ている・待っている端末の数。受け付けていない間は 0。</param>
public sealed record RemoteViewSnapshot(RemoteViewPhase Phase, string Detail, RemoteViewQuality Quality,
    string? TargetProcessName, double StreamedSeconds, long VideoFrames, bool VideoStalled, string? ViewerUrl,
    bool AcceptingViewers = false, int Viewers = 0);

/// <summary>保存済みの設定の表示用。パスワードは持たず、保存済みかどうかだけを返す。</summary>
public sealed record RemoteViewSettingsView(string? PublishUrl, string? ViewerUrl, string? PublishUser,
    bool HasPublishPassword, RemoteViewQuality Quality, bool AcceptViewers = false);

public interface IRemoteViewIntents
{
    RemoteViewSnapshot Current();
    void Start();
    Task StopAsync();
    RemoteViewSettingsView LoadSettings();

    /// <summary>publishPassword が null の時は、保存済みのパスワードを変えない。</summary>
    RemoteViewSettingsView SaveSettings(string publishUrl, string viewerUrl, string publishUser,
        string? publishPassword, RemoteViewQuality quality);

    /// <summary>
    /// 遠隔表示を受け付けるかを保存する。入の間は、見る URL を開いた端末がいる時だけ送り、いなくなると止める。
    /// 切にした時は、送っている配信も止める（止まるまで待てる）。アプリを起動し直しても覚えている。
    /// </summary>
    Task<RemoteViewSettingsView> SetAcceptViewersAsync(bool enabled);
}
