using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class UserInputPauseStateTests
{
    [Theory]
    [InlineData(0xF0)]
    [InlineData(0xF1)]
    [InlineData(0xF2)]
    [InlineData(0xF3)]
    [InlineData(0xF4)]
    [InlineData(0xF5)]
    [InlineData(0xF6)]
    [InlineData(0xF7)]
    [InlineData(0xF8)]
    [InlineData(0xF9)]
    [InlineData(0xFA)]
    [InlineData(0xFB)]
    public void 日本語入力のモード状態はスキャンコードがあっても初期押下へ取り込まない(int vk)
    {
        Assert.False(WindowsUserInputMonitor.HasStartupPhysicalKey(vk, 0x29));
    }

    [Fact]
    public void スキャンコードに対応しないOS状態を起動前の実キー押下にしない()
    {
        Assert.False(WindowsUserInputMonitor.HasStartupPhysicalKey(0xF4, 0));
        Assert.True(WindowsUserInputMonitor.HasStartupPhysicalKey(0x41, 0x1E));
        Assert.True(WindowsUserInputMonitor.HasStartupPhysicalKey(1, 0));
    }
    [Fact]
    public void 前面化の準備中に手入力が始まったらタスクバーを押さない()
    {
        var canSend = true;
        var clicked = false;
        var success = WindowsTaskbarNanoWindowActivator.TryEnsureForeground(() => false, () =>
        {
            canSend = false;
            return () => { clicked = true; return new("テスト", 1, null, null, null); };
        }, () => canSend);
        Assert.False(success);
        Assert.False(clicked);
    }

    [Fact]
    public void Nanoの入力は無入力時間と押下数を変えない()
    {
        long now = 0;
        var state = new UserInputPauseState(() => now);
        Assert.True(state.Snapshot().Paused);
        now = 3000;
        Assert.False(state.Snapshot().Paused);
        now = 4000;
        state.NanoInput(); state.NanoInput();
        Assert.False(state.Snapshot().Paused);
        Assert.Equal(0, state.Snapshot().HeldCount);
        Assert.Equal(0, state.Snapshot().UserEvents);
        Assert.Equal(2, state.Snapshot().NanoEvents);
    }

    [Fact]
    public void 離した合図が届かない日本語入力のモードキーは押下に数えず三秒後に再開する()
    {
        long now = 0;
        var state = new UserInputPauseState(() => now);
        // 実測: 英数(0xF0)と半角/全角(0xF4)は押した合図だけが届き、OSも押下中と返し続ける。
        now = 10_000; WindowsUserInputMonitor.Key(state, 10, 0xF0, true);
        now = 10_100; WindowsUserInputMonitor.Key(state, 10, 0xF4, true);
        Assert.Equal(0, state.Snapshot().HeldCount);
        Assert.Equal(2, state.Snapshot().UserEvents);
        now = 13_099; Assert.True(state.Snapshot().Paused);
        now = 13_100; Assert.False(state.Snapshot().Paused);
        // 通常のキーは従来どおり、離すまで押下に数える。
        WindowsUserInputMonitor.Key(state, 10, 0x41, true);
        now = 60_000; Assert.True(state.Snapshot().Paused);
        Assert.Equal(1, state.Snapshot().HeldCount);
        WindowsUserInputMonitor.Key(state, 10, 0x41, false);
        now = 63_000; Assert.False(state.Snapshot().Paused);
    }

    [Fact]
    public void 離した合図を取りこぼしたキーは入力が三秒途絶えた時にOSの押下状態で外す()
    {
        // 実測: Botは押下1のまま9分半止まり、同じ時刻にOSが押下中と返すキーは無かった。
        long now = 0;
        var down = new HashSet<int> { 0x41, 0x10001 };
        var state = new UserInputPauseState(() => now, down.Contains);
        now = 10_000; state.Button(10, 0x41, true); state.Button(10, 0x10001, true);
        // 入力が続いている間はOSへ聞かず、届いた合図だけで数える。
        down.Clear();
        now = 12_999; Assert.Equal(2, state.Snapshot().HeldCount);
        Assert.Equal([0x41, 0x10001], state.Snapshot().HeldCodes!);
        // 3秒途絶えたらOSへ確かめ、離されているキーを外して再開する。
        now = 13_000;
        var resumed = state.Snapshot();
        Assert.False(resumed.Paused);
        Assert.Equal(0, resumed.HeldCount);
        Assert.Equal(2, resumed.LostReleases);
        // OSが押下中と返すキーは、押しっぱなしとして待ち続ける。
        down.Add(0x57);
        state.Button(10, 0x57, true);
        now = 60_000;
        Assert.True(state.Snapshot().Paused);
        Assert.Equal(2, state.Snapshot().LostReleases);
    }

    [Fact]
    public void 移動やホイール後に三秒待ち押しっぱなしなら再開しない()
    {
        long now = 4000;
        var state = new UserInputPauseState(() => now);
        now = 8000; state.Activity();
        now = 10999; Assert.True(state.Snapshot().Paused);
        now = 11000; Assert.False(state.Snapshot().Paused);
        state.Button(10, 65, true);
        now = 30000; Assert.True(state.Snapshot().Paused);
        state.Button(10, 65, false);
        now = 32999; Assert.True(state.Snapshot().Paused);
        now = 33000; Assert.False(state.Snapshot().Paused);
    }

    [Fact]
    public void 別デバイスの同じキーとマウスボタンは全て離れるまで待つ()
    {
        long now = 0;
        var state = new UserInputPauseState(() => now);
        state.Button(1, 65, true); state.Button(2, 65, true);
        state.Button(2, 65, true); // キーリピートで押下数を増やさない。
        state.Button(1, 0x10001, true);
        Assert.Equal(3, state.Snapshot().HeldCount);
        state.Button(1, 65, false); state.Button(2, 65, false);
        now = 10000; Assert.True(state.Snapshot().Paused);
        state.Button(1, 0x10001, false);
        now = 13000; Assert.False(state.Snapshot().Paused);
    }

    [Fact]
    public void 開始前の押下と切断も解放として扱える()
    {
        long now = 0;
        var state = new UserInputPauseState(() => now);
        state.SeedHeld(65);
        now = 10000; Assert.True(state.Snapshot().Paused);
        state.Button(5, 65, false);
        now = 13000; Assert.False(state.Snapshot().Paused);
        state.Button(5, 66, true); state.Removed(5);
        now = 16000; Assert.False(state.Snapshot().Paused);
    }

    [Theory]
    [InlineData(16, 0x2A, 0, 0xA0)]
    [InlineData(16, 0x36, 0, 0xA1)]
    [InlineData(17, 0x1D, 2, 0xA3)]
    [InlineData(18, 0x38, 0, 0xA4)]
    public void 左右の修飾キーを押下状態として区別する(int vk, int scan, int flags, int expected) =>
        Assert.Equal(expected, WindowsUserInputMonitor.KeyboardCode(vk, scan, flags));
}
