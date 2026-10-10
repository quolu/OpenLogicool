using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class PhysicalInputPauseBridgeTests
{
    private static (UserInputPauseState State, PhysicalInputPauseBridge Bridge) Create(Func<long> milliseconds)
    {
        PhysicalInputPauseBridge? bridge = null;
        // WindowsUserInputMonitorと同じ形: 橋渡しの符号だけ橋渡しへ、それ以外はOSの代わりに「離している」と答える。
        var state = new UserInputPauseState(milliseconds,
            code => code == PhysicalInputPauseBridge.Code && bridge!.IsHeld);
        bridge = new PhysicalInputPauseBridge(state);
        return (state, bridge);
    }

    [Fact]
    public void 観測の合間の短い押下でも一時停止になり五秒変化が無ければ解除する()
    {
        long now = 10_000;
        var (state, bridge) = Create(() => now);
        bridge.Apply(new ResidentPhysicalInput(0, 0));
        // 押して離すまでが観測の合間に済み、件数だけが2つ増えている。
        now = 20_000;
        bridge.Apply(new ResidentPhysicalInput(2, 0));
        var snapshot = state.Snapshot();
        Assert.True(snapshot.Paused);
        Assert.Equal(0, snapshot.HeldCount);
        Assert.Equal(1, snapshot.UserEvents);
        now = 24_999;
        bridge.Apply(new ResidentPhysicalInput(2, 0));
        Assert.True(state.Snapshot().Paused);
        now = 25_000;
        bridge.Apply(new ResidentPhysicalInput(2, 0));
        Assert.False(state.Snapshot().Paused);
    }

    [Fact]
    public void 押し続けは五秒を超えても一時停止のままで離してから五秒で解除する()
    {
        long now = 10_000;
        var (state, bridge) = Create(() => now);
        bridge.Apply(new ResidentPhysicalInput(0, 0));
        now = 20_000;
        bridge.Apply(new ResidentPhysicalInput(1, 1));
        Assert.Equal(1, state.Snapshot().HeldCount);
        Assert.Equal([PhysicalInputPauseBridge.Code], state.Snapshot().HeldCodes!);
        // 件数が変わらないまま長く押している間は、OSでなく橋渡しの押下で確かめて保つ。
        now = 60_000;
        bridge.Apply(new ResidentPhysicalInput(1, 1));
        var holding = state.Snapshot();
        Assert.True(holding.Paused);
        Assert.Equal(1, holding.HeldCount);
        Assert.Equal(0, holding.LostReleases);
        // 離した。
        now = 70_000;
        bridge.Apply(new ResidentPhysicalInput(2, 0));
        Assert.True(state.Snapshot().Paused);
        Assert.Equal(0, state.Snapshot().HeldCount);
        now = 74_999;
        bridge.Apply(new ResidentPhysicalInput(2, 0));
        Assert.True(state.Snapshot().Paused);
        now = 75_000;
        bridge.Apply(new ResidentPhysicalInput(2, 0));
        Assert.False(state.Snapshot().Paused);
    }

    [Fact]
    public void 常駐が無い観測では何も起きない()
    {
        long now = 10_000;
        var (state, bridge) = Create(() => now);
        bridge.Apply(null);
        now = 20_000;
        bridge.Apply(null);
        var snapshot = state.Snapshot();
        Assert.False(snapshot.Paused);
        Assert.Equal(0, snapshot.HeldCount);
        Assert.Equal(0, snapshot.UserEvents);
        Assert.False(bridge.IsHeld);
    }

    [Fact]
    public void 押下中に常駐が無くなったら押下を離す()
    {
        long now = 10_000;
        var (state, bridge) = Create(() => now);
        bridge.Apply(new ResidentPhysicalInput(1, 1));
        Assert.Equal(1, state.Snapshot().HeldCount);
        now = 20_000;
        bridge.Apply(null);
        Assert.False(bridge.IsHeld);
        Assert.Equal(0, state.Snapshot().HeldCount);
        // 常駐が戻った時の件数は0から数え直しなので、基準にするだけで手入力にしない。
        now = 30_000;
        bridge.Apply(new ResidentPhysicalInput(0, 0));
        Assert.False(state.Snapshot().Paused);
    }

    [Fact]
    public void 最初の観測は件数が0でなくても一時停止にしない()
    {
        long now = 10_000;
        var (state, bridge) = Create(() => now);
        now = 20_000;
        bridge.Apply(new ResidentPhysicalInput(5, 0));
        var snapshot = state.Snapshot();
        Assert.False(snapshot.Paused);
        Assert.Equal(0, snapshot.UserEvents);
        // 基準の後に増えた件数は手入力になる。
        bridge.Apply(new ResidentPhysicalInput(6, 0));
        Assert.True(state.Snapshot().Paused);
    }

    [Fact]
    public void 最初の観測が押下中なら押下として扱う()
    {
        long now = 10_000;
        var (state, bridge) = Create(() => now);
        now = 20_000;
        bridge.Apply(new ResidentPhysicalInput(5, 1));
        var snapshot = state.Snapshot();
        Assert.True(snapshot.Paused);
        Assert.Equal(1, snapshot.HeldCount);
        Assert.True(bridge.IsHeld);
    }
}
