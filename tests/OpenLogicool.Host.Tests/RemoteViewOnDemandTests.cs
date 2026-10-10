using System.IO;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class RemoteViewOnDemandPolicyTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(15);

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void 誰もいない所へ見に来たら1回だけ開始を出す()
    {
        var policy = new RemoteViewOnDemandPolicy(Idle);

        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(0), viewers: 0, running: false));
        Assert.Equal(RemoteViewOnDemandAction.Start, policy.Step(At(1), viewers: 1, running: false));
        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(2), viewers: 1, running: false));
        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(3), viewers: 2, running: true));
    }

    [Fact]
    public void いなくなって決めた時間がたつまでは止めない()
    {
        var policy = new RemoteViewOnDemandPolicy(Idle);
        policy.Step(At(0), viewers: 1, running: false);

        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(10), viewers: 0, running: true));
        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(24.9), viewers: 0, running: true));
        Assert.Equal(RemoteViewOnDemandAction.Stop, policy.Step(At(25), viewers: 0, running: true));
    }

    [Fact]
    public void ページの再試行の合間に一瞬いなくなっても止めず送り直しもしない()
    {
        var policy = new RemoteViewOnDemandPolicy(Idle);
        policy.Step(At(0), viewers: 1, running: false);

        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(20), viewers: 0, running: true));
        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(22), viewers: 1, running: true));
        // 戻ってきた時点で、いない時間の数え直しになる。
        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(30), viewers: 0, running: true));
        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(44), viewers: 0, running: true));
        Assert.Equal(RemoteViewOnDemandAction.Stop, policy.Step(At(45), viewers: 0, running: true));
    }

    [Fact]
    public void 配信が失敗しても見ている端末がいる間は送り直さず_いなくなってから次に見に来た時に送り直す()
    {
        var policy = new RemoteViewOnDemandPolicy(Idle);
        Assert.Equal(RemoteViewOnDemandAction.Start, policy.Step(At(0), viewers: 1, running: false));

        // 送り始めた後に失敗して止まっている。見ている端末は再試行を続けている。
        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(5), viewers: 1, running: false));
        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(60), viewers: 1, running: false));

        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(61), viewers: 0, running: false));
        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(76), viewers: 0, running: false));
        Assert.Equal(RemoteViewOnDemandAction.Start, policy.Step(At(80), viewers: 1, running: false));
    }

    [Fact]
    public void 誰も見ていないのに動いている配信は決めた時間の後に止める()
    {
        var policy = new RemoteViewOnDemandPolicy(Idle);

        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(0), viewers: 0, running: true));
        Assert.Equal(RemoteViewOnDemandAction.Stop, policy.Step(At(15), viewers: 0, running: true));
    }

    [Fact]
    public void すでに動いている所へ見に来た時は開始を出さない()
    {
        var policy = new RemoteViewOnDemandPolicy(Idle);

        Assert.Equal(RemoteViewOnDemandAction.None, policy.Step(At(0), viewers: 1, running: true));
    }
}

public sealed class HttpRemoteViewViewerQueryTests
{
    [Theory]
    [InlineData("https://stream.example.com/game/whip", "https://stream.example.com/game/viewers", "game")]
    [InlineData("https://stream.example.com/rooms/main/whip", "https://stream.example.com/rooms/main/viewers", "rooms/main")]
    [InlineData("http://127.0.0.1:8889/game/whip", "http://127.0.0.1:8889/game/viewers", "game")]
    public void 聞く先とpathの名前は送り先のURLから決まる(string publishUrl, string viewersUrl, string pathName)
    {
        Assert.Equal(viewersUrl, HttpRemoteViewViewerQuery.ViewersUrl(publishUrl).ToString());
        Assert.Equal(pathName, HttpRemoteViewViewerQuery.PathName(publishUrl));
    }

    [Theory]
    [InlineData("https://stream.example.com/game")]
    [InlineData("https://stream.example.com/whip")]
    [InlineData("not a url")]
    public void whipで終わらない送り先では聞く先を決めない(string publishUrl)
    {
        var error = Assert.Throws<InvalidOperationException>(() => HttpRemoteViewViewerQuery.ViewersUrl(publishUrl));
        Assert.Contains("whip", error.Message);
    }

    [Fact]
    public void 見ている接続と送り手を待っている接続だけを_そのpathのぶんだけ数える()
    {
        const string json = """
            {"itemCount":5,"pageCount":1,"items":[
              {"id":"a","state":"read","path":"game","peerConnectionEstablished":true},
              {"id":"b","state":"read","path":"game","peerConnectionEstablished":false},
              {"id":"c","state":"publish","path":"game","peerConnectionEstablished":true},
              {"id":"d","state":"read","path":"other","peerConnectionEstablished":true},
              {"id":"e","state":"idle","path":""}
            ]}
            """;

        Assert.Equal(2, HttpRemoteViewViewerQuery.CountReaders(json, "game"));
        Assert.Equal(1, HttpRemoteViewViewerQuery.CountReaders(json, "other"));
        Assert.Equal(0, HttpRemoteViewViewerQuery.CountReaders("""{"itemCount":0,"pageCount":0,"items":[]}""", "game"));
    }

    [Fact]
    public void 一覧の無い応答は数えずに明示のエラーにする()
    {
        Assert.Throws<InvalidDataException>(() => HttpRemoteViewViewerQuery.CountReaders("""{"error":"x"}""", "game"));
    }
}

public sealed class HostRemoteViewOnDemandTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"remote-view-ondemand-{Guid.NewGuid():N}");
    private readonly RemoteViewSettingsStoreTests.FakeSecretStore secrets = new();
    private readonly FakeQuery query = new();
    private TimeSpan now;
    private int launches;

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private HostRemoteViewIntents Intents()
    {
        var runtime = new RemoteViewRuntime(
            _ =>
            {
                Interlocked.Increment(ref launches);
                return new IdleEncoder();
            },
            _ => new SilentAudio());
        return new HostRemoteViewIntents(
            RemoteViewSettingsStore.ForDatabase(Path.Combine(directory, "input-studio.db")),
            secrets,
            new MacroTargetSettingsStore(directory),
            processName => new WindowsGameTarget(
                0x3030, 77, processName, "title", new GameCaptureScreenBounds(0, 0, 1, 1), @"C:\Games\game.exe"),
            _ => (1280, 720),
            runtime,
            owned: null,
            viewerQuery: query,
            clock: () => now);
    }

    private HostRemoteViewIntents ReadyIntents()
    {
        var intents = Intents();
        intents.SaveSettings("https://relay.example/game/whip", "https://relay.example/game/", "publisher", "secretpw", RemoteViewQuality.Standard);
        new MacroTargetSettingsStore(directory).Save("Game");
        return intents;
    }

    [Fact]
    public void 受け付けていない間は聞きに行かず送らない()
    {
        using var intents = ReadyIntents();
        query.Viewers = 3;

        intents.PollViewers();

        Assert.Equal(0, query.Calls);
        Assert.Equal(0, launches);
        var current = intents.Current();
        Assert.False(current.AcceptingViewers);
        Assert.Equal(RemoteViewPhase.Stopped, current.Phase);
    }

    [Fact]
    public void 受け付けている間は_見に来た時に送り始め_いなくなって決めた時間の後に止める()
    {
        using var intents = ReadyIntents();
        Assert.True(intents.SetAcceptViewers(true).AcceptViewers);

        intents.PollViewers();
        Assert.Equal(0, launches);
        Assert.Equal("見に来るのを待っています。", intents.Current().Detail);
        Assert.True(intents.Current().AcceptingViewers);

        query.Viewers = 1;
        now = TimeSpan.FromSeconds(1);
        intents.PollViewers();
        Assert.Equal(1, launches);
        Assert.Equal(("https://relay.example/game/whip", "publisher:secretpw"), query.LastRequest);
        Assert.NotEqual(RemoteViewPhase.Stopped, intents.Current().Phase);
        Assert.Equal(1, intents.Current().Viewers);

        // 見ている間は送り続ける。
        now = TimeSpan.FromSeconds(40);
        intents.PollViewers();
        Assert.Equal(1, launches);

        query.Viewers = 0;
        now = TimeSpan.FromSeconds(41);
        intents.PollViewers();
        Assert.NotEqual(RemoteViewPhase.Stopped, intents.Current().Phase);

        now = TimeSpan.FromSeconds(41) + HostRemoteViewIntents.IdleStop;
        intents.PollViewers();
        Assert.Equal(RemoteViewPhase.Stopped, intents.Current().Phase);
        Assert.Equal(1, launches);
    }

    [Fact]
    public void 入り切りは保存され_作り直した入口でも続く()
    {
        using (var first = ReadyIntents())
        {
            first.SetAcceptViewers(true);
        }

        using var second = Intents();
        Assert.True(second.LoadSettings().AcceptViewers);
        Assert.True(second.Current().AcceptingViewers);

        query.Viewers = 1;
        second.PollViewers();
        Assert.Equal(1, launches);
    }

    [Fact]
    public void 設定を保存し直しても受け付けの入り切りは変わらない()
    {
        using var intents = ReadyIntents();
        intents.SetAcceptViewers(true);

        var view = intents.SaveSettings("https://relay.example/game/whip", "https://relay.example/game/", "publisher", null, RemoteViewQuality.Fine);

        Assert.True(view.AcceptViewers);
        Assert.Equal(RemoteViewQuality.Fine, view.Quality);
    }

    [Fact]
    public void 切にすると送っている配信も止まり_見ている端末がいても送らない()
    {
        using var intents = ReadyIntents();
        intents.SetAcceptViewers(true);
        query.Viewers = 1;
        intents.PollViewers();
        Assert.Equal(1, launches);

        Assert.False(intents.SetAcceptViewers(false).AcceptViewers);
        Assert.Equal(RemoteViewPhase.Stopped, intents.Current().Phase);

        var callsBefore = query.Calls;
        intents.PollViewers();
        Assert.Equal(callsBefore, query.Calls);
        Assert.Equal(1, launches);
    }

    [Fact]
    public void 聞けなかった回は送り始めも止めもせず_理由を状態へ出す()
    {
        using var intents = ReadyIntents();
        intents.SetAcceptViewers(true);
        query.Error = new System.Net.Http.HttpRequestException("名前を引けません");

        intents.PollViewers();

        Assert.Equal(0, launches);
        var current = intents.Current();
        Assert.Equal(RemoteViewPhase.Stopped, current.Phase);
        Assert.Contains("問い合わせできません", current.Detail);
        Assert.Contains("名前を引けません", current.Detail);
        Assert.DoesNotContain("secretpw", current.Detail);

        // 次の回に聞ければ、そのまま進む。
        query.Error = null;
        query.Viewers = 1;
        intents.PollViewers();
        Assert.Equal(1, launches);
    }

    [Fact]
    public void 対象のゲームが無くて送り始められない時は_理由を状態へ出して送り直しを繰り返さない()
    {
        using var intents = Intents();
        intents.SaveSettings("https://relay.example/game/whip", "https://relay.example/game/", "publisher", "secretpw", RemoteViewQuality.Standard);
        intents.SetAcceptViewers(true);
        query.Viewers = 1;

        intents.PollViewers();
        Assert.Equal(0, launches);
        Assert.Contains("対象", intents.Current().Detail);

        new MacroTargetSettingsStore(directory).Save("Game");
        now = TimeSpan.FromSeconds(5);
        intents.PollViewers();
        // 見ている端末が居続ける間は、自動では送り直さない。理由は出したままにする。
        Assert.Equal(0, launches);
        Assert.Contains("対象", intents.Current().Detail);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 設定が揃っていない時は受け付けを入れない(bool withSettings)
    {
        using var intents = Intents();
        if (withSettings)
        {
            intents.SaveSettings("https://relay.example/game/whip", "https://relay.example/game/", "publisher", "secretpw", RemoteViewQuality.Standard);
            secrets.Delete();
        }

        Assert.Throws<InvalidOperationException>(() => intents.SetAcceptViewers(true));
        Assert.False(intents.LoadSettings().AcceptViewers);
    }

    [Fact]
    public void 聞く先を決められない送り先では受け付けを入れない()
    {
        using var intents = Intents();
        intents.SaveSettings("https://relay.example/game", "https://relay.example/game/", "publisher", "secretpw", RemoteViewQuality.Standard);

        var error = Assert.Throws<InvalidOperationException>(() => intents.SetAcceptViewers(true));
        Assert.Contains("whip", error.Message);
        Assert.False(intents.LoadSettings().AcceptViewers);
    }

    private sealed class FakeQuery : IRemoteViewViewerQuery
    {
        public int Viewers;
        public int Calls;
        public Exception? Error;
        public (string PublishUrl, string Authorization) LastRequest;

        public int CountViewers(string publishUrl, string authorization)
        {
            Calls++;
            LastRequest = (publishUrl, authorization);
            if (Error is not null) throw Error;
            return Viewers;
        }
    }

    private sealed class IdleEncoder : IRemoteViewEncoderProcess
    {
        private readonly System.Collections.Concurrent.BlockingCollection<string> lines = [];
        private volatile bool exited;

        public Stream StandardInput { get; } = Stream.Null;

        public bool HasExited => exited;

        public int? ExitCode => exited ? 0 : null;

        public IEnumerable<string> ReadErrorLines() => lines.GetConsumingEnumerable();

        public void CloseStandardInput() => Exit();

        public bool WaitForExit(TimeSpan timeout) => SpinWait.SpinUntil(() => exited, timeout);

        public void Kill() => Exit();

        public void Dispose()
        {
        }

        private void Exit()
        {
            exited = true;
            lines.CompleteAdding();
        }
    }

    private sealed class SilentAudio : IRemoteViewAudioSource
    {
        public int ReadStereo(Span<short> interleaved) => 0;

        public void Dispose()
        {
        }
    }
}
