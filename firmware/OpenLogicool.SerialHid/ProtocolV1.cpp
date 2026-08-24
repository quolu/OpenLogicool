#include "ProtocolV1.h"

namespace openlogicool {
namespace protocol_v1 {

uint16_t ReadUInt16LittleEndian(const uint8_t* source) {
  return static_cast<uint16_t>(source[0]) |
         (static_cast<uint16_t>(source[1]) << 8);
}

void WriteUInt16LittleEndian(uint8_t* destination, uint16_t value) {
  destination[0] = static_cast<uint8_t>(value & 0xFF);
  destination[1] = static_cast<uint8_t>(value >> 8);
}

uint16_t ComputeCrc(const uint8_t* data, uint16_t length) {
  uint16_t crc = 0xFFFF;
  for (uint16_t index = 0; index < length; ++index) {
    crc ^= static_cast<uint16_t>(data[index]) << 8;
    for (uint8_t bit = 0; bit < 8; ++bit) {
      crc = (crc & 0x8000) != 0
          ? static_cast<uint16_t>((crc << 1) ^ 0x1021)
          : static_cast<uint16_t>(crc << 1);
    }
  }
  return crc;
}

bool IsKnownMessageKind(uint8_t rawKind) {
  return rawKind >= static_cast<uint8_t>(MessageKind::Hello) &&
         rawKind <= static_cast<uint8_t>(MessageKind::MouseDelta);
}

bool IsValidSetStatePayload(const uint8_t* payload, uint16_t length) {
  if (length != 8 || (payload[7] & 0xE0) != 0) {
    return false;
  }

  uint8_t previous = 0;
  bool sawPadding = false;
  for (uint8_t index = 1; index <= kMaxNormalKeys; ++index) {
    const uint8_t usage = payload[index];
    if (usage == 0) {
      sawPadding = true;
      continue;
    }
    if (sawPadding || usage < 0x04 || usage > 0xDF || usage <= previous) {
      return false;
    }
    previous = usage;
  }

  return true;
}

bool IsValidMouseDeltaPayload(const uint8_t* payload, uint16_t length) {
  return length == 3 && payload[0] != 0x80 && payload[1] != 0x80 && payload[2] != 0x80;
}

bool TryNegotiateCapabilities(uint16_t requested, uint16_t& negotiated) {
  if ((requested & ~kSupportedCapabilities) != 0) {
    return false;
  }
  negotiated = requested;
  return true;
}

bool DecodeFrame(
    const uint8_t* frame,
    uint16_t frameLength,
    FrameView& result,
    FaultCode& fault,
    uint16_t& correlatedSequence,
    uint8_t& offendingKind) {
  correlatedSequence = frameLength >= 6 ? ReadUInt16LittleEndian(frame + 4) : 0;
  offendingKind = frameLength >= 4 ? frame[3] : 0;

  if (frameLength < kHeaderLength + kCrcLength) {
    fault = FaultCode::LengthMismatch;
    return false;
  }
  if (frame[0] != kMagic0 || frame[1] != kMagic1) {
    correlatedSequence = 0;
    offendingKind = 0;
    fault = FaultCode::BadMagic;
    return false;
  }

  const uint16_t payloadLength = ReadUInt16LittleEndian(frame + 6);
  if (payloadLength > kMaxPayloadLength ||
      frameLength != kHeaderLength + payloadLength + kCrcLength) {
    fault = FaultCode::LengthMismatch;
    return false;
  }

  const uint16_t expectedCrc = ReadUInt16LittleEndian(frame + kHeaderLength + payloadLength);
  if (ComputeCrc(frame, kHeaderLength + payloadLength) != expectedCrc) {
    fault = FaultCode::ChecksumMismatch;
    return false;
  }
  if (frame[2] != kVersion) {
    fault = FaultCode::UnsupportedVersion;
    return false;
  }
  if (!IsKnownMessageKind(offendingKind)) {
    fault = FaultCode::UnknownMessage;
    return false;
  }
  if (correlatedSequence == 0 && offendingKind != static_cast<uint8_t>(MessageKind::Fault)) {
    fault = FaultCode::SequenceViolation;
    return false;
  }

  result.kind = static_cast<MessageKind>(offendingKind);
  result.sequence = correlatedSequence;
  result.payload = frame + kHeaderLength;
  result.payloadLength = payloadLength;
  return true;
}

uint16_t EncodeFrame(
    MessageKind kind,
    uint16_t sequence,
    const uint8_t* payload,
    uint16_t payloadLength,
    uint8_t* destination,
    uint16_t destinationLength) {
  const uint16_t frameLength = kHeaderLength + payloadLength + kCrcLength;
  if (payloadLength > kMaxPayloadLength || destinationLength < frameLength ||
      (sequence == 0 && kind != MessageKind::Fault)) {
    return 0;
  }

  destination[0] = kMagic0;
  destination[1] = kMagic1;
  destination[2] = kVersion;
  destination[3] = static_cast<uint8_t>(kind);
  WriteUInt16LittleEndian(destination + 4, sequence);
  WriteUInt16LittleEndian(destination + 6, payloadLength);
  for (uint16_t index = 0; index < payloadLength; ++index) {
    destination[kHeaderLength + index] = payload[index];
  }
  WriteUInt16LittleEndian(
      destination + kHeaderLength + payloadLength,
      ComputeCrc(destination, kHeaderLength + payloadLength));
  return frameLength;
}

}  // namespace protocol_v1
}  // namespace openlogicool
