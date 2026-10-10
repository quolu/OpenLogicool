using OpenLogicool.Contracts.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class RemoteViewFfmpegArgumentsTests
{
    private const string Url = "https://relay.example/game/whip";

    private static RemoteViewSettings Settings(RemoteViewQuality quality) =>
        new(RemoteViewSettings.CurrentSchemaVersion, Url, "https://relay.example/game", "publisher", quality);

    [Theory]
    [InlineData(RemoteViewQuality.Standard, 1280, 720, "3000k")]
    [InlineData(RemoteViewQuality.Fine, 1920, 1080, "8000k")]
    public void 画質ごとの大きさとビットレートが引数に入る(RemoteViewQuality quality, int width, int height, string bitrate)
    {
        var arguments = RemoteViewFfmpegArguments.Build(Settings(quality), 0x1A2B, "publisher:secret");

        Assert.Contains(
            $"gfxcapture=hwnd={0x1A2B}:max_framerate=30:width={width}:height={height}" +
            ":resize_mode=scale_aspect:capture_cursor=1",
            arguments);
        Assert.Equal(bitrate, ValueAfter(arguments, "-b:v"));
        Assert.Equal("0", ValueAfter(arguments, "-bf"));
        Assert.Equal("60", ValueAfter(arguments, "-g"));
    }

    [Fact]
    public void 音はs16leの48kHzステレオをstdinから受け_時間の上限を付けない()
    {
        var arguments = RemoteViewFfmpegArguments.Build(Settings(RemoteViewQuality.Standard), 1, "publisher:secret");

        var audioInput = arguments.ToList().IndexOf("pipe:0");
        Assert.Equal(["-f", "s16le", "-ar", "48000", "-ch_layout", "stereo", "-i", "pipe:0"],
            arguments.Skip(audioInput - 7).Take(8));
        Assert.DoesNotContain("-t", arguments);
    }

    [Fact]
    public void WHIPで認証を渡し_送り先のURLが末尾に来る()
    {
        var arguments = RemoteViewFfmpegArguments.Build(Settings(RemoteViewQuality.Fine), 1, "publisher:secret");

        Assert.Equal("whip", ValueAfter(arguments, "-f", occurrence: 3));
        Assert.Equal("publisher:secret", ValueAfter(arguments, "-authorization"));
        Assert.Equal(Url, arguments[^1]);
        Assert.Equal("-authorization", arguments[^3]);
    }

    [Fact]
    public void 送り先が未設定なら引数を作らない()
    {
        var settings = RemoteViewSettings.Default;

        Assert.Throws<InvalidOperationException>(() => RemoteViewFfmpegArguments.Build(settings, 1, "publisher:secret"));
    }

    private static string ValueAfter(IReadOnlyList<string> arguments, string option, int occurrence = 1)
    {
        var seen = 0;
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == option && ++seen == occurrence)
            {
                return arguments[index + 1];
            }
        }

        throw new Xunit.Sdk.XunitException($"引数に {option}（{occurrence} 個目）がありません。");
    }
}
