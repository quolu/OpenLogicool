using System.IO;
using OpenLogicool.Contracts.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostRemoteViewIntentsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"remote-view-intents-{Guid.NewGuid():N}");
    private readonly RemoteViewSettingsStoreTests.FakeSecretStore secrets = new();
    private IReadOnlyList<string>? launchedArguments;
    private int audioProcessId;

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
            arguments =>
            {
                launchedArguments = arguments;
                return new IdleEncoder();
            },
            processId =>
            {
                Volatile.Write(ref audioProcessId, processId);
                return new SilentAudio();
            });
        return new HostRemoteViewIntents(
            RemoteViewSettingsStore.ForDatabase(Path.Combine(directory, "input-studio.db")),
            secrets,
            new MacroTargetSettingsStore(directory),
            processName => new WindowsGameTarget(
                0x3030, 77, processName, "title", new GameCaptureScreenBounds(0, 0, 1, 1), @"C:\Games\game.exe"),
            _ => (1711, 1085),
            runtime);
    }

    private void SaveCompleteSettings(HostRemoteViewIntents intents) => intents.SaveSettings(
        "https://relay.example/game/whip", "https://relay.example/game", "publisher", "secretpw", RemoteViewQuality.Standard);

    [Fact]
    public async Task 設定と秘密と対象が揃えば開始でき_停止で戻る()
    {
        var intents = Intents();
        SaveCompleteSettings(intents);
        new MacroTargetSettingsStore(directory).Save("Game");

        intents.Start();

        // 音源は配信の thread で作られる。対象の process id がそこへ渡ること。
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref audioProcessId) == 77, TimeSpan.FromSeconds(10)));
        Assert.Equal("publisher:secretpw", ValueAfter(launchedArguments!, "-authorization"));
        var capture = launchedArguments!.First(argument => argument.StartsWith("gfxcapture", StringComparison.Ordinal));
        Assert.Contains($"gfxcapture=hwnd={0x3030}:", capture);
        // 窓の描画領域（1711x1085）と同じ形で、720p の枠に収まる大きさで送る。
        Assert.Contains(":width=1136:height=720:", capture);
        var running = intents.Current();
        Assert.Equal("Game", running.TargetProcessName);
        Assert.Equal("https://relay.example/game", running.ViewerUrl);
        Assert.NotEqual(RemoteViewPhase.Stopped, running.Phase);
        Assert.DoesNotContain("secretpw", running.Detail);

        await intents.StopAsync();
        Assert.Equal(RemoteViewPhase.Stopped, intents.Current().Phase);
    }

    [Fact]
    public void 設定が無い時は開始せず明示のエラーにする()
    {
        var intents = Intents();

        var error = Assert.Throws<InvalidOperationException>(intents.Start);
        Assert.Contains("送信先", error.Message);
        Assert.Null(launchedArguments);
    }

    [Fact]
    public void 送信用パスワードが無い時は開始しない()
    {
        var intents = Intents();
        SaveCompleteSettings(intents);
        new MacroTargetSettingsStore(directory).Save("Game");
        secrets.Delete();

        var error = Assert.Throws<InvalidOperationException>(intents.Start);
        Assert.Contains("パスワード", error.Message);
        Assert.Null(launchedArguments);
    }

    [Fact]
    public void 対象のゲームが未設定の時は開始しない()
    {
        var intents = Intents();
        SaveCompleteSettings(intents);

        var error = Assert.Throws<InvalidOperationException>(intents.Start);
        Assert.Contains("対象", error.Message);
        Assert.Null(launchedArguments);
    }

    private static string ValueAfter(IReadOnlyList<string> arguments, string option)
    {
        var index = arguments.ToList().IndexOf(option);
        Assert.True(index >= 0 && index + 1 < arguments.Count, $"{option} がありません。");
        return arguments[index + 1];
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
