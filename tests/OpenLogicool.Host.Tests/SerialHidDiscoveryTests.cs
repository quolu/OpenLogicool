using System.IO;
using OpenLogicool.Desktop;
using OpenLogicool.Input;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class SerialHidDiscoveryTests
{
    private static readonly SerialHidCandidate CandidateA =
        new(@"USB\VID_1B4F&PID_9206\A", "COM7", "path-a", 0x1B4F, 0x9206);
    private static readonly SerialHidCandidate CandidateB =
        new(@"USB\VID_1B4F&PID_9206\B", "COM8", "path-b", 0x1B4F, 0x9206);

    [Fact]
    public void Setupapi_identity_filter_accepts_only_sparkfun_runtime_vid_pid()
    {
        Assert.True(SetupApiSerialCandidateEnumerator.TryParseSparkFunRuntimeIdentity(
            CandidateA.DeviceInstanceId, out var vendorId, out var productId));
        Assert.Equal((ushort)0x1B4F, vendorId);
        Assert.Equal((ushort)0x9206, productId);
        Assert.True(SetupApiSerialCandidateEnumerator.TryParseSparkFunRuntimeIdentity(
            @"USB\VID_1B4F&PID_9205\BOOT", out _, out var bootProductId));
        Assert.Equal((ushort)0x9205, bootProductId);
        Assert.False(SetupApiSerialCandidateEnumerator.TryParseSparkFunRuntimeIdentity(
            @"USB\VID_2341&PID_8036\OTHER", out _, out _));
    }

    [Fact]
    public void Discovery_requires_exactly_one_successful_handshake()
    {
        Assert.Throws<SerialHidDiscoveryException>(() => Service([]).Resolve(null));

        var automatic = Service([CandidateA]).Resolve(null);
        using (automatic.Session)
        {
            Assert.Equal(CandidateA.DeviceInstanceId, automatic.Candidate.DeviceInstanceId);
        }

        var selected = Service([CandidateA]).Resolve(CandidateA.DeviceInstanceId);
        using (selected.Session)
        {
            Assert.Equal(CandidateA.DeviceInstanceId, selected.Candidate.DeviceInstanceId);
        }

        var ambiguous = Assert.Throws<SerialHidDiscoveryException>(() => Service([CandidateA, CandidateB]).Resolve(null));
        Assert.Contains("2台", ambiguous.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Saved_device_identity_restricts_handshake_without_using_com_number()
    {
        var factory = new FakeExchangeFactory();
        var service = new SerialHidDiscoveryService(new FakeCandidates([CandidateA, CandidateB]), factory);

        var selection = service.Resolve(CandidateB.DeviceInstanceId);
        using (selection.Session)
        {
            Assert.Equal(CandidateB.DeviceInstanceId, selection.Candidate.DeviceInstanceId);
            Assert.Equal([CandidateB.DeviceInstanceId], factory.OpenedDeviceInstanceIds);
        }
    }

    [Fact]
    public void Protocol_version_mismatch_is_reported_separately()
    {
        var service = new SerialHidDiscoveryService(
            new FakeCandidates([CandidateA]),
            new FakeExchangeFactory(responseProtocolVersion: 2));

        var error = Assert.Throws<SerialHidDiscoveryException>(() => service.Resolve(null));

        Assert.Contains("version", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v1", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Connection_test_performs_handshake_all_up_and_close()
    {
        var factory = new FakeExchangeFactory();
        var service = new SerialHidDiscoveryService(new FakeCandidates([CandidateA]), factory);

        var result = service.Test(CandidateA.DeviceInstanceId);

        Assert.True(result.Success, result.StatusLine);
        var exchange = Assert.Single(factory.Exchanges);
        Assert.Equal(
            [SerialHidMessageKind.Hello, SerialHidMessageKind.AllUp],
            exchange.RequestKinds);
        Assert.Equal(1, exchange.DisposeCount);
    }

    [Fact]
    public void Response_frame_assembler_handles_partial_chunks_and_magic_resynchronization()
    {
        var frame = SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, 42, []);
        var assembler = new SerialHidResponseFrameAssembler();
        byte[]? completed = null;

        foreach (var chunk in new byte[][]
                 {
                     [0x00, 0x4F, 0x4F],
                     [0x4C, frame[2], frame[3]],
                     frame[4..7],
                     frame[7..],
                 })
        {
            foreach (var value in chunk)
            {
                completed = assembler.Accept(value) ?? completed;
            }
        }

        Assert.Equal(frame, completed);
        Assert.Equal(SerialHidMessageKind.Ack, SerialHidProtocolV1.Decode(completed!).Kind);
    }

    [Fact]
    public void Response_frame_assembler_rejects_oversized_declared_payload_before_body()
    {
        var assembler = new SerialHidResponseFrameAssembler();
        var header = new byte[] { 0x4F, 0x4C, 1, 6, 9, 0, 33, 0 };

        var error = Assert.Throws<SerialHidProtocolException>(() =>
        {
            foreach (var value in header)
            {
                assembler.Accept(value);
            }
        });

        Assert.Equal(SerialHidFaultCode.LengthMismatch, error.FaultCode);
        Assert.Equal((ushort)9, error.Sequence);
    }

    [Fact]
    public void Serial_route_discovery_failure_is_terminal_without_sendinput_fallback()
    {
        var settings = new SerialHidOutputSettings(
            SerialHidOutputSettings.CurrentSchemaVersion,
            ResidentOutputRoute.SerialHid,
            CandidateA.DeviceInstanceId);
        var factory = ResidentOutputSessionFactory.Create(settings, "unused-watchdog.exe", Service([]));

        Assert.Throws<SerialHidDiscoveryException>(() => factory());
    }

    [Fact]
    public void Settings_store_roundtrips_pnp_identity_and_rejects_com_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openlogicool-serial-settings-{Guid.NewGuid():N}");
        try
        {
            var store = new SerialHidOutputSettingsStore(directory);
            var settings = new SerialHidOutputSettings(
                SerialHidOutputSettings.CurrentSchemaVersion,
                ResidentOutputRoute.SerialHid,
                CandidateA.DeviceInstanceId);

            store.Save(settings);

            Assert.Equal(settings, store.Load());
            Assert.Throws<InvalidDataException>(() => store.Save(settings with { SelectedDeviceInstanceId = "COM7" }));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(ResidentOutputRoute.SendInput, null, "次に常駐")]
    [InlineData(ResidentOutputRoute.SerialHid, ResidentOutputRoute.SerialHid, "使用中")]
    [InlineData(ResidentOutputRoute.SerialHid, ResidentOutputRoute.SendInput, "次回起動から")]
    public void Status_distinguishes_requested_and_active_route(
        ResidentOutputRoute requested,
        ResidentOutputRoute? active,
        string expected)
    {
        Assert.Contains(expected, HostSerialHidSettingsIntent.DescribeStatus(requested, active), StringComparison.Ordinal);
    }

    [Fact]
    public void Serial_hid_setting_is_saved_only_after_successful_connection_test()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openlogicool-serial-intent-{Guid.NewGuid():N}");
        try
        {
            var store = new SerialHidOutputSettingsStore(directory);
            var discovery = Service([CandidateA]);
            var intent = new HostSerialHidSettingsIntent(store, discovery, () => ResidentOutputRoute.SendInput);

            var result = intent.SaveAndTest(OutputRouteChoice.SerialHid, CandidateA.DeviceInstanceId);

            Assert.True(result.Success, result.Snapshot.StatusLine);
            Assert.Equal(ResidentOutputRoute.SerialHid, store.Load().RequestedRoute);
            Assert.Contains("次回起動から", result.Snapshot.StatusLine, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Failed_serial_connection_test_does_not_replace_saved_sendinput_route()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openlogicool-serial-failed-save-{Guid.NewGuid():N}");
        try
        {
            var store = new SerialHidOutputSettingsStore(directory);
            var intent = new HostSerialHidSettingsIntent(store, Service([]), () => null);

            var result = intent.SaveAndTest(OutputRouteChoice.SerialHid, CandidateA.DeviceInstanceId);

            Assert.False(result.Success);
            Assert.Equal(ResidentOutputRoute.SendInput, store.Load().RequestedRoute);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Next_factory_creation_uses_saved_serial_route()
    {
        var settings = new SerialHidOutputSettings(
            SerialHidOutputSettings.CurrentSchemaVersion,
            ResidentOutputRoute.SerialHid,
            CandidateA.DeviceInstanceId);
        var factory = ResidentOutputSessionFactory.Create(settings, "unused-watchdog.exe", Service([CandidateA]));

        using var session = factory();

        Assert.Equal(ResidentOutputRoute.SerialHid, session.Route);
    }

    private static SerialHidDiscoveryService Service(IReadOnlyList<SerialHidCandidate> candidates) =>
        new(new FakeCandidates(candidates), new FakeExchangeFactory());

    private sealed class FakeCandidates(IReadOnlyList<SerialHidCandidate> candidates) : ISerialHidCandidateEnumerator
    {
        public IReadOnlyList<SerialHidCandidate> EnumerateCandidates() => candidates;
    }

    private sealed class FakeExchangeFactory(byte responseProtocolVersion = SerialHidProtocolV1.Version) : ISerialHidExchangeFactory
    {
        public List<string> OpenedDeviceInstanceIds { get; } = [];
        public List<FakeExchange> Exchanges { get; } = [];

        public ISerialHidFrameExchange Open(SerialHidCandidate candidate)
        {
            OpenedDeviceInstanceIds.Add(candidate.DeviceInstanceId);
            var exchange = new FakeExchange(responseProtocolVersion);
            Exchanges.Add(exchange);
            return exchange;
        }
    }

    private sealed class FakeExchange(byte responseProtocolVersion) : ISerialHidFrameExchange
    {
        public List<SerialHidMessageKind> RequestKinds { get; } = [];
        public int DisposeCount { get; private set; }

        public byte[] Exchange(ReadOnlyMemory<byte> requestFrame, TimeSpan timeout)
        {
            var request = SerialHidProtocolV1.Decode(requestFrame.Span);
            RequestKinds.Add(request.Kind);
            return request.Kind == SerialHidMessageKind.Hello
                ? Ready(request.Sequence)
                : SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, request.Sequence, []);
        }

        private byte[] Ready(ushort sequence)
        {
            var frame = SerialHidProtocolV1.Encode(
                SerialHidMessageKind.Ready,
                sequence,
                [1, 0, 0, 1, 7, 0, 6, 150, 0]);
            if (responseProtocolVersion == SerialHidProtocolV1.Version)
            {
                return frame;
            }

            frame[2] = responseProtocolVersion;
            var crc = SerialHidProtocolV1.ComputeCrc(frame.AsSpan(0, frame.Length - SerialHidProtocolV1.CrcLength));
            frame[^2] = (byte)crc;
            frame[^1] = (byte)(crc >> 8);
            return frame;
        }

        public void Dispose() => DisposeCount++;
    }
}
