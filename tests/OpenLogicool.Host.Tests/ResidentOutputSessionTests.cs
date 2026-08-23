using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G600;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class ResidentOutputSessionTests
{
    [Fact]
    public void Serial_hid_session_uses_serial_emitter_without_windows_watchdog()
    {
        using var exchange = new FakeExchange();
        using var session = CreateSession(exchange);

        session.Start();

        Assert.Equal(ResidentOutputRoute.SerialHid, session.Route);
        Assert.IsType<SerialHidEmitter>(session.Emitter);
        Assert.Null(session.BackgroundFailure);
    }

    [Fact]
    public void Handled_stop_releases_owned_state_then_all_up_ack_then_closes_transport()
    {
        var exchange = new FakeExchange();
        using var session = CreateSession(exchange);
        session.Start();
        session.Emitter.Emit([Down("Key:A"), Down("Mouse:Left")]);
        session.Emitter.Emit([Up("Key:A"), Up("Mouse:Left")]);

        session.Stop();

        var kinds = exchange.RequestKinds();
        Assert.Equal(SerialHidMessageKind.Hello, kinds[0]);
        Assert.Equal(2, kinds.Count(kind => kind == SerialHidMessageKind.SetState));
        Assert.Equal(SerialHidMessageKind.AllUp, kinds[^1]);
        Assert.Equal(1, exchange.DisposeCount);
    }

    [Fact]
    public void Heartbeat_fault_propagates_and_stop_does_not_retry_or_implicitly_resume()
    {
        var exchange = new FakeExchange { FailHeartbeat = true };
        using var session = CreateSession(exchange, TimeSpan.FromMilliseconds(1));
        session.Start();

        Assert.True(SpinWait.SpinUntil(() => session.BackgroundFailure is not null, TimeSpan.FromSeconds(1)));
        var failure = Assert.IsType<SerialHidSessionFaultException>(session.BackgroundFailure);
        Assert.Equal(SerialHidSessionFaultKind.Timeout, failure.Kind);
        var requestsBeforeStop = exchange.RequestKinds().Count;

        session.Stop();

        Assert.Equal(requestsBeforeStop, exchange.RequestKinds().Count);
        Assert.DoesNotContain(SerialHidMessageKind.AllUp, exchange.RequestKinds());
        Assert.Equal(1, exchange.DisposeCount);
    }

    [Fact]
    public void Serial_hid_and_g600_onboard_are_explicitly_exclusive()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ResidentOutputPolicy.EnsureStartAllowed(ResidentOutputRoute.SerialHid, g600OnboardActive: true));

        Assert.Contains("同時に使えません", error.Message, StringComparison.Ordinal);
        ResidentOutputPolicy.EnsureStartAllowed(ResidentOutputRoute.SerialHid, g600OnboardActive: false);
        ResidentOutputPolicy.EnsureStartAllowed(ResidentOutputRoute.SendInput, g600OnboardActive: true);
    }

    [Fact]
    public void G600_legacy_suppression_is_selected_by_output_route()
    {
        Assert.Equal(
            G600LegacySuppressionMode.IntermediateUsage,
            G600LeftoverHostSupport.SuppressionModeFor(ResidentOutputRoute.SendInput));
        Assert.Equal(
            G600LegacySuppressionMode.NoOutput,
            G600LeftoverHostSupport.SuppressionModeFor(ResidentOutputRoute.SerialHid));
    }

    [Fact]
    public void Heartbeat_interval_must_be_shorter_than_firmware_lease()
    {
        using var exchange = new FakeExchange();

        Assert.Throws<ArgumentOutOfRangeException>(() => new SerialHidResidentOutputSession(
            exchange,
            new SerialHidSemanticVersion(1, 0, 0),
            TimeSpan.FromMilliseconds(80),
            TimeSpan.FromMilliseconds(SerialHidProtocolV1.LeaseMilliseconds)));
    }

    private static SerialHidResidentOutputSession CreateSession(
        FakeExchange exchange,
        TimeSpan? heartbeatInterval = null) =>
        new(
            exchange,
            new SerialHidSemanticVersion(1, 0, 0),
            TimeSpan.FromMilliseconds(80),
            heartbeatInterval ?? TimeSpan.FromMilliseconds(140));

    private static MappedOutputEdge Down(string output) => new(output, PhysicalInputEdge.Down);
    private static MappedOutputEdge Up(string output) => new(output, PhysicalInputEdge.Up);

    private sealed class FakeExchange : ISerialHidFrameExchange
    {
        private readonly object _gate = new();
        private readonly List<byte[]> _requests = [];

        public bool FailHeartbeat { get; init; }
        public int DisposeCount { get; private set; }

        public byte[] Exchange(ReadOnlyMemory<byte> requestFrame, TimeSpan timeout)
        {
            var requestBytes = requestFrame.ToArray();
            var request = SerialHidProtocolV1.Decode(requestBytes);
            lock (_gate)
            {
                _requests.Add(requestBytes);
            }

            if (FailHeartbeat && request.Kind == SerialHidMessageKind.Heartbeat)
            {
                throw new TimeoutException("fake heartbeat timeout");
            }

            if (request.Kind == SerialHidMessageKind.Hello)
            {
                return SerialHidProtocolV1.Encode(
                    SerialHidMessageKind.Ready,
                    request.Sequence,
                    [1, 0, 0, 1, 7, 0, 6, 150, 0]);
            }

            return SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, request.Sequence, []);
        }

        public IReadOnlyList<SerialHidMessageKind> RequestKinds()
        {
            lock (_gate)
            {
                return _requests
                    .Select(request => SerialHidProtocolV1.Decode(request).Kind)
                    .ToArray();
            }
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
