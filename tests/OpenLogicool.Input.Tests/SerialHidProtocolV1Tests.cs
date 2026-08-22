using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class SerialHidProtocolV1Tests
{
    private sealed record GoldenFile(string Schema, GoldenCrc Crc, GoldenVector[] Vectors);
    private sealed record GoldenCrc(string Algorithm, string CheckAscii, string Check);
    private sealed record GoldenVector(string Name, string Kind, ushort Sequence, string PayloadHex, string FrameHex);

    private static byte[] Hex(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => Convert.ToByte(part, 16))
                .ToArray();

    private static GoldenFile Golden()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "protocol-v1-golden-vectors.json"));
        return JsonSerializer.Deserialize<GoldenFile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("golden vectorを読めません。");
    }

    [Fact]
    public void Csharp_codec_matches_every_shared_golden_vector()
    {
        var golden = Golden();

        Assert.Equal("openlogicool.serial-hid.protocol-v1-golden-vectors.v1", golden.Schema);
        foreach (var vector in golden.Vectors)
        {
            var kind = Enum.Parse<SerialHidMessageKind>(vector.Kind);
            var payload = Hex(vector.PayloadHex);
            var encoded = SerialHidProtocolV1.Encode(kind, vector.Sequence, payload);

            Assert.Equal(Hex(vector.FrameHex), encoded);
            var decoded = SerialHidProtocolV1.Decode(encoded);
            Assert.Equal(SerialHidProtocolV1.Version, decoded.Version);
            Assert.Equal(kind, decoded.Kind);
            Assert.Equal(vector.Sequence, decoded.Sequence);
            Assert.Equal(payload, decoded.Payload);
        }
    }

    [Fact]
    public void Crc_variant_and_endianness_are_fixed()
    {
        var golden = Golden();
        Assert.Equal("CRC-16/CCITT-FALSE", golden.Crc.Algorithm);
        Assert.Equal("29B1", golden.Crc.Check);
        Assert.Equal(0x29B1, SerialHidProtocolV1.ComputeCrc(Encoding.ASCII.GetBytes(golden.Crc.CheckAscii)));

        var frame = SerialHidProtocolV1.Encode(SerialHidMessageKind.AllUp, 0x1234, []);
        Assert.Equal([0x34, 0x12], frame.AsSpan(4, 2).ToArray());
        Assert.Equal([0x00, 0x00], frame.AsSpan(6, 2).ToArray());
    }

    [Fact]
    public void Corruption_unknown_version_length_and_kind_are_explicit_faults()
    {
        var corrupted = SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, 8, []);
        corrupted[^1] ^= 0x01;
        AssertFault(SerialHidFaultCode.ChecksumMismatch, corrupted, sequence: 8);

        var version = SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, 9, []);
        version[2] = 2;
        RewriteCrc(version);
        AssertFault(SerialHidFaultCode.UnsupportedVersion, version, sequence: 9);

        var length = SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, 10, []);
        length[6] = 1;
        AssertFault(SerialHidFaultCode.LengthMismatch, length, sequence: 10);

        var kind = SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, 11, []);
        kind[3] = 0x7F;
        RewriteCrc(kind);
        AssertFault(SerialHidFaultCode.UnknownMessage, kind, sequence: 11);
    }

    [Fact]
    public void Request_sequence_wraps_without_using_zero()
    {
        Assert.Equal((ushort)1, SerialHidProtocolV1.NextRequestSequence(0));
        Assert.Equal((ushort)2, SerialHidProtocolV1.NextRequestSequence(1));
        Assert.Equal((ushort)1, SerialHidProtocolV1.NextRequestSequence(ushort.MaxValue));
    }

    [Fact]
    public void Set_state_rejects_unsorted_duplicate_reserved_and_non_trailing_zero_usages()
    {
        AssertInvalidState([0, 0x06, 0x04, 0, 0, 0, 0, 0]);
        AssertInvalidState([0, 0x04, 0x04, 0, 0, 0, 0, 0]);
        AssertInvalidState([0, 0x04, 0, 0x06, 0, 0, 0, 0]);
        AssertInvalidState([0, 0x01, 0, 0, 0, 0, 0, 0]);
        AssertInvalidState([0, 0xE0, 0, 0, 0, 0, 0, 0]);
        AssertInvalidState([0, 0xE8, 0, 0, 0, 0, 0, 0]);
        AssertInvalidState([0, 0x04, 0, 0, 0, 0, 0, 0x20]);
    }

    [Fact]
    public void Sequence_zero_is_reserved_for_uncorrelated_fault()
    {
        var request = Assert.Throws<SerialHidProtocolException>(
            () => SerialHidProtocolV1.Encode(SerialHidMessageKind.AllUp, sequence: 0, []));
        Assert.Equal(SerialHidFaultCode.SequenceViolation, request.FaultCode);

        var response = Assert.Throws<SerialHidProtocolException>(
            () => SerialHidProtocolV1.Encode(SerialHidMessageKind.Ack, sequence: 0, []));
        Assert.Equal(SerialHidFaultCode.SequenceViolation, response.FaultCode);

        var fault = SerialHidProtocolV1.Encode(
            SerialHidMessageKind.Fault,
            sequence: 0,
            [(byte)SerialHidFaultCode.BadMagic, 0]);
        Assert.Equal((ushort)0, SerialHidProtocolV1.Decode(fault).Sequence);
    }

    private static void AssertFault(SerialHidFaultCode code, byte[] frame, ushort sequence)
    {
        var error = Assert.Throws<SerialHidProtocolException>(() => SerialHidProtocolV1.Decode(frame));
        Assert.Equal(code, error.FaultCode);
        Assert.Equal(sequence, error.Sequence);
    }

    private static void RewriteCrc(byte[] frame)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(frame.Length - 2),
            SerialHidProtocolV1.ComputeCrc(frame.AsSpan(0, frame.Length - 2)));
    }

    private static void AssertInvalidState(byte[] payload)
    {
        var error = Assert.Throws<SerialHidProtocolException>(
            () => SerialHidProtocolV1.Encode(SerialHidMessageKind.SetState, sequence: 1, payload));
        Assert.Equal(SerialHidFaultCode.InvalidPayload, error.FaultCode);
    }
}
