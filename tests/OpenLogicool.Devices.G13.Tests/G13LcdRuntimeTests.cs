using OpenLogicool.Devices.G13;
using Xunit;

namespace OpenLogicool.Devices.G13.Tests;

public sealed class G13LcdRuntimeTests
{
    [Fact]
    public void Latest_frame_is_written_once_and_unchanged_state_is_not_rewritten()
    {
        var transport = new FakeTransport { ConnectionKey = "device-a" };
        using var runtime = new G13LcdRuntime(transport, TimeSpan.FromHours(1));
        runtime.RequestFrame(Frame(0x11));
        runtime.RequestFrame(Frame(0x22));
        runtime.Start();

        Assert.True(SpinWait.SpinUntil(() => runtime.Status.AppliedRevision == 2, TimeSpan.FromSeconds(1)));
        runtime.RunOnce();

        Assert.Single(transport.Reports);
        Assert.Equal(0x22, transport.Reports[0][G13LcdFrame.HeaderLength]);
        Assert.Equal(2, runtime.Status.AppliedRevision);
        runtime.Stop(clearDisplay: false);
    }

    [Fact]
    public void Reconnection_rewrites_the_latest_frame_without_a_new_request()
    {
        var transport = new FakeTransport { ConnectionKey = "device-a" };
        using var runtime = new G13LcdRuntime(transport, TimeSpan.FromHours(1));
        runtime.RequestFrame(Frame(0x33));
        runtime.Start();
        Assert.True(SpinWait.SpinUntil(() => runtime.Status.AppliedRevision == 1, TimeSpan.FromSeconds(1)));

        transport.ConnectionKey = null;
        runtime.RunOnce();
        transport.ConnectionKey = "device-b";
        runtime.RunOnce();

        Assert.Equal(2, transport.Reports.Count);
        Assert.True(runtime.Status.IsConnected);
        Assert.Null(runtime.Status.Failure);
        runtime.Stop(clearDisplay: false);
    }

    [Fact]
    public void Transport_fault_is_visible_and_does_not_advance_applied_revision()
    {
        var transport = new FakeTransport { ConnectionKey = "device-a", Failure = new IOException("write fault") };
        using var runtime = new G13LcdRuntime(transport, TimeSpan.FromHours(1));
        runtime.RequestFrame(Frame(0x44));
        runtime.Start();
        Assert.True(SpinWait.SpinUntil(() => runtime.Status.Failure is not null, TimeSpan.FromSeconds(1)));

        Assert.False(runtime.Status.IsConnected);
        Assert.Equal(0, runtime.Status.AppliedRevision);
        Assert.Equal("write fault", runtime.Status.Failure);
        runtime.Stop(clearDisplay: false);
    }

    private static byte[] Frame(byte value) =>
        Enumerable.Repeat(value, G13LcdFrame.FramebufferLength).ToArray();

    private sealed class FakeTransport : IG13LcdTransport
    {
        public string? ConnectionKey { get; set; }

        public Exception? Failure { get; set; }

        public List<byte[]> Reports { get; } = [];

        public string? TryGetConnectionKey() => ConnectionKey;

        public int Write(ReadOnlyMemory<byte> report)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Reports.Add(report.ToArray());
            return report.Length;
        }
    }
}
