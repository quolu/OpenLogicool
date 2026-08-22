#include <Arduino.h>
#include <HID.h>

#include "OpenLogicoolHid.h"
#include "ProtocolV1.h"

using openlogicool::OpenLogicoolHid;
using namespace openlogicool::protocol_v1;

namespace {

constexpr uint8_t kFirmwareVersionMajor = 1;
constexpr uint8_t kFirmwareVersionMinor = 0;
constexpr uint8_t kFirmwareVersionPatch = 0;

uint8_t inputFrame[kMaxFrameLength];
uint16_t inputLength = 0;
uint16_t expectedFrameLength = 0;
bool protocolReady = false;
bool leaseArmed = false;
bool releasePending = false;
bool usbWasConfigured = false;
uint16_t lastAcceptedSequence = 0;
uint32_t lastValidFrameAt = 0;

void ResetReader() {
  inputLength = 0;
  expectedFrameLength = 0;
}

void ResetProtocolState() {
  protocolReady = false;
  leaseArmed = false;
  lastAcceptedSequence = 0;
  ResetReader();
}

void EnterFailClosedRelease() {
  ResetProtocolState();
  releasePending = !OpenLogicoolHid.AllUp();
}

uint16_t NextSequence(uint16_t current) {
  return current == 0xFFFF ? 1 : static_cast<uint16_t>(current + 1);
}

void SendFrame(MessageKind kind, uint16_t sequence, const uint8_t* payload, uint16_t payloadLength) {
  uint8_t frame[kMaxFrameLength];
  const uint16_t length = EncodeFrame(kind, sequence, payload, payloadLength, frame, sizeof(frame));
  if (length != 0) {
    Serial.write(frame, length);
  }
}

void SendAck(uint16_t sequence) {
  SendFrame(MessageKind::Ack, sequence, nullptr, 0);
}

void SendFault(FaultCode code, uint16_t sequence, uint8_t offendingKind) {
  const uint8_t payload[2] = {static_cast<uint8_t>(code), offendingKind};
  SendFrame(MessageKind::Fault, sequence, payload, sizeof(payload));
}

void ArmLease() {
  lastValidFrameAt = millis();
  leaseArmed = true;
}

bool HasExpectedSequence(uint16_t sequence) {
  return protocolReady && sequence == NextSequence(lastAcceptedSequence);
}

bool HasPayloadLength(const FrameView& frame, uint16_t expected) {
  if (frame.payloadLength == expected) {
    return true;
  }
  SendFault(FaultCode::InvalidPayload, frame.sequence, static_cast<uint8_t>(frame.kind));
  return false;
}

void ProcessHello(const FrameView& frame) {
  if (!HasPayloadLength(frame, 5)) {
    return;
  }
  const uint16_t requestedCapabilities = ReadUInt16LittleEndian(frame.payload + 3);
  if ((requestedCapabilities & ~kSupportedCapabilities) != 0) {
    SendFault(FaultCode::UnsupportedCapability, frame.sequence, static_cast<uint8_t>(frame.kind));
    return;
  }

  EnterFailClosedRelease();
  if (releasePending) {
    SendFault(FaultCode::InternalFault, frame.sequence, static_cast<uint8_t>(frame.kind));
    return;
  }

  protocolReady = true;
  lastAcceptedSequence = frame.sequence;
  ArmLease();
  uint8_t ready[9] = {
      kFirmwareVersionMajor,
      kFirmwareVersionMinor,
      kFirmwareVersionPatch,
      kVersion,
      0,
      0,
      kMaxNormalKeys,
      0,
      0,
  };
  WriteUInt16LittleEndian(ready + 4, kSupportedCapabilities);
  WriteUInt16LittleEndian(ready + 7, kLeaseMilliseconds);
  SendFrame(MessageKind::Ready, frame.sequence, ready, sizeof(ready));
}

void ProcessSetState(const FrameView& frame) {
  if (!IsValidSetStatePayload(frame.payload, frame.payloadLength)) {
    SendFault(FaultCode::InvalidPayload, frame.sequence, static_cast<uint8_t>(frame.kind));
    return;
  }
  if (!OpenLogicoolHid.Apply(frame.payload[0], frame.payload + 1, frame.payload[7])) {
    EnterFailClosedRelease();
    SendFault(FaultCode::InternalFault, frame.sequence, static_cast<uint8_t>(frame.kind));
    return;
  }
  lastAcceptedSequence = frame.sequence;
  ArmLease();
  SendAck(frame.sequence);
}

void ProcessAllUp(const FrameView& frame) {
  if (!HasPayloadLength(frame, 0)) {
    return;
  }
  if (!OpenLogicoolHid.AllUp()) {
    EnterFailClosedRelease();
    SendFault(FaultCode::InternalFault, frame.sequence, static_cast<uint8_t>(frame.kind));
    return;
  }
  lastAcceptedSequence = frame.sequence;
  ArmLease();
  SendAck(frame.sequence);
}

void ProcessHeartbeat(const FrameView& frame) {
  if (!HasPayloadLength(frame, 0)) {
    return;
  }
  lastAcceptedSequence = frame.sequence;
  ArmLease();
  SendAck(frame.sequence);
}

void ProcessFrame(const uint8_t* bytes, uint16_t length) {
  FrameView frame = {};
  FaultCode fault = FaultCode::InternalFault;
  uint16_t sequence = 0;
  uint8_t offendingKind = 0;
  if (!DecodeFrame(bytes, length, frame, fault, sequence, offendingKind)) {
    SendFault(fault, sequence, offendingKind);
    return;
  }

  if (releasePending) {
    SendFault(FaultCode::InternalFault, frame.sequence, static_cast<uint8_t>(frame.kind));
    return;
  }

  if (frame.kind == MessageKind::Hello) {
    ProcessHello(frame);
    return;
  }
  if (!HasExpectedSequence(frame.sequence)) {
    SendFault(FaultCode::SequenceViolation, frame.sequence, static_cast<uint8_t>(frame.kind));
    return;
  }

  switch (frame.kind) {
    case MessageKind::SetState:
      ProcessSetState(frame);
      return;
    case MessageKind::AllUp:
      ProcessAllUp(frame);
      return;
    case MessageKind::Heartbeat:
      ProcessHeartbeat(frame);
      return;
    default:
      SendFault(FaultCode::UnknownMessage, frame.sequence, static_cast<uint8_t>(frame.kind));
      return;
  }
}

void AcceptSerialByte(uint8_t value) {
  if (inputLength == 0) {
    if (value == kMagic0) {
      inputFrame[inputLength++] = value;
    }
    return;
  }
  if (inputLength == 1 && value != kMagic1) {
    inputLength = value == kMagic0 ? 1 : 0;
    return;
  }

  inputFrame[inputLength++] = value;
  if (inputLength == kHeaderLength) {
    const uint16_t payloadLength = ReadUInt16LittleEndian(inputFrame + 6);
    if (payloadLength > kMaxPayloadLength) {
      SendFault(
          FaultCode::LengthMismatch,
          ReadUInt16LittleEndian(inputFrame + 4),
          inputFrame[3]);
      ResetReader();
      return;
    }
    expectedFrameLength = kHeaderLength + payloadLength + kCrcLength;
  }
  if (expectedFrameLength != 0 && inputLength == expectedFrameLength) {
    ProcessFrame(inputFrame, inputLength);
    ResetReader();
  }
}

void PollUsbConfiguration() {
  const bool configured = USBDevice.configured();
  if (configured == usbWasConfigured) {
    return;
  }
  usbWasConfigured = configured;
  ResetProtocolState();
  if (configured) {
    releasePending = !OpenLogicoolHid.AllUp();
  } else {
    releasePending = false;
  }
}

void RetryPendingRelease() {
  if (releasePending && USBDevice.configured()) {
    releasePending = !OpenLogicoolHid.AllUp();
  }
}

void ExpireLeaseIfNeeded() {
  if (!leaseArmed || static_cast<uint32_t>(millis() - lastValidFrameAt) < kLeaseMilliseconds) {
    return;
  }
  EnterFailClosedRelease();
}

}  // namespace

void setup() {
  ResetProtocolState();
  Serial.begin(115200);
  usbWasConfigured = USBDevice.configured();
  releasePending = usbWasConfigured && !OpenLogicoolHid.AllUp();
}

void loop() {
  PollUsbConfiguration();
  RetryPendingRelease();
  while (Serial.available() > 0) {
    AcceptSerialByte(static_cast<uint8_t>(Serial.read()));
  }
  ExpireLeaseIfNeeded();
}
