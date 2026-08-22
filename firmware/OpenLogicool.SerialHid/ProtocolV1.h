#pragma once

#include <Arduino.h>

namespace openlogicool {
namespace protocol_v1 {

constexpr uint8_t kMagic0 = 0x4F;
constexpr uint8_t kMagic1 = 0x4C;
constexpr uint8_t kVersion = 0x01;
constexpr uint16_t kMaxPayloadLength = 32;
constexpr uint16_t kHeaderLength = 8;
constexpr uint16_t kCrcLength = 2;
constexpr uint16_t kMaxFrameLength = kHeaderLength + kMaxPayloadLength + kCrcLength;
constexpr uint16_t kLeaseMilliseconds = 150;
constexpr uint16_t kSupportedCapabilities = 0x0007;
constexpr uint8_t kMaxNormalKeys = 6;

enum class MessageKind : uint8_t {
  Hello = 0x01,
  Ready = 0x02,
  SetState = 0x03,
  AllUp = 0x04,
  Heartbeat = 0x05,
  Ack = 0x06,
  Fault = 0x07,
};

enum class FaultCode : uint8_t {
  BadMagic = 0x01,
  UnsupportedVersion = 0x02,
  ChecksumMismatch = 0x03,
  LengthMismatch = 0x04,
  UnknownMessage = 0x05,
  InvalidPayload = 0x06,
  UnsupportedCapability = 0x07,
  SequenceViolation = 0x08,
  InternalFault = 0x09,
};

struct FrameView {
  MessageKind kind;
  uint16_t sequence;
  const uint8_t* payload;
  uint16_t payloadLength;
};

uint16_t ReadUInt16LittleEndian(const uint8_t* source);
void WriteUInt16LittleEndian(uint8_t* destination, uint16_t value);
uint16_t ComputeCrc(const uint8_t* data, uint16_t length);
bool IsKnownMessageKind(uint8_t rawKind);
bool IsValidSetStatePayload(const uint8_t* payload, uint16_t length);

bool DecodeFrame(
    const uint8_t* frame,
    uint16_t frameLength,
    FrameView& result,
    FaultCode& fault,
    uint16_t& correlatedSequence,
    uint8_t& offendingKind);

uint16_t EncodeFrame(
    MessageKind kind,
    uint16_t sequence,
    const uint8_t* payload,
    uint16_t payloadLength,
    uint8_t* destination,
    uint16_t destinationLength);

}  // namespace protocol_v1
}  // namespace openlogicool
