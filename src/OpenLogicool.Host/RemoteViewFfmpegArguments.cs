using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Host;

/// <summary>画質ごとの映像の大きさ・コマ数・ビットレート。</summary>
public sealed record RemoteViewQualityProfile(int Width, int Height, int Fps, int VideoKbps)
{
    public static RemoteViewQualityProfile For(RemoteViewQuality quality) => quality switch
    {
        RemoteViewQuality.Standard => new(1280, 720, 30, 3000),
        RemoteViewQuality.Fine => new(1920, 1080, 30, 8000),
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "未対応の画質です。"),
    };
}

/// <summary>
/// 遠隔表示の ffmpeg の引数を組み立てる pure な関数。
/// 映像は ffmpeg が窓を自分で取り込み、音は stdin から s16le の 48kHz ステレオで受ける。
/// 停止は stdin を閉じて行うので、時間の上限（-t）は付けない。
/// </summary>
public static class RemoteViewFfmpegArguments
{
    public const int AudioSampleRate = 48000;

    /// <summary>
    /// 取り込みの上限を、送るコマ数の何倍にするか。同じ値にすると、ゲームの描画の間合いと噛み合わず、
    /// 送るコマ数へ揃える段で同じコマが毎秒2回ほど繰り返される（15秒で28〜36回の実測。2倍で0回）。
    /// </summary>
    public const int CaptureOversampling = 2;

    /// <summary>
    /// WHIP の UDP 送信の溜め場。ffmpeg の既定のままだと、別の機器の中継サーバーへ送る時に
    /// 最初のコマで送信が詰まって終了する（4MiB で 720p・3Mbps を20秒送り切った実測）。
    /// </summary>
    public const int WhipSendBufferBytes = 4 * 1024 * 1024;

    public static IReadOnlyList<string> Build(RemoteViewSettings settings, nint window, string authorization)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(authorization);
        if (settings.PublishUrl is null)
        {
            throw new InvalidOperationException("送信先のURLが設定されていません。");
        }

        var profile = RemoteViewQualityProfile.For(settings.Quality);
        return
        [
            "-hide_banner",
            "-loglevel", "info",
            "-f", "lavfi",
            "-i",
            $"gfxcapture=hwnd={window}:max_framerate={profile.Fps * CaptureOversampling}:width={profile.Width}:height={profile.Height}" +
            ":resize_mode=scale_aspect:capture_cursor=1",
            "-f", "s16le",
            "-ar", AudioSampleRate.ToString(),
            "-ch_layout", "stereo",
            "-i", "pipe:0",
            "-fps_mode", "cfr",
            "-r", profile.Fps.ToString(),
            "-c:v", "h264_nvenc",
            "-preset", "p1",
            "-tune", "ull",
            "-zerolatency", "1",
            "-rc", "cbr",
            "-b:v", $"{profile.VideoKbps}k",
            "-bf", "0",
            "-g", (profile.Fps * 2).ToString(),
            "-c:a", "libopus",
            "-b:a", "96k",
            "-application", "lowdelay",
            "-ac", "2",
            "-f", "whip",
            "-ts_buffer_size", WhipSendBufferBytes.ToString(),
            "-authorization", authorization,
            settings.PublishUrl,
        ];
    }
}
