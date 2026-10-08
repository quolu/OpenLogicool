using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class UserInputPauseStateTests
{
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
