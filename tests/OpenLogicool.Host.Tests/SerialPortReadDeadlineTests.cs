using OpenLogicool.Host;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class SerialPortReadDeadlineTests
{
    [Fact]
    public void 正常ACKが受信済みなら処理が遅れても一回だけ読み取る()
    {
        var bytes = SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, 42, []);
        var deadline = new SerialHidReadDeadline(TimeSpan.FromMilliseconds(80));
        var assembler = new SerialHidResponseFrameAssembler();
        byte[]? response = null;
        for (var index = 0; index < bytes.Length; index++)
        {
            var elapsed = TimeSpan.FromMilliseconds(index < 3 ? 0 : 100);
            Assert.InRange(deadline.ReadTimeoutMilliseconds(elapsed, bytes.Length - index), 1, 80);
            response = assembler.Accept(bytes[index]) ?? response;
        }
        Assert.Equal((ushort)42, SerialHidProtocolV1.Decode(Assert.IsType<byte[]>(response)).Sequence);
    }

    [Fact]
    public void 期限で未受信なら追加の待機を与えない()
    {
        var deadline = new SerialHidReadDeadline(TimeSpan.FromMilliseconds(80));
        Assert.Equal(1, deadline.ReadTimeoutMilliseconds(TimeSpan.FromMilliseconds(79.5), 0));
        Assert.Throws<TimeoutException>(() => deadline.ReadTimeoutMilliseconds(TimeSpan.FromMilliseconds(80), 0));
    }

    [Fact]
    public void 期限後に到着する追加バイトで待機を延長しない()
    {
        var deadline = new SerialHidReadDeadline(TimeSpan.FromMilliseconds(80));
        for (var index = 0; index < 3; index++)
            Assert.Equal(1, deadline.ReadTimeoutMilliseconds(TimeSpan.FromMilliseconds(100), 3 + index));
        Assert.Throws<TimeoutException>(() => deadline.ReadTimeoutMilliseconds(TimeSpan.FromMilliseconds(101), 100));
    }
}
