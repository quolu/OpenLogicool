using System.Globalization;
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

    /// <summary>
    /// 取り込む窓の描画領域と同じ形で、この画質の枠に収まる送る大きさ（偶数）。拡大はしない。
    /// 枠の大きさのまま送ると、形の違う窓の絵が枠の左へ詰められ、黒い帯が付いて視聴側で小さく・片寄って見える。
    /// </summary>
    public (int Width, int Height) Fit(int sourceWidth, int sourceHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        var scale = Math.Min(1.0, Math.Min((double)Width / sourceWidth, (double)Height / sourceHeight));
        return (Math.Min(Width, Even(sourceWidth * scale)), Math.Min(Height, Even(sourceHeight * scale)));

        static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2.0, MidpointRounding.AwayFromZero) * 2);
    }
}

/// <summary>取り込む窓と、その描画領域の大きさ。</summary>
public sealed record RemoteViewSource(nint Window, int Width, int Height);

/// <summary>
/// 遠隔表示の ffmpeg の引数を組み立てる pure な関数。
/// 映像は ffmpeg が窓を自分で取り込み、音は stdin から s16le の 48kHz ステレオで受ける。
/// 停止は stdin を閉じて行うので、時間の上限（-t）は付けない。
/// <para>
/// 映像と音の時刻は、どちらも ffmpeg が読んだ時の現在時刻で振らせ、同じ起点（起動した時刻）を引く。
/// 音は時刻を持たずに届くので、届いた量から時刻を数えると、渡せなかった間に失った音のぶんだけ映像より遅れたままになる。
/// ffmpeg は遅れている音を待って映像の取り込みを止めるため、同じ絵の繰り返しが配信の終わりまで続く
/// （中継サーバーとつなぐ最初の約1秒で起きる。20秒で593コマ中416コマの実測。同じ時計にすると最初の43コマだけ）。
/// </para>
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

    public static IReadOnlyList<string> Build(
        RemoteViewSettings settings, RemoteViewSource source, string authorization, DateTimeOffset clockOrigin)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(authorization);
        if (settings.PublishUrl is null)
        {
            throw new InvalidOperationException("送信先のURLが設定されていません。");
        }

        var profile = RemoteViewQualityProfile.For(settings.Quality);
        var (width, height) = profile.Fit(source.Width, source.Height);
        var originOffset = "-" + (clockOrigin.ToUnixTimeMilliseconds() / 1000.0).ToString("F3", CultureInfo.InvariantCulture);
        return
        [
            "-hide_banner",
            "-loglevel", "info",
            "-use_wallclock_as_timestamps", "1",
            "-itsoffset", originOffset,
            "-f", "lavfi",
            "-i",
            $"gfxcapture=hwnd={source.Window}:max_framerate={profile.Fps * CaptureOversampling}:width={width}:height={height}" +
            ":resize_mode=scale_aspect:capture_cursor=1",
            "-use_wallclock_as_timestamps", "1",
            "-itsoffset", originOffset,
            "-f", "s16le",
            "-ar", AudioSampleRate.ToString(),
            "-ch_layout", "stereo",
            "-i", "pipe:0",
            "-copyts",
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
            "-af", "aresample=async=1",
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
