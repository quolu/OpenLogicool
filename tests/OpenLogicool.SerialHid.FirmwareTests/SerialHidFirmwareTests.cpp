#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

#include "FirmwareLease.h"
#include "ProtocolV1.h"

using namespace openlogicool;
using namespace openlogicool::protocol_v1;

namespace {

int HexDigit(char value) {
  if (value >= '0' && value <= '9') return value - '0';
  if (value >= 'A' && value <= 'F') return value - 'A' + 10;
  if (value >= 'a' && value <= 'f') return value - 'a' + 10;
  return -1;
}

std::vector<uint8_t> ParseHex(const char* text) {
  const size_t length = std::strlen(text);
  if ((length % 2) != 0) return {};
  std::vector<uint8_t> result(length / 2);
  for (size_t index = 0; index < length; index += 2) {
    const int high = HexDigit(text[index]);
    const int low = HexDigit(text[index + 1]);
    if (high < 0 || low < 0) return {};
    result[index / 2] = static_cast<uint8_t>((high << 4) | low);
  }
  return result;
}

int Decode(const char* hex) {
  const auto bytes = ParseHex(hex);
  if (bytes.empty()) return 2;
  FrameView frame = {};
  FaultCode fault = FaultCode::InternalFault;
  uint16_t sequence = 0;
  uint8_t offendingKind = 0;
  if (!DecodeFrame(bytes.data(), static_cast<uint16_t>(bytes.size()), frame, fault, sequence, offendingKind)) {
    std::printf("fault|%u|%u|%u\n", static_cast<unsigned>(fault), sequence, offendingKind);
    return 0;
  }

  std::printf("ok|%u|%u|", static_cast<unsigned>(frame.kind), frame.sequence);
  for (uint16_t index = 0; index < frame.payloadLength; ++index) {
    std::printf("%02X", frame.payload[index]);
  }
  std::printf("\n");
  return 0;
}

int Lease() {
  FirmwareLease lease;
  lease.Reset();
  if (lease.IsExpired(1000)) return 10;
  lease.Arm(1000);
  if (lease.IsExpired(1149) || !lease.IsExpired(1150)) return 11;

  lease.Reset();
  lease.Arm(0xFFFFFFF0u);
  if (lease.IsExpired(0x00000085u) || !lease.IsExpired(0x00000086u)) return 12;
  std::printf("lease|ok|150\n");
  return 0;
}

int Faults() {
  uint8_t frame[kMaxFrameLength] = {};
  const uint16_t length = EncodeFrame(MessageKind::Ack, 9, nullptr, 0, frame, sizeof(frame));
  FrameView decoded = {};
  FaultCode fault = FaultCode::InternalFault;
  uint16_t sequence = 0;
  uint8_t offendingKind = 0;

  frame[length - 1] ^= 0xFF;
  if (DecodeFrame(frame, length, decoded, fault, sequence, offendingKind) ||
      fault != FaultCode::ChecksumMismatch || sequence != 9) {
    return 20;
  }

  frame[length - 1] ^= 0xFF;
  frame[2] = 2;
  WriteUInt16LittleEndian(frame + length - kCrcLength, ComputeCrc(frame, length - kCrcLength));
  if (DecodeFrame(frame, length, decoded, fault, sequence, offendingKind) ||
      fault != FaultCode::UnsupportedVersion || sequence != 9) {
    return 21;
  }

  std::printf("faults|ok|checksum|version\n");
  return 0;
}

}  // namespace

int main(int argc, char** argv) {
  if (argc == 3 && std::strcmp(argv[1], "decode") == 0) return Decode(argv[2]);
  if (argc == 2 && std::strcmp(argv[1], "lease") == 0) return Lease();
  if (argc == 2 && std::strcmp(argv[1], "faults") == 0) return Faults();
  return 1;
}
